using NAudio.Wave;

namespace WarMusic.Audio;

/// <summary>
/// A bounded single-producer/single-consumer monitor queue. Small stereo-frame
/// corrections absorb independent device-clock drift without audible full-buffer resets.
/// </summary>
internal sealed class AdaptiveMonitorBuffer : ISampleProvider
{
    private readonly object sync = new();
    private readonly float[] samples;
    private readonly int targetSamples;
    private readonly int lowWaterSamples;
    private readonly int highWaterSamples;
    private int readPosition;
    private int writePosition;
    private int available;
    private long underruns;
    private long overruns;
    private long corrections;
    private int maximumBufferedSamples;
    private float[] readScratch = [];

    public AdaptiveMonitorBuffer(
        WaveFormat waveFormat,
        TimeSpan capacity,
        TimeSpan targetLatency)
    {
        if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new ArgumentException("The monitor queue requires floating-point audio.", nameof(waveFormat));
        }

        WaveFormat = waveFormat;
        var channels = waveFormat.Channels;
        var capacitySamples = Frames(capacity) * channels;
        samples = new float[Math.Max(channels * 2, capacitySamples)];
        targetSamples = Math.Min(samples.Length / 2, Frames(targetLatency) * channels);
        lowWaterSamples = Math.Max(channels, targetSamples - Frames(TimeSpan.FromMilliseconds(20)) * channels);
        highWaterSamples = Math.Min(samples.Length - channels, targetSamples + Frames(TimeSpan.FromMilliseconds(20)) * channels);

        // Initial silence gives the cable clock time to begin feeding the monitor clock.
        available = targetSamples;
        writePosition = targetSamples % samples.Length;
        maximumBufferedSamples = available;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// Converts samples to IEEE-float bytes without NAudio's WaveBuffer union.
    /// WaveBuffer.FloatBuffer is a byte[] type-punned as float[]; Array.Copy rejects that.
    /// </summary>
    public IWaveProvider ToWaveProvider() => new IeeeFloatWaveProvider(this);

    public long Underruns => Interlocked.Read(ref underruns);
    public long Overruns => Interlocked.Read(ref overruns);
    public long Corrections => Interlocked.Read(ref corrections);
    public double MaximumBufferedMilliseconds =>
        Volatile.Read(ref maximumBufferedSamples) * 1000d / (WaveFormat.SampleRate * WaveFormat.Channels);

    public void Write(float[] source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        count = Math.Min(count, source.Length);
        count -= count % WaveFormat.Channels;
        var offset = 0;
        if (count > samples.Length)
        {
            offset = count - samples.Length;
            offset -= offset % WaveFormat.Channels;
            count -= offset;
        }

        lock (sync)
        {
            var overflow = available + count - samples.Length;
            if (overflow > 0)
            {
                overflow += WaveFormat.Channels - 1;
                overflow -= overflow % WaveFormat.Channels;
                Skip(Math.Min(overflow, available));
                Interlocked.Increment(ref overruns);
            }

            CopyIntoRing(source, offset, count);
            available += count;
            if (available > maximumBufferedSamples)
            {
                Volatile.Write(ref maximumBufferedSamples, available);
            }
        }
    }

    public int Read(float[] destination, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        count -= count % WaveFormat.Channels;
        if (count <= 0)
        {
            return 0;
        }

        lock (sync)
        {
            if (readScratch.Length < count)
            {
                readScratch = new float[count];
            }

            Array.Clear(readScratch, 0, count);

            // Compare the residual depth after this callback, not the depth before
            // consuming its block. Equal device clocks should need no correction.
            var projected = available - count;
            if (projected > highWaterSamples && available >= count + WaveFormat.Channels)
            {
                Skip(WaveFormat.Channels);
                Interlocked.Increment(ref corrections);
            }

            var duplicateFrame = projected < lowWaterSamples
                && available >= count - WaveFormat.Channels
                && count > WaveFormat.Channels;
            var wanted = duplicateFrame ? count - WaveFormat.Channels : count;
            var copied = Math.Min(wanted, available);
            copied -= copied % WaveFormat.Channels;
            CopyFromRing(readScratch, 0, copied);
            available -= copied;

            if (duplicateFrame && copied >= WaveFormat.Channels)
            {
                Array.Copy(readScratch, copied - WaveFormat.Channels,
                    readScratch, copied, WaveFormat.Channels);
                copied += WaveFormat.Channels;
                Interlocked.Increment(ref corrections);
            }

            if (copied < count)
            {
                Interlocked.Increment(ref underruns);
            }

            // NAudio SampleToWaveProvider passes WaveBuffer.FloatBuffer, a byte[]
            // overlapping a float[] view. Array.Copy rejects that pair.
            Buffer.BlockCopy(readScratch, 0, destination, offset * sizeof(float), count * sizeof(float));
        }

        return count;
    }

    private int Frames(TimeSpan duration) => (int)Math.Round(duration.TotalSeconds * WaveFormat.SampleRate);

    private void Skip(int count)
    {
        readPosition = (readPosition + count) % samples.Length;
        available -= count;
    }

    private void CopyIntoRing(float[] source, int offset, int count)
    {
        var first = Math.Min(count, samples.Length - writePosition);
        Array.Copy(source, offset, samples, writePosition, first);
        var second = count - first;
        if (second > 0)
        {
            Array.Copy(source, offset + first, samples, 0, second);
        }

        writePosition = (writePosition + count) % samples.Length;
    }

    private void CopyFromRing(float[] destination, int offset, int count)
    {
        var first = Math.Min(count, samples.Length - readPosition);
        Array.Copy(samples, readPosition, destination, offset, first);
        var second = count - first;
        if (second > 0)
        {
            Array.Copy(samples, 0, destination, offset + first, second);
        }

        readPosition = (readPosition + count) % samples.Length;
    }

    private sealed class IeeeFloatWaveProvider(ISampleProvider source) : IWaveProvider
    {
        private float[] samples = new float[8192];

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var sampleCount = count / sizeof(float);
            sampleCount -= sampleCount % Math.Max(1, WaveFormat.Channels);
            if (sampleCount <= 0)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            if (samples.Length < sampleCount)
            {
                samples = new float[sampleCount];
            }

            var read = source.Read(samples, 0, sampleCount);
            var bytes = read * sizeof(float);
            Buffer.BlockCopy(samples, 0, buffer, offset, bytes);
            if (bytes < count)
            {
                Array.Clear(buffer, offset + bytes, count - bytes);
            }

            return count;
        }
    }
}
