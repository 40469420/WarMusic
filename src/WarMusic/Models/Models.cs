using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace WarMusic.Models;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Changed(name); return true; }
}
public class Sound : Observable
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    string name = ""; public string Name { get => name; set => Set(ref name, value); }
    string collection = "General"; public string Collection { get => collection; set => Set(ref collection, value); }
    bool favorite; public bool Favorite { get => favorite; set => Set(ref favorite, value); }
    public float NormalizationGain { get; set; } = 1;
    public bool Analyzed { get; set; }
    string hotkey = ""; public string Hotkey { get => hotkey; set => Set(ref hotkey, value); }
    string color = "#566737"; public string Color { get => color; set => Set(ref color, value); }
    public override string ToString() => Name;
}
public class Profile : Observable
{
    string _Name = "Default"; public string Name { get => _Name; set => Set(ref _Name, value); }
    string _MicId = ""; public string MicId { get => _MicId; set => Set(ref _MicId, value); }
    string _MonitorId = ""; public string MonitorId { get => _MonitorId; set => Set(ref _MonitorId, value); }
    string _CableId = ""; public string CableId { get => _CableId; set => Set(ref _CableId, value); }
    string _SourceName = ""; public string SourceName { get => _SourceName; set => Set(ref _SourceName, value); }
    string _Collection = "All sounds"; public string Collection { get => _Collection; set => Set(ref _Collection, value); }
    float _MicGain = 1; public float MicGain { get => _MicGain; set => Set(ref _MicGain, value); }
    float _AppGain = 4; public float AppGain { get => _AppGain; set => Set(ref _AppGain, value); }
    float _ClipGain = .65f; public float ClipGain { get => _ClipGain; set => Set(ref _ClipGain, value); }
    float _SendGain = 1; public float SendGain { get => _SendGain; set => Set(ref _SendGain, value); }
    float _MonitorGain = .65f; public float MonitorGain { get => _MonitorGain; set => Set(ref _MonitorGain, value); }
    bool _DuckEnabled = true; public bool DuckEnabled { get => _DuckEnabled; set => Set(ref _DuckEnabled, value); }
    int _DuckMode; public int DuckMode { get => _DuckMode; set => Set(ref _DuckMode, value); }
    float _ThresholdDb = -35; public float ThresholdDb { get => _ThresholdDb; set => Set(ref _ThresholdDb, value); }
    float _ReductionDb = 15; public float ReductionDb { get => _ReductionDb; set => Set(ref _ReductionDb, value); }
    float _AttackMs = 30; public float AttackMs { get => _AttackMs; set => Set(ref _AttackMs, value); }
    float _HoldMs = 350; public float HoldMs { get => _HoldMs; set => Set(ref _HoldMs, value); }
    float _ReleaseMs = 600; public float ReleaseMs { get => _ReleaseMs; set => Set(ref _ReleaseMs, value); }
    bool _DuckMonitor; public bool DuckMonitor { get => _DuckMonitor; set => Set(ref _DuckMonitor, value); }
    bool _ClipDucking = true; public bool ClipDucking { get => _ClipDucking; set => Set(ref _ClipDucking, value); }
    bool _MonitorApplication; public bool MonitorApplication { get => _MonitorApplication; set => Set(ref _MonitorApplication, value); }
    bool _Normalize = true; public bool Normalize { get => _Normalize; set => Set(ref _Normalize, value); }
    int _TransmitMode; public int TransmitMode { get => _TransmitMode; set => Set(ref _TransmitMode, value); }
    string _CommsKey = "CapsLock"; public string CommsKey { get => _CommsKey; set => Set(ref _CommsKey, value); }
    string _HoldKey = "F8"; public string HoldKey { get => _HoldKey; set => Set(ref _HoldKey, value); }
    string _PanicKey = "Control+Shift+F12"; public string PanicKey { get => _PanicKey; set => Set(ref _PanicKey, value); }
    string _ToggleKey = "Control+Shift+F9"; public string ToggleKey { get => _ToggleKey; set => Set(ref _ToggleKey, value); }
    string _PlayKey = "Control+Shift+F10"; public string PlayKey { get => _PlayKey; set => Set(ref _PlayKey, value); }
}
public class Settings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool SetupPromptShown { get; set; }
    public bool CloseToTray { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public float FadeSeconds { get; set; } = 2;
    public double OverlayLeft { get; set; } = 80; public double OverlayTop { get; set; } = 80;
    public string ActiveProfile { get; set; } = "Default";
    public List<Profile> Profiles { get; set; } = [new(), new() { Name = "Arma" }, new() { Name = "Wardogs" }];
    public List<Sound> Sounds { get; set; } = [];
}
public record Device(string Id, string Name) { public override string ToString() => Name; }
public record AppSource(int Pid, string Name) { public override string ToString() => $"{Name}  ·  {Pid}"; }


