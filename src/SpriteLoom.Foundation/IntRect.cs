namespace SpriteLoom.Foundation;

public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static IntRect FromBounds(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);
}
