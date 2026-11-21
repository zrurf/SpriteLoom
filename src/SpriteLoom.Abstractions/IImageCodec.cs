using SpriteLoom.Foundation;

namespace SpriteLoom.Abstractions;

public interface IImageCodec
{
    string Id { get; }

    IReadOnlyCollection<string> Extensions { get; }

    bool CanDecode(string path);

    RgbaImage Decode(ReadOnlySpan<byte> data);

    IReadOnlyCollection<string> EncodableFormats { get; }

    bool CanEncode(string format);

    byte[] Encode(RgbaImage image, string format, int quality);

    byte[] EncodePng(RgbaImage image);
}
