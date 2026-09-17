using System.Windows.Input;

namespace WarMusic.Services;

public sealed partial class MainViewModel
{
    OverlayHudState overlayHud = OverlayHudState.Describe(false, false, false, null, false, false, null, false, 0);
    bool overlayInteractive;
    public event Action? OverlayShown;
    public event Action? OverlayInteractionChanged;
    public event Action? OverlayLayoutChanged;
    public event Action? OverlayPlacementReset;
    public ICommand UnlockOverlayCommand { get; private set; } = null!;
    public ICommand LockOverlayCommand { get; private set; } = null!;
    public ICommand OverlayInteractCommand { get; private set; } = null!;
    public ICommand ResetOverlayPositionCommand { get; private set; } = null!;
    public ICommand ToggleOverlayLayoutCommand { get; private set; } = null!;
    public bool OverlayInteractive => overlayInteractive;
    public bool OverlayLocked => !overlayInteractive;
    public string OverlayLockLabel => overlayInteractive ? "UNLOCKED" : "LOCKED";
    public string OverlayLayoutGlyph => OverlayExpanded ? "▴" : "▾";
    public string OverlayHeadline => overlayHud.Headline;
    public string OverlayKind => overlayHud.Kind;
    public string OverlayMusic => overlayHud.Music;
    public string OverlayCable => overlayHud.Cable;
    public string OverlayApplication => overlayHud.Application;
    public bool OverlayDucking => overlayHud.Ducking;
    public bool CanPlayNext => overlayHud.CanPlayNext;
    public string OverlayPlayLabel => Engine.IsPaused ? "Resume local" : "Pause local";
    public string OverlayHint => overlayInteractive
        ? $"Esc lock · {OverlayInteractKey} toggle · {OverlayToggleKey} hide"
        : "";
    public double OverlayWidth { get => settings.OverlayWidth; set => settings.OverlayWidth = OverlayPlacement.ClampWidth(value); }
    public double OverlayHeight { get => settings.OverlayHeight; set => settings.OverlayHeight = OverlayPlacement.ClampHeight(value); }
    public double OverlayOpacity
    {
        get => settings.OverlayOpacity;
        set { settings.OverlayOpacity = OverlayPlacement.ClampOpacity(value); Changed(); Changed(nameof(OverlayOpacityPercent)); }
    }
    public double OverlayOpacityPercent { get => OverlayOpacity * 100; set => OverlayOpacity = value / 100; }
    public bool OverlayExpanded
    {
        get => settings.OverlayExpanded;
        set
        {
            if (settings.OverlayExpanded == value)
            {
                return;
            }

            settings.OverlayExpanded = value;
            Changed();
            Changed(nameof(OverlayLayoutGlyph));
            OverlayLayoutChanged?.Invoke();
        }
    }
    public string OverlayToggleKey { get => settings.OverlayToggleKey; set { settings.OverlayToggleKey = value ?? ""; Changed(); Changed(nameof(OverlayHint)); } }
    public string OverlayInteractKey { get => settings.OverlayInteractKey; set { settings.OverlayInteractKey = value ?? ""; Changed(); Changed(nameof(OverlayHint)); } }

    void InitializeOverlay()
    {
        UnlockOverlayCommand = Cmd(() => { SetOverlayInteractive(true); OverlayShown?.Invoke(); });
        LockOverlayCommand = Cmd(() => { LockOverlay(); OverlayShown?.Invoke(); });
        OverlayInteractCommand = Cmd(() => { SetOverlayInteractive(!overlayInteractive); OverlayShown?.Invoke(); });
        ResetOverlayPositionCommand = Cmd(ResetOverlayPlacement);
        ToggleOverlayLayoutCommand = Cmd(() => OverlayExpanded = !OverlayExpanded);
        Queue.CollectionChanged += (_, _) => UpdateOverlayHud();
    }

    public void LockOverlay() => SetOverlayInteractive(false);

    void SetOverlayInteractive(bool value)
    {
        if (overlayInteractive == value)
        {
            return;
        }

        overlayInteractive = value;
        Changed(nameof(OverlayInteractive));
        Changed(nameof(OverlayLocked));
        Changed(nameof(OverlayLockLabel));
        Changed(nameof(OverlayHint));
        OverlayInteractionChanged?.Invoke();
    }

    void ResetOverlayPlacement()
    {
        settings.OverlayLeft = OverlayPlacement.DefaultLeft;
        settings.OverlayTop = OverlayPlacement.DefaultTop;
        settings.OverlayWidth = OverlayPlacement.DefaultWidth;
        settings.OverlayHeight = OverlayPlacement.DefaultExpandedHeight;
        OverlayPlacementReset?.Invoke();
        Save();
        Notice = "Overlay moved back onto this display.";
    }

    void UpdateOverlayHud()
    {
        var next = OverlayHudState.Describe(
            Engine.Routing,
            Engine.Transmitting,
            Engine.SourcePid != 0,
            Profile.SourceName,
            Engine.HasLocalTrack,
            Engine.IsPaused,
            Engine.PlayingName,
            Engine.DuckGain < .95f,
            Queue.Count);
        if (next == overlayHud)
        {
            return;
        }

        overlayHud = next;
        Changed(nameof(OverlayHeadline));
        Changed(nameof(OverlayKind));
        Changed(nameof(OverlayMusic));
        Changed(nameof(OverlayCable));
        Changed(nameof(OverlayApplication));
        Changed(nameof(OverlayDucking));
        Changed(nameof(CanPlayNext));
        Changed(nameof(OverlayPlayLabel));
    }
}
