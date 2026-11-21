namespace SpriteLoom.Model.Manifest;

public sealed record AtlasContainer(
    int Page,
    int Width,
    int Height,
    string PixelFormat,
    string Alpha,
    IReadOnlyList<AtlasVariant> Variants,
    IReadOnlyList<AtlasStoredFrame> Stored);
