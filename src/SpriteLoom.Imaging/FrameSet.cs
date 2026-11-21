namespace SpriteLoom.Imaging;

public sealed class FrameSet
{
    public required IReadOnlyList<Frame> Frames { get; init; }

    public IReadOnlyList<StoredFrame> Stored { get; init; } = [];
}
