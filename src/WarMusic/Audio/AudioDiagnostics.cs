using System.Diagnostics;

namespace WarMusic.Audio;

public readonly record struct AudioHealthSnapshot(
    long RenderCallbacks,
    long RenderedFrames,
    long MonitorUnderruns,
    long InputUnderruns,
    long MonitorOverruns,
    long DriftCorrections,
    long InputOverruns,
    double MaximumMonitorBufferMilliseconds,
    double MaximumInputBufferMilliseconds,
    double MaximumRenderMilliseconds,
    long Reconnects);

internal sealed class AudioDiagnostics
{
    private long renderCallbacks;
    private long renderedFrames;
    private long maximumRenderTicks;
    private long reconnects;

    public void RecordRender(int frames, long elapsedTicks)
    {
        Interlocked.Increment(ref renderCallbacks);
        Interlocked.Add(ref renderedFrames, frames);
        var observed = Interlocked.Read(ref maximumRenderTicks);
        while (elapsedTicks > observed)
        {
            var prior = Interlocked.CompareExchange(ref maximumRenderTicks, elapsedTicks, observed);
            if (prior == observed)
            {
                break;
            }

            observed = prior;
        }
    }

    public void RecordReconnect() => Interlocked.Increment(ref reconnects);

    public AudioHealthSnapshot Snapshot(
        AdaptiveMonitorBuffer? monitor,
        CaptureQueue? microphone,
        CaptureQueue? application)
    {
        var inputCorrections = (microphone?.Corrections ?? 0) + (application?.Corrections ?? 0);
        var monitorCorrections = monitor?.Corrections ?? 0;
        return new AudioHealthSnapshot(
            Interlocked.Read(ref renderCallbacks),
            Interlocked.Read(ref renderedFrames),
            monitor?.Underruns ?? 0,
            (microphone?.Underruns ?? 0) + (application?.Underruns ?? 0),
            monitor?.Overruns ?? 0,
            inputCorrections + monitorCorrections,
            (microphone?.Overruns ?? 0) + (application?.Overruns ?? 0),
            monitor?.MaximumBufferedMilliseconds ?? 0,
            Math.Max(microphone?.MaximumBufferedMilliseconds ?? 0,
                application?.MaximumBufferedMilliseconds ?? 0),
            Interlocked.Read(ref maximumRenderTicks) * 1000d / Stopwatch.Frequency,
            Interlocked.Read(ref reconnects));
    }
}
