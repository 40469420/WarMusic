using NAudio.Wave;

namespace WarMusic.Audio;

/// <summary>Keeps capture latency bounded without ever clearing the entire input queue.</summary>
internal sealed class CaptureQueue
{
    private readonly BufferedWaveProvider buffer;
    private readonly byte[] discard;
    private readonly int targetBytes;
    private readonly int highWaterBytes;
    private long corrections;
    private long underruns;
    private long overruns;
    private long maximumBufferedBytes;

    public CaptureQueue(WaveFormat format)
    {
        buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(250))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        targetBytes = Align(format.AverageBytesPerSecond * 60 / 1000, format.BlockAlign);
        highWaterBytes = Align(format.AverageBytesPerSecond * 90 / 1000, format.BlockAlign);
        discard = new byte[Math.Max(format.BlockAlign, Align(format.AverageBytesPerSecond / 1000, format.BlockAlign))];
        Samples = new ObservedSampleProvider(this, buffer.ToSampleProvider());
    }

    public ISampleProvider Samples { get; }
    public long Corrections => Interlocked.Read(ref corrections);
    public long Underruns => Interlocked.Read(ref underruns);
    public long Overruns => Interlocked.Read(ref overruns);
    public double MaximumBufferedMilliseconds =>
        Interlocked.Read(ref maximumBufferedBytes) * 1000d / buffer.WaveFormat.AverageBytesPerSecond;

    public void Write(byte[] bytes, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        count = Math.Min(count, bytes.Length);
        var offset = 0;
        if (count > buffer.BufferLength)
        {
            offset = count - buffer.BufferLength;
            offset -= offset % buffer.WaveFormat.BlockAlign;
            count -= offset;
            Interlocked.Increment(ref overruns);
        }

        TrimGradually();
        var needed = count - (buffer.BufferLength - buffer.BufferedBytes);
        while (needed > 0 && buffer.BufferedBytes > 0)
        {
            var drop = Math.Min(discard.Length, needed);
            drop = Align(drop, buffer.WaveFormat.BlockAlign);
            buffer.Read(discard, 0, drop);
            needed -= drop;
            Interlocked.Increment(ref corrections);
            Interlocked.Increment(ref overruns);
        }

        buffer.AddSamples(bytes, offset, count);
        UpdateMaximum(buffer.BufferedBytes);
    }

    private void TrimGradually()
    {
        if (buffer.BufferedBytes <= highWaterBytes)
        {
            return;
        }

        var excess = buffer.BufferedBytes - targetBytes;
        var drop = Math.Min(discard.Length, excess);
        drop = Align(drop, buffer.WaveFormat.BlockAlign);
        if (drop <= 0)
        {
            return;
        }

        buffer.Read(discard, 0, drop);
        Interlocked.Increment(ref corrections);
    }

    private void UpdateMaximum(int value)
    {
        var observed = Interlocked.Read(ref maximumBufferedBytes);
        while (value > observed)
        {
            var prior = Interlocked.CompareExchange(ref maximumBufferedBytes, value, observed);
            if (prior == observed)
            {
                return;
            }

            observed = prior;
        }
    }

    private static int Align(int bytes, int blockAlign) => Math.Max(blockAlign, bytes - bytes % blockAlign);

    private sealed class ObservedSampleProvider(CaptureQueue owner, ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] destination, int offset, int count)
        {
            var frames = count / Math.Max(1, WaveFormat.Channels);
            var requiredBytes = frames * owner.buffer.WaveFormat.BlockAlign;
            if (requiredBytes > 0 && owner.buffer.BufferedBytes < requiredBytes)
            {
                Interlocked.Increment(ref owner.underruns);
            }

            return source.Read(destination, offset, count);
        }
    }
}
