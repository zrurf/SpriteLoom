using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging.Tests;

public sealed class MipmapGeneratorTests
{
    [Fact]
    public void Generates_chain_until_one_pixel()
    {
        var image = CreateImage(4, 4, 100);
        var levels = MipmapGenerator.Generate(image);

        Assert.Equal([4, 2, 1], levels.Select(level => level.Width));
        Assert.Equal([4, 2, 1], levels.Select(level => level.Height));
    }

    [Fact]
    public void Downsample_averages_quadrants()
    {
        var image = new RgbaImage(2, 2);
        image.SetPixel(0, 0, 0, 0, 0, 255);
        image.SetPixel(1, 0, 100, 100, 100, 255);
        image.SetPixel(0, 1, 200, 200, 200, 255);
        image.SetPixel(1, 1, 255, 255, 255, 255);

        var level = MipmapGenerator.Downsample(image);

        Assert.Equal(1, level.Width);
        Assert.Equal(1, level.Height);
        Assert.Equal((byte)139, level.GetPixel(0, 0).Red);
        Assert.Equal((byte)255, level.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Odd_dimensions_shrink_to_floor()
    {
        var image = CreateImage(5, 3, 20);
        var levels = MipmapGenerator.Generate(image);

        Assert.Equal([5, 2, 1], levels.Select(level => level.Width));
        Assert.Equal([3, 1, 1], levels.Select(level => level.Height));
    }

    private static RgbaImage CreateImage(int width, int height, byte value)
    {
        var image = new RgbaImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, value, value, value, 255);
            }
        }

        return image;
    }
}
