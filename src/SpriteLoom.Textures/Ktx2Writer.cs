using System.Buffers.Binary;
using System.Text;
using SpriteLoom.Foundation;

namespace SpriteLoom.Textures;

public static class Ktx2Writer
{
    private static ReadOnlySpan<byte> Identifier =>
        [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int HeaderOffset = 12;
    private const int HeaderSize = 68;
    private const int LevelIndexEntrySize = 24;

    public static byte[] WriteRaw(RgbaImage image, TextureFormat format, DitherMode dither = DitherMode.None)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(format);

        if (format.PixelFormat is not { } pixelFormat)
        {
            throw new ArgumentException("Format is not a raw texture format.", nameof(format));
        }

        var data = PixelFormatConverter.Convert(image, pixelFormat, dither);
        return Write(format, image.Width, image.Height,
            [new Ktx2Level(image.Width, image.Height, data)]);
    }

    public static byte[] Write(
        TextureFormat format,
        int width,
        int height,
        IReadOnlyList<Ktx2Level> levels,
        IReadOnlyDictionary<string, byte[]>? keyValues = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            throw new Ktx2Exception("SL3031", "KTX2 requires at least one mip level.");
        }

        ValidateLevels(format, width, height, levels);

        var keyValueBytes = WriteKeyValues(keyValues);
        var dfdBytes = WriteDfd(format);
        var levelIndexOffset = HeaderOffset + HeaderSize;
        var dfdOffset = Align4(levelIndexOffset + levels.Count * LevelIndexEntrySize);
        var kvdOffset = keyValueBytes.Length == 0 ? 0 : Align4(dfdOffset + dfdBytes.Length);
        var dataOffset = Align8(kvdOffset == 0
            ? dfdOffset + dfdBytes.Length
            : kvdOffset + keyValueBytes.Length);

        var offsets = new ulong[levels.Count];
        var lengths = new ulong[levels.Count];
        var cursor = dataOffset;

        for (var index = levels.Count - 1; index >= 0; index--)
        {
            cursor = Align8(cursor);
            offsets[index] = (ulong)cursor;
            lengths[index] = (ulong)levels[index].Data.Length;
            cursor += levels[index].Data.Length;
        }

        var totalSize = cursor;
        var output = new byte[totalSize];
        Identifier.CopyTo(output);
        WriteHeader(output.AsSpan(HeaderOffset, HeaderSize), format, width, height, levels.Count,
            dfdOffset, dfdBytes.Length, kvdOffset, keyValueBytes.Length);
        WriteLevelIndex(output.AsSpan(levelIndexOffset), levels, offsets, lengths);
        dfdBytes.CopyTo(output.AsSpan(dfdOffset));

        if (keyValueBytes.Length > 0)
        {
            keyValueBytes.CopyTo(output.AsSpan(kvdOffset));
        }

        for (var index = 0; index < levels.Count; index++)
        {
            levels[index].Data.CopyTo(output.AsSpan((int)offsets[index]));
        }

        return output;
    }

    private static void ValidateLevels(
        TextureFormat format,
        int width,
        int height,
        IReadOnlyList<Ktx2Level> levels)
    {
        var expectedWidth = width;
        var expectedHeight = height;

        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];

            if (level.Width != expectedWidth || level.Height != expectedHeight)
            {
                throw new Ktx2Exception(
                    "SL3031",
                    $"Mip level {index} must be {expectedWidth}x{expectedHeight}, got {level.Width}x{level.Height}.");
            }

            var expectedBytes = format.ExpectedBytes(level.Width, level.Height);

            if (level.Data.Length != expectedBytes)
            {
                throw new Ktx2Exception(
                    "SL3020",
                    $"Mip level {index} requires {expectedBytes} bytes, got {level.Data.Length}.");
            }

            expectedWidth = Math.Max(1, expectedWidth / 2);
            expectedHeight = Math.Max(1, expectedHeight / 2);
        }
    }

    private static void WriteHeader(
        Span<byte> buffer,
        TextureFormat format,
        int width,
        int height,
        int levelCount,
        int dfdOffset,
        int dfdLength,
        int kvdOffset,
        int kvdLength)
    {
        buffer.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, format.VkFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[16..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], (uint)levelCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[32..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[36..], (uint)dfdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[40..], (uint)dfdLength);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[44..], kvdLength == 0 ? 0u : (uint)kvdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[48..], (uint)kvdLength);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[52..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[60..], 0);
    }

    private static void WriteLevelIndex(
        Span<byte> buffer,
        IReadOnlyList<Ktx2Level> levels,
        IReadOnlyList<ulong> offsets,
        IReadOnlyList<ulong> lengths)
    {
        for (var index = 0; index < levels.Count; index++)
        {
            var entry = buffer.Slice(index * LevelIndexEntrySize, LevelIndexEntrySize);
            BinaryPrimitives.WriteUInt64LittleEndian(entry, offsets[index]);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], lengths[index]);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[16..], lengths[index]);
        }
    }

    private static byte[] WriteDfd(TextureFormat format)
    {
        var blockSize = 24 + format.Samples.Count * 16;
        var dfd = new byte[4 + blockSize];
        BinaryPrimitives.WriteUInt32LittleEndian(dfd, (uint)dfd.Length);

        var block = dfd.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(block, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], 2 | ((uint)blockSize << 16));
        BinaryPrimitives.WriteUInt32LittleEndian(
            block[8..],
            format.DfdModel |
            ((uint)1 << 8) |
            ((format.Srgb ? 2u : 1u) << 16));
        BinaryPrimitives.WriteUInt32LittleEndian(
            block[12..],
            (uint)(format.BlockWidth - 1) | ((uint)(format.BlockHeight - 1) << 8));
        BinaryPrimitives.WriteUInt32LittleEndian(block[16..], (uint)format.BytesPerBlock);
        BinaryPrimitives.WriteUInt32LittleEndian(block[20..], 0);

        for (var index = 0; index < format.Samples.Count; index++)
        {
            var sample = format.Samples[index];
            var qualifiers = sample.Linear || (format.Srgb && sample.Channel == 15) ? 0x10u : 0u;
            var upper = format.IsRaw && sample.Bits < 32
                ? (1u << sample.Bits) - 1
                : uint.MaxValue;
            var destination = block.Slice(24 + index * 16, 16);
            BinaryPrimitives.WriteUInt32LittleEndian(
                destination,
                (uint)sample.Offset |
                ((uint)(sample.Bits - 1) << 16) |
                (((uint)sample.Channel | qualifiers) << 24));
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], upper);
        }

        return dfd;
    }

    private static byte[] WriteKeyValues(IReadOnlyDictionary<string, byte[]>? keyValues)
    {
        if (keyValues is null || keyValues.Count == 0)
        {
            return [];
        }

        using var stream = new MemoryStream();

        foreach (var (key, value) in keyValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            WriteUInt32(stream, (uint)keyBytes.Length);
            stream.Write(keyBytes);
            WriteUInt32(stream, (uint)value.Length);
            stream.Write(value);

            while (stream.Length % 4 != 0)
            {
                stream.WriteByte(0);
            }
        }

        return stream.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static int Align8(int value) => (value + 7) & ~7;
}
