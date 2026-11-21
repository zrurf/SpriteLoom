using System.Text;
using SpriteLoom.Abstractions;

namespace SpriteLoom.Pipeline.Caching;

public sealed class ArtifactCache
{
    public ArtifactCache(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }

    public bool TryLoad(
        string passId,
        string key,
        out IReadOnlyDictionary<string, Artifact> outputs)
    {
        var path = PathFor(passId, key);
        outputs = new Dictionary<string, Artifact>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false), leaveOpen: false);
            var count = reader.ReadInt32();
            var restored = new Dictionary<string, Artifact>(StringComparer.Ordinal);

            for (var index = 0; index < count; index++)
            {
                restored[reader.ReadString()] = ArtifactCodec.Read(reader);
            }

            outputs = restored;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or EndOfStreamException)
        {
            File.Delete(path);
            return false;
        }
    }

    public void Store(
        string passId,
        string key,
        IReadOnlyDictionary<string, Artifact> outputs)
    {
        var path = PathFor(passId, key);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";

        using (var stream = File.Create(temporaryPath))
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: false))
        {
            writer.Write(outputs.Count);

            foreach (var (port, artifact) in outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.Write(port);
                ArtifactCodec.Write(writer, artifact);
            }
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private string PathFor(string passId, string key)
    {
        var safePass = string.Concat(passId.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
        return Path.Combine(RootPath, safePass, key + ".bin");
    }
}
