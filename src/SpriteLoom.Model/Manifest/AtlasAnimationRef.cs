namespace SpriteLoom.Model.Manifest;

public sealed record AtlasAnimationRef(
    int UriIndex,
    string File,
    Digest Digest,
    int FrameCount);
