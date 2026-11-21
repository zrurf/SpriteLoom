using SpriteLoom.Abstractions;

namespace SpriteLoom.Packing;

internal static class PackingMath
{
    public static int RoundUp(int value, int align) =>
        align <= 1 ? value : (value + align - 1) / align * align;

    public static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }

    public static (int Width, int Height) FinalizeSize(int usedWidth, int usedHeight, PackOptions options)
    {
        if (usedWidth > options.MaxWidth || usedHeight > options.MaxHeight)
        {
            throw new InvalidOperationException("Packed content exceeds the configured page limits.");
        }

        var width = RoundUp(Math.Max(1, usedWidth), options.Align);
        var height = RoundUp(Math.Max(1, usedHeight), options.Align);

        if (options.Pow2)
        {
            width = NextPowerOfTwo(width);
            height = NextPowerOfTwo(height);
        }

        return (Math.Min(width, options.MaxWidth), Math.Min(height, options.MaxHeight));
    }
}
