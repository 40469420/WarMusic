using NAudio.Wave;
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
