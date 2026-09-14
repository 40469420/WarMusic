namespace WarMusic.Audio;

public readonly record struct MixOptions
{
    public MixOptions()
    {
    }

    public float MicGain { get; init; } = 1;
    public float AppGain { get; init; } = .5f;
    public float ClipGain { get; init; } = .65f;
    public float SendGain { get; init; } = .65f;
    public float MonitorGain { get; init; } = .65f;
    public bool DuckEnabled { get; init; } = true;
    public int DuckMode { get; init; }
    public float ThresholdDb { get; init; } = -35;
    public float ReductionDb { get; init; } = 15;
    public float AttackMs { get; init; } = 30;
    public float HoldMs { get; init; } = 350;
    public float ReleaseMs { get; init; } = 600;
    public bool DuckMonitor { get; init; }
    public bool ClipDucking { get; init; } = true;
    public bool MonitorApplication { get; init; }
    public bool MonitorMicrophone { get; init; }
    public float MicrophoneMonitorGain { get; init; } = .5f;
}

public sealed class DuckEnvelope
{
    private bool voice;
    private float hold;

    public float Gain { get; private set; } = 1;

    public float Step(float level, bool key, MixOptions options, float milliseconds)
    {
        var threshold = MathF.Pow(10, options.ThresholdDb / 20);
        voice = level > threshold || (voice && level > threshold * .70795f);
        var active = options.DuckEnabled
            && (options.DuckMode == 0 ? voice : options.DuckMode == 1 ? key : voice || key);
        if (active)
        {
            hold = options.HoldMs;
        }
        else
        {
            hold = Math.Max(0, hold - milliseconds);
        }

        var target = options.DuckEnabled && (active || hold > 0)
            ? MathF.Pow(10, -options.ReductionDb / 20)
            : 1;
        var time = target < Gain ? options.AttackMs : options.ReleaseMs;
        Gain += (target - Gain) * (1 - MathF.Exp(-milliseconds / Math.Max(1, time)));
        return Gain;
    }

    public void Reset()
    {
        voice = false;
        hold = 0;
        Gain = 1;
    }
}

public sealed class TransmissionGate
{
    private bool blockedUntilRelease;

    public bool Enabled { get; private set; }

    public void Toggle() => Enabled = !Enabled;

    public void Set(bool value) => Enabled = value;

    public void Panic()
    {
        Enabled = false;
        blockedUntilRelease = true;
    }

    public void Hold(bool pressed)
    {
        if (!pressed)
        {
            blockedUntilRelease = false;
            Enabled = false;
        }
        else if (!blockedUntilRelease)
        {
            Enabled = true;
        }
    }
}

public sealed class MixProcessor
{
    private readonly PeakLimiter sendLimiter = new();
    private readonly PeakLimiter monitorLimiter = new();
    private float clipEnvelope = 1;

    public DuckEnvelope Duck { get; } = new();
    public float MicPeak { get; private set; }
    public float AppPeak { get; private set; }
    public float MusicPeak { get; private set; }
    public float MusicRms { get; private set; }
    public float AppRms { get; private set; }
    public float OutputPeak { get; private set; }
    public bool Limited { get; private set; }

    public void Process(
        float[] mic,
        float[] app,
        float[] clip,
        float[] send,
        float[] monitor,
        int count,
        MixOptions options,
        bool transmit,
        bool preview,
        bool comms,
        bool clipActive)
    {
        count -= count % 2;
        MicPeak = 0;
        AppPeak = 0;
        MusicPeak = 0;
        OutputPeak = 0;
        Limited = false;
        double musicSquares = 0;
        double appSquares = 0;

        for (var i = 0; i < count; i++)
        {
            var micSample = Finite(mic[i]);
            var appSample = Finite(app[i]);
            appSquares += (double)appSample * appSample;
            MicPeak = Math.Max(MicPeak, Math.Abs(micSample * options.MicGain));
            AppPeak = Math.Max(AppPeak, Math.Abs(appSample));
        }

        // Dynamics are calculated once per stereo frame to preserve stereo balance.
        for (var i = 0; i < count; i += 2)
        {
            var micLeft = Finite(mic[i]);
            var micRight = Finite(mic[i + 1]);
            var appLeft = Finite(app[i]);
            var appRight = Finite(app[i + 1]);
            var clipLeft = Finite(clip[i]);
            var clipRight = Finite(clip[i + 1]);

            var voiceLevel = Math.Max(Math.Abs(micLeft), Math.Abs(micRight)) * options.MicGain;
            var duck = Duck.Step(voiceLevel, comms, options, 1000f / 48000);
            var clipTarget = options.ClipDucking && clipActive && !preview ? .35f : 1;
            clipEnvelope += (clipTarget - clipEnvelope)
                * (1 - MathF.Exp(-1f / (48000 * (clipTarget < clipEnvelope ? .03f : .3f))));
            var appDuck = Math.Min(duck, clipEnvelope);

            var musicLeft = appLeft * options.AppGain * appDuck
                + (preview ? 0 : clipLeft * options.ClipGain * duck);
            var musicRight = appRight * options.AppGain * appDuck
                + (preview ? 0 : clipRight * options.ClipGain * duck);
            var sentLeft = transmit ? musicLeft * options.SendGain : 0;
            var sentRight = transmit ? musicRight * options.SendGain : 0;
            MusicPeak = Math.Max(MusicPeak, Math.Max(Math.Abs(sentLeft), Math.Abs(sentRight)));
            musicSquares += (double)sentLeft * sentLeft + (double)sentRight * sentRight;

            var limited = sendLimiter.Process(
                micLeft * options.MicGain + sentLeft,
                micRight * options.MicGain + sentRight,
                out send[i],
                out send[i + 1]);
            Limited |= limited;
            OutputPeak = Math.Max(OutputPeak, Math.Max(Math.Abs(send[i]), Math.Abs(send[i + 1])));

            var listenLeft = (options.MonitorApplication
                    ? appLeft * options.AppGain * (options.DuckMonitor ? appDuck : 1)
                    : 0)
                + clipLeft * options.ClipGain * (options.DuckMonitor && !preview ? duck : 1);
            var listenRight = (options.MonitorApplication
                    ? appRight * options.AppGain * (options.DuckMonitor ? appDuck : 1)
                    : 0)
                + clipRight * options.ClipGain * (options.DuckMonitor && !preview ? duck : 1);
            var monitoredMicLeft = options.MonitorMicrophone
                ? micLeft * options.MicGain * options.MicrophoneMonitorGain
                : 0;
            var monitoredMicRight = options.MonitorMicrophone
                ? micRight * options.MicGain * options.MicrophoneMonitorGain
                : 0;
            monitorLimiter.Process(
                listenLeft * options.MonitorGain + monitoredMicLeft,
                listenRight * options.MonitorGain + monitoredMicRight,
                out monitor[i],
                out monitor[i + 1]);
        }

        AppRms = (float)Math.Sqrt(appSquares / Math.Max(1, count));
        MusicRms = (float)Math.Sqrt(musicSquares / Math.Max(1, count));
    }

    public void ResetDynamics()
    {
        Duck.Reset();
        clipEnvelope = 1;
        sendLimiter.Reset();
        monitorLimiter.Reset();
    }

    public static float NormalizationGain(double meanSquare, float peak)
    {
        if (meanSquare < 1e-8 || peak < .0001f)
        {
            return 1;
        }

        // RMS matching to -20 dBFS, capped at +6 dB; peak protection is a separate stage.
        return (float)Math.Min(Math.Min(.1 / Math.Sqrt(meanSquare), 2), .95 / Math.Max(peak, .0001));
    }

    private static float Finite(float sample) => float.IsFinite(sample) ? sample : 0;
}

internal sealed class PeakLimiter
{
    public const float Threshold = .96f;
    private const float ReleaseSeconds = .05f;
    private static readonly float ReleaseCoefficient = 1 - MathF.Exp(-1 / (48000 * ReleaseSeconds));
    private float gain = 1;

    public bool Process(float left, float right, out float limitedLeft, out float limitedRight)
    {
        left = float.IsFinite(left) ? left : 0;
        right = float.IsFinite(right) ? right : 0;
        var peak = Math.Max(Math.Abs(left), Math.Abs(right));
        var requiredGain = peak > Threshold ? Threshold / peak : 1;
        gain = requiredGain < gain ? requiredGain : gain + (1 - gain) * ReleaseCoefficient;
        limitedLeft = Math.Clamp(left * gain, -Threshold, Threshold);
        limitedRight = Math.Clamp(right * gain, -Threshold, Threshold);
        return gain < .9999f;
    }

    public void Reset() => gain = 1;
}
