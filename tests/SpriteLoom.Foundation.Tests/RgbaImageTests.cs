using SpriteLoom.Foundation;

namespace SpriteLoom.Foundation.Tests;

public sealed class RgbaImageTests
{
    [Fact]
    public void Set_and_get_pixel_round_trips()
    {
        var image = new RgbaImage(2, 2);
        image.SetPixel(1, 1, 10, 20, 30, 40);

        Assert.Equal((10, 20, 30, 40), image.GetPixel(1, 1));
    }

    [Fact]
    public void Crop_copies_the_requested_region()
    {
        var image = new RgbaImage(3, 3);
        image.SetPixel(1, 1, 1, 2, 3, 255);
        image.SetPixel(2, 2, 4, 5, 6, 255);

        var cropped = image.Crop(new IntRect(1, 1, 2, 2));

        Assert.Equal(2, cropped.Width);
        Assert.Equal((1, 2, 3, 255), cropped.GetPixel(0, 0));
        Assert.Equal((4, 5, 6, 255), cropped.GetPixel(1, 1));
    }

    [Fact]
    public void Blit_copies_source_pixels()
    {
        var source = new RgbaImage(2, 2);
        source.SetPixel(0, 0, 7, 8, 9, 255);

        var target = new RgbaImage(4, 4);
        target.Blit(source, 1, 1);

        Assert.Equal((7, 8, 9, 255), target.GetPixel(1, 1));
    }

    [Fact]
    public void Crop_rejects_out_of_bounds_rectangles()
    {
        var image = new RgbaImage(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(new IntRect(1, 1, 2, 2)));
    }
}
