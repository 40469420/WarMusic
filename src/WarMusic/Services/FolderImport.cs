using System.IO;

namespace WarMusic.Services;

internal sealed record ImportEntry(string Path, string Collection);

internal static class FolderImport
{
    internal static List<ImportEntry> Discover(IEnumerable<string> inputs, CancellationToken cancellationToken)
    {
        var result = new List<ImportEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = Path.GetFullPath(input);
            if (!Directory.Exists(full))
            {
                Add(full, "General");
                continue;
            }

            var root = Path.TrimEndingDirectorySeparator(full);
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, Path.GetDirectoryName(file)!);
                var collection = new DirectoryInfo(root).Name;
                if (relative != ".") collection += "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
                Add(file, collection);
            }
        }

        return result;

        void Add(string file, string collection)
        {
            var extension = Path.GetExtension(file);
            if ((extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) && seen.Add(file))
                result.Add(new ImportEntry(file, collection));
        }
    }
}
