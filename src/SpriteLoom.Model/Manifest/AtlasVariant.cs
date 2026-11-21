namespace SpriteLoom.Model.Manifest;

public sealed record AtlasVariant(
    string Id,
    string Codec,
    string File,
    Digest Digest)
{
    public string? PixelFormat { get; init; }
}
