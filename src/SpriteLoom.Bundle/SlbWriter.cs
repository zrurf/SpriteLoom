using System.Buffers.Binary;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public static class SlbWriter
{
    public static byte[] Write(IReadOnlyList<SlbFile> files, SlbWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        options ??= new SlbWriteOptions();

        var mask = options.Checksums == HashAlgorithmKind.None
            ? HashAlgorithmKind.XxHash64
            : options.Checksums;
        var signer = options.Signer;

        var ordered = files
            .OrderBy(file => Priority(file.Kind))
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

        var payloadCount = ordered.Length;
        var signatureBlockId = (uint)payloadCount;
        var blockCount = payloadCount + (signer is null ? 0 : 1);
        var signaturePath = signer is null ? null : $"signatures/{signer.KeyId}.sig.cbor";

        var indexEntries = ordered
            .Select((file, index) => new SlbIndexEntry(file.Path, (uint)index, file.Kind))
            .ToList();

        if (signaturePath is not null)
        {
            indexEntries.Add(new SlbIndexEntry(signaturePath, signatureBlockId, SlbBlockKind.Signature));
        }

        var indexBytes = SlbIndex.Write(indexEntries);
        var digestBytes = SlbChecksums.DigestBytes(mask);
        var digestRegionOffset = SlbChecksums.Align8(64 + 64 * blockCount);
        var indexOffset = SlbChecksums.Align8(digestRegionOffset + digestBytes * blockCount);
        var payloadStart = SlbChecksums.Align64(indexOffset + indexBytes.Length);

        var offsets = new ulong[payloadCount];
        var cursor = payloadStart;

        for (var index = 0; index < payloadCount; index++)
        {
            offsets[index] = (ulong)cursor;
            cursor = SlbChecksums.Align64(checked(cursor + ordered[index].Content.Length));
        }

        var signatureOffset = cursor;
        byte[] signatureBytes = [];
        byte[] merkleRoot = [];

        if (signer is not null)
        {
            var payloadTableBytes = new byte[64 * payloadCount];

            for (var index = 0; index < payloadCount; index++)
            {
                BuildEntry(ordered[index], (uint)index, offsets[index], digestBytes, mask, signer is not null)
                    .Write(payloadTableBytes.AsSpan(index * 64, 64));
            }

            merkleRoot = SlbMerkle.Compute(ordered.Select(file => file.Content).ToArray());
            var blockTableDigest = HashAlgorithms.Compute(HashAlgorithmKind.Blake3, payloadTableBytes);
            var indexDigest = HashAlgorithms.Compute(HashAlgorithmKind.Blake3, indexBytes);

            var document = new SignatureDocument
            {
                Layer = "bundle",
                MerkleRoot = merkleRoot,
                BlockTableDigest = new DigestValue("blake3", blockTableDigest),
                IndexDigest = new DigestValue("blake3", indexDigest),
                Signers = [new SignerEntry(signer!.KeyId, signer.Algorithm, signer.PublicKey, [])]
            };

            var signature = signer!.Sign(document.Encode(includeSignatures: false));
            signatureBytes = document.WithSignature(signature).Encode(includeSignatures: true);
            cursor = SlbChecksums.Align64(checked(signatureOffset + signatureBytes.Length));
        }

        var contentEnd = cursor;
        var fileSize = contentEnd + (options.WriteFooter ? SlbFormat.FooterSize : 0);
        var buffer = new byte[fileSize];

        for (var index = 0; index < payloadCount; index++)
        {
            var file = ordered[index];
            var entry = BuildEntry(file, (uint)index, offsets[index], digestBytes, mask, signer is not null);
            entry.Write(buffer.AsSpan(64 + index * SlbFormat.BlockEntrySize, SlbFormat.BlockEntrySize));
            file.Content.CopyTo(buffer.AsSpan((int)offsets[index]));
            SlbChecksums.WriteDigests(
                buffer.AsSpan(digestRegionOffset + index * digestBytes, digestBytes),
                mask,
                file.Content);
        }

        if (signer is not null)
        {
            var entry = new SlbBlockEntry
            {
                Offset = (ulong)signatureOffset,
                StoredSize = (ulong)signatureBytes.Length,
                RawSize = (ulong)signatureBytes.Length,
                BlockId = signatureBlockId,
                Kind = SlbBlockKind.Signature,
                Codec = SlbCodec.None,
                Flags = SlbBlockFlags.None,
                ChecksumAlgorithms = mask,
                DigestOffset = (uint)(payloadCount * digestBytes)
            };

            entry.Write(buffer.AsSpan(64 + payloadCount * SlbFormat.BlockEntrySize, SlbFormat.BlockEntrySize));
            signatureBytes.CopyTo(buffer.AsSpan(signatureOffset));
            SlbChecksums.WriteDigests(
                buffer.AsSpan(digestRegionOffset + payloadCount * digestBytes, digestBytes),
                mask,
                signatureBytes);
        }

        indexBytes.CopyTo(buffer.AsSpan(indexOffset));

        if (options.WriteFooter)
        {
            new SlbFooter
            {
                IndexOffset = (ulong)indexOffset,
                IndexSize = (uint)indexBytes.Length
            }.Write(buffer.AsSpan(contentEnd, SlbFormat.FooterSize));
        }

        var flags = SlbHeaderFlags.SequentialOrder;

        if (signer is not null)
        {
            flags |= SlbHeaderFlags.Signed;
        }

        if (options.WriteFooter)
        {
            flags |= SlbHeaderFlags.HasFooter;
        }

        var header = new SlbHeader
        {
            BlockCount = (uint)blockCount,
            Flags = flags,
            FileSize = (ulong)fileSize,
            IndexOffset = (ulong)indexOffset,
            IndexSize = (uint)indexBytes.Length,
            IndexCrc32 = SlbChecksums.ComputeCrc32(indexBytes)
        };

        header.Write(buffer.AsSpan(0, SlbFormat.HeaderSize));
        var headerCrc = SlbChecksums.ComputeHeaderCrc(buffer.AsSpan(0, SlbFormat.HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), headerCrc);

        return buffer;
    }

    public static void WriteFile(IReadOnlyList<SlbFile> files, string path, SlbWriteOptions? options = null)
    {
        var bytes = Write(files, options);
        File.WriteAllBytes(path, bytes);
    }

    private static SlbBlockEntry BuildEntry(
        SlbFile file,
        uint blockId,
        ulong offset,
        int digestBytes,
        HashAlgorithmKind mask,
        bool signed) =>
        new()
        {
            Offset = offset,
            StoredSize = (ulong)file.Content.Length,
            RawSize = (ulong)file.Content.Length,
            BlockId = blockId,
            Kind = file.Kind,
            Codec = SlbCodec.None,
            Flags = (signed ? SlbBlockFlags.SignedLeaf : SlbBlockFlags.None) |
                    (file.Kind is SlbBlockKind.Manifest or SlbBlockKind.Container or SlbBlockKind.Image
                        ? SlbBlockFlags.Critical
                        : SlbBlockFlags.None),
            ChecksumAlgorithms = mask,
            DigestOffset = (uint)(blockId * digestBytes)
        };

    private static int Priority(SlbBlockKind kind) => kind switch
    {
        SlbBlockKind.Manifest => 0,
        SlbBlockKind.Container => 1,
        SlbBlockKind.Animation => 2,
        SlbBlockKind.Image => 3,
        SlbBlockKind.Mesh => 4,
        SlbBlockKind.Blob => 5,
        SlbBlockKind.Signature => 6,
        _ => 99
    };
}
