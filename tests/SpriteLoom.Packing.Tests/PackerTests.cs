using SpriteLoom.Abstractions;
using SpriteLoom.Packing;

namespace SpriteLoom.Packing.Tests;

public sealed class PackerTests
{
    [Fact]
    public void Basic_packs_all_items_without_overlap() =>
        AssertValid(new BasicPacker());

    [Fact]
    public void MaxRects_packs_all_items_without_overlap() =>
        AssertValid(new MaxRectsPacker());

    [Fact]
    public void Basic_is_deterministic() =>
        AssertDeterministic(new BasicPacker());

    [Fact]
    public void MaxRects_is_deterministic() =>
        AssertDeterministic(new MaxRectsPacker());

    [Fact]
    public void Both_packers_create_multiple_pages_when_needed()
    {
        var items = Enumerable.Range(0, 4)
            .Select(index => new PackItem(index, 10, 10))
            .ToArray();
        var options = new PackOptions(16, 16, 0, false, 1, false);

        Assert.True(new BasicPacker().Pack(items, options).Count > 1);
        Assert.True(new MaxRectsPacker().Pack(items, options).Count > 1);
    }

    [Fact]
    public void Empty_input_produces_no_pages()
    {
        var options = new PackOptions(32, 32, 1, false, 1, false);

        Assert.Empty(new BasicPacker().Pack([], options));
        Assert.Empty(new MaxRectsPacker().Pack([], options));
    }

    private static void AssertValid(IPacker packer)
    {
        var items = new[]
        {
            new PackItem(0, 32, 16),
            new PackItem(1, 8, 8),
            new PackItem(2, 20, 24),
            new PackItem(3, 16, 8),
            new PackItem(4, 4, 30),
            new PackItem(5, 12, 12)
        };
        var options = new PackOptions(64, 64, 1, false, 1, false);
        var pages = packer.Pack(items, options);

        Assert.NotEmpty(pages);

        var placed = new HashSet<int>();

        foreach (var page in pages)
        {
            Assert.InRange(page.Width, 1, options.MaxWidth);
            Assert.InRange(page.Height, 1, options.MaxHeight);

            var rectangles = new List<(int X, int Y, int Width, int Height)>();

            foreach (var placement in page.Placements)
            {
                var item = items[placement.ItemIndex];
                Assert.True(placed.Add(item.Index), $"Item {item.Index} was placed more than once.");

                Assert.InRange(placement.X, 0, page.Width - item.Width);
                Assert.InRange(placement.Y, 0, page.Height - item.Height);

                var rectangle = (placement.X, placement.Y, item.Width, item.Height);
                foreach (var other in rectangles)
                {
                    Assert.False(Overlaps(rectangle, other), $"Items overlap on page {page.Width}x{page.Height}.");
                }

                rectangles.Add(rectangle);
            }
        }

        Assert.Equal(items.Length, placed.Count);
    }

    private static void AssertDeterministic(IPacker packer)
    {
        var items = new[]
        {
            new PackItem(0, 20, 10),
            new PackItem(1, 10, 20),
            new PackItem(2, 12, 12),
            new PackItem(3, 6, 6)
        };
        var options = new PackOptions(48, 48, 2, true, 4, false);

        var first = packer.Pack(items, options);
        var second = packer.Pack(items, options);

        Assert.Equal(first.Count, second.Count);

        for (var page = 0; page < first.Count; page++)
        {
            Assert.Equal(first[page].Width, second[page].Width);
            Assert.Equal(first[page].Height, second[page].Height);
            Assert.Equal(first[page].Placements, second[page].Placements);
        }
    }

    private static bool Overlaps(
        (int X, int Y, int Width, int Height) left,
        (int X, int Y, int Width, int Height) right) =>
        left.X < right.X + right.Width && right.X < left.X + left.Width &&
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;
}
