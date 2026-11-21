using SpriteLoom.Foundation;

namespace SpriteLoom.Model.Manifest;

public sealed record AtlasStoredFrame(
    IntRect Rect,
    int Rotation,
    Digest Digest);
