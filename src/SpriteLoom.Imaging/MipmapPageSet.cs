using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Imaging;

public sealed record MipmapPage(
    int Index,
    PageLayout Layout,
    IReadOnlyList<RgbaImage> Levels);

public sealed record MipmapPageSet(IReadOnlyList<MipmapPage> Pages);
