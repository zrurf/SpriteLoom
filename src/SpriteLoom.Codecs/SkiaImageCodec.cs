using System.Runtime.InteropServices;
using SkiaSharp;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Codecs;

public sealed class SkiaImageCodec : IImageCodec
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];
    private static readonly string[] SupportedFormats = ["png", "webp", "jpeg", "bmp"];

    public string Id => "skia";

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public IReadOnlyCollection<string> EncodableFormats => SupportedFormats;

    public bool CanDecode(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public bool CanEncode(string format) =>
        SupportedFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    public RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        using var decoded = SKBitmap.Decode(data.ToArray())
            ?? throw new InvalidDataException("Image data could not be decoded.");
        using var converted = decoded.Copy(SKColorType.Rgba8888)
            ?? throw new InvalidDataException("Image data could not be converted to RGBA8888.");

        var width = converted.Width;
        var height = converted.Height;
        var stride = width * RgbaImage.BytesPerPixel;
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(
                converted.GetPixels() + y * converted.RowBytes,
                pixels,
                y * stride,
                stride);
        }

        return new RgbaImage(width, height, pixels);
    }

    public byte[] Encode(RgbaImage image, string format, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);

        var skFormat = format switch
        {
            "png" => SKEncodedImageFormat.Png,
            "webp" => SKEncodedImageFormat.Webp,
            "jpeg" => SKEncodedImageFormat.Jpeg,
            "bmp" => SKEncodedImageFormat.Bmp,
            _ => throw new ArgumentException($"Unsupported image format: {format}", nameof(format))
        };

        if (skFormat == SKEncodedImageFormat.Jpeg && PixelFormatConverter.HasTransparency(image))
        {
            throw new PixelFormatException("SL3010", "JPEG does not support transparency.");
        }

        var info = new SKImageInfo(
            image.Width,
            image.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);

        using var skImage = SKImage.FromPixelCopy(info, image.Pixels.ToArray());
        using var data = skImage.Encode(skFormat, Math.Clamp(quality, 0, 100))
            ?? throw new InvalidDataException($"Image could not be encoded as {format}.");

        return data.ToArray();
    }

    public byte[] EncodePng(RgbaImage image) => Encode(image, "png", 100);
}
