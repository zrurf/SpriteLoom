using System.Buffers.Binary;
using SpriteLoom.Foundation;
using SpriteLoom.Textures;

namespace SpriteLoom.Textures.Tests;

public sealed class Ktx2WriterTests
{
    [Fact]
    public void Raw_ktx2_has_valid_header_and_levels()
    {
        var image = CreateImage(2, 2);
        var format = TextureFormats.Get("ktx2-rgba8888");
        var bytes = Ktx2Writer.WriteRaw(image, format, DitherMode.None);

        Assert.Equal(
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A },
            bytes[..12]);

        var header = bytes.AsSpan(12, 68);
        Assert.Equal(43u, BinaryPrimitives.ReadUInt32LittleEndian(header));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(header[8..]));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(header[12..]));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(header[28..]));

        var dfdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        var dfdLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[40..]);
        var dfdTotal = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dfdOffset));
        Assert.Equal(dfdLength, dfdTotal);
        Assert.Equal(4 + 24 + 4 * 16, dfdLength);

        var levelOffset = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(80));
        var levelLength = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(88));
        Assert.Equal(16, levelLength);
        Assert.Equal(0, levelOffset % 8);
        Assert.True(image.Pixels.SequenceEqual(bytes.AsSpan(levelOffset, levelLength)));
    }

    [Fact]
    public void Compressed_ktx2_writes_dfd_model()
    {
        var format = TextureFormats.Get("ktx2-bc1");
        var bytes = Ktx2Writer.Write(format, 4, 4,
            [new Ktx2Level(4, 4, new byte[8])]);

        var header = bytes.AsSpan(12, 68);
        var dfdOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[36..]);
        var model = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dfdOffset + 4 + 8));
        var bytesPlane = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(dfdOffset + 4 + 16));

        Assert.Equal(128u, model & 0xFF);
        Assert.Equal(8u, bytesPlane & 0xFF);
    }

    [Fact]
    public void Invalid_level_size_is_rejected()
    {
        var format = TextureFormats.Get("ktx2-bc1");

        var exception = Assert.Throws<Ktx2Exception>(() =>
            Ktx2Writer.Write(format, 4, 4, [new Ktx2Level(4, 4, new byte[7])]));

        Assert.Equal("SL3020", exception.Code);
    }

    [Fact]
    public void Invalid_mip_dimensions_are_rejected()
    {
        var format = TextureFormats.Get("ktx2-rgba8888");

        var exception = Assert.Throws<Ktx2Exception>(() =>
            Ktx2Writer.Write(format, 4, 4,
            [
                new Ktx2Level(4, 4, new byte[64]),
                new Ktx2Level(1, 1, new byte[4])
            ]));

        Assert.Equal("SL3031", exception.Code);
    }

    [Fact]
    public void Required_alignment_is_lcm_of_blocks()
    {
        Assert.Equal(4, TextureFormats.RequiredAlignment(["ktx2-astc4x4"]));
        Assert.Equal(12, TextureFormats.RequiredAlignment(["ktx2-astc4x4", "ktx2-astc12x12"]));
        Assert.Equal(24, TextureFormats.RequiredAlignment(["ktx2-astc8x8", "ktx2-astc12x12"]));
    }

    private static RgbaImage CreateImage(int width, int height)
    {
        var image = new RgbaImage(width, height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, (byte)(x * 100), (byte)(y * 100), 40, 255);
            }
        }

        return image;
    }
}
