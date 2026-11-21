namespace SpriteLoom.Model.Manifest;

public sealed record AtlasPage(
    int Index,
    int UriIndex,
    string Group,
    int Order,
    bool Required,
    string ContainerPath,
    Digest ContainerDigest,
    int Width,
    int Height,
    string PixelFormat,
    IReadOnlyList<AtlasVariant> Variants);
