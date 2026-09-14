using System.IO.Compression;
using System.Text.Json;
using NAudio.Wave;
using WarMusic.Audio;
using WarMusic.Models;
using WarMusic.Services;

namespace WarMusic.UnitTests;

public sealed class StorageTests
{
    [Fact]
    public void InstalledAndPortableModesUseSeparateDataRoots()
    {
        using var workspace = new TestWorkspace();
        var app = Path.Combine(workspace.Root, "app");
        var local = Path.Combine(workspace.Root, "local");
        Directory.CreateDirectory(app);

        Store.Initialize(StorageMode.Installed, app, local);
        Assert.Equal(Path.Combine(local, "WarMusic"), Store.Root);
        Assert.Equal(StorageMode.Installed, Store.Mode);

        Store.Initialize(StorageMode.Portable, app, local);
        Assert.Equal(app, Store.Root);
        Assert.Equal(StorageMode.Portable, Store.Mode);
    }

    [Fact]
    public void InstalledModeCopiesLegacyDataAndLeavesOriginalUntouched()
    {
        using var workspace = new TestWorkspace();
        var app = Path.Combine(workspace.Root, "portable");
        var legacyData = Path.Combine(app, "data");
        var local = Path.Combine(workspace.Root, "local");
        Directory.CreateDirectory(legacyData);
        File.WriteAllText(Path.Combine(app, "WarMusic.root"), "");
        File.WriteAllText(Path.Combine(legacyData, "settings.json"), "legacy");

        Store.Initialize(StorageMode.Installed, app, local);

        Assert.Equal("legacy", File.ReadAllText(Path.Combine(Store.Data, "settings.json")));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(legacyData, "settings.json")));
        Assert.Contains("left unchanged", Store.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstalledModeNeverWritesUnderApplicationDirectory()
    {
        using var workspace = new TestWorkspace();
        var app = Path.Combine(workspace.Root, "Program Files", "WarMusic");
        var local = Path.Combine(workspace.Root, "local");
        Directory.CreateDirectory(app);

        Store.Initialize(StorageMode.Installed, app, local);
        Store.Save(new Settings());

        Assert.False(Directory.Exists(Path.Combine(app, "data")));
        Assert.True(File.Exists(Path.Combine(local, "WarMusic", "data", "settings.json")));
    }

    [Fact]
    public void EmbeddedSetupGuideSupportsStandaloneExecutable()
    {
        using var workspace = new TestWorkspace();

        var path = Store.ResolveSetupGuide();

        Assert.Equal(Path.Combine(Store.Data, "docs", "SETUP.md"), path);
        Assert.Contains("# Audio setup", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptedLegacyMigrationIsRebuiltFromTheOriginal()
    {
        using var workspace = new TestWorkspace();
        var app = Path.Combine(workspace.Root, "portable");
        var local = Path.Combine(workspace.Root, "local");
        Directory.CreateDirectory(Path.Combine(app, "data"));
        File.WriteAllText(Path.Combine(app, "data", "settings.json"), "complete");
        var staging = Path.Combine(local, "WarMusic", "data.migrating");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "settings.json"), "partial");

        Store.Initialize(StorageMode.Installed, app, local);

        Assert.Equal("complete", File.ReadAllText(Path.Combine(Store.Data, "settings.json")));
        Assert.False(Directory.Exists(staging));
        Assert.Equal("complete", File.ReadAllText(Path.Combine(app, "data", "settings.json")));
    }

    [Fact]
    public void UnversionedSettingsAreBackedUpAndMigrated()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(Store.Data, "settings.json");
        File.WriteAllText(path, """
            {
              "ActiveProfile": "Legacy",
              "Profiles": [{ "Name": "Legacy" }],
              "Sounds": []
            }
            """);

        var settings = Store.Load();

        Assert.Equal(Settings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.NotEmpty(Directory.GetFiles(Store.Data, "settings.json.pre-v1-*"));
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(Settings.CurrentSchemaVersion,
            saved.RootElement.GetProperty(nameof(Settings.SchemaVersion)).GetInt32());
    }

    [Fact]
    public void CurrentSettingsLoadWithoutRunningMigrationAgain()
    {
        using var workspace = new TestWorkspace();
        var settings = new Settings { ActiveProfile = "Arma" };
        Store.Save(settings);
        var path = Path.Combine(Store.Data, "settings.json");
        var before = File.ReadAllText(path);

        var loaded = Store.Load();

        Assert.Equal("Arma", loaded.ActiveProfile);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Store.Data, "settings.json.pre-v*-*"));
    }

    [Fact]
    public void NewerSettingsArePreservedBeforeDefaultsAreUsed()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(Store.Data, "settings.json");
        var future = new Settings { SchemaVersion = Settings.CurrentSchemaVersion + 1 };
        var original = JsonSerializer.Serialize(future);
        File.WriteAllText(path, original);

        var loaded = Store.Load();

        Assert.Equal(Settings.CurrentSchemaVersion, loaded.SchemaVersion);
        var preserved = Assert.Single(Directory.GetFiles(Store.Data, "settings.json.newer-*"));
        Assert.Equal(original, File.ReadAllText(preserved));
        Assert.Contains("newer", Store.RecoveryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InterruptedTemporarySaveDoesNotReplaceValidSettings()
    {
        using var workspace = new TestWorkspace();
        var settings = new Settings { ActiveProfile = "Wardogs" };
        Store.Save(settings);
        File.WriteAllText(Path.Combine(Store.Data, "settings.json.tmp-interrupted"), "partial");

        Assert.Equal("Wardogs", Store.Load().ActiveProfile);
    }

    [Fact]
    public void BoostIsLimitedAndSurvivesProfileSave()
    {
        using var workspace = new TestWorkspace();
        var mixer = new MixProcessor();
        var send = new float[960];
        var options = new MixOptions
        {
            DuckEnabled = false,
            MicGain = 1,
            AppGain = 16,
            ClipGain = 1,
            SendGain = 1,
            MonitorGain = 1,
        };

        mixer.Process(TestWorkspace.Filled(.2f), TestWorkspace.Filled(.2f), TestWorkspace.Filled(0),
            send, new float[960], send.Length, options, true, false, false, false);
        Assert.True(mixer.Limited);
        Assert.All(send, sample => Assert.InRange(sample, -.96f, .96f));

        var settings = new Settings();
        settings.Profiles[0].AppGain = 16;
        Store.Sanitize(settings.Profiles[0]);
        Store.Save(settings);
        Assert.Equal(16, Store.Load().Profiles[0].AppGain);
    }

    [Fact]
    public void PortableBackupRoundTripPreservesSoundsAndHotkeys()
    {
        using var workspace = new TestWorkspace();
        var file = Path.Combine(Store.Data, "backup-source.wav");
        var archive = Path.Combine(Store.Data, "roundtrip.warmusic");
        using (var wave = new WaveFileWriter(file, AudioEngine.Format))
        {
            wave.WriteSamples(new float[960], 0, 960);
        }

        var data = new Settings();
        data.Profiles[0].ToggleKey = "Control+F6";
        data.Sounds.Add(new Sound
        {
            Path = file,
            Name = "Round trip",
            Hotkey = "Control+1",
            Color = "#365E70",
        });

        PortableBackup.Export(archive, data);
        var imported = PortableBackup.Import(archive);

        Assert.Single(imported.Sounds);
        Assert.Equal("Control+1", imported.Sounds[0].Hotkey);
        Assert.Equal("Control+F6", imported.Profiles[0].ToggleKey);
        var restored = Path.Combine(Store.Root, imported.Sounds[0].Path);
        Assert.True(File.Exists(restored));
        Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(restored));
    }

    [Fact]
    public void MalformedBackupCannotEscapeLibraryOrReplaceSettings()
    {
        using var workspace = new TestWorkspace();
        var archive = Path.Combine(Store.Data, "hostile.warmusic");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("settings.json").Open());
            writer.Write(JsonSerializer.Serialize(new Settings
            {
                Sounds = [new Sound { Path = "sounds/../../settings.json" }],
            }));
        }

        var before = Directory.GetDirectories(Path.Combine(Store.Data, "library")).Length;
        Assert.Throws<InvalidDataException>(() => PortableBackup.Import(archive));
        Assert.Equal(before, Directory.GetDirectories(Path.Combine(Store.Data, "library")).Length);
    }

    [Fact]
    public void SettingsRoundTripAppliesBounds()
    {
        using var workspace = new TestWorkspace();
        var settings = new Settings();
        settings.Profiles[0].MicGain = 100;
        Store.Sanitize(settings.Profiles[0]);
        Store.Save(settings);

        var loaded = Store.Load();
        Assert.Equal(2, loaded.Profiles[0].MicGain);
        Assert.Equal(3, loaded.Profiles.Count);
    }

    [Fact]
    public void CorruptSettingsRecoverAndPreserveOriginal()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(Store.Data, "settings.json"), "broken JSON");

        var settings = Store.Load();

        Assert.Equal(3, settings.Profiles.Count);
        Assert.NotEmpty(Directory.GetFiles(Store.Data, "settings.json.corrupt-*"));
    }

    [Fact]
    public void StructurallyInvalidSettingsRecoverAndPreserveOriginal()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(Store.Data, "settings.json"), "[]");

        var settings = Store.Load();

        Assert.Equal(3, settings.Profiles.Count);
        Assert.NotEmpty(Directory.GetFiles(Store.Data, "settings.json.corrupt-*"));
    }
}
