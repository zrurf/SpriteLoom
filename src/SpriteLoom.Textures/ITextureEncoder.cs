using SpriteLoom.Foundation;
using SpriteLoom.Imaging;

namespace SpriteLoom.Textures;

public interface ITextureEncoder
{
    string Id { get; }

    bool CanEncode(TextureFormat format);

    byte[] EncodeLevel(RgbaImage image, TextureFormat format, TextureQuality quality);

    byte[] EncodeContainer(
        IReadOnlyList<RgbaImage> levels,
        TextureFormat format,
        TextureQuality quality)
    {
        var ktxLevels = new Ktx2Level[levels.Count];

        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];
            ktxLevels[index] = new Ktx2Level(
                level.Width,
                level.Height,
                EncodeLevel(level, format, quality));
        }

        return Ktx2Writer.Write(
            format,
            levels[0].Width,
            levels[0].Height,
            ktxLevels);
    }
}

public sealed class TextureEncoderRegistry
{
    private readonly IReadOnlyList<ITextureEncoder> _encoders;

    public TextureEncoderRegistry(IReadOnlyList<ITextureEncoder> encoders)
    {
        _encoders = encoders;
    }

    public static TextureEncoderRegistry Empty { get; } = new([]);

    public ITextureEncoder? Find(TextureFormat format) =>
        _encoders.FirstOrDefault(candidate => candidate.CanEncode(format));
}
