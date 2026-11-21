using SpriteLoom.Abstractions;

namespace SpriteLoom.Textures;

public enum TextureQuality
{
    Fast,
    Balanced,
    High
}

public static class TextureQualities
{
    public static TextureQuality Parse(string name) => name switch
    {
        "fast" => TextureQuality.Fast,
        "balanced" => TextureQuality.Balanced,
        "high" => TextureQuality.High,
        _ => throw new ArgumentException($"Unknown texture quality: {name}", nameof(name))
    };

    public static string GetName(TextureQuality quality) => quality switch
    {
        TextureQuality.Fast => "fast",
        TextureQuality.Balanced => "balanced",
        TextureQuality.High => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, "Unknown texture quality.")
    };
}

public sealed record TexturePageVariant(
    string Id,
    string Codec,
    string File,
    string PixelFormat,
    byte[] Data);

public sealed record TexturePage(
    int Index,
    PageLayout Layout,
    IReadOnlyList<TexturePageVariant> Variants);

public sealed record TexturePageSet(IReadOnlyList<TexturePage> Pages);
