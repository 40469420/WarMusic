using NAudio.Wave.SampleProviders;
using WarMusic.Audio;
using WarMusic.Services;

namespace WarMusic.UnitTests;

public sealed class UtilityTests
{
    [Fact]
    public void FadesReverseSmoothlyAndPanicResetsState()
    {
        var fade = new MusicFade();
        fade.Begin(true, 2, false);
        fade.Step(.5f);
        Assert.InRange(fade.Gain, .2499f, .2501f);
        fade.Begin(false, 1, true);
        Assert.InRange(fade.Gain, .2499f, .2501f);
        Assert.True(fade.Step(1));
        Assert.Equal(0, fade.Gain);
        Assert.False(fade.Active);
        fade.Reset();
        Assert.Equal(1, fade.Gain);
        Assert.False(fade.Active);
    }

    [Fact]
    public void LevelAdviceBoostsQuietSourcesAndProtectsPeaks()
    {
        Assert.InRange(LevelAdvisor.Recommend(.01, .04f, 1), 9.999f, 10.001f);
        Assert.True(LevelAdvisor.Recommend(.01, .8f, 1) < 1);
        Assert.Throws<InvalidOperationException>(() => LevelAdvisor.Recommend(0, 0, 1));
    }

    [Fact]
    public void MonoResamplingProducesStereoFortyEightKilohertz()
    {
        var source = new SignalGenerator(22050, 1)
        {
            Frequency = 440,
            Gain = .1,
            Type = SignalGeneratorType.Sin,
        };
        var converted = Devices.Stereo48(source);
        var values = new float[960];

        Assert.Equal(960, converted.Read(values, 0, values.Length));
        Assert.Equal(48000, converted.WaveFormat.SampleRate);
        Assert.Equal(2, converted.WaveFormat.Channels);
        for (var i = 0; i < values.Length; i += 2)
        {
            Assert.InRange(Math.Abs(values[i] - values[i + 1]), 0, .00001f);
        }
    }

    [Fact]
    public void VirtualCableDetectionAcceptsCommonPlaybackNames()
    {
        Assert.True(Devices.HasVirtualCable([new("one", "CABLE Input (VB-Audio Virtual Cable)")]));
        Assert.True(Devices.HasVirtualCable([new("one", "Example Virtual Cable Playback")]));
        Assert.False(Devices.HasVirtualCable([new("one", "Speakers")]));
        Assert.False(Devices.IsVirtualCablePlayback("Headphones"));
    }
}
