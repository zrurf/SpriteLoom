using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using SpriteLoom.Bundle;
using SpriteLoom.Model.Serialization;

namespace SpriteLoom.Gui;

internal sealed class LoadedPackage : IDisposable
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly Dictionary<string, byte[]> _jsonFiles = new(StringComparer.Ordinal);
    private readonly JsonDocument _manifest;
    private readonly List<PageInfo> _pages;
    private readonly List<JsonElement> _records;
    private readonly List<int> _toRecord;
    private readonly List<string> _uris;
    private readonly Dictionary<int, Bitmap> _pageBitmaps = [];
    private readonly object _pageBitmapLock = new();
    private bool _disposed;

    private LoadedPackage(
        IReadOnlyDictionary<string, byte[]> files,
        JsonDocument manifest,
        string manifestFormat,
        List<PageInfo> pages,
        List<JsonElement> records,
        List<int> toRecord,
        List<string> uris)
    {
        _files = files;
        _manifest = manifest;
        ManifestFormat = manifestFormat;
        ManifestText = JsonSerializer.Serialize(
            manifest.RootElement,
            new JsonSerializerOptions { WriteIndented = true });
        _pages = pages;
        _records = records;
        _toRecord = toRecord;
        _uris = uris;
    }

    public string ManifestFormat { get; }

    public string ManifestText { get; }

    public string PackageId =>
        _manifest.RootElement.GetProperty("package").GetProperty("id").GetString() ?? string.Empty;

    public int PageCount => _pages.Count;

    public int FrameCount => _records.Count;

    public int AnimationCount => _manifest.RootElement.GetProperty("animations").GetArrayLength();

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

            manifestPath = FindManifest(files);
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
                : FindManifest(files);
        }

        if (!files.ContainsKey(manifestPath))
        {
            throw new FileNotFoundException("Manifest not found.", manifestPath);
        }

        var isCbor = manifestPath.EndsWith(".cbor", StringComparison.OrdinalIgnoreCase);
        var manifestBytes = isCbor
            ? CborJsonConverter.ToJson(files[manifestPath])
            : files[manifestPath];
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
            using var container = JsonDocument.Parse(ReadJsonFile(files, containerPath));
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

        return new LoadedPackage(
            files,
            manifest,
            isCbor ? "CBOR" : "JSON",
            pages,
            records,
            toRecord,
            uris);
    }

    private static string FindManifest(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files.ContainsKey("manifest.json"))
        {
            return "manifest.json";
        }

        if (files.ContainsKey("manifest.cbor"))
        {
            return "manifest.cbor";
        }

        throw new FileNotFoundException("Package has no manifest.json or manifest.cbor.");
    }

    private static byte[] ReadJsonFile(IReadOnlyDictionary<string, byte[]> files, string path)
    {
        if (!files.TryGetValue(path, out var bytes))
        {
            throw new FileNotFoundException("Package file not found.", path);
        }

        return path.EndsWith(".cbor", StringComparison.OrdinalIgnoreCase)
            ? CborJsonConverter.ToJson(bytes)
            : bytes;
    }

    private byte[] ReadJson(string path)
    {
        if (_jsonFiles.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var bytes = ReadJsonFile(_files, path);
        _jsonFiles[path] = bytes;
        return bytes;
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
        using var document = JsonDocument.Parse(ReadJson(file));
        var frames = new List<string>();

        foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray())
        {
            frames.Add(frame.ValueKind == JsonValueKind.Number
                ? _uris[frame.GetInt32()]
                : frame.GetString()!);
        }

        var events = document.RootElement.TryGetProperty("events", out var eventArray)
            ? eventArray
                .EnumerateArray()
                .Select(value => new AnimationEventInfo(
                    value.GetProperty("frame").GetInt32(),
                    value.GetProperty("kind").GetString() ?? string.Empty))
                .ToArray()
            : [];

        return new AnimationInfo(
            uri,
            frames,
            document.RootElement.GetProperty("durations-ms")
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToList(),
            document.RootElement.GetProperty("loop").GetBoolean(),
            document.RootElement.GetProperty("playback").GetString() ?? "forward")
        {
            Events = events
        };
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

    public bool TryGetPageBitmap(int page, out Bitmap? bitmap)
    {
        lock (_pageBitmapLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pageBitmaps.TryGetValue(page, out bitmap);
        }
    }

    public Bitmap GetPageBitmap(int page)
    {
        lock (_pageBitmapLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pageBitmaps.TryGetValue(page, out var cached))
            {
                return cached;
            }

            var info = _pages[page];
            var variant = info.Variants.FirstOrDefault(candidate => candidate.Id is "png" or "webp")
                ?? info.Variants.FirstOrDefault(
                    candidate => !candidate.File.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Page {page} has no viewable raster variant.");
            var bitmap = new Bitmap(new MemoryStream(_files[variant.File]));
            _pageBitmaps[page] = bitmap;
            return bitmap;
        }
    }

    public void Dispose()
    {
        lock (_pageBitmapLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var bitmap in _pageBitmaps.Values)
            {
                bitmap.Dispose();
            }

            _pageBitmaps.Clear();
        }

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
    public IReadOnlyList<AnimationEventInfo> Events { get; init; } = [];

    public int TotalDurationMs => DurationsMs.Sum();
}

public sealed record AnimationEventInfo(int Frame, string Kind);

public sealed record TimelineFrameInfo(int Index, int DurationMs, string EventText)
{
    public string Label => $"#{Index + 1}";

    public string DurationLabel => $"{DurationMs} ms";

    public bool HasEvent => EventText.Length > 0;
}
