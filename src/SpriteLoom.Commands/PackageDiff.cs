using System.Text.Json;

namespace SpriteLoom.Commands;

internal sealed record PackageDiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed);

internal static class PackageDiff
{
    public static PackageDiffResult Compare(PackageFiles left, PackageFiles right)
    {
        var leftFrames = ReadFrames(left);
        var rightFrames = ReadFrames(right);

        var added = rightFrames.Keys
            .Where(uri => !leftFrames.ContainsKey(uri))
            .OrderBy(uri => uri, StringComparer.Ordinal)
            .ToArray();
        var removed = leftFrames.Keys
            .Where(uri => !rightFrames.ContainsKey(uri))
            .OrderBy(uri => uri, StringComparer.Ordinal)
            .ToArray();
        var changed = leftFrames.Keys
            .Where(uri => rightFrames.TryGetValue(uri, out var value) && value != leftFrames[uri])
            .OrderBy(uri => uri, StringComparer.Ordinal)
            .ToArray();

        return new PackageDiffResult(added, removed, changed);
    }

    private static Dictionary<string, string> ReadFrames(PackageFiles package)
    {
        if (!package.HasJsonManifest)
        {
            throw new NotSupportedException("diff currently requires JSON manifests.");
        }

        using var manifest = JsonDocument.Parse(package.ManifestBytes);
        var root = manifest.RootElement;
        var uris = root.GetProperty("uris")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
        var toRecord = root.GetProperty("frames")
            .GetProperty("to-record")
            .EnumerateArray()
            .Select(value => value.GetInt64())
            .ToArray();
        var records = root.GetProperty("frames")
            .GetProperty("records")
            .EnumerateArray()
            .Select(value => value.GetRawText())
            .ToArray();
        var pages = root.GetProperty("pages")
            .EnumerateArray()
            .Select((page, index) => new
            {
                Index = index,
                Container = page.GetProperty("container").GetString() ?? string.Empty
            })
            .ToDictionary(page => page.Index, page => page.Container);

        var containers = new Dictionary<int, string[]>(pages.Count);

        foreach (var (pageIndex, containerPath) in pages)
        {
            if (!package.Files.TryGetValue(containerPath, out var bytes))
            {
                continue;
            }

            using var container = JsonDocument.Parse(bytes);
            containers[pageIndex] = container.RootElement
                .GetProperty("stored")
                .EnumerateArray()
                .Select(value => value.GetProperty("rect").GetRawText())
                .ToArray();
        }

        var frames = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var uriIndex = 0; uriIndex < uris.Length && uriIndex < toRecord.Length; uriIndex++)
        {
            var recordIndex = toRecord[uriIndex];

            if (recordIndex < 0 || recordIndex >= records.Length)
            {
                continue;
            }

            using var record = JsonDocument.Parse(records[recordIndex]);
            var page = record.RootElement.GetProperty("page").GetInt32();
            var stored = record.RootElement.GetProperty("stored").GetInt32();
            var rect = containers.TryGetValue(page, out var rects) && stored < rects.Length
                ? rects[stored]
                : string.Empty;

            frames[uris[uriIndex]] = $"{record.RootElement.GetRawText()}|{rect}";
        }

        return frames;
    }
}
