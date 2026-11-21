using SpriteLoom.Codecs;
using SpriteLoom.Foundation;

namespace SpriteLoom.Integration.Tests;

public sealed class RasterCodecTests
{
    [Fact]
    public void Skia_encodes_and_decodes_webp()
    {
        var codec = new SkiaImageCodec();
        var image = CreateImage();

        var data = codec.Encode(image, "webp", 90);
        var decoded = codec.Decode(data);

        Assert.True(data.Length > 0);
        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
    }

    [Theory]
    [InlineData("jxl")]
    [InlineData("avif")]
    public void Magick_encodes_and_decodes_modern_formats(string format)
    {
        var codec = new MagickImageCodec();
        var image = CreateImage();

        var data = codec.Encode(image, format, 90);
        var decoded = codec.Decode(data);

        Assert.True(data.Length > 0);
        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
    }

    [Fact]
    public void Jpeg_rejects_transparency()
    {
        var codec = new SkiaImageCodec();
        var image = CreateImage(128);

        var exception = Assert.Throws<PixelFormatException>(() => codec.Encode(image, "jpeg", 90));

        Assert.Equal("SL3010", exception.Code);
    }

    private static RgbaImage CreateImage(byte alpha = 255)
    {
        var image = new RgbaImage(4, 4);

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, (byte)(x * 60), (byte)(y * 60), 40, alpha);
            }
        }

        return image;
    }
}
