namespace WarMusic.Services;

internal sealed record RoutingStatus(string Source, string Music, string Output)
{
    internal static RoutingStatus Describe(bool routing, bool sourceConnected, bool sourceSignal,
        bool transmitting, bool musicSignal, bool outputSignal, bool headphonesOnly, bool holdMode)
    {
        var source = !sourceConnected ? "No application connected" : sourceSignal ? "Receiving application audio" : "Connected · waiting for playback";
        var music = !routing ? "Music is not routed" : !transmitting
            ? holdMode ? "Music muted · hold your transmit key" : "Music muted · enable it when ready"
            : musicSignal ? "Music signal in cable mix" : "Music enabled · no music signal";
        var output = !routing ? headphonesOnly ? "Headphones only · cable off" : "Cable disconnected"
            : outputSignal ? "Sending audio to cable" : "Cable connected · mix is silent";
        return new(source, music, output);
    }
}
