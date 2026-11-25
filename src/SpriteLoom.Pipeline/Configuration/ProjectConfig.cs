using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline.Configuration;

public sealed class ProjectConfig
{
    public required string BaseDirectory { get; init; }

    public required string PackageId { get; init; }

    public string? DisplayName { get; init; }

    public required IReadOnlyList<SourceSpec> Sources { get; init; }

    public required IReadOnlyList<AnimationSpec> Animations { get; init; }

    public required string FramePattern { get; init; }

    public required string Packer { get; init; }

    public required PackOptions PackOptions { get; init; }

    public required int TrimThreshold { get; init; }

    public required IReadOnlyList<string> RasterFormats { get; init; }

    public required int RasterQuality { get; init; }

    public bool PngFallback { get; init; } = true;

    public required BleedOptions BleedOptions { get; init; }

    public required IReadOnlyList<string> TextureTargets { get; init; }

    public required PixelFormat TexturePixelFormat { get; init; }

    public required DitherMode TextureDither { get; init; }

    public required bool Mipmaps { get; init; }

    public required TextureQuality TextureQuality { get; init; }

    public required string OutputPath { get; init; }

    public required string OutputKind { get; init; }

    public string? SigningKeyPath { get; init; }

    public PackageEncryptionOptions? Encryption { get; init; }

    public required ManifestFormat ManifestFormat { get; init; }
}
