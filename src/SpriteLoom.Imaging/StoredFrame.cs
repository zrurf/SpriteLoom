using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging;

public sealed class StoredFrame
{
    public required ContentHash Hash { get; init; }

    public required RgbaImage Pixels { get; init; }
}
