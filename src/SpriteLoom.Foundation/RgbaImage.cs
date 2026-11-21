namespace SpriteLoom.Foundation;

public sealed class RgbaImage
{
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    public RgbaImage(int width, int height)
        : this(width, height, new byte[checked(width * height * BytesPerPixel)])
    {
    }

    public RgbaImage(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != width * height * BytesPerPixel)
        {
            throw new ArgumentException("Pixel buffer length does not match the image dimensions.", nameof(pixels));
        }

        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * BytesPerPixel;

    public ReadOnlySpan<byte> Pixels => _pixels;

    public static RgbaImage CreateTransparent(int width, int height) => new(width, height);

    public RgbaImage Clone() => new(Width, Height, (byte[])_pixels.Clone());

    public RgbaImage Crop(IntRect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Right > Width || rect.Bottom > Height || rect.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Crop rectangle is outside the image.");
        }

        var result = new RgbaImage(rect.Width, rect.Height);

        for (var row = 0; row < rect.Height; row++)
        {
            var sourceOffset = ((rect.Y + row) * Width + rect.X) * BytesPerPixel;
            var targetOffset = row * rect.Width * BytesPerPixel;
            _pixels.AsSpan(sourceOffset, rect.Width * BytesPerPixel)
                .CopyTo(result._pixels.AsSpan(targetOffset));
        }

        return result;
    }

    public void Blit(RgbaImage source, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);

        if (x + source.Width > Width || y + source.Height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Source image does not fit at the target position.");
        }

        for (var row = 0; row < source.Height; row++)
        {
            var sourceOffset = row * source.Stride;
            var targetOffset = ((y + row) * Width + x) * BytesPerPixel;
            source._pixels.AsSpan(sourceOffset, source.Stride)
                .CopyTo(_pixels.AsSpan(targetOffset, source.Stride));
        }
    }

    public void SetPixel(int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinates are outside the image.");
        }

        var offset = (y * Width + x) * BytesPerPixel;
        _pixels[offset] = red;
        _pixels[offset + 1] = green;
        _pixels[offset + 2] = blue;
        _pixels[offset + 3] = alpha;
    }

    public (byte Red, byte Green, byte Blue, byte Alpha) GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinates are outside the image.");
        }

        var offset = (y * Width + x) * BytesPerPixel;
        return (_pixels[offset], _pixels[offset + 1], _pixels[offset + 2], _pixels[offset + 3]);
    }

    public static RgbaImage FromBytes(int width, int height, ReadOnlySpan<byte> pixels) =>
        new(width, height, pixels.ToArray());
}
