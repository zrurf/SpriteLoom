using SpriteLoom.Foundation;

namespace SpriteLoom.Textures;

public readonly record struct DfdSample(int Channel, int Bits, int Offset, bool Linear = false);

public sealed record TextureFormat(
    string Id,
    string Codec,
    uint VkFormat,
    int BlockWidth,
    int BlockHeight,
    int BytesPerBlock,
    uint DfdModel,
    IReadOnlyList<DfdSample> Samples,
    bool Srgb,
    PixelFormat? PixelFormat = null,
    bool Basis = false)
{
    public bool IsRaw => PixelFormat is not null;

    public string Extension => "ktx2";

    public int ExpectedBytes(int width, int height)
    {
        if (IsRaw)
        {
            return width * height * BytesPerBlock;
        }

        var blocksX = (width + BlockWidth - 1) / BlockWidth;
        var blocksY = (height + BlockHeight - 1) / BlockHeight;
        return blocksX * blocksY * BytesPerBlock;
    }
}
