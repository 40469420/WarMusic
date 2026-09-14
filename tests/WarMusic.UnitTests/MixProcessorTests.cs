using WarMusic.Audio;

namespace WarMusic.UnitTests;

public sealed class MixProcessorTests
{
    private static readonly MixOptions DefaultOptions = new()
    {
        DuckEnabled = false,
        MicGain = 1,
        AppGain = 1,
        ClipGain = 1,
        SendGain = 1,
        MonitorGain = 1,
    };

    [Fact]
    public void BoostLiftsQuietMusicWithoutChangingVoice()
    {
        var mixer = new MixProcessor();
        var send = new float[960];
        var monitor = new float[960];

        mixer.Process(TestWorkspace.Filled(.1f), TestWorkspace.Filled(.005f), TestWorkspace.Filled(0),
            send, monitor, send.Length, DefaultOptions with { AppGain = 16 }, true, false, false, false);

        Assert.InRange(send[0], .1799f, .1801f);
        Assert.InRange(mixer.MusicRms, .0799f, .0801f);

        mixer.Process(TestWorkspace.Filled(.1f), TestWorkspace.Filled(.005f), TestWorkspace.Filled(0),
            send, monitor, send.Length, DefaultOptions with { AppGain = 16 }, false, false, false, false);

        Assert.Equal(0, mixer.MusicRms);
        Assert.Equal(0, mixer.MusicPeak);
        Assert.InRange(send[0], .0999f, .1001f);
    }

    [Fact]
    public void MusicMutePreservesMicrophone()
    {
        var send = new float[960];
        new MixProcessor().Process(TestWorkspace.Filled(.1f), TestWorkspace.Filled(.3f),
            TestWorkspace.Filled(.2f), send, new float[960], send.Length, DefaultOptions,
            false, false, false, true);

        Assert.All(send, sample => Assert.InRange(sample, .09999f, .10001f));
    }

    [Fact]
    public void PrivatePreviewCannotEnterGameMix()
    {
        var send = new float[960];
        var monitor = new float[960];
        new MixProcessor().Process(TestWorkspace.Filled(.1f), TestWorkspace.Filled(0),
            TestWorkspace.Filled(.4f), send, monitor, send.Length, DefaultOptions,
            true, true, false, true);

        Assert.All(send, sample => Assert.InRange(sample, .09999f, .10001f));
        Assert.All(monitor, sample => Assert.InRange(sample, .39999f, .40001f));
    }

    [Fact]
    public void ApplicationMonitorDefaultsOff()
    {
        var send = new float[960];
        var monitor = new float[960];
        new MixProcessor().Process(TestWorkspace.Filled(0), TestWorkspace.Filled(.3f),
            TestWorkspace.Filled(0), send, monitor, send.Length, DefaultOptions,
            true, false, false, false);

        Assert.All(monitor, sample => Assert.Equal(0, sample));
        Assert.True(send[0] > .29f);
    }

    [Fact]
    public void PeakLimiterBoundsSummedSources()
    {
        var mixer = new MixProcessor();
        var send = new float[960];
        mixer.Process(TestWorkspace.Filled(2), TestWorkspace.Filled(2), TestWorkspace.Filled(2),
            send, new float[960], send.Length, DefaultOptions, true, false, false, true);

        Assert.All(send, sample => Assert.InRange(sample, -.96f, .96f));
        Assert.True(mixer.Limited);
    }

    [Fact]
    public void PanicBlocksHeldKeyUntilReleased()
    {
        var gate = new TransmissionGate();
        gate.Hold(true);
        Assert.True(gate.Enabled);
        gate.Panic();
        gate.Hold(true);
        Assert.False(gate.Enabled);
        gate.Hold(false);
        gate.Hold(true);
        Assert.True(gate.Enabled);
    }

    [Fact]
    public void PanicClearsToggleLatch()
    {
        var gate = new TransmissionGate();
        gate.Toggle();
        gate.Panic();
        Assert.False(gate.Enabled);
        gate.Toggle();
        Assert.True(gate.Enabled);
    }

    [Fact]
    public void VoiceDuckingUsesAttackHoldAndRelease()
    {
        var envelope = new DuckEnvelope();
        var options = new MixOptions();
        for (var i = 0; i < 20; i++) envelope.Step(.2f, false, options, 10);
        Assert.True(envelope.Gain < .2f);
        for (var i = 0; i < 20; i++) envelope.Step(0, false, options, 10);
        Assert.True(envelope.Gain < .2f);
        for (var i = 0; i < 400; i++) envelope.Step(0, false, options, 10);
        Assert.True(envelope.Gain > .99f);
    }

    [Fact]
    public void KeyOnlyModeIgnoresVoice()
    {
        var envelope = new DuckEnvelope();
        var options = new MixOptions { DuckMode = 1 };
        for (var i = 0; i < 30; i++) envelope.Step(.5f, false, options, 10);
        Assert.True(envelope.Gain > .999f);
        for (var i = 0; i < 30; i++) envelope.Step(0, true, options, 10);
        Assert.True(envelope.Gain < .2f);
    }

    [Fact]
    public void ThresholdHysteresisAvoidsChatter()
    {
        var envelope = new DuckEnvelope();
        var options = new MixOptions { HoldMs = 0, AttackMs = 1 };
        var threshold = MathF.Pow(10, options.ThresholdDb / 20);
        envelope.Step(threshold * 1.1f, false, options, 10);
        for (var i = 0; i < 20; i++) envelope.Step(threshold * .85f, false, options, 10);
        Assert.True(envelope.Gain < .2f);
    }

    [Fact]
    public void DuckingKeepsStereoGainsEqual()
    {
        var send = new float[960];
        new MixProcessor().Process(TestWorkspace.Filled(.1f), TestWorkspace.Filled(.3f),
            TestWorkspace.Filled(0), send, new float[960], send.Length, new MixOptions(),
            true, false, false, false);

        for (var i = 0; i < send.Length; i += 2)
        {
            Assert.Equal(send[i], send[i + 1]);
        }
    }

    [Fact]
    public void NormalizationDoesNotAmplifySilenceOrExceedSixDecibels()
    {
        Assert.Equal(1, MixProcessor.NormalizationGain(0, 0));
        Assert.True(MixProcessor.NormalizationGain(.0001, .02f) <= 2);
        Assert.True(MixProcessor.NormalizationGain(.25, 1) <= .95f);
    }

    [Fact]
    public void LiveMicrophoneMonitorIsIndependentAndOptIn()
    {
        var mixer = new MixProcessor();
        var send = new float[960];
        var monitor = new float[960];
        var mic = TestWorkspace.Filled(.2f);

        mixer.Process(mic, TestWorkspace.Filled(0), TestWorkspace.Filled(0), send, monitor,
            send.Length, DefaultOptions, false, false, false, false);
        Assert.All(monitor, sample => Assert.Equal(0, sample));

        mixer.Process(mic, TestWorkspace.Filled(0), TestWorkspace.Filled(0), send, monitor,
            send.Length, DefaultOptions with
            {
                MonitorMicrophone = true,
                MicrophoneMonitorGain = .25f,
                MonitorGain = 0,
            }, false, false, false, false);

        Assert.All(monitor, sample => Assert.InRange(sample, .04999f, .05001f));
        Assert.All(send, sample => Assert.InRange(sample, .19999f, .20001f));
    }

    [Fact]
    public void SteadyStateMixingDoesNotAllocate()
    {
        var mixer = new MixProcessor();
        var mic = TestWorkspace.Filled(.1f);
        var app = TestWorkspace.Filled(.05f);
        var clip = TestWorkspace.Filled(0);
        var send = new float[960];
        var monitor = new float[960];
        for (var i = 0; i < 10; i++)
        {
            mixer.Process(mic, app, clip, send, monitor, send.Length,
                DefaultOptions, true, false, false, false);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            mixer.Process(mic, app, clip, send, monitor, send.Length,
                DefaultOptions, true, false, false, false);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
