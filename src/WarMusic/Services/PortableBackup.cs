using System.IO;
using System.IO.Compression;
using System.Text.Json;
using WarMusic.Models;
namespace WarMusic.Services;

public static class PortableBackup
{
    public static void Export(string path, Settings source)
    {
        var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(source))!;
        string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                for (int i = 0; i < copy.Sounds.Count; i++)
                {
                    var sound = copy.Sounds[i]; string file = Path.IsPathRooted(sound.Path) ? sound.Path : Path.Combine(Store.Root, sound.Path);
                    if (!File.Exists(file)) throw new FileNotFoundException("Missing sound: " + sound.Name);
                    sound.Path = $"sounds/{i}{Path.GetExtension(file).ToLowerInvariant()}"; zip.CreateEntryFromFile(file, sound.Path);
                }
                using var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()); writer.Write(JsonSerializer.Serialize(copy));
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Settings Import(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = zip.GetEntry("settings.json") ?? throw new InvalidDataException("This is not a WarMusic backup.");
        if (manifest.Length > 10_000_000) throw new InvalidDataException("Backup manifest is too large.");
        using var reader = new StreamReader(manifest.Open()); var data = JsonSerializer.Deserialize<Settings>(reader.ReadToEnd()) ?? throw new InvalidDataException("Invalid backup.");
        if (data.Sounds == null || data.Profiles == null || data.Profiles.Count == 0 || data.Sounds.Count > 10000 || data.Profiles.Count > 1000) throw new InvalidDataException("Invalid backup contents.");
        long total = 0; var entries = new List<ZipArchiveEntry>();
        foreach (var sound in data.Sounds)
        {
            if (sound == null || string.IsNullOrWhiteSpace(sound.Path) || !sound.Path.StartsWith("sounds/", StringComparison.Ordinal) || sound.Path.Contains("..") || sound.Path.Contains('\\')) throw new InvalidDataException("Invalid sound path.");
            if (string.IsNullOrWhiteSpace(sound.Name)) sound.Name = "Restored sound"; if (string.IsNullOrWhiteSpace(sound.Collection)) sound.Collection = "General"; sound.Hotkey ??= ""; if (sound.Color == null || !System.Text.RegularExpressions.Regex.IsMatch(sound.Color, "^#[0-9a-fA-F]{6}$")) sound.Color = "#566737"; if (!float.IsFinite(sound.NormalizationGain) || sound.NormalizationGain <= 0) sound.NormalizationGain = 1;
            string ext = Path.GetExtension(sound.Path); if (ext is not ".wav" and not ".mp3") throw new InvalidDataException("Unsupported sound in backup.");
            var entry = zip.GetEntry(sound.Path) ?? throw new InvalidDataException("A sound is missing from this backup.");
            total += entry.Length; if (entry.Length > 500_000_000 || total > 2_000_000_000) throw new InvalidDataException("Backup exceeds the 2 GB import limit."); entries.Add(entry);
        }
        foreach (var profile in data.Profiles) { if (profile == null || string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("Invalid loadout."); Store.Sanitize(profile); }
        string folder = Path.Combine(Store.Data, "library", "restore-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try { for (int i = 0; i < data.Sounds.Count; i++) { var sound = data.Sounds[i]; string file = Path.Combine(folder, i + Path.GetExtension(sound.Path)); entries[i].ExtractToFile(file); using var audio = new NAudio.Wave.AudioFileReader(file); sound.Id = Guid.NewGuid().ToString("N"); sound.Path = Path.GetRelativePath(Store.Root, file); } return data; }
        catch { Directory.Delete(folder, true); throw; }
    }
}

