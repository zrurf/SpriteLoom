using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging;

public static class MipmapGenerator
{
    public static IReadOnlyList<RgbaImage> Generate(RgbaImage baseImage)
    {
        ArgumentNullException.ThrowIfNull(baseImage);

        var levels = new List<RgbaImage> { baseImage };
        var current = baseImage;

        while (current.Width > 1 || current.Height > 1)
        {
            current = Downsample(current);
            levels.Add(current);
        }

        return levels;
    }

    public static RgbaImage Downsample(RgbaImage source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var width = Math.Max(1, source.Width / 2);
        var height = Math.Max(1, source.Height / 2);
        var target = new RgbaImage(width, height);
        var sourcePixels = source.Pixels;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sumRed = 0;
                var sumGreen = 0;
                var sumBlue = 0;
                var sumAlpha = 0;
                var count = 0;

                for (var dy = 0; dy < 2; dy++)
                {
                    var sourceY = Math.Min(source.Height - 1, y * 2 + dy);

                    for (var dx = 0; dx < 2; dx++)
                    {
                        var sourceX = Math.Min(source.Width - 1, x * 2 + dx);
                        var offset = (sourceY * source.Width + sourceX) * RgbaImage.BytesPerPixel;
                        sumRed += sourcePixels[offset];
                        sumGreen += sourcePixels[offset + 1];
                        sumBlue += sourcePixels[offset + 2];
                        sumAlpha += sourcePixels[offset + 3];
                        count++;
                    }
                }

                target.SetPixel(
                    x,
                    y,
                    (byte)((sumRed + count / 2) / count),
                    (byte)((sumGreen + count / 2) / count),
                    (byte)((sumBlue + count / 2) / count),
                    (byte)((sumAlpha + count / 2) / count));
            }
        }

        return target;
    }
}
