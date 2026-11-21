namespace SpriteLoom.Model.Manifest;

public sealed class AtlasManifest
{
    public const int CurrentFormatMajor = 1;
    public const int CurrentFormatMinor = 0;

    public required string BundleId { get; init; }

    public required string PackageId { get; init; }

    public string? PackageDisplayName { get; init; }

    public required string GeneratorName { get; init; }

    public required string GeneratorVersion { get; init; }

    public required IReadOnlyList<string> Uris { get; init; }

    public required IReadOnlyList<AtlasPage> Pages { get; init; }

    public required IReadOnlyList<uint> ToRecord { get; init; }

    public required IReadOnlyList<AtlasFrameRecord> Records { get; init; }

    public required IReadOnlyList<AtlasAnimationRef> Animations { get; init; }

    public IReadOnlyList<AtlasBlobRef> Blobs { get; init; } = [];
}
