using SpriteLoom.Codecs;
using SpriteLoom.Textures;

namespace SpriteLoom.Hosting;

public sealed record SloomTextureCapability(string Id, string Codec, bool Available)
{
    public string Status => Available ? "available" : "unavailable";
}

public sealed record SloomCapabilities(
    IReadOnlyList<string> ImageFormats,
    bool NativeEncoderAvailable,
    IReadOnlyList<SloomTextureCapability> TextureTargets,
    IReadOnlyList<string> Packers)
{
    public static SloomCapabilities Describe()
    {
        var codecs = ImageCodecs.CreateDefault();
        var native = SpriteLoom.Textures.NativeTextureEncoder.TryCreate();
        var targets = TextureFormats.All
            .Select(format => new SloomTextureCapability(
                format.Id,
                format.Codec,
                format.IsRaw || native?.CanEncode(format) == true))
            .ToArray();

        return new SloomCapabilities(
            codecs.EncodableFormats.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            native is not null,
            targets,
            ["pack.basic", "pack.maxrects"]);
    }
}
