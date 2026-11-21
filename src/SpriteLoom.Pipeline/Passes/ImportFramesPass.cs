using System.Globalization;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;
using SpriteLoom.Pipeline.Import;

namespace SpriteLoom.Pipeline.Passes;

public sealed class ImportFramesPass : IPass
{
    private readonly IImageCodec _codec;

    public ImportFramesPass(IImageCodec codec)
    {
        _codec = codec;
    }

    public PassDescriptor Descriptor { get; } = new(
        "import.frames",
        "1.0",
        [],
        [new PortSpec("out", ArtifactKind.FrameSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var packageId = PassParameters.GetString(context.Parameters, "packageId");
        var pattern = PassParameters.GetString(context.Parameters, "framePattern", "{relpath}");
        var sources = PassParameters.GetValue<IReadOnlyList<SourceSpec>>(context.Parameters, "sources")
            ?? throw new InvalidOperationException("The import pass requires a source list.");

        var frames = new List<Frame>();

        foreach (var source in sources)
        {
            if (string.Equals(source.Kind, "texturepacker", StringComparison.OrdinalIgnoreCase))
            {
                var metadataPath = Path.GetFullPath(Path.Combine(context.BaseDirectory, source.Path));
                var imagePath = source.ImagePath is null
                    ? null
                    : Path.GetFullPath(Path.Combine(context.BaseDirectory, source.ImagePath));

                frames.AddRange(TexturePackerImporter.Import(
                    metadataPath,
                    imagePath,
                    _codec,
                    source.Id,
                    packageId));
                continue;
            }

            var root = Path.GetFullPath(Path.Combine(context.BaseDirectory, source.Path));
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {root}");
            }

            var files = Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(_codec.CanDecode)
                .Select(path => (FullPath: path, RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/')))
                .ToArray();

            if (string.Equals(source.Order, "lexicographic", StringComparison.Ordinal))
            {
                Array.Sort(files, (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
            }
            else
            {
                Array.Sort(files, (left, right) =>
                    NaturalStringComparer.Instance.Compare(left.RelativePath, right.RelativePath));
            }

            for (var index = 0; index < files.Length; index++)
            {
                var file = files[index];
                var stem = Path.GetFileNameWithoutExtension(file.RelativePath);
                var relativeWithoutExtension = RemoveExtension(file.RelativePath);
                var rendered = RenderPattern(pattern, stem, relativeWithoutExtension, source.Id, index);
                var uriPath = $"{source.Id}/{rendered}";
                var uri = SloomIri.Create(SloomIriKind.Frame, packageId, uriPath);
                var pixels = _codec.Decode(File.ReadAllBytes(file.FullPath));

                frames.Add(new Frame
                {
                    Uri = uri,
                    SourceId = source.Id,
                    SourceIndex = index,
                    Pixels = pixels,
                    OriginWidth = pixels.Width,
                    OriginHeight = pixels.Height,
                    OffsetX = 0,
                    OffsetY = 0,
                    PivotX = (uint)(pixels.Width * 8),
                    PivotY = (uint)(pixels.Height * 8),
                    ContentHash = ContentHash.Compute(pixels.Pixels),
                    Trimmed = false
                });
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.FrameSet, 0, new FrameSet { Frames = frames })
            });
    }

    private static string RenderPattern(string pattern, string stem, string relativePath, string sourceId, int index) =>
        pattern
            .Replace("{stem}", stem, StringComparison.Ordinal)
            .Replace("{relpath}", relativePath, StringComparison.Ordinal)
            .Replace("{source}", sourceId, StringComparison.Ordinal)
            .Replace("{index}", index.ToString("D4", CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string RemoveExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length == 0 ? path : path[..^extension.Length];
    }
}
