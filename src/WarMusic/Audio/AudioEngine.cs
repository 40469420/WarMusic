using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WarMusic.Models;
using WarMusic.Services;

namespace WarMusic.Audio;

public sealed class AudioEngine : IDisposable
{
    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private readonly ISampleProvider? suppliedMicrophone;
    private readonly object audioLock = new();
    private readonly object sourceLock = new();
    private readonly object gateLock = new();
    private readonly object probeLock = new();
    private readonly MusicFade fade = new();
    private readonly TransmissionGate gate = new();
    private readonly MixProcessor mixer = new();
    private readonly AudioDiagnostics diagnostics = new();
    private readonly List<MMDevice> devices = [];
    private MMDeviceEnumerator? enumerator;
    private WasapiCapture? microphone;
    private WasapiOut? output;
    private WasapiOut? headphones;
    private CaptureQueue? microphoneQueue;
    private CaptureQueue? applicationQueue;
    private AdaptiveMonitorBuffer? monitorQueue;
    private ISampleProvider? microphoneSamples;
    private ISampleProvider? applicationSamples;
    private ISampleProvider? localSamples;
    private AudioFileReader? local;
    private ProcessCapture? capture;
    private RenderWaveProvider? renderProvider;
    private int sourceGeneration;
    private int faulted;
    private volatile bool running;
    private MixOptions options = new();
    private volatile bool comms;
    private volatile bool liveMicrophone;
    private bool paused;
    private bool preview;
    private bool loop;
    private float localGain = 1;
    private float[]? recording;
    private int recorded;
    private bool recordActive;
    private MixProcessor testMixer = new();
    private bool probing;
    private double probeSquares;
    private long probeSamples;
    private float probePeak;

    public AudioEngine(ISampleProvider? microphoneSource = null)
    {
        suppliedMicrophone = microphoneSource;
    }

    public bool Fading
    {
        get
        {
            lock (gateLock)
            {
                return fade.Active;
            }
        }
    }

    public bool LiveMicrophone => liveMicrophone;
    public bool Routing { get; private set; }
    public bool Running => running;

    public bool Transmitting
    {
        get
        {
            lock (gateLock)
            {
                return gate.Enabled;
            }
        }
    }

    public bool IsPreview
    {
        get
        {
            lock (audioLock)
            {
                return preview && local is not null;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (audioLock)
            {
                return local is not null && !paused;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (audioLock)
            {
                return paused;
            }
        }
    }

    public bool HasLocalTrack
    {
        get
        {
            lock (audioLock)
            {
                return local is not null;
            }
        }
    }

    public bool Recording
    {
        get
        {
            lock (audioLock)
            {
                return recordActive;
            }
        }
    }

    public bool HasRecording
    {
        get
        {
            lock (audioLock)
            {
                return recording is not null && recorded > 0 && !recordActive;
            }
        }
    }

    public string PlayingName { get; private set; } = "Nothing playing";
    public int SourcePid { get; private set; }
    public float AppRms => mixer.AppRms;
    public float MusicPeak => mixer.MusicPeak;
    public float MusicRms => mixer.MusicRms;
    public float MicPeak => mixer.MicPeak;
    public float AppPeak => mixer.AppPeak;
    public float OutputPeak => mixer.OutputPeak;
    public float DuckGain => mixer.Duck.Gain;
    public bool Limited => mixer.Limited;
    public AudioHealthSnapshot Health => diagnostics.Snapshot(monitorQueue, microphoneQueue, applicationQueue);

    public TimeSpan Position
    {
        get
        {
            lock (audioLock)
            {
                return local?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (audioLock)
            {
                return local?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public event Action<string>? Error;
    public event Action? TrackEnded;

    public void Configure(MixOptions value)
    {
        lock (gateLock)
        {
            options = value;
        }
    }

    public void SetComms(bool value) => comms = value;

    public void FadeTo(bool on, float seconds)
    {
        lock (gateLock)
        {
            if (!Routing)
            {
                return;
            }

            fade.Begin(on, seconds, gate.Enabled);
            if (on)
            {
                gate.Set(true);
            }
        }
    }

    public void Toggle()
    {
        lock (gateLock)
        {
            if (Routing)
            {
                fade.Reset();
                gate.Toggle();
            }
        }
    }

    public void Hold(bool pressed)
    {
        lock (gateLock)
        {
            gate.Hold(Routing && pressed);
        }
    }

    public void Panic()
    {
        lock (gateLock)
        {
            gate.Panic();
            fade.Reset();
            mixer.ResetDynamics();
        }

        lock (audioLock)
        {
            testMixer.ResetDynamics();
        }
    }

    public void SetLoop(bool value)
    {
        lock (audioLock)
        {
            loop = value;
        }
    }

    public void SetLiveMicrophone(Profile profile, bool enabled)
    {
        if (enabled)
        {
            if (!Routing && !liveMicrophone)
            {
                Start(profile, route: false, microphoneTest: true);
            }

            liveMicrophone = true;
        }
        else
        {
            liveMicrophone = false;
            if (!Routing)
            {
                Stop();
            }
        }
    }

    public void Start(Profile profile, bool route, bool microphoneTest = false)
    {
        Stop();
        try
        {
            if (route && !Devices.HasVirtualCable(Devices.List(DataFlow.Render)))
            {
                throw new InvalidOperationException(
                    "VB-CABLE is unavailable. Install or enable it, then select CABLE Input before starting a route.");
            }

            Interlocked.Exchange(ref faulted, 0);
            enumerator = new MMDeviceEnumerator();
            if (profile.MonitorId == profile.CableId && !string.IsNullOrEmpty(profile.CableId))
            {
                throw new InvalidOperationException(
                    "Headphones and the virtual cable must be different outputs to prevent feedback.");
            }

            var monitor = GetDevice(profile.MonitorId);
            MMDevice? cable = null;
            if (route || microphoneTest)
            {
                cable = route ? GetDevice(profile.CableId) : null;
                if (route && !Devices.IsVirtualCablePlayback(cable!.FriendlyName))
                {
                    throw new InvalidOperationException(
                        "The game output must be a virtual-cable playback endpoint such as CABLE Input.");
                }

                ConfigureMicrophone(profile, cable, microphoneTest);
            }

            headphones = new WasapiOut(monitor, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
            headphones.PlaybackStopped += HeadphonesStopped;

            if (route)
            {
                monitorQueue = new AdaptiveMonitorBuffer(
                    Format,
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromMilliseconds(50));
                renderProvider = new RenderWaveProvider(this, returnMonitor: false, monitorQueue);
                output = new WasapiOut(cable!, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
                output.Init(renderProvider);
                output.PlaybackStopped += OutputStopped;
                headphones.Init(monitorQueue.ToWaveProvider());
            }
            else
            {
                renderProvider = new RenderWaveProvider(this, returnMonitor: true, monitorQueue: null);
                headphones.Init(renderProvider);
            }

            Routing = route;
            running = true;
            microphone?.StartRecording();
            output?.Play();
            headphones.Play();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public async Task ConnectSource(int pid)
    {
        if (!Routing)
        {
            throw new InvalidOperationException("Start the microphone route before connecting an application.");
        }

        if (Recording)
        {
            throw new InvalidOperationException("Finish the mix test before reconnecting application audio.");
        }

        if (pid == Environment.ProcessId)
        {
            throw new InvalidOperationException("WarMusic cannot capture itself.");
        }

        DisconnectSource();
        var generation = sourceGeneration;
        var next = new ProcessCapture();
        var queue = new CaptureQueue(ProcessCapture.Format);
        next.Data += queue.Write;
        next.Failed += exception =>
        {
            lock (sourceLock)
            {
                if (capture == next)
                {
                    applicationSamples = null;
                    applicationQueue = null;
                    SourcePid = 0;
                }
            }

            Panic();
            Error?.Invoke("Application capture unavailable: " + exception.Message);
        };

        try
        {
            await next.StartAsync(pid);
            bool accept;
            lock (sourceLock)
            {
                accept = running && Routing && sourceGeneration == generation;
                if (accept)
                {
                    capture = next;
                    applicationQueue = queue;
                    applicationSamples = queue.Samples;
                    SourcePid = pid;
                }
            }

            if (!accept)
            {
                next.Dispose();
                throw new InvalidOperationException(
                    "The route changed while connecting. Connect again when the new route is ready.");
            }
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    public void DisconnectSource()
    {
        ProcessCapture? previous;
        lock (sourceLock)
        {
            sourceGeneration++;
            previous = capture;
            capture = null;
            applicationSamples = null;
            applicationQueue = null;
            SourcePid = 0;
        }

        previous?.Dispose();
    }

    public void Play(Sound sound, bool privatePreview, bool normalize)
    {
        if (!Running)
        {
            throw new InvalidOperationException("Choose headphones and start preview or routing in Setup.");
        }

        if (!privatePreview && !Routing)
        {
            throw new InvalidOperationException("Start routing in Setup, or use Private preview.");
        }

        var path = Path.IsPathRooted(sound.Path) ? sound.Path : Path.Combine(Store.Root, sound.Path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("This sound file is missing. Import it again.", path);
        }

        var next = new AudioFileReader(path);
        ISampleProvider samples;
        try
        {
            samples = Devices.Stereo48(next);
        }
        catch
        {
            next.Dispose();
            throw;
        }

        lock (audioLock)
        {
            local?.Dispose();
            local = next;
            localSamples = samples;
            preview = privatePreview;
            paused = false;
            localGain = normalize ? sound.NormalizationGain : 1;
            PlayingName = sound.Name;
        }
    }

    public void Pause()
    {
        lock (audioLock)
        {
            if (local is not null)
            {
                paused = !paused;
            }
        }
    }

    public void StopLocal()
    {
        lock (audioLock)
        {
            local?.Dispose();
            local = null;
            localSamples = null;
            paused = false;
            preview = false;
            PlayingName = "Nothing playing";
        }
    }

    public void Seek(double seconds)
    {
        lock (audioLock)
        {
            if (local is null)
            {
                return;
            }

            local.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, local.TotalTime.TotalSeconds));
            localSamples = Devices.Stereo48(local);
        }
    }

    public void BeginTest()
    {
        if (!Routing)
        {
            throw new InvalidOperationException("Start routing before testing the microphone mix.");
        }

        lock (audioLock)
        {
            recording = new float[48000 * 2 * 10];
            recorded = 0;
            testMixer = new MixProcessor();
            recordActive = true;
        }
    }

    public void EndTest()
    {
        lock (audioLock)
        {
            recordActive = false;
        }
    }

    public void DeleteTest()
    {
        lock (audioLock)
        {
            recordActive = false;
            recording = null;
            recorded = 0;
        }
    }

    public void PreviewTest()
    {
        var path = Path.Combine(Store.Data, "mix-test.wav");
        StopLocal();
        lock (audioLock)
        {
            if (recordActive || recording is null || recorded == 0)
            {
                throw new InvalidOperationException("Record a short test first.");
            }

            using var writer = new WaveFileWriter(path, Format);
            writer.WriteSamples(recording, 0, recorded);
        }

        Play(new Sound { Path = path, Name = "Private mix test" }, privatePreview: true, normalize: false);
    }

    public void BeginLevelProbe()
    {
        lock (probeLock)
        {
            probeSquares = 0;
            probeSamples = 0;
            probePeak = 0;
            probing = true;
        }
    }

    public (double Rms, float Peak) EndLevelProbe()
    {
        lock (probeLock)
        {
            probing = false;
            return (Math.Sqrt(probeSquares / Math.Max(1, probeSamples)), probePeak);
        }
    }

    public void MarkReconnect() => diagnostics.RecordReconnect();

    public void Stop()
    {
        liveMicrophone = false;
        running = false;
        Routing = false;
        Panic();
        DisconnectSource();

        if (microphone is not null)
        {
            microphone.DataAvailable -= MicrophoneDataAvailable;
            microphone.RecordingStopped -= MicrophoneStopped;
            microphone.StopRecording();
            microphone.Dispose();
            microphone = null;
        }

        if (output is not null)
        {
            output.PlaybackStopped -= OutputStopped;
            output.Stop();
            output.Dispose();
            output = null;
        }

        if (headphones is not null)
        {
            headphones.PlaybackStopped -= HeadphonesStopped;
            headphones.Stop();
            headphones.Dispose();
            headphones = null;
        }

        microphoneSamples = null;
        microphoneQueue = null;
        applicationSamples = null;
        applicationQueue = null;
        monitorQueue = null;
        renderProvider = null;
        foreach (var device in devices)
        {
            device.Dispose();
        }

        devices.Clear();
        enumerator?.Dispose();
        enumerator = null;
        StopLocal();
        EndTest();
    }

    public void Dispose()
    {
        Stop();
        DeleteTest();
        var path = Path.Combine(Store.Data, "mix-test.wav");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private MMDevice GetDevice(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Choose the audio devices in Setup first.");
        }

        var device = enumerator!.GetDevice(id);
        if (device.State != DeviceState.Active)
        {
            device.Dispose();
            throw new InvalidOperationException("A saved device is disconnected. Refresh devices in Setup.");
        }

        devices.Add(device);
        return device;
    }

    private void ConfigureMicrophone(Profile profile, MMDevice? cable, bool microphoneTest)
    {
        if (suppliedMicrophone is not null)
        {
            microphoneSamples = Devices.Stereo48(suppliedMicrophone);
            return;
        }

        var microphoneDevice = GetDevice(profile.MicId);
        if (microphoneDevice.FriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)
            && (microphoneTest
                || cable?.FriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase) == true))
        {
            throw new InvalidOperationException(
                "Choose your physical microphone, not the cable's recording endpoint. Feeding the cable into itself creates feedback.");
        }

        microphone = new WasapiCapture(microphoneDevice);
        microphoneQueue = new CaptureQueue(microphone.WaveFormat);
        microphoneSamples = Devices.Stereo48(microphoneQueue.Samples);
        microphone.DataAvailable += MicrophoneDataAvailable;
        microphone.RecordingStopped += MicrophoneStopped;
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs eventArgs) =>
        microphoneQueue?.Write(eventArgs.Buffer, eventArgs.BytesRecorded);

    private void MicrophoneStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (running && eventArgs.Exception is not null)
        {
            Fault("Microphone was lost: " + eventArgs.Exception.Message);
        }
    }

    private void HeadphonesStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (running && eventArgs.Exception is not null)
        {
            Fault("Headphone output was lost: " + eventArgs.Exception.Message);
        }
    }

    private void OutputStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (running && eventArgs.Exception is not null)
        {
            Fault("Game input output was lost: " + eventArgs.Exception.Message);
        }
    }

    private void RenderBlock(
        float[] mic,
        float[] app,
        float[] clip,
        float[] send,
        float[] monitor,
        float[] silentApp,
        float[] testSend,
        float[] testMonitor,
        int count)
    {
        Array.Clear(mic, 0, count);
        Array.Clear(app, 0, count);
        Array.Clear(clip, 0, count);
        microphoneSamples?.Read(mic, 0, count);
        lock (sourceLock)
        {
            applicationSamples?.Read(app, 0, count);
        }

        bool privateMode;
        bool active;
        var ended = false;
        lock (audioLock)
        {
            privateMode = preview;
            active = local is not null && !paused;
            if (active && localSamples is not null)
            {
                ended = ReadLocalSamples(clip, count);
            }
        }

        MixOptions blockOptions;
        bool transmitted;
        lock (gateLock)
        {
            var blockSeconds = count / (float)(Format.SampleRate * Format.Channels);
            if (fade.Step(blockSeconds))
            {
                gate.Set(false);
            }

            blockOptions = options with
            {
                MonitorMicrophone = liveMicrophone,
                SendGain = options.SendGain * fade.Gain,
            };
            transmitted = gate.Enabled;
            mixer.Process(mic, app, clip, send, monitor, count, blockOptions,
                transmitted, privateMode, comms, active);
        }

        lock (probeLock)
        {
            if (probing)
            {
                probeSquares += (double)mixer.AppRms * mixer.AppRms * count;
                probeSamples += count;
                probePeak = Math.Max(probePeak, mixer.AppPeak);
            }
        }

        lock (audioLock)
        {
            if (recordActive && recording is not null)
            {
                testMixer.Process(mic, silentApp, clip, testSend, testMonitor, count, blockOptions,
                    transmitted, privateMode, comms, active);
                var copyCount = Math.Min(count, recording.Length - recorded);
                Array.Copy(testSend, 0, recording, recorded, copyCount);
                recorded += copyCount;
                if (recorded == recording.Length)
                {
                    recordActive = false;
                }
            }
        }

        if (ended && !privateMode)
        {
            TrackEnded?.Invoke();
        }
    }

    private bool ReadLocalSamples(float[] destination, int count)
    {
        var filled = 0;
        var restarted = false;
        while (filled < count && local is not null && localSamples is not null)
        {
            var read = localSamples.Read(destination, filled, count - filled);
            for (var i = filled; i < filled + read; i++)
            {
                destination[i] *= localGain;
            }

            filled += read;
            if (filled == count)
            {
                return false;
            }

            if (loop && local.TotalTime > TimeSpan.Zero && !restarted)
            {
                local.Position = 0;
                restarted = true;
                continue;
            }

            local.Dispose();
            local = null;
            localSamples = null;
            PlayingName = "Nothing playing";
            return true;
        }

        return false;
    }

    private void Fault(string text)
    {
        if (Interlocked.Exchange(ref faulted, 1) != 0)
        {
            return;
        }

        Panic();
        Routing = false;
        Store.Log(text + " | " + Health);
        Error?.Invoke(text);
    }

    private sealed class RenderWaveProvider : IWaveProvider
    {
        private const int InitialSampleCapacity = 8192;
        private readonly AudioEngine engine;
        private readonly bool returnMonitor;
        private readonly AdaptiveMonitorBuffer? monitorQueue;
        private float[] mic = new float[InitialSampleCapacity];
        private float[] app = new float[InitialSampleCapacity];
        private float[] clip = new float[InitialSampleCapacity];
        private float[] send = new float[InitialSampleCapacity];
        private float[] monitor = new float[InitialSampleCapacity];
        private float[] silentApp = new float[InitialSampleCapacity];
        private float[] testSend = new float[InitialSampleCapacity];
        private float[] testMonitor = new float[InitialSampleCapacity];

        public RenderWaveProvider(
            AudioEngine engine,
            bool returnMonitor,
            AdaptiveMonitorBuffer? monitorQueue)
        {
            this.engine = engine;
            this.returnMonitor = returnMonitor;
            this.monitorQueue = monitorQueue;
        }

        public WaveFormat WaveFormat => Format;

        public int Read(byte[] buffer, int offset, int count)
        {
            var started = Stopwatch.GetTimestamp();
            var sampleCount = count / sizeof(float);
            sampleCount -= sampleCount % Format.Channels;
            EnsureCapacity(sampleCount);

            try
            {
                if (engine.running)
                {
                    engine.RenderBlock(mic, app, clip, send, monitor, silentApp,
                        testSend, testMonitor, sampleCount);
                }
                else
                {
                    Array.Clear(send, 0, sampleCount);
                    Array.Clear(monitor, 0, sampleCount);
                }

                monitorQueue?.Write(monitor, sampleCount);
                var selected = returnMonitor ? monitor : send;
                var bytes = sampleCount * sizeof(float);
                Buffer.BlockCopy(selected, 0, buffer, offset, bytes);
                if (bytes < count)
                {
                    Array.Clear(buffer, offset + bytes, count - bytes);
                }
            }
            catch (Exception exception)
            {
                Array.Clear(buffer, offset, count);
                engine.Fault("Audio rendering stopped: " + exception.Message);
            }
            finally
            {
                engine.diagnostics.RecordRender(
                    sampleCount / Format.Channels,
                    Stopwatch.GetTimestamp() - started);
            }

            return count;
        }

        private void EnsureCapacity(int count)
        {
            if (mic.Length >= count)
            {
                return;
            }

            var capacity = Math.Max(1024, mic.Length);
            while (capacity < count)
            {
                capacity *= 2;
            }

            mic = new float[capacity];
            app = new float[capacity];
            clip = new float[capacity];
            send = new float[capacity];
            monitor = new float[capacity];
            silentApp = new float[capacity];
            testSend = new float[capacity];
            testMonitor = new float[capacity];
        }
    }
}
