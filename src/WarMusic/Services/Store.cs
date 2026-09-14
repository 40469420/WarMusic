using System.Globalization;
using System.IO;
using System.Text.Json;
using WarMusic.Models;

namespace WarMusic.Services;

internal enum StorageMode
{
    Installed,
    Portable,
    Test,
}

public static class Store
{
    internal const string PortableMarker = "WarMusic.portable";
    private const string SetupGuideResource = "WarMusic.Docs.SETUP.md";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Root { get; private set; } = "";
    public static string Data => Path.Combine(Root, "data");
    public static string ContentRoot { get; private set; } = "";
    public static string? RecoveryMessage { get; private set; }
    internal static StorageMode Mode { get; private set; }

    public static void Initialize() => Initialize(forcePortable: false);

    public static void Initialize(bool forcePortable)
    {
        var applicationRoot = Path.GetFullPath(AppContext.BaseDirectory);
        var portable = forcePortable || File.Exists(Path.Combine(applicationRoot, PortableMarker));
        Initialize(
            portable ? StorageMode.Portable : StorageMode.Installed,
            applicationRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    public static void Initialize(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Configure(Path.GetFullPath(root), StorageMode.Test, Path.GetFullPath(root));
        EnsureDataDirectories();
    }

    internal static void Initialize(StorageMode mode, string applicationRoot, string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);

        applicationRoot = Path.GetFullPath(applicationRoot);
        var root = mode == StorageMode.Portable
            ? applicationRoot
            : Path.Combine(Path.GetFullPath(localAppDataRoot), "WarMusic");

        Configure(root, mode, FindContentRoot(applicationRoot));
        if (mode == StorageMode.Installed)
        {
            TryMigrateLegacyData(applicationRoot);
        }

        EnsureDataDirectories();
    }

    private static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Path.Combine(Data, "library"));
    }

    private static void Configure(string root, StorageMode mode, string contentRoot)
    {
        Root = root;
        Mode = mode;
        ContentRoot = contentRoot;
        RecoveryMessage = null;
        Directory.CreateDirectory(Root);
    }

    public static Settings Load()
    {
        var path = Path.Combine(Data, "settings.json");
        if (!File.Exists(path))
        {
            return new Settings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var storedVersion = ReadSchemaVersion(json);
            if (storedVersion > Settings.CurrentSchemaVersion)
            {
                RecoveryMessage = "Settings were created by a newer WarMusic version. Defaults were used and the newer file was preserved.";
                TryPreserveFile(path, "newer");
                Log($"Settings schema {storedVersion} is newer than supported schema {Settings.CurrentSchemaVersion}.");
                return new Settings();
            }

            var settings = JsonSerializer.Deserialize<Settings>(json)
                ?? throw new InvalidDataException("Settings file is empty.");
            Validate(settings);

            foreach (var profile in settings.Profiles)
            {
                Sanitize(profile);
            }

            if (storedVersion < Settings.CurrentSchemaVersion)
            {
                Preserve(path, $"pre-v{Settings.CurrentSchemaVersion}");
                Migrate(settings, storedVersion);
                Save(settings);
                RecoveryMessage = "Settings were upgraded. A copy of the previous file was preserved.";
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            RecoveryMessage = "Settings could not be loaded. Defaults were used and the original was preserved.";
            TryPreserveFile(path, "corrupt");
            Log(ex.ToString());
            return new Settings();
        }
    }

    public static void Save(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = Settings.CurrentSchemaVersion;
        Directory.CreateDirectory(Data);

        var path = Path.Combine(Data, "settings.json");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, Options);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static void Sanitize(Profile profile)
    {
        profile.MicGain = Bound(profile.MicGain, 0, 2, 1);
        profile.AppGain = Bound(profile.AppGain, 0, 16, 4);
        profile.ClipGain = Bound(profile.ClipGain, 0, 2, .65f);
        profile.SendGain = Bound(profile.SendGain, 0, 1, 1);
        profile.MonitorGain = Bound(profile.MonitorGain, 0, 1, .65f);
        profile.ThresholdDb = Bound(profile.ThresholdDb, -65, -10, -35);
        profile.ReductionDb = Bound(profile.ReductionDb, 0, 40, 15);
        profile.AttackMs = Bound(profile.AttackMs, 1, 500, 30);
        profile.HoldMs = Bound(profile.HoldMs, 0, 2000, 350);
        profile.ReleaseMs = Bound(profile.ReleaseMs, 10, 3000, 600);
        profile.DuckMode = Math.Clamp(profile.DuckMode, 0, 2);
        profile.TransmitMode = Math.Clamp(profile.TransmitMode, 0, 1);
    }

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(Data);
            var path = Path.Combine(Data, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                File.Move(path, path + ".old", overwrite: true);
            }

            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never interrupt audio or shutdown paths.
        }
    }

    internal static string ResolveSetupGuide()
    {
        var adjacent = Path.Combine(ContentRoot, "docs", "SETUP.md");
        if (File.Exists(adjacent))
        {
            return adjacent;
        }

        var destination = Path.Combine(Data, "docs", "SETUP.md");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = typeof(Store).Assembly.GetManifestResourceStream(SetupGuideResource)
            ?? throw new InvalidOperationException("The embedded setup guide is unavailable.");
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
        source.CopyTo(output);
        output.Flush(flushToDisk: true);
        return destination;
    }

    private static int ReadSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Settings must contain a JSON object.");
        }

        return document.RootElement.TryGetProperty(nameof(Settings.SchemaVersion), out var value)
            && value.TryGetInt32(out var version)
                ? version
                : 0;
    }

    private static void Migrate(Settings settings, int storedVersion)
    {
        while (storedVersion < Settings.CurrentSchemaVersion)
        {
            storedVersion = storedVersion switch
            {
                0 => 1,
                _ => throw new InvalidDataException($"No settings migration exists for version {storedVersion}."),
            };
        }

        settings.SchemaVersion = storedVersion;
    }

    private static void Validate(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ActiveProfile)
            || settings.Profiles is null
            || settings.Profiles.Count == 0
            || settings.Profiles.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.Name))
            || settings.Sounds is null
            || settings.Sounds.Any(sound => sound is null
                || sound.Path is null
                || sound.Name is null
                || sound.Collection is null
                || sound.Hotkey is null
                || sound.Color is null))
        {
            throw new InvalidDataException("Settings are incomplete.");
        }
    }

    private static float Bound(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    internal static string FindContentRoot(string applicationRoot)
    {
        var current = new DirectoryInfo(applicationRoot);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WarMusic.root")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return applicationRoot;
    }

    private static void TryMigrateLegacyData(string applicationRoot)
    {
        var legacyRoot = FindContentRoot(applicationRoot);
        var legacyData = Path.Combine(legacyRoot, "data");
        if (!Directory.Exists(legacyData)
            || PathsEqual(legacyData, Data)
            || (Directory.Exists(Data) && Directory.EnumerateFileSystemEntries(Data).Any()))
        {
            return;
        }

        var staging = Data + ".migrating";
        try
        {
            DeleteDirectoryIfPresent(staging);

            CopyDirectory(legacyData, staging);
            DeleteDirectoryIfPresent(Data);

            Directory.Move(staging, Data);
            RecoveryMessage = "Existing portable data was copied to the installed data folder. The original was left unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(staging);

            RecoveryMessage = "Existing portable data could not be migrated automatically. Use Export/Restore after startup.";
            Log(ex.ToString());
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((source, destination));
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            Directory.CreateDirectory(current.Destination);
            foreach (var file in Directory.EnumerateFiles(current.Source, "*", options))
            {
                File.Copy(file, Path.Combine(current.Destination, Path.GetFileName(file)), overwrite: false);
            }

            foreach (var directory in Directory.EnumerateDirectories(current.Source, "*", options))
            {
                pending.Push((directory, Path.Combine(current.Destination, Path.GetFileName(directory))));
            }
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            Directory.Delete(path, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectoryIfPresent(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryPreserveFile(string path, string label)
    {
        try
        {
            Preserve(path, label);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Preserve(string path, string label)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        File.Copy(path, $"{path}.{label}-{stamp}", overwrite: false);
    }
}
