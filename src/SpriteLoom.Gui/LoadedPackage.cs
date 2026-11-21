using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using SpriteLoom.Bundle;

namespace SpriteLoom.Gui;

internal sealed class LoadedPackage : IDisposable
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly JsonDocument _manifest;
    private readonly List<PageInfo> _pages;
    private readonly List<JsonElement> _records;
    private readonly List<int> _toRecord;
    private readonly List<string> _uris;
    private readonly Dictionary<int, Bitmap> _pageBitmaps = [];

    private LoadedPackage(
        IReadOnlyDictionary<string, byte[]> files,
        JsonDocument manifest,
        List<PageInfo> pages,
        List<JsonElement> records,
        List<int> toRecord,
        List<string> uris)
    {
        _files = files;
        _manifest = manifest;
        _pages = pages;
        _records = records;
        _toRecord = toRecord;
        _uris = uris;
    }

    public static LoadedPackage Open(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string manifestPath;

        if (File.Exists(fullPath) && fullPath.EndsWith(".slb", StringComparison.OrdinalIgnoreCase))
        {
            var archive = SlbReader.ReadFile(fullPath);

            foreach (var (name, bytes) in archive.Files)
            {
                files[name] = bytes;
            }

            manifestPath = files.ContainsKey("manifest.json")
                ? "manifest.json"
                : throw new InvalidDataException("Bundle has no JSON manifest.");
        }
        else
        {
            var root = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath)!;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                files[relative] = File.ReadAllBytes(file);
            }

            manifestPath = File.Exists(fullPath)
                ? Path.GetRelativePath(root, fullPath).Replace('\\', '/')
                : "manifest.json";
        }

        if (!files.TryGetValue(manifestPath, out var manifestBytes))
        {
            throw new FileNotFoundException("Manifest not found.", manifestPath);
        }

        var manifest = JsonDocument.Parse(manifestBytes);
        var rootElement = manifest.RootElement;

        if (rootElement.GetProperty("format").GetString() != "sprite-loom")
        {
            throw new InvalidDataException("Not a SpriteLoom manifest.");
        }

        var uris = rootElement.GetProperty("uris")
            .EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToList();
        var pages = new List<PageInfo>();

        foreach (var page in rootElement.GetProperty("pages").EnumerateArray())
        {
            var containerPath = page.GetProperty("container").GetString()!;
            using var container = JsonDocument.Parse(files[containerPath]);
            var stored = container.RootElement.GetProperty("stored")
                .EnumerateArray()
                .Select(value =>
                {
                    var rect = value.GetProperty("rect");
                    return new Rect(
                        rect[0].GetInt32(),
                        rect[1].GetInt32(),
                        rect[2].GetInt32(),
                        rect[3].GetInt32());
                })
                .ToList();
            var variants = page.GetProperty("variants")
                .EnumerateArray()
                .Select(value => new VariantInfo(
                    value.GetProperty("id").GetString()!,
                    value.GetProperty("file").GetString()!))
                .ToList();

            pages.Add(new PageInfo(
                page.GetProperty("size")[0].GetInt32(),
                page.GetProperty("size")[1].GetInt32(),
                variants,
                stored));
        }

        var records = rootElement.GetProperty("frames").GetProperty("records")
            .EnumerateArray()
            .ToList();
        var toRecord = rootElement.GetProperty("frames").GetProperty("to-record")
            .EnumerateArray()
            .Select(value => value.GetInt64())
            .Select(value => value == uint.MaxValue ? -1 : (int)value)
            .ToList();

        return new LoadedPackage(files, manifest, pages, records, toRecord, uris);
    }

    public IReadOnlyList<string> AnimationUris =>
        _manifest.RootElement.GetProperty("animations")
            .EnumerateArray()
            .Select(value => _uris[value.GetProperty("uri").GetInt32()])
            .ToList();

    public AnimationInfo LoadAnimation(string uri)
    {
        var reference = _manifest.RootElement.GetProperty("animations")
            .EnumerateArray()
            .FirstOrDefault(value => _uris[value.GetProperty("uri").GetInt32()] == uri);

        if (reference.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException($"Unknown animation: {uri}");
        }

        var file = reference.GetProperty("file").GetString()!;
        using var document = JsonDocument.Parse(_files[file]);
        var frames = new List<string>();

        foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray())
        {
            frames.Add(frame.ValueKind == JsonValueKind.Number
                ? _uris[frame.GetInt32()]
                : frame.GetString()!);
        }

        return new AnimationInfo(
            uri,
            frames,
            document.RootElement.GetProperty("durations-ms")
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToList(),
            document.RootElement.GetProperty("loop").GetBoolean(),
            document.RootElement.GetProperty("playback").GetString() ?? "forward");
    }

    public FrameInfo GetFrame(string uri)
    {
        var uriIndex = _uris.IndexOf(uri);

        if (uriIndex < 0)
        {
            throw new InvalidDataException($"Unknown URI: {uri}");
        }

        var recordIndex = _toRecord[uriIndex];

        if (recordIndex < 0)
        {
            throw new InvalidDataException($"URI is not a frame: {uri}");
        }

        var record = _records[recordIndex];
        var page = record.GetProperty("page").GetInt32();
        var stored = record.GetProperty("stored").GetInt32();
        var origin = record.GetProperty("origin");
        var offset = record.GetProperty("offset");
        var rect = _pages[page].Stored[stored];

        return new FrameInfo(
            page,
            rect,
            origin[0].GetInt32(),
            origin[1].GetInt32(),
            offset[0].GetInt32(),
            offset[1].GetInt32());
    }

    public Bitmap GetPageBitmap(int page)
    {
        if (_pageBitmaps.TryGetValue(page, out var cached))
        {
            return cached;
        }

        var info = _pages[page];
        var variant = info.Variants.FirstOrDefault(candidate => candidate.Id is "png" or "webp")
            ?? info.Variants.First(candidate => !candidate.File.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase));
        var bitmap = new Bitmap(new MemoryStream(_files[variant.File]));
        _pageBitmaps[page] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var bitmap in _pageBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _pageBitmaps.Clear();
        _manifest.Dispose();
    }

    private sealed record PageInfo(
        int Width,
        int Height,
        IReadOnlyList<VariantInfo> Variants,
        IReadOnlyList<Rect> Stored);

    private sealed record VariantInfo(string Id, string File);
}

public sealed record FrameInfo(
    int Page,
    Rect Rect,
    int OriginWidth,
    int OriginHeight,
    int OffsetX,
    int OffsetY);

public sealed record AnimationInfo(
    string Uri,
    IReadOnlyList<string> FrameUris,
    IReadOnlyList<int> DurationsMs,
    bool Loop,
    string Playback)
{
    public int TotalDurationMs => DurationsMs.Sum();
}
