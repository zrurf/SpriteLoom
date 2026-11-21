namespace SpriteLoom.Imaging;

public sealed record EncodedPageVariant(
    string Id,
    string Codec,
    string File,
    byte[] Data,
    string? PixelFormat = null);
