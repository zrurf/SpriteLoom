namespace SpriteLoom.Pipeline;

public sealed record SourceSpec(
    string Id,
    string Kind,
    string Path,
    string? ImagePath,
    string Order,
    int FrameDurationMs);
