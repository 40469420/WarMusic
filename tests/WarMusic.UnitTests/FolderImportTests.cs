using WarMusic.Services;

namespace WarMusic.UnitTests;

public sealed class FolderImportTests
{
    [Fact]
    public void FindsNestedAudioAndDeduplicatesOverlappingInputs()
    {
        using var workspace = new TestWorkspace();
        var root = Path.Combine(workspace.Root, "Music");
        var nested = Path.Combine(root, "Ambient", "Night");
        Directory.CreateDirectory(nested);
        var song = Path.Combine(nested, "song.MP3");
        File.WriteAllText(song, "fixture");
        File.WriteAllText(Path.Combine(root, "clip.wav"), "fixture");
        File.WriteAllText(Path.Combine(root, "notes.txt"), "ignore");
        var entries = FolderImport.Discover([root, song], CancellationToken.None);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, item => item.Path == song && item.Collection == "Music/Ambient/Night");
        Assert.Contains(entries, item => item.Collection == "Music");
    }

    [Fact]
    public void CancelledScanStopsBeforeEnumerating()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => FolderImport.Discover(["unused"], cancellation.Token));
    }
}
