using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;

namespace SpriteLoom.Imaging.Tests;

public sealed class FrameOperationsTests
{
    [Fact]
    public void Trim_crops_transparent_border()
    {
        var image = RgbaImage.CreateTransparent(8, 8);
        Fill(image, 3, 3, 2, 2, 10, 20, 30, 255);

        var trimmed = FrameOperations.Trim(
            new FrameSet { Frames = [CreateFrame("a1", image)] },
            new TrimOptions());

        var result = Assert.Single(trimmed.Frames);
        Assert.Equal(2, result.Pixels.Width);
        Assert.Equal(2, result.Pixels.Height);
        Assert.Equal(3, result.OffsetX);
        Assert.Equal(3, result.OffsetY);
        Assert.True(result.Trimmed);
        Assert.Equal((10, 20, 30, 255), result.Pixels.GetPixel(0, 0));
    }

    [Fact]
    public void Trim_returns_single_pixel_for_empty_frame()
    {
        var frame = CreateFrame("empty", RgbaImage.CreateTransparent(4, 4));

        var trimmed = FrameOperations.Trim(
            new FrameSet { Frames = [frame] },
            new TrimOptions());

        var result = Assert.Single(trimmed.Frames);
        Assert.Equal(1, result.Pixels.Width);
        Assert.Equal(1, result.Pixels.Height);
        Assert.Equal(1, result.OffsetX);
        Assert.Equal(1, result.OffsetY);
    }

    [Fact]
    public void Deduplicate_shares_identical_frames()
    {
        var first = CreateFrame("a", MakeSolid(4, 4));
        var duplicate = CreateFrame("b", MakeSolid(4, 4));
        var different = CreateFrame("c", MakeSolid(6, 6));

        var deduplicated = FrameOperations.Deduplicate(
            new FrameSet { Frames = [first, duplicate, different] });

        Assert.Equal(2, deduplicated.Stored.Count);
        Assert.Equal(first.StoredIndex, duplicate.StoredIndex);
        Assert.NotEqual(first.StoredIndex, different.StoredIndex);
    }

    [Fact]
    public void Compose_blits_frames_into_page()
    {
        var frames = FrameOperations.Deduplicate(
            new FrameSet { Frames = [CreateFrame("a", MakeSolid(2, 2))] });
        var layout = new PageLayout(8, 8, [new Placement(0, 2, 3, 0)]);

        var page = Assert.Single(FrameOperations.Compose(frames, [layout]));

        Assert.Equal((200, 100, 50, 255), page.Pixels.GetPixel(2, 3));
        Assert.Equal(0, page.Pixels.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void Bleed_extrudes_edge_pixels()
    {
        var frames = FrameOperations.Deduplicate(
            new FrameSet { Frames = [CreateFrame("a", MakeSolid(2, 2))] });
        var layout = new PageLayout(8, 8, [new Placement(0, 2, 2, 0)]);
        var pages = FrameOperations.Compose(frames, [layout]);

        FrameOperations.Bleed(pages, frames, new BleedOptions(Extrude: 1, AlphaDilate: false));

        Assert.Equal((200, 100, 50, 255), pages[0].Pixels.GetPixel(1, 2));
        Assert.Equal((200, 100, 50, 255), pages[0].Pixels.GetPixel(4, 3));
    }

    private static Frame CreateFrame(string name, RgbaImage image) =>
        new()
        {
            Uri = SloomIri.Create(SloomIriKind.Frame, "heroes", $"run/{name}"),
            SourceId = "run",
            SourceIndex = 0,
            Pixels = image,
            OriginWidth = image.Width,
            OriginHeight = image.Height,
            OffsetX = 0,
            OffsetY = 0,
            PivotX = (uint)(image.Width * 8),
            PivotY = (uint)(image.Height * 8),
            ContentHash = ContentHash.Compute(image.Pixels)
        };

    private static RgbaImage MakeSolid(int width, int height)
    {
        var image = new RgbaImage(width, height);
        Fill(image, 0, 0, width, height, 200, 100, 50, 255);
        return image;
    }

    private static void Fill(
        RgbaImage image,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                image.SetPixel(column, row, red, green, blue, alpha);
            }
        }
    }
}
