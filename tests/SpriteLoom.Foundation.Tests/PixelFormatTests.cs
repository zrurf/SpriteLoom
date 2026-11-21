using SpriteLoom.Foundation;

namespace SpriteLoom.Foundation.Tests;

public sealed class PixelFormatTests
{
    [Fact]
    public void Names_round_trip()
    {
        foreach (var format in Enum.GetValues<PixelFormat>())
        {
            Assert.Equal(format, PixelFormats.Parse(PixelFormats.GetName(format)));
        }

        Assert.Equal("ordered4", PixelFormats.GetDitherName(DitherMode.Ordered4));
        Assert.Equal(DitherMode.Ordered8, PixelFormats.ParseDither("ordered8"));
    }

    [Fact]
    public void Rgba_and_bgra_reorder_channels()
    {
        var image = CreateImage(1, 1, 10, 20, 30, 255);

        Assert.Equal([10, 20, 30, 255], PixelFormatConverter.Convert(image, PixelFormat.Rgba8888));
        Assert.Equal([30, 20, 10, 255], PixelFormatConverter.Convert(image, PixelFormat.Bgra8888));
        Assert.Equal([10, 20, 30], PixelFormatConverter.Convert(image, PixelFormat.Rgb888));
    }

    [Fact]
    public void Rgb565_packs_little_endian()
    {
        var image = CreateImage(1, 1, 255, 0, 0, 255);
        var bytes = PixelFormatConverter.Convert(image, PixelFormat.Rgb565);

        Assert.Equal(2, bytes.Length);
        var packed = (ushort)(bytes[0] | (bytes[1] << 8));
        Assert.Equal(31, packed >> 11);
        Assert.Equal(0, (packed >> 5) & 63);
    }

    [Fact]
    public void Rgba4444_packs_all_channels()
    {
        var image = CreateImage(1, 1, 255, 128, 0, 255);
        var bytes = PixelFormatConverter.Convert(image, PixelFormat.Rgba4444);
        var packed = (ushort)(bytes[0] | (bytes[1] << 8));

        Assert.Equal(15, packed >> 12);
        Assert.Equal(8, (packed >> 8) & 15);
        Assert.Equal(0, (packed >> 4) & 15);
        Assert.Equal(15, packed & 15);
    }

    [Fact]
    public void R8_uses_luminance()
    {
        var image = CreateImage(1, 1, 255, 255, 255, 255);
        Assert.Equal([255], PixelFormatConverter.Convert(image, PixelFormat.R8));
    }

    [Fact]
    public void Dither_is_deterministic()
    {
        var image = CreateImage(4, 4, 100, 100, 100, 255);

        var first = PixelFormatConverter.Convert(image, PixelFormat.Rgb565, DitherMode.Ordered4);
        var second = PixelFormatConverter.Convert(image, PixelFormat.Rgb565, DitherMode.Ordered4);

        Assert.Equal(first, second);
        Assert.NotEqual(first, PixelFormatConverter.Convert(image, PixelFormat.Rgb565));
    }

    [Fact]
    public void Opaque_formats_reject_transparency()
    {
        var image = CreateImage(1, 1, 10, 20, 30, 128);

        var exception = Assert.Throws<PixelFormatException>(
            () => PixelFormatConverter.Convert(image, PixelFormat.Rgb565));

        Assert.Equal("SL3010", exception.Code);
    }

    private static RgbaImage CreateImage(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var image = new RgbaImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, red, green, blue, alpha);
            }
        }

        return image;
    }
}
