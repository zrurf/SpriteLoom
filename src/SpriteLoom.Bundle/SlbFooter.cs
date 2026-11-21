using System.Buffers.Binary;

namespace SpriteLoom.Bundle;

public sealed class SlbFooter
{
    public const uint FooterMagic = 0x46424C53;

    public required ulong IndexOffset { get; init; }

    public required ulong IndexSize { get; init; }

    public void Write(Span<byte> buffer)
    {
        if (buffer.Length < SlbFormat.FooterSize)
        {
            throw new ArgumentException("Footer buffer is too small.", nameof(buffer));
        }

        buffer[..SlbFormat.FooterSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, FooterMagic);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[4..], SlbFormat.FormatMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[6..], SlbFormat.FormatMinor);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..], IndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[16..], IndexSize);
        BinaryPrimitives.WriteUInt32LittleEndian(
            buffer[60..],
            SlbChecksums.ComputeCrc32(buffer[..60]));
    }

    public static SlbFooter Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < SlbFormat.FooterSize)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FooterTruncated, "SLB footer is truncated.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer) != FooterMagic)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FooterMagicMismatch, "SLB footer magic is invalid.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[60..]) !=
            SlbChecksums.ComputeCrc32(buffer[..60]))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FooterCrcMismatch, "SLB footer CRC check failed.");
        }

        return new SlbFooter
        {
            IndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]),
            IndexSize = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..])
        };
    }
}
