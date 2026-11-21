using ImageMagick;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Codecs;

public sealed class MagickImageCodec : IImageCodec
{
    private static readonly string[] SupportedExtensions = [".png", ".webp", ".jxl", ".avif"];
    private static readonly Lazy<IReadOnlyDictionary<string, MagickFormat>> AvailableFormats =
        new(FindAvailableFormats);

    public string Id => "magick";

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public IReadOnlyCollection<string> EncodableFormats => AvailableFormats.Value.Keys.ToArray();

    public bool CanDecode(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public bool CanEncode(string format) =>
        AvailableFormats.Value.ContainsKey(format);

    public RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            using var image = new MagickImage(data.ToArray());
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;
            var width = checked((int)image.Width);
            var height = checked((int)image.Height);
            var pixels = image.ToByteArray(MagickFormat.Rgba);

            if (pixels.Length != width * height * RgbaImage.BytesPerPixel)
            {
                using var collection = image.GetPixels();
                pixels = collection.ToByteArray(PixelMapping.RGBA)
                    ?? throw new InvalidDataException("Image pixels could not be read.");
            }

            return new RgbaImage(width, height, pixels);
        }
        catch (MagickException exception)
        {
            throw new InvalidDataException($"Image data could not be decoded: {exception.Message}", exception);
        }
    }

    public byte[] Encode(RgbaImage image, string format, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!AvailableFormats.Value.TryGetValue(format, out var magickFormat))
        {
            throw new ArgumentException($"ImageMagick does not support writing {format}.", nameof(format));
        }

        try
        {
            var settings = new MagickReadSettings
            {
                Width = (uint)image.Width,
                Height = (uint)image.Height,
                Format = MagickFormat.Rgba,
                Depth = 8,
                ColorSpace = ColorSpace.sRGB
            };

            using var magick = new MagickImage(image.Pixels.ToArray(), settings);
            magick.Format = magickFormat;
            magick.Quality = (uint)Math.Clamp(quality, 1, 100);
            magick.Strip();
            return magick.ToByteArray();
        }
        catch (MagickException exception)
        {
            throw new InvalidDataException($"Image could not be encoded as {format}: {exception.Message}", exception);
        }
    }

    public byte[] EncodePng(RgbaImage image) => Encode(image, "png", 100);

    private static IReadOnlyDictionary<string, MagickFormat> FindAvailableFormats()
    {
        var candidates = new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = MagickFormat.Png,
            ["webp"] = MagickFormat.WebP,
            ["jxl"] = MagickFormat.Jxl,
            ["avif"] = MagickFormat.Avif
        };
        var available = new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, format) in candidates)
        {
            try
            {
                if (MagickFormatInfo.Create(format)?.SupportsWriting == true)
                {
                    available[name] = format;
                }
            }
            catch (Exception)
            {
            }
        }

        return available;
    }
}
