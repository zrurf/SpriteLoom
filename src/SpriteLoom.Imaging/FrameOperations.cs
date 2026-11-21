using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging;

public static class FrameOperations
{
    public static FrameSet Trim(FrameSet input, TrimOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var threshold = Math.Clamp(options.Threshold, 0, 255);
        var frames = new List<Frame>(input.Frames.Count);

        foreach (var frame in input.Frames)
        {
            if (frame.Trimmed)
            {
                frames.Add(frame);
                continue;
            }

            var bounds = FindOpaqueBounds(frame.Pixels, threshold);

            if (bounds.IsEmpty)
            {
                var x = (frame.Pixels.Width - 1) / 2;
                var y = (frame.Pixels.Height - 1) / 2;
                var pixel = frame.Pixels.Crop(new IntRect(x, y, 1, 1));
                frames.Add(WithPixels(frame, pixel, x, y, trimmed: true));
                continue;
            }

            frames.Add(WithPixels(frame, frame.Pixels.Crop(bounds), bounds.X, bounds.Y, trimmed: true));
        }

        return new FrameSet { Frames = frames };
    }

    public static FrameSet Deduplicate(FrameSet input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var stored = new List<StoredFrame>();
        var index = new Dictionary<(ContentHash Hash, int Width, int Height), int>();

        foreach (var frame in input.Frames)
        {
            var key = (frame.ContentHash, frame.Pixels.Width, frame.Pixels.Height);
            if (!index.TryGetValue(key, out var storedIndex))
            {
                storedIndex = stored.Count;
                stored.Add(new StoredFrame { Hash = frame.ContentHash, Pixels = frame.Pixels });
                index.Add(key, storedIndex);
            }

            frame.StoredIndex = storedIndex;
        }

        return new FrameSet { Frames = input.Frames, Stored = stored };
    }

    public static IReadOnlyList<PageImage> Compose(FrameSet frameSet, IReadOnlyList<PageLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(frameSet);
        ArgumentNullException.ThrowIfNull(layouts);

        var pages = new List<PageImage>(layouts.Count);

        for (var pageIndex = 0; pageIndex < layouts.Count; pageIndex++)
        {
            var layout = layouts[pageIndex];
            var pixels = RgbaImage.CreateTransparent(layout.Width, layout.Height);

            foreach (var placement in layout.Placements)
            {
                if (placement.Rotation != 0)
                {
                    throw new NotSupportedException("Rotated placements are not supported yet.");
                }

                var stored = frameSet.Stored[placement.ItemIndex];
                pixels.Blit(stored.Pixels, placement.X, placement.Y);
            }

            pages.Add(new PageImage { Index = pageIndex, Pixels = pixels, Layout = layout });
        }

        return pages;
    }

    public static void Bleed(IReadOnlyList<PageImage> pages, FrameSet frameSet, BleedOptions options)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(frameSet);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var page in pages)
        {
            if (options.Extrude > 0)
            {
                Extrude(page, frameSet, options.Extrude);
            }

            if (options.AlphaDilate)
            {
                DilateAlpha(page.Pixels);
            }
        }
    }

    private static Frame WithPixels(Frame frame, RgbaImage pixels, int offsetX, int offsetY, bool trimmed) =>
        new()
        {
            Uri = frame.Uri,
            SourceId = frame.SourceId,
            SourceIndex = frame.SourceIndex,
            Pixels = pixels,
            OriginWidth = frame.OriginWidth,
            OriginHeight = frame.OriginHeight,
            OffsetX = offsetX,
            OffsetY = offsetY,
            PivotX = frame.PivotX,
            PivotY = frame.PivotY,
            ContentHash = ContentHash.Compute(pixels.Pixels),
            Trimmed = trimmed,
            StoredIndex = frame.StoredIndex
        };

    private static IntRect FindOpaqueBounds(RgbaImage image, int threshold)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;
        var pixels = image.Pixels;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (pixels[(y * image.Width + x) * RgbaImage.BytesPerPixel + 3] < threshold)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        return maxX < 0 ? default : new IntRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void Extrude(PageImage page, FrameSet frameSet, int radius)
    {
        var image = page.Pixels;

        foreach (var placement in page.Layout.Placements)
        {
            var stored = frameSet.Stored[placement.ItemIndex];
            var width = stored.Pixels.Width;
            var height = stored.Pixels.Height;
            var left = placement.X;
            var top = placement.Y;
            var right = left + width - 1;
            var bottom = top + height - 1;

            for (var offset = 0; offset < radius; offset++)
            {
                for (var x = left; x <= right; x++)
                {
                    CopyPixel(image, x, top, x, top - offset - 1);
                    CopyPixel(image, x, bottom, x, bottom + offset + 1);
                }

                for (var y = top; y <= bottom; y++)
                {
                    CopyPixel(image, left, y, left - offset - 1, y);
                    CopyPixel(image, right, y, right + offset + 1, y);
                }
            }
        }
    }

    private static void CopyPixel(RgbaImage image, int sourceX, int sourceY, int targetX, int targetY)
    {
        if ((uint)targetX >= (uint)image.Width || (uint)targetY >= (uint)image.Height)
        {
            return;
        }

        var pixel = image.GetPixel(sourceX, sourceY);
        image.SetPixel(targetX, targetY, pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
    }

    private static void DilateAlpha(RgbaImage image)
    {
        var pixels = image.Pixels;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var offset = (y * image.Width + x) * RgbaImage.BytesPerPixel;
                if (pixels[offset + 3] != 0)
                {
                    continue;
                }

                var red = 0;
                var green = 0;
                var blue = 0;
                var count = 0;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        var ny = y + dy;
                        if ((uint)nx >= (uint)image.Width || (uint)ny >= (uint)image.Height)
                        {
                            continue;
                        }

                        var neighbor = (ny * image.Width + nx) * RgbaImage.BytesPerPixel;
                        if (pixels[neighbor + 3] == 0)
                        {
                            continue;
                        }

                        red += pixels[neighbor];
                        green += pixels[neighbor + 1];
                        blue += pixels[neighbor + 2];
                        count++;
                    }
                }

                if (count > 0)
                {
                    image.SetPixel(x, y, (byte)(red / count), (byte)(green / count), (byte)(blue / count), 0);
                }
            }
        }
    }
}
