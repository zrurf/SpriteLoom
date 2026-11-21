using SpriteLoom.Abstractions;

namespace SpriteLoom.Packing;

public sealed class MaxRectsPacker : IPacker
{
    public string Id => "pack.maxrects";

    public IReadOnlyList<PageLayout> Pack(IReadOnlyList<PackItem> items, PackOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        if (items.Count == 0)
        {
            return [];
        }

        var ordered = items
            .OrderByDescending(item => Math.Max(item.Width, item.Height))
            .ThenByDescending(item => (long)item.Width * item.Height)
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
        private readonly List<Rect> _free = [];
        private int _usedWidth;
        private int _usedHeight;

        public PageBuilder(PackOptions options)
        {
            _options = options;
            _free.Add(new Rect(0, 0, options.MaxWidth, options.MaxHeight));
        }

        public List<Placement> Placements { get; } = [];

        public bool TryAdd(PackItem item, out Placement placement)
        {
            var width = item.Width + _options.Padding;
            var height = item.Height + _options.Padding;
            var bestIndex = -1;
            var bestShort = int.MaxValue;
            var bestLong = int.MaxValue;
            var bestY = int.MaxValue;
            var bestX = int.MaxValue;

            for (var index = 0; index < _free.Count; index++)
            {
                var free = _free[index];
                if (free.Width < width || free.Height < height)
                {
                    continue;
                }

                var shortSide = Math.Min(free.Width - width, free.Height - height);
                var longSide = Math.Max(free.Width - width, free.Height - height);

                if (shortSide > bestShort ||
                    (shortSide == bestShort && longSide > bestLong) ||
                    (shortSide == bestShort && longSide == bestLong && free.Y > bestY) ||
                    (shortSide == bestShort && longSide == bestLong && free.Y == bestY && free.X >= bestX))
                {
                    continue;
                }

                bestIndex = index;
                bestShort = shortSide;
                bestLong = longSide;
                bestY = free.Y;
                bestX = free.X;
            }

            if (bestIndex < 0)
            {
                placement = default;
                return false;
            }

            var target = _free[bestIndex];
            placement = new Placement(item.Index, target.X, target.Y, 0);

            var placed = new Rect(target.X, target.Y, width, height);
            Split(placed);
            Prune();

            _usedWidth = Math.Max(_usedWidth, placed.Right);
            _usedHeight = Math.Max(_usedHeight, placed.Bottom);
            return true;
        }

        public PageLayout Build()
        {
            var (width, height) = PackingMath.FinalizeSize(_usedWidth, _usedHeight, _options);
            return new PageLayout(width, height, Placements);
        }

        private void Split(Rect placed)
        {
            var additions = new List<Rect>();

            for (var index = _free.Count - 1; index >= 0; index--)
            {
                var free = _free[index];
                if (!Intersects(free, placed))
                {
                    continue;
                }

                _free.RemoveAt(index);

                if (free.X < placed.X)
                {
                    additions.Add(new Rect(free.X, free.Y, placed.X - free.X, free.Height));
                }

                if (placed.Right < free.Right)
                {
                    additions.Add(new Rect(placed.Right, free.Y, free.Right - placed.Right, free.Height));
                }

                if (free.Y < placed.Y)
                {
                    additions.Add(new Rect(free.X, free.Y, free.Width, placed.Y - free.Y));
                }

                if (placed.Bottom < free.Bottom)
                {
                    additions.Add(new Rect(free.X, placed.Bottom, free.Width, free.Bottom - placed.Bottom));
                }
            }

            _free.AddRange(additions);
            Prune();
        }

        private void Prune()
        {
            for (var index = _free.Count - 1; index >= 0; index--)
            {
                for (var other = 0; other < _free.Count; other++)
                {
                    if (index == other)
                    {
                        continue;
                    }

                    if (Contains(_free[other], _free[index]))
                    {
                        _free.RemoveAt(index);
                        break;
                    }
                }
            }
        }

        private static bool Intersects(Rect left, Rect right) =>
            left.X < right.Right && right.X < left.Right &&
            left.Y < right.Bottom && right.Y < left.Bottom;

        private static bool Contains(Rect outer, Rect inner) =>
            outer.X <= inner.X && outer.Y <= inner.Y &&
            outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
    }

    private readonly record struct Rect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;
    }
}
