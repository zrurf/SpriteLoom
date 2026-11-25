using System.Buffers.Binary;
using System.IO.Hashing;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

internal static class SlbChecksums
{
    public static int Align8(int value) => (value + 7) & ~7;

    public static int Align64(int value) => (value + 63) & ~63;

    public static int DigestBytes(HashAlgorithmKind mask) =>
        HashAlgorithms.Expand(mask).Sum(kind => Align8(HashAlgorithms.GetLength(kind)));

    public static uint ComputeHeaderCrc(ReadOnlySpan<byte> header)
    {
        Span<byte> copy = stackalloc byte[SlbFormat.HeaderSize];
        header[..SlbFormat.HeaderSize].CopyTo(copy);
        copy[12..16].Clear();
        var hash = Crc32.Hash(copy);
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }

    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        var hash = Crc32.Hash(data);
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }

    public static void WriteDigests(
        Span<byte> destination,
        HashAlgorithmKind mask,
        ReadOnlySpan<byte> content)
    {
        var cursor = 0;

        foreach (var kind in HashAlgorithms.Expand(mask))
        {
            var digest = HashAlgorithms.Compute(kind, content);
            digest.CopyTo(destination[cursor..]);
            cursor += Align8(digest.Length);
        }
    }

    public static void VerifyDigests(
        ReadOnlySpan<byte> file,
        int digestRegionOffset,
        SlbBlockEntry entry,
        ReadOnlySpan<byte> content) =>
        VerifyDigests(
            file.Slice(digestRegionOffset + (int)entry.DigestOffset),
            entry,
            content);

    public static void VerifyDigests(
        ReadOnlySpan<byte> digestRegion,
        SlbBlockEntry entry,
        ReadOnlySpan<byte> content)
    {
        var cursor = 0;

        foreach (var kind in HashAlgorithms.Expand(entry.ChecksumAlgorithms))
        {
            var length = HashAlgorithms.GetLength(kind);
            var expected = digestRegion.Slice(cursor, length);
            var actual = HashAlgorithms.Compute(kind, content);

            if (!actual.AsSpan().SequenceEqual(expected))
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.DigestMismatch,
                    $"SLB block {entry.BlockId} failed {HashAlgorithms.GetName(kind)} verification.");
            }

            cursor += Align8(length);
        }
    }
}
