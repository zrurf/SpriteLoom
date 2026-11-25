using SpriteLoom.Bundle;
using SpriteLoom.Security;

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

    public static PackageFiles Open(string target, SymmetricKey? decryptionKey = null)
    {
        var fullPath = Path.GetFullPath(target);

        if (!File.Exists(fullPath) &&
            !Directory.Exists(fullPath) &&
            File.Exists(fullPath + SencFile.Extension))
        {
            fullPath += SencFile.Extension;
        }

        if (File.Exists(fullPath) && fullPath.EndsWith(".slb", StringComparison.OrdinalIgnoreCase))
        {
            var archive = SlbReader.ReadFile(fullPath, decryptionKey: decryptionKey);
            var manifestPath = FindManifest(archive.Files.Keys)
                ?? throw new InvalidDataException("Bundle does not contain a manifest.");
            return new PackageFiles(archive.Files, fullPath, manifestPath);
        }

        if (File.Exists(fullPath))
        {
            var name = Path.GetFileName(fullPath);

            if (name is "manifest.json" or "manifest.cbor" ||
                (name.EndsWith(SencFile.Extension, StringComparison.OrdinalIgnoreCase) &&
                 name.StartsWith("manifest.", StringComparison.Ordinal)))
            {
                var root = Path.GetDirectoryName(fullPath)!;
                var manifestFiles = ReadDirectory(root, decryptionKey);
                var manifest = FindManifest(manifestFiles.Keys)
                    ?? throw new FileNotFoundException("No manifest found in the package directory.", root);
                return new PackageFiles(manifestFiles, root, manifest);
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

        var directoryFiles = ReadDirectory(fullPath, decryptionKey);
        var directoryManifest = FindManifest(directoryFiles.Keys)
            ?? throw new FileNotFoundException("No manifest found in the package directory.", fullPath);

        return new PackageFiles(directoryFiles, fullPath, directoryManifest);
    }

    private static Dictionary<string, byte[]> ReadDirectory(string root, SymmetricKey? decryptionKey)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');

            var bytes = File.ReadAllBytes(path);

            if (!relativePath.EndsWith(SencFile.Extension, StringComparison.OrdinalIgnoreCase) ||
                !SencFile.HasMagic(bytes))
            {
                files[relativePath] = bytes;
                continue;
            }

            if (decryptionKey is null)
            {
                throw new InvalidDataException(
                    $"SL5022: Encrypted package file requires --decrypt-key: {relativePath}");
            }

            files[relativePath[..^SencFile.Extension.Length]] = SencFile.Decrypt(bytes, decryptionKey);
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
