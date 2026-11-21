using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Codecs;

public sealed class CompositeImageCodec : IImageCodec
{
    private readonly IReadOnlyList<IImageCodec> _codecs;

    public CompositeImageCodec(params IImageCodec[] codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);

        if (codecs.Length == 0)
        {
            throw new ArgumentException("At least one codec is required.", nameof(codecs));
        }

        _codecs = codecs;
        Extensions = codecs
            .SelectMany(codec => codec.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        EncodableFormats = codecs
            .SelectMany(codec => codec.EncodableFormats)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string Id => "composite";

    public IReadOnlyCollection<string> Extensions { get; }

    public IReadOnlyCollection<string> EncodableFormats { get; }

    public bool CanDecode(string path) =>
        _codecs.Any(codec => codec.CanDecode(path));

    public bool CanEncode(string format) =>
        _codecs.Any(codec => codec.CanEncode(format));

    public RgbaImage Decode(ReadOnlySpan<byte> data)
    {
        Exception? last = null;

        foreach (var codec in _codecs)
        {
            try
            {
                return codec.Decode(data);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
            }
        }

        throw new InvalidDataException("Image data could not be decoded by any codec.", last);
    }

    public byte[] Encode(RgbaImage image, string format, int quality)
    {
        Exception? last = null;

        foreach (var codec in _codecs)
        {
            if (!codec.CanEncode(format))
            {
                continue;
            }

            try
            {
                return codec.Encode(image, format, quality);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
            }
        }

        throw new InvalidDataException($"No codec could encode {format}.", last);
    }

    public byte[] EncodePng(RgbaImage image) => Encode(image, "png", 100);
}

public static class ImageCodecs
{
    public static IImageCodec CreateDefault() =>
        new CompositeImageCodec(new SkiaImageCodec(), new MagickImageCodec());
}
