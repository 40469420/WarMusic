using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WarMusic.Audio;

namespace WarMusic.UnitTests;

public sealed class AudioBufferTests
{
    [Fact]
    public void MonitorBufferUsesIncrementalDriftCorrectionAtHighWater()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var buffer = new AdaptiveMonitorBuffer(
            format,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(50));
        var block = TestWorkspace.Filled(.25f);
        buffer.Write(block, block.Length);
        buffer.Write(block, block.Length);
        buffer.Write(block, block.Length);
        buffer.Write(block, block.Length);

        var output = new float[960];
        Assert.Equal(output.Length, buffer.Read(output, 0, output.Length));
        Assert.True(buffer.Corrections > 0);
        Assert.Equal(0, buffer.Overruns);
    }

    [Fact]
    public void MonitorBufferDoesNotCorrectWhenResidualDepthIsOnTarget()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var buffer = new AdaptiveMonitorBuffer(
            format,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(50));
        var block = TestWorkspace.Filled(.25f);
        buffer.Write(block, block.Length);

        var output = new float[block.Length];
        Assert.Equal(output.Length, buffer.Read(output, 0, output.Length));
        Assert.Equal(0, buffer.Corrections);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public void MonitorBufferDuplicatesOneFrameWhenResidualDepthIsLow()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var buffer = new AdaptiveMonitorBuffer(
            format,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(50));
        var output = new float[960];

        buffer.Read(output, 0, output.Length);
        buffer.Read(output, 0, output.Length);
        buffer.Read(output, 0, output.Length);

        Assert.Equal(1, buffer.Corrections);
        Assert.Equal(0, buffer.Underruns);
    }

    [Fact]
    public void MonitorBufferFillsNAudioWaveBufferWithoutTypeMismatch()
    {
        var buffer = CreatePrimedMonitorBuffer(out var block);

        var bytes = new byte[block.Length * sizeof(float)];
        var waveBuffer = new WaveBuffer(bytes);
        Assert.IsType<byte[]>(waveBuffer.FloatBuffer);
        Assert.Equal(block.Length, buffer.Read(waveBuffer.FloatBuffer, 0, block.Length));
        AssertFloatBlock(bytes, .25f);
    }

    [Fact]
    public void MonitorBufferWorksWithNAudioSampleToWaveProvider()
    {
        var buffer = CreatePrimedMonitorBuffer(out var block);
        var provider = buffer.ToWaveProvider();
        var output = new byte[block.Length * sizeof(float)];
        Assert.Equal(output.Length, provider.Read(output, 0, output.Length));
        AssertFloatBlock(output, .25f);
    }

    [Fact]
    public void MonitorBufferWorksWithNAudioWaveBufferBackedSampleToWaveProvider()
    {
        var buffer = CreatePrimedMonitorBuffer(out var block);
        ISampleProvider samples = buffer;
        var provider = new SampleToWaveProvider(samples);
        var output = new byte[block.Length * sizeof(float)];
        Assert.Equal(output.Length, provider.Read(output, 0, output.Length));
        AssertFloatBlock(output, .25f);
    }

    private static AdaptiveMonitorBuffer CreatePrimedMonitorBuffer(out float[] block)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var buffer = new AdaptiveMonitorBuffer(
            format,
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromMilliseconds(50));
        block = TestWorkspace.Filled(.25f);
        for (var i = 0; i < 8; i++)
        {
            buffer.Write(block, block.Length);
        }

        var discard = new float[block.Length];
        for (var i = 0; i < 6; i++)
        {
            buffer.Read(discard, 0, discard.Length);
        }

        return buffer;
    }

    private static void AssertFloatBlock(byte[] bytes, float expected)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        Assert.Contains(values, sample => sample >= expected - .0001f && sample <= expected + .0001f);
        Assert.All(values, sample => Assert.False(float.IsNaN(sample)));
    }

    [Fact]
    public void MonitorBufferReturnsSilenceAndCountsUnderrunWhenStarved()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var buffer = new AdaptiveMonitorBuffer(
            format,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1));
        var output = Enumerable.Repeat(1f, 960).ToArray();

        Assert.Equal(output.Length, buffer.Read(output, 0, output.Length));
        Assert.True(buffer.Underruns > 0);
        Assert.Contains(0, output);
    }

    [Fact]
    public void CaptureQueueCountsInputStarvation()
    {
        var queue = new CaptureQueue(new WaveFormat(48000, 16, 2));
        var output = new float[960];

        Assert.Equal(output.Length, queue.Samples.Read(output, 0, output.Length));
        Assert.Equal(1, queue.Underruns);

        queue.Write(new byte[output.Length * sizeof(short)], output.Length * sizeof(short));
        Assert.Equal(output.Length, queue.Samples.Read(output, 0, output.Length));
        Assert.Equal(1, queue.Underruns);
    }

    [Fact]
    public void PeakLimiterPreservesStereoBalanceWithoutExceedingCeiling()
    {
        var limiter = new PeakLimiter();

        Assert.True(limiter.Process(2, 1, out var left, out var right));
        Assert.InRange(left, .9599f, .9601f);
        Assert.InRange(right, .4799f, .4801f);

        limiter.Process(float.NaN, float.PositiveInfinity, out left, out right);
        Assert.Equal(0, left);
        Assert.Equal(0, right);
    }
}
