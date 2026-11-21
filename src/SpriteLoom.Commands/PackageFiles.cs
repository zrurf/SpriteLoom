using SpriteLoom.Bundle;

namespace SpriteLoom.Commands;

internal sealed class PackageFiles
{
    private PackageFiles(
        IReadOnlyDictionary<string, byte[]> files,
        string sourcePath,
        string manifestPath)
    {
        Files = files;
        SourcePath = sourcePath;
        ManifestPath = manifestPath;
    }

    public IReadOnlyDictionary<string, byte[]> Files { get; }

    public string SourcePath { get; }

    public string ManifestPath { get; }

    public byte[] ManifestBytes => Files[ManifestPath];

    public bool HasJsonManifest => ManifestPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public static PackageFiles Open(string target)
    {
        var fullPath = Path.GetFullPath(target);

        if (File.Exists(fullPath) && fullPath.EndsWith(".slb", StringComparison.OrdinalIgnoreCase))
        {
            var archive = SlbReader.ReadFile(fullPath);
            var manifestPath = FindManifest(archive.Files.Keys)
                ?? throw new InvalidDataException("Bundle does not contain a manifest.");
            return new PackageFiles(archive.Files, fullPath, manifestPath);
        }

        if (File.Exists(fullPath))
        {
            var name = Path.GetFileName(fullPath);

            if (name is "manifest.json" or "manifest.cbor")
            {
                var root = Path.GetDirectoryName(fullPath)!;
                return new PackageFiles(ReadDirectory(root), fullPath, name);
            }

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [name] = File.ReadAllBytes(fullPath)
            };
            return new PackageFiles(files, fullPath, name);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("Package path not found.", fullPath);
        }

        var directoryFiles = ReadDirectory(fullPath);
        var directoryManifest = FindManifest(directoryFiles.Keys)
            ?? throw new FileNotFoundException("No manifest found in the package directory.", fullPath);

        return new PackageFiles(directoryFiles, fullPath, directoryManifest);
    }

    private static Dictionary<string, byte[]> ReadDirectory(string root)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            files[relativePath] = File.ReadAllBytes(path);
        }

        return files;
    }

    private static string? FindManifest(IEnumerable<string> paths)
    {
        var entries = paths.ToHashSet(StringComparer.Ordinal);

        if (entries.Contains("manifest.json"))
        {
            return "manifest.json";
        }

        if (entries.Contains("manifest.cbor"))
        {
            return "manifest.cbor";
        }

        return entries.FirstOrDefault(path =>
            path.EndsWith("/manifest.json", StringComparison.Ordinal) ||
            path.EndsWith("/manifest.cbor", StringComparison.Ordinal));
    }
}
