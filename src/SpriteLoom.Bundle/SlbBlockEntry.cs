using System.Buffers.Binary;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public sealed class SlbBlockEntry
{
    public ulong Offset { get; set; }

    public ulong StoredSize { get; set; }

    public ulong RawSize { get; set; }

    public uint BlockId { get; set; }

    public SlbBlockKind Kind { get; set; }

    public SlbCodec Codec { get; set; }

    public SlbBlockFlags Flags { get; set; }

    public uint ChunkCount { get; set; }

    public ushort KeyId { get; set; }

    public HashAlgorithmKind ChecksumAlgorithms { get; set; }

    public uint DigestOffset { get; set; }

    public void Write(Span<byte> buffer)
    {
        if (buffer.Length < SlbFormat.BlockEntrySize)
        {
            throw new ArgumentException("Block entry buffer is too small.", nameof(buffer));
        }

        buffer[..SlbFormat.BlockEntrySize].Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, Offset);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[8..], StoredSize);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[16..], RawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], BlockId);
        buffer[28] = (byte)Kind;
        buffer[29] = (byte)Codec;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[30..], (ushort)Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[32..], ChunkCount);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[36..], KeyId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[38..], (ushort)ChecksumAlgorithms);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[40..], DigestOffset);
    }

    public static SlbBlockEntry Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < SlbFormat.BlockEntrySize)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.BlockEntryTruncated, "SLB block entry is truncated.");
        }

        return new SlbBlockEntry
        {
            Offset = BinaryPrimitives.ReadUInt64LittleEndian(buffer),
            StoredSize = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]),
            RawSize = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]),
            BlockId = BinaryPrimitives.ReadUInt32LittleEndian(buffer[24..]),
            Kind = (SlbBlockKind)buffer[28],
            Codec = (SlbCodec)buffer[29],
            Flags = (SlbBlockFlags)BinaryPrimitives.ReadUInt16LittleEndian(buffer[30..]),
            ChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]),
            KeyId = BinaryPrimitives.ReadUInt16LittleEndian(buffer[36..]),
            ChecksumAlgorithms = (HashAlgorithmKind)BinaryPrimitives.ReadUInt16LittleEndian(buffer[38..]),
            DigestOffset = BinaryPrimitives.ReadUInt32LittleEndian(buffer[40..])
        };
    }
}
