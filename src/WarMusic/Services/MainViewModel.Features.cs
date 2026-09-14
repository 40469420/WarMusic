using System.Windows.Input;
using Microsoft.Win32;
using WarMusic.Audio;
using WarMusic.Models;
namespace WarMusic.Services;

public sealed partial class MainViewModel
{
    bool wantRoute, wantSource, recovering, measuring; int levelGeneration;
    public event Action? OverlayRequested;
    public event Action? TrayPreferencesChanged;
    public ICommand ReadyCommand { get; private set; } = null!;
    public ICommand MeasureCommand { get; private set; } = null!;
    public ICommand ApplyLevelCommand { get; private set; } = null!;
    public ICommand FadeInCommand { get; private set; } = null!;
    public ICommand FadeOutCommand { get; private set; } = null!;
    public ICommand OverlayCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    public ICommand RestoreCommand { get; private set; } = null!;
    public ICommand TileCommand { get; private set; } = null!;
    public ICommand ColorCommand { get; private set; } = null!;
    public bool CloseToTray { get => settings.CloseToTray; set { settings.CloseToTray = value; Changed(); Save(); TrayPreferencesChanged?.Invoke(); } }
    public bool StartMinimized { get => settings.StartMinimized; set { settings.StartMinimized = value; Changed(); Save(); } }
    public bool AutoReconnect { get => settings.AutoReconnect; set { settings.AutoReconnect = value; Changed(); Save(); } }
    public bool StartWithWindows { get => StartupRegistration.Enabled; set { Safe(() => StartupRegistration.Set(value)); Changed(); } }
    public float FadeSeconds { get => settings.FadeSeconds; set { settings.FadeSeconds = float.IsFinite(value) ? Math.Clamp(value, .1f, 10) : 2; Changed(); Changed(nameof(FadeLabel)); } }
    public string FadeLabel => $"{FadeSeconds:0.#} seconds";
    public double OverlayLeft { get => settings.OverlayLeft; set => settings.OverlayLeft = value; }
    public double OverlayTop { get => settings.OverlayTop; set => settings.OverlayTop = value; }
    string levelStatus = "Play music, then measure for five seconds. Music stays muted during measurement.";
    public string LevelStatus { get => levelStatus; set => Set(ref levelStatus, value); }
    float? suggestedGain;
    public bool CanTransmitControl => !Measuring;
    public bool CanApplyLevel => suggestedGain.HasValue && !measuring;
    public bool Measuring { get => measuring; private set { measuring = value; Changed(); Changed(nameof(CanTransmitControl)); Changed(nameof(CanApplyLevel)); } }
    void StopRecovery() { wantRoute = wantSource = false; levelGeneration++; suggestedGain = null; Changed(nameof(CanApplyLevel)); }
    void InitializeFeatures()
    {
        if (!float.IsFinite(settings.FadeSeconds)) settings.FadeSeconds = 2; settings.FadeSeconds = Math.Clamp(settings.FadeSeconds, .1f, 10);
        ReadyCommand = new ActionCommand(async _ => await Ready());
        MeasureCommand = new ActionCommand(async _ => await Measure());
        ApplyLevelCommand = Cmd(() => { if (!CanApplyLevel) return; Profile.AppGain = suggestedGain!.Value; Save(); LevelStatus = $"Applied {Profile.AppGain:0.#}× boost. Enable music when ready."; suggestedGain = null; Changed(nameof(CanApplyLevel)); });
        FadeInCommand = Cmd(() => FadeMusic(true)); FadeOutCommand = Cmd(() => FadeMusic(false)); OverlayCommand = Cmd(() => OverlayRequested?.Invoke());
        TileCommand = new ActionCommand(p => Safe(() => { if (p is Sound sound) { SelectedSound = sound; Engine.Play(sound, false, Profile.Normalize); } }));
        ColorCommand = Cmd(() => { if (SelectedSound == null) return; string[] colors = ["#566737", "#365E70", "#75483C", "#62507B", "#79632E"]; SelectedSound.Color = colors[(Array.IndexOf(colors, SelectedSound.Color) + 1) % colors.Length]; Save(); });
        ExportCommand = new ActionCommand(async _ => await Export()); RestoreCommand = new ActionCommand(async _ => await Restore());
    }
    public async Task Ready()
    {
        if (recovering || connecting) return; recovering = true; wantRoute = wantSource = true;
        try { Save(); Engine.Start(Profile, true); Refresh(); if (SelectedApp != null) await Connect(); else SourceState = "Route ready · waiting for saved application"; Notice = "Ready. Voice is routed; enable music when you want to broadcast."; }
        catch (Exception ex) when (IsExpectedOperationFailure(ex)) { Notice = ex.Message; SourceState = "Waiting for saved devices · automatic recovery will retry"; }
        finally { recovering = false; }
    }
    internal async Task Recover()
    {
        if (disposed || !AutoReconnect || !wantRoute || recovering || connecting || Measuring) return; recovering = true;
        try
        {
            var outputs = Devices.List(NAudio.CoreAudioApi.DataFlow.Render); var inputs = Devices.List(NAudio.CoreAudioApi.DataFlow.Capture);
            bool present = outputs.Any(d => d.Id == Profile.CableId) && outputs.Any(d => d.Id == Profile.MonitorId) && inputs.Any(d => d.Id == Profile.MicId);
            if (!present) { if (Engine.Running) Engine.Stop(); SourceState = "Waiting for saved devices to reconnect"; return; }
            if (!Engine.Routing) { Engine.Start(Profile, true); Engine.MarkReconnect(); Notice = "Devices recovered. Music is muted; enable it when ready."; }
            if (!wantSource) return;
            var source = Devices.Applications().FirstOrDefault(a => a.Name.Equals(Profile.SourceName, StringComparison.OrdinalIgnoreCase));
            if (source == null) { if (Engine.SourcePid != 0) { Engine.Panic(); Engine.DisconnectSource(); } SourceState = "Waiting for saved application to reopen"; return; }
            source = Devices.ResolveApplication(source);
            if (Engine.SourcePid != source.Pid) { Engine.Panic(); await Engine.ConnectSource(source.Pid); Engine.MarkReconnect(); SourceState = "Application reconnected · music muted"; Notice = "Source recovered. Enable music when ready."; }
        }
        catch (Exception ex) when (IsExpectedOperationFailure(ex)) { SourceState = "Recovery pending · " + ex.Message; }
        catch (Exception ex) { EscalateUnexpected(ex); }
        finally { recovering = false; }
    }
    void FadeMusic(bool on) { if (Measuring) throw new InvalidOperationException("Finish measuring before enabling music."); if (!Engine.Routing) throw new InvalidOperationException("Use Ready first."); if (Profile.TransmitMode == 1) throw new InvalidOperationException("Fades use toggle mode. Switch transmission mode in Controls."); Engine.FadeTo(on, FadeSeconds); Notice = on ? "Fading music in…" : "Fading music out…"; }
    async Task Measure()
    {
        if (Measuring) return;
        if (!Engine.Routing || Engine.SourcePid == 0) { LevelStatus = "Use Ready and connect a playing application first."; return; }
        var target = Profile; int generation = ++levelGeneration, pid = Engine.SourcePid; Engine.Panic(); suggestedGain = null; Measuring = true;
        try
        {
            Engine.BeginLevelProbe(); int active = 0;
            for (int i = 0; i < 50; i++) { LevelStatus = $"Measuring music… {5 - i / 10} seconds"; await Task.Delay(100); if (disposed || generation != levelGeneration || Profile != target || !Engine.Routing || Engine.SourcePid != pid) throw new InvalidOperationException("Measurement cancelled because the route changed."); double rms = Engine.AppRms; if (rms > .0001) { active++; } }
            if (active < 10) throw new InvalidOperationException("Not enough music detected. Start playback and measure again.");
            var measured = Engine.EndLevelProbe(); suggestedGain = LevelAdvisor.Recommend(measured.Rms, measured.Peak, Profile.SendGain);
            LevelStatus = $"Suggested boost: {suggestedGain:0.#}×. Apply it, then enable music. " + (suggestedGain >= 15.99 ? "Source is very quiet; raise its player volume if needed." : "");
        }
        catch (Exception ex) when (IsExpectedOperationFailure(ex)) { LevelStatus = ex.Message; }
        finally { Engine.EndLevelProbe(); Measuring = false; Changed(nameof(CanApplyLevel)); }
    }
    async Task Export()
    {
        if (Busy) return; var dialog = new SaveFileDialog { Filter = "WarMusic backup|*.warmusic", FileName = "WarMusic-backup.warmusic" }; if (dialog.ShowDialog() != true) return;
        Busy = true; try { Save(); var snapshot = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!; await Task.Run(() => PortableBackup.Export(dialog.FileName, snapshot)); Notice = "Backup saved with all sounds and loadouts."; } catch (Exception ex) when (IsExpectedOperationFailure(ex)) { Notice = "Backup failed: " + ex.Message; } finally { Busy = false; }
    }
    async Task Restore()
    {
        if (Busy) return; var dialog = new OpenFileDialog { Filter = "WarMusic backup|*.warmusic" }; if (dialog.ShowDialog() != true) return;
        Busy = true; try
        {
            var imported = await Task.Run(() => PortableBackup.Import(dialog.FileName)); StopRecovery(); Engine.Stop();
            foreach (var sound in imported.Sounds) { sound.PropertyChanged += SoundChanged; Sounds.Add(sound); }
            foreach (var loadout in imported.Profiles) { string basis = loadout.Name; int suffix = 1; while (Profiles.Any(p => p.Name.Equals(loadout.Name, StringComparison.OrdinalIgnoreCase))) loadout.Name = $"{basis} (restored {suffix++})"; loadout.PropertyChanged += ProfileChanged; Profiles.Add(loadout); }
            RebuildCollections(); RegisterHotkeys(); Save(); Changed(nameof(LibraryCount)); Notice = $"Restored {imported.Sounds.Count} sounds and {imported.Profiles.Count} loadouts. Existing items kept. Check devices before routing.";
        }
        catch (Exception ex) when (IsExpectedOperationFailure(ex)) { Notice = "Restore failed: " + ex.Message; }
        finally { Busy = false; }
    }
}

