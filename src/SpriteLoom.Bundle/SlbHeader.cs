using System.Buffers.Binary;

namespace SpriteLoom.Bundle;

public sealed class SlbHeader
{
    public ushort FormatMajor { get; set; } = SlbFormat.FormatMajor;

    public ushort FormatMinor { get; set; } = SlbFormat.FormatMinor;

    public uint HeaderSize { get; set; } = SlbFormat.HeaderSize;

    public uint HeaderCrc32 { get; set; }

    public ulong BlockTableOffset { get; set; } = SlbFormat.HeaderSize;

    public uint BlockCount { get; set; }

    public SlbHeaderFlags Flags { get; set; }

    public ulong FileSize { get; set; }

    public ulong IndexOffset { get; set; }

    public uint IndexSize { get; set; }

    public uint IndexCrc32 { get; set; }

    public ulong Reserved { get; set; }

    public void Write(Span<byte> buffer)
    {
        if (buffer.Length < SlbFormat.HeaderSize)
        {
            throw new ArgumentException("Header buffer is too small.", nameof(buffer));
        }

        SlbFormat.Magic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..], FormatMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[6..], FormatMinor);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[8..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], HeaderCrc32);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[16..], BlockTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], BlockCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], (uint)Flags);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[32..], FileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[40..], IndexOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[48..], IndexSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[52..], IndexCrc32);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[56..], Reserved);
    }

    public static SlbHeader Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < SlbFormat.HeaderSize)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.HeaderTruncated, "SLB header is truncated.");
        }

        if (!buffer[..4].SequenceEqual(SlbFormat.Magic))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.HeaderMagicMismatch, "SLB magic does not match.");
        }

        return new SlbHeader
        {
            FormatMajor = BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]),
            FormatMinor = BinaryPrimitives.ReadUInt16LittleEndian(buffer[6..]),
            HeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]),
            HeaderCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(buffer[12..]),
            BlockTableOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]),
            BlockCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer[24..]),
            Flags = (SlbHeaderFlags)BinaryPrimitives.ReadUInt32LittleEndian(buffer[28..]),
            FileSize = BinaryPrimitives.ReadUInt64LittleEndian(buffer[32..]),
            IndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[40..]),
            IndexSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[48..]),
            IndexCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(buffer[52..]),
            Reserved = BinaryPrimitives.ReadUInt64LittleEndian(buffer[56..])
        };
    }
}
