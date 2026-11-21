using SpriteLoom.Abstractions;

namespace SpriteLoom.Packing;

public sealed class BasicPacker : IPacker
{
    public string Id => "pack.basic";

    public IReadOnlyList<PageLayout> Pack(IReadOnlyList<PackItem> items, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        if (items.Count == 0)
        {
            return [];
        }

        var ordered = items
            .OrderByDescending(item => item.Height)
            .ThenByDescending(item => item.Width)
            .ThenBy(item => item.Index)
            .ToArray();

        var pages = new List<PageBuilder>();
        var current = new PageBuilder(options);
        pages.Add(current);

        foreach (var item in ordered)
        {
            if (!current.TryAdd(item, out var placement))
            {
                current = new PageBuilder(options);
                pages.Add(current);

                if (!current.TryAdd(item, out placement))
                {
                    throw new InvalidOperationException($"Frame {item.Index} does not fit on an empty page.");
                }
            }

            current.Placements.Add(placement);
        }

        return pages.Select(page => page.Build()).ToArray();
    }

    private sealed class PageBuilder
    {
        private readonly PackOptions _options;
        private readonly List<Shelf> _shelves = [];
        private int _usedWidth;
        private int _usedHeight;

        public PageBuilder(PackOptions options)
        {
            _options = options;
        }

        public List<Placement> Placements { get; } = [];

        public bool TryAdd(PackItem item, out Placement placement)
        {
            foreach (var shelf in _shelves)
            {
                if (item.Height <= shelf.Height &&
                    shelf.CursorX + item.Width + _options.Padding <= _options.MaxWidth)
                {
                    placement = new Placement(item.Index, shelf.CursorX, shelf.Y + _options.Padding, 0);
                    shelf.CursorX += item.Width + _options.Padding;
                    _usedWidth = Math.Max(_usedWidth, shelf.CursorX);
                    return true;
                }
            }

            var shelfY = _shelves.Count == 0
                ? 0
                : _shelves[^1].Y + _shelves[^1].Height + _options.Padding;

            if (shelfY + item.Height + _options.Padding > _options.MaxHeight)
            {
                placement = default;
                return false;
            }

            var newShelf = new Shelf(shelfY, item.Height)
            {
                CursorX = _options.Padding + item.Width + _options.Padding
            };

            _shelves.Add(newShelf);
            placement = new Placement(item.Index, _options.Padding, shelfY + _options.Padding, 0);
            _usedWidth = Math.Max(_usedWidth, newShelf.CursorX);
            _usedHeight = shelfY + item.Height + _options.Padding;
            return true;
        }

        public PageLayout Build()
        {
            var (width, height) = PackingMath.FinalizeSize(_usedWidth, _usedHeight, _options);
            return new PageLayout(width, height, Placements);
        }
    }

    private sealed class Shelf(int y, int height)
    {
        public int Y { get; } = y;

        public int Height { get; } = height;

        public int CursorX { get; set; }
    }
}
