using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WarMusic.Audio;
using WarMusic.Models;
using WarMusic.Services;

internal static class HardwareSoak
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static void PrintDevices()
    {
        Console.WriteLine("Playback devices (--monitor and --cable):");
        foreach (var device in Devices.List(DataFlow.Render))
        {
            Console.WriteLine($"  {device.Name}\n    {device.Id}");
        }

        Console.WriteLine("\nRecording devices (--mic):");
        foreach (var device in Devices.List(DataFlow.Capture))
        {
            Console.WriteLine($"  {device.Name}\n    {device.Id}");
        }
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var duration = ParseDuration(args);
            var monitor = ResolveRequired(Devices.List(DataFlow.Render), args, "--monitor");
            var cable = ResolveRequired(Devices.List(DataFlow.Render), args, "--cable");
            var microphone = ResolveRequired(Devices.List(DataFlow.Capture), args, "--mic");
            if (monitor.Id == cable.Id)
            {
                throw new InvalidOperationException("The monitor and cable must be different playback devices.");
            }

            if (!IsCablePlayback(cable.Name))
            {
                throw new InvalidOperationException("--cable must identify CABLE Input (the VB-CABLE playback endpoint).");
            }

            if (microphone.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("--mic must identify a physical microphone, not CABLE Output.");
            }

            var runRoot = Path.Combine(
                Path.GetTempPath(),
                "WarMusic-HardwareTests",
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Store.Initialize(runRoot);
            var resultsPath = Path.Combine(Store.Data, "soak-results.jsonl");
            var clipPath = Path.Combine(Store.Data, "soak-clip.wav");
            CreateClip(clipPath);

            var toneMilliseconds = checked((int)Math.Ceiling(duration.TotalMilliseconds + TimeSpan.FromMinutes(1).TotalMilliseconds));
            using var source = StartToneProcess(monitor.Id, toneMilliseconds);
            using var engine = new AudioEngine();
            string? failure = null;
            engine.Error += message => Volatile.Write(ref failure, message);
            var profile = new Profile
            {
                MicId = microphone.Id,
                MonitorId = monitor.Id,
                CableId = cable.Id,
            };
            var options = new MixOptions
            {
                DuckEnabled = true,
                DuckMode = 2,
                ThresholdDb = -35,
                ReductionDb = 15,
                AttackMs = 30,
                HoldMs = 350,
                ReleaseMs = 600,
                MicGain = 1,
                AppGain = 1,
                ClipGain = .35f,
                SendGain = 1,
                MonitorGain = .4f,
                MonitorApplication = true,
                MonitorMicrophone = true,
                MicrophoneMonitorGain = .15f,
            };
            engine.Configure(options);
            engine.Start(profile, route: true);
            engine.SetLiveMicrophone(profile, enabled: true);

            using var enumerator = new MMDeviceEnumerator();
            var captureEndpoint = Devices.List(DataFlow.Capture)
                .FirstOrDefault(device => device.Name.StartsWith("CABLE Output", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("CABLE Output was not found for the continuity measurement.");
            using var receivingDevice = enumerator.GetDevice(captureEndpoint.Id);
            using var receiving = new WasapiCapture(receivingDevice);
            var gaps = new GapDetector(receiving.WaveFormat, TimeSpan.FromSeconds(5));
            receiving.DataAvailable += (_, eventArgs) => gaps.Observe(eventArgs.Buffer, eventArgs.BytesRecorded);
            receiving.StartRecording();

            await Task.Delay(TimeSpan.FromSeconds(2));
            await engine.ConnectSource(source.Id);
            engine.SetLoop(true);
            engine.Play(new Sound { Name = "Soak clip", Path = clipPath }, privatePreview: false, normalize: false);
            engine.Toggle();

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                Console.WriteLine($"Running hardware soak for {duration}. Results: {resultsPath}");
                var clock = Stopwatch.StartNew();
                var second = 0;
                while (clock.Elapsed < duration && !cancellation.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
                    second++;
                    if (source.HasExited)
                    {
                        throw new InvalidOperationException("The synthetic process-audio source stopped early.");
                    }

                    var reportedFailure = Volatile.Read(ref failure);
                    if (!string.IsNullOrWhiteSpace(reportedFailure))
                    {
                        throw new InvalidOperationException(reportedFailure);
                    }

                    ExerciseControls(engine, options, clipPath, second);
                    var health = engine.Health;
                    var sample = new
                    {
                        elapsedSeconds = Math.Round(clock.Elapsed.TotalSeconds, 1),
                        health,
                        maximumDetectedGapMilliseconds = gaps.MaximumGapMilliseconds,
                        engine.Routing,
                        engine.Running,
                        engine.Transmitting,
                        engine.SourcePid,
                    };
                    await File.AppendAllTextAsync(
                        resultsPath,
                        JsonSerializer.Serialize(sample) + Environment.NewLine,
                        cancellation.Token);

                    if (second % 60 == 0)
                    {
                        Console.WriteLine(
                            $"{clock.Elapsed:hh\\:mm\\:ss} · callbacks {health.RenderCallbacks} · "
                            + $"buffer {health.MaximumMonitorBufferMilliseconds:0.0} ms · "
                            + $"render {health.MaximumRenderMilliseconds:0.0} ms · "
                            + $"gap {gaps.MaximumGapMilliseconds:0.0} ms");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.WriteLine("Soak cancelled; partial results were retained.");
                return 2;
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
                receiving.StopRecording();
            }

            var final = engine.Health;
            engine.Panic();
            if (engine.Transmitting)
            {
                throw new InvalidOperationException("Panic did not leave music transmission muted.");
            }

            if (!engine.Running || !engine.Routing || engine.SourcePid != source.Id)
            {
                throw new InvalidOperationException("The route or process capture stopped during the soak.");
            }

            if (final.MaximumMonitorBufferMilliseconds > 250.1 || final.MaximumInputBufferMilliseconds > 250.1)
            {
                throw new InvalidOperationException("An audio buffer exceeded its fixed 250 ms capacity.");
            }

            if (final.MaximumRenderMilliseconds > 30)
            {
                throw new InvalidOperationException(
                    $"The render callback exceeded 30 ms ({final.MaximumRenderMilliseconds:0.0} ms).");
            }

            if (gaps.MaximumGapMilliseconds > 30)
            {
                throw new InvalidOperationException(
                    $"CABLE Output contained a {gaps.MaximumGapMilliseconds:0.0} ms detected gap.");
            }

            if (gaps.NonSilentFrames == 0 || final.RenderCallbacks == 0)
            {
                throw new InvalidOperationException("No measurable cable audio or render activity was observed.");
            }

            Console.WriteLine("PASS Hardware soak completed with bounded buffers and no detected gap over 30 ms.");
            Console.WriteLine(JsonSerializer.Serialize(final, IndentedJson));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Hardware soak: " + exception);
            return 1;
        }
    }

    private static void ExerciseControls(AudioEngine engine, MixOptions options, string clipPath, int second)
    {
        switch (second % 60)
        {
            case 10:
                engine.SetComms(true);
                break;
            case 12:
                engine.SetComms(false);
                break;
            case 20:
                engine.Panic();
                if (engine.Transmitting)
                {
                    throw new InvalidOperationException("Panic failed to mute transmission.");
                }

                engine.Toggle();
                break;
            case 30:
                engine.Play(new Sound { Name = "Private soak preview", Path = clipPath },
                    privatePreview: true, normalize: false);
                break;
            case 35:
                engine.Play(new Sound { Name = "Soak clip", Path = clipPath },
                    privatePreview: false, normalize: false);
                break;
            case 40:
                engine.StopLocal();
                engine.Play(new Sound { Name = "Soak clip", Path = clipPath },
                    privatePreview: false, normalize: false);
                break;
            case 50:
                engine.Configure(options with { AppGain = .85f });
                break;
            case 55:
                engine.Configure(options);
                break;
        }
    }

    private static TimeSpan ParseDuration(string[] args)
    {
        var minutes = Option(args, "--minutes");
        if (minutes is not null)
        {
            return TimeSpan.FromMinutes(ParsePositive(minutes, "--minutes"));
        }

        var hours = Option(args, "--hours");
        return TimeSpan.FromHours(hours is null ? 8 : ParsePositive(hours, "--hours"));
    }

    private static double ParsePositive(string value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number <= 0
            || number > 24)
        {
            throw new ArgumentException($"{name} must be a number greater than zero and no more than 24.");
        }

        return number;
    }

    private static Device ResolveRequired(IReadOnlyCollection<Device> devices, string[] args, string optionName)
    {
        var selector = Option(args, optionName)
            ?? throw new ArgumentException($"{optionName} is required. Run with --list-devices to copy a device ID.");
        var exact = devices.FirstOrDefault(device => device.Id.Equals(selector, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var matches = devices
            .Where(device => device.Name.Contains(selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No active device matched {optionName} '{selector}'."),
            _ => throw new ArgumentException($"Multiple devices matched {optionName} '{selector}'; use the full device ID."),
        };
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool IsCablePlayback(string name) =>
        name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
        || name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);

    private static Process StartToneProcess(string deviceId, int durationMilliseconds)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "--tone", "440", deviceId, durationMilliseconds.ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the process-audio source.");
    }

    private static void CreateClip(string path)
    {
        using var writer = new WaveFileWriter(path, AudioEngine.Format);
        var source = new SignalGenerator(48000, 2)
        {
            Frequency = 660,
            Gain = .03,
            Type = SignalGeneratorType.Sin,
        };
        var samples = new float[4800];
        for (var block = 0; block < 100; block++)
        {
            source.Read(samples, 0, samples.Length);
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }

    private sealed class GapDetector
    {
        private readonly WaveFormat format;
        private readonly bool floatingPoint;
        private readonly long ignoredFrames;
        private long observedFrames;
        private long silentFrames;
        private long maximumSilentFrames;
        private long nonSilentFrames;

        public GapDetector(WaveFormat format, TimeSpan ignoreAtStart)
        {
            this.format = format;
            floatingPoint = format.Encoding == WaveFormatEncoding.IeeeFloat
                || format is WaveFormatExtensible extensible
                && extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
            ignoredFrames = (long)(ignoreAtStart.TotalSeconds * format.SampleRate);
        }

        public double MaximumGapMilliseconds =>
            Interlocked.Read(ref maximumSilentFrames) * 1000d / format.SampleRate;
        public long NonSilentFrames => Interlocked.Read(ref nonSilentFrames);

        public void Observe(byte[] buffer, int count)
        {
            var bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample is < 2 or > 4 || format.BlockAlign <= 0)
            {
                return;
            }

            var frames = count / format.BlockAlign;
            for (var frame = 0; frame < frames; frame++)
            {
                var frameOffset = frame * format.BlockAlign;
                var maximum = 0d;
                for (var channel = 0; channel < format.Channels; channel++)
                {
                    maximum = Math.Max(maximum, ReadMagnitude(buffer, frameOffset + channel * bytesPerSample, bytesPerSample));
                }

                if (Interlocked.Increment(ref observedFrames) <= ignoredFrames)
                {
                    continue;
                }

                if (maximum <= .00001)
                {
                    UpdateMaximum(Interlocked.Increment(ref silentFrames));
                }
                else
                {
                    Interlocked.Exchange(ref silentFrames, 0);
                    Interlocked.Increment(ref nonSilentFrames);
                }
            }
        }

        private void UpdateMaximum(long value)
        {
            var observed = Interlocked.Read(ref maximumSilentFrames);
            while (value > observed)
            {
                var prior = Interlocked.CompareExchange(ref maximumSilentFrames, value, observed);
                if (prior == observed)
                {
                    return;
                }

                observed = prior;
            }
        }

        private double ReadMagnitude(byte[] buffer, int offset, int bytesPerSample)
        {
            if (floatingPoint && bytesPerSample == 4)
            {
                var value = BitConverter.ToSingle(buffer, offset);
                return float.IsFinite(value) ? Math.Abs(value) : 0;
            }

            return bytesPerSample switch
            {
                2 => Math.Abs(BitConverter.ToInt16(buffer, offset) / 32768d),
                3 => Math.Abs(ReadInt24(buffer, offset) / 8388608d),
                4 => Math.Abs(BitConverter.ToInt32(buffer, offset) / 2147483648d),
                _ => 0,
            };
        }

        private static int ReadInt24(byte[] buffer, int offset)
        {
            var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
            return (value & 0x800000) == 0 ? value : value | unchecked((int)0xFF000000);
        }
    }
}
