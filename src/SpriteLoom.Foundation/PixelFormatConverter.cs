namespace SpriteLoom.Foundation;

public static class PixelFormatConverter
{
    private static readonly int[] Bayer4 =
    [
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    ];

    private static readonly int[] Bayer8 =
    [
         0, 32,  8, 40,  2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44,  4, 36, 14, 46,  6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
         3, 35, 11, 43,  1, 33,  9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47,  7, 39, 13, 45,  5, 37,
        63, 31, 55, 23, 61, 29, 53, 21
    ];

    public static byte[] Convert(
        RgbaImage image,
        PixelFormat format,
        DitherMode dither = DitherMode.None)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (PixelFormats.RequiresOpaque(format) && HasTransparency(image))
        {
            throw new PixelFormatException(
                "SL3010",
                $"Pixel format {PixelFormats.GetName(format)} does not support alpha.");
        }

        return format switch
        {
            PixelFormat.Rgba8888 => image.Pixels.ToArray(),
            PixelFormat.Bgra8888 => ConvertBgra8888(image),
            PixelFormat.Rgb888 => ConvertRgb888(image),
            PixelFormat.Rgb565 => ConvertRgb565(image, dither),
            PixelFormat.Rgba4444 => ConvertRgba4444(image, dither),
            PixelFormat.R8 => ConvertR8(image),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
        };
    }

    public static bool HasTransparency(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var pixels = image.Pixels;

        for (var index = 3; index < pixels.Length; index += RgbaImage.BytesPerPixel)
        {
            if (pixels[index] != 255)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ConvertBgra8888(RgbaImage image)
    {
        var source = image.Pixels;
        var result = new byte[source.Length];

        for (var index = 0; index < source.Length; index += RgbaImage.BytesPerPixel)
        {
            result[index] = source[index + 2];
            result[index + 1] = source[index + 1];
            result[index + 2] = source[index];
            result[index + 3] = source[index + 3];
        }

        return result;
    }

    private static byte[] ConvertRgb888(RgbaImage image)
    {
        var source = image.Pixels;
        var result = new byte[image.Width * image.Height * 3];
        var target = 0;

        for (var index = 0; index < source.Length; index += RgbaImage.BytesPerPixel)
        {
            result[target++] = source[index];
            result[target++] = source[index + 1];
            result[target++] = source[index + 2];
        }

        return result;
    }

    private static byte[] ConvertRgb565(RgbaImage image, DitherMode dither)
    {
        var source = image.Pixels;
        var result = new byte[image.Width * image.Height * 2];
        var matrixCount = MatrixCount(dither);
        var target = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var sourceIndex = (y * image.Width + x) * RgbaImage.BytesPerPixel;
                var threshold = Threshold(dither, x, y);
                var red = Quantize(source[sourceIndex], 5, threshold, matrixCount);
                var green = Quantize(source[sourceIndex + 1], 6, threshold, matrixCount);
                var blue = Quantize(source[sourceIndex + 2], 5, threshold, matrixCount);
                var packed = (ushort)((red << 11) | (green << 5) | blue);
                result[target++] = (byte)packed;
                result[target++] = (byte)(packed >> 8);
            }
        }

        return result;
    }

    private static byte[] ConvertRgba4444(RgbaImage image, DitherMode dither)
    {
        var source = image.Pixels;
        var result = new byte[image.Width * image.Height * 2];
        var matrixCount = MatrixCount(dither);
        var target = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var sourceIndex = (y * image.Width + x) * RgbaImage.BytesPerPixel;
                var threshold = Threshold(dither, x, y);
                var alphaThreshold = dither == DitherMode.None
                    ? 0
                    : (threshold + matrixCount / 2) % matrixCount;
                var red = Quantize(source[sourceIndex], 4, threshold, matrixCount);
                var green = Quantize(source[sourceIndex + 1], 4, threshold, matrixCount);
                var blue = Quantize(source[sourceIndex + 2], 4, threshold, matrixCount);
                var alpha = Quantize(source[sourceIndex + 3], 4, alphaThreshold, matrixCount);
                var packed = (ushort)((red << 12) | (green << 8) | (blue << 4) | alpha);
                result[target++] = (byte)packed;
                result[target++] = (byte)(packed >> 8);
            }
        }

        return result;
    }

    private static byte[] ConvertR8(RgbaImage image)
    {
        var source = image.Pixels;
        var result = new byte[image.Width * image.Height];
        var target = 0;

        for (var index = 0; index < source.Length; index += RgbaImage.BytesPerPixel)
        {
            result[target++] = (byte)(
                (77 * source[index] + 150 * source[index + 1] + 29 * source[index + 2] + 128) >> 8);
        }

        return result;
    }

    private static int MatrixCount(DitherMode dither) => dither switch
    {
        DitherMode.None => 0,
        DitherMode.Ordered4 => 16,
        DitherMode.Ordered8 => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(dither), dither, "Unknown dither mode.")
    };

    private static int Threshold(DitherMode dither, int x, int y) => dither switch
    {
        DitherMode.None => 0,
        DitherMode.Ordered4 => Bayer4[((y & 3) << 2) | (x & 3)],
        DitherMode.Ordered8 => Bayer8[((y & 7) << 3) | (x & 7)],
        _ => throw new ArgumentOutOfRangeException(nameof(dither), dither, "Unknown dither mode.")
    };

    private static int Quantize(byte value, int bits, int threshold, int matrixCount)
    {
        var levels = (1 << bits) - 1;

        if (matrixCount == 0)
        {
            return (value * levels + 127) / 255;
        }

        var bias = ((threshold * 2 - (matrixCount - 1)) * 255) / (2 * matrixCount * levels);
        var adjusted = Math.Clamp(value + bias, 0, 255);
        return (adjusted * levels + 127) / 255;
    }
}
