namespace WarMusic.Services;

internal sealed record OverlayHudState(
    string Headline,
    string Kind,
    string Music,
    string Cable,
    string Application,
    bool Ducking,
    bool LocalPlayback,
    bool CanPlayNext)
{
    internal static OverlayHudState Describe(
        bool routing,
        bool transmitting,
        bool sourceConnected,
        string? sourceName,
        bool hasLocalTrack,
        bool localPaused,
        string? localName,
        bool ducking,
        int queued)
    {
        string headline;
        string kind;
        if (hasLocalTrack)
        {
            headline = string.IsNullOrWhiteSpace(localName) ? "Local sound" : localName.Trim();
            kind = localPaused ? "Local sound · paused" : "Local sound playing";
        }
        else if (sourceConnected)
        {
            headline = string.IsNullOrWhiteSpace(sourceName) ? "Application audio" : sourceName.Trim();
            kind = "Application audio · capture only";
        }
        else
        {
            headline = "No local sound";
            kind = "Idle";
        }

        var music = !routing ? "Music not routed" : transmitting ? "Music enabled" : "Music muted";
        var cable = !routing ? "Cable disconnected" : "Cable connected";
        var application = !sourceConnected
            ? "No application connected"
            : string.IsNullOrWhiteSpace(sourceName) ? "Application connected" : sourceName.Trim();
        return new(headline, kind, music, cable, application, ducking, hasLocalTrack, queued > 0);
    }
}
