using SpriteLoom.Foundation;

namespace SpriteLoom.Textures;

public static class TextureFormats
{
    private static readonly TextureFormat[] Formats =
    [
        Raw("ktx2-rgba8888", 43, PixelFormat.Rgba8888,
        [
            new DfdSample(0, 8, 0),
            new DfdSample(1, 8, 8),
            new DfdSample(2, 8, 16),
            new DfdSample(15, 8, 24)
        ]),
        Raw("ktx2-bgra8888", 50, PixelFormat.Bgra8888,
        [
            new DfdSample(2, 8, 0),
            new DfdSample(1, 8, 8),
            new DfdSample(0, 8, 16),
            new DfdSample(15, 8, 24)
        ]),
        Raw("ktx2-rgb888", 29, PixelFormat.Rgb888,
        [
            new DfdSample(0, 8, 0),
            new DfdSample(1, 8, 8),
            new DfdSample(2, 8, 16)
        ]),
        Raw("ktx2-rgb565", 4, PixelFormat.Rgb565,
        [
            new DfdSample(2, 5, 0),
            new DfdSample(1, 6, 5),
            new DfdSample(0, 5, 11)
        ], srgb: false),
        Raw("ktx2-rgba4444", 2, PixelFormat.Rgba4444,
        [
            new DfdSample(15, 4, 0),
            new DfdSample(2, 4, 4),
            new DfdSample(1, 4, 8),
            new DfdSample(0, 4, 12)
        ], srgb: false),
        Raw("ktx2-r8", 15, PixelFormat.R8,
        [
            new DfdSample(0, 8, 0)
        ]),
        Compressed("ktx2-bc1", "bc1", 133, 134, 4, 4, 8, 128,
            [new DfdSample(0, 64, 0)]),
        Compressed("ktx2-bc3", "bc3", 135, 136, 4, 4, 16, 130,
            [new DfdSample(15, 64, 0), new DfdSample(0, 64, 0)]),
        Compressed("ktx2-bc4", "bc4", 139, 139, 4, 4, 8, 131,
            [new DfdSample(0, 64, 0)]),
        Compressed("ktx2-bc5", "bc5", 141, 141, 4, 4, 16, 132,
            [new DfdSample(0, 64, 0), new DfdSample(1, 64, 0)]),
        Compressed("ktx2-bc7", "bc7", 145, 146, 4, 4, 16, 134,
            [new DfdSample(0, 128, 0)]),
        Compressed("ktx2-etc2-rgba", "etc2", 151, 152, 4, 4, 16, 161,
            [new DfdSample(15, 64, 0), new DfdSample(2, 64, 0)]),
        Compressed("ktx2-astc4x4", "astc", 157, 158, 4, 4, 16, 162,
            [new DfdSample(0, 128, 0)]),
        Compressed("ktx2-astc6x6", "astc", 165, 166, 6, 6, 16, 162,
            [new DfdSample(0, 128, 0)]),
        Compressed("ktx2-astc8x8", "astc", 171, 172, 8, 8, 16, 162,
            [new DfdSample(0, 128, 0)]),
        Compressed("ktx2-astc12x12", "astc", 183, 184, 12, 12, 16, 162,
            [new DfdSample(0, 128, 0)]),
        new TextureFormat("ktx2-basis-uastc", "basis-uastc", 0, 4, 4, 16, 166,
            [new DfdSample(0, 128, 0)], Srgb: true, Basis: true),
        new TextureFormat("ktx2-basis-etc1s", "basis-etc1s", 0, 4, 4, 8, 163,
            [new DfdSample(0, 64, 0)], Srgb: true, Basis: true)
    ];

    private static readonly Dictionary<string, TextureFormat> ById =
        Formats.ToDictionary(format => format.Id, StringComparer.Ordinal);

    public static IReadOnlyList<TextureFormat> All => Formats;

    public static TextureFormat? Find(string id) =>
        ById.TryGetValue(id, out var format) ? format : null;

    public static TextureFormat Get(string id) =>
        Find(id) ?? throw new ArgumentException($"Unknown texture target: {id}", nameof(id));

    public static int RequiredAlignment(IEnumerable<string> targets)
    {
        var alignment = 4;

        foreach (var target in targets)
        {
            var format = Get(target);
            alignment = Lcm(alignment, format.BlockWidth);
            alignment = Lcm(alignment, format.BlockHeight);
        }

        return alignment;
    }

    private static int Lcm(int left, int right) =>
        left / Gcd(left, right) * right;

    private static int Gcd(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }

    private static TextureFormat Raw(
        string id,
        uint vkFormat,
        PixelFormat pixelFormat,
        IReadOnlyList<DfdSample> samples,
        bool srgb = true) =>
        new(id, "ktx2", vkFormat, 1, 1, PixelFormats.GetBytesPerPixel(pixelFormat), 1,
            samples, srgb, PixelFormat: pixelFormat);

    private static TextureFormat Compressed(
        string id,
        string codec,
        uint vkFormat,
        uint vkFormatSrgb,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock,
        uint dfdModel,
        IReadOnlyList<DfdSample> samples) =>
        new(id, codec, vkFormatSrgb, blockWidth, blockHeight, bytesPerBlock,
            dfdModel, samples, Srgb: true);
}
