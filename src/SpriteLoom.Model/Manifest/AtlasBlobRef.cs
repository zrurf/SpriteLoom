namespace SpriteLoom.Model.Manifest;

public sealed record AtlasBlobRef(
    int UriIndex,
    string File,
    Digest Digest,
    long Size);
