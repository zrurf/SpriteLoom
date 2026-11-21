using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging;

public sealed class PageImage
{
    public required int Index { get; init; }

    public required RgbaImage Pixels { get; init; }

    public required PageLayout Layout { get; init; }

    public byte[]? EncodedPng { get; set; }

    public List<EncodedPageVariant> Variants { get; } = [];
}
