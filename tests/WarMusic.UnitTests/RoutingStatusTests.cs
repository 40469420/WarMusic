using WarMusic.Services;

namespace WarMusic.UnitTests;

public sealed class RoutingStatusTests
{
    [Fact]
    public void IncomingMusicDoesNotMeanItIsRouted()
    {
        var status = RoutingStatus.Describe(true, true, true, false, false, true, false, false);
        Assert.Equal("Receiving application audio", status.Source);
        Assert.Equal("Music muted · enable it when ready", status.Music);
        Assert.Equal("Sending audio to cable", status.Output);
    }

    [Fact]
    public void HeadphoneActivityDoesNotClaimCableOutput()
    {
        var status = RoutingStatus.Describe(false, true, true, true, true, true, true, false);
        Assert.Equal("Headphones only · cable off", status.Output);
        Assert.Equal("Music is not routed", status.Music);
    }

    [Fact]
    public void LocalSoundsCanBeSentWithoutAnApplication()
    {
        var status = RoutingStatus.Describe(true, false, false, true, true, true, false, false);
        Assert.Equal("No application connected", status.Source);
        Assert.Equal("Music signal in cable mix", status.Music);
    }

    [Fact]
    public void SilentRouteAndHoldModeExplainWhatIsMissing()
    {
        var status = RoutingStatus.Describe(true, true, false, false, false, false, false, true);
        Assert.Equal("Connected · waiting for playback", status.Source);
        Assert.Equal("Music muted · hold your transmit key", status.Music);
        Assert.Equal("Cable connected · mix is silent", status.Output);
    }

    [Fact]
    public void EnabledButZeroVolumeDoesNotClaimMusicSignal()
    {
        var status = RoutingStatus.Describe(true, true, true, true, false, true, false, false);
        Assert.Equal("Music enabled · no music signal", status.Music);
    }
}
