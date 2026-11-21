namespace SpriteLoom.Model.Serialization;

public sealed record ManifestSummary(
    string Format,
    int FormatMajor,
    int FormatMinor,
    int UriCount,
    int PageCount,
    int FrameCount,
    int AnimationCount);
