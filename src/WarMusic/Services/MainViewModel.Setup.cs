using WarMusic.Models;

namespace WarMusic.Services;

public sealed partial class MainViewModel
{
    public bool ShouldShowSetup => !settings.SetupPromptShown;

    public void DismissSetup()
    {
        settings.SetupPromptShown = true;
        Save();
    }

    public void CompleteSetup(Device microphone, Device headphones, Device cable)
    {
        SelectedMic = microphone;
        SelectedMonitor = headphones;
        SelectedCable = cable;
        settings.SetupPromptShown = true;
        Save();
        Notice = "Devices saved. Press Connect, then enable music when ready. Select the cable's recording endpoint in your game.";
    }

    RoutingStatus routingStatus = RoutingStatus.Describe(false, false, false, false, false, false, false, false);
    int sourceSignalTicks, musicSignalTicks, outputSignalTicks;
    public string SourceRoutingStatus => routingStatus.Source;
    public string MusicRoutingStatus => routingStatus.Music;
    public string CableRoutingStatus => routingStatus.Output;
    public string CableDestination => SelectedCable?.Name ?? "Choose a cable in Audio or Quick setup";

    void UpdateRoutingStatus()
    {
        // Hold activity briefly so short gaps in speech/music do not flicker the labels.
        sourceSignalTicks = Engine.SourcePid == 0 || !Engine.Running ? 0 : Engine.AppPeak > .0001f ? 30 : Math.Max(0, sourceSignalTicks - 1);
        musicSignalTicks = !Engine.Routing || !Engine.Transmitting ? 0 : Engine.MusicPeak > .0001f ? 30 : Math.Max(0, musicSignalTicks - 1);
        outputSignalTicks = !Engine.Routing ? 0 : Engine.OutputPeak > .0001f ? 30 : Math.Max(0, outputSignalTicks - 1);
        var next = RoutingStatus.Describe(Engine.Routing, Engine.SourcePid != 0, sourceSignalTicks > 0,
            Engine.Transmitting, musicSignalTicks > 0, outputSignalTicks > 0, Engine.Running, Profile.TransmitMode == 1);
        if (next != routingStatus)
        {
            routingStatus = next;
            Changed(nameof(SourceRoutingStatus));
            Changed(nameof(MusicRoutingStatus));
            Changed(nameof(CableRoutingStatus));
        }
        Changed(nameof(CableDestination));
    }
}
