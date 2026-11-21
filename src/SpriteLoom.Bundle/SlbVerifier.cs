using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public static class SlbVerifier
{
    public static IReadOnlyList<string> Verify(SlbArchive archive, IVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(verifier);

        var errors = new List<string>();
        var signatureEntry = archive.Blocks.FirstOrDefault(entry => entry.Kind == SlbBlockKind.Signature);

        if (signatureEntry is null)
        {
            errors.Add($"{BundleDiagnostics.MissingSignature}: SLB does not contain a signature block.");
            return errors;
        }

        var signatureBytes = archive.RawBytes
            .AsSpan((int)signatureEntry.Offset, (int)signatureEntry.StoredSize)
            .ToArray();
        var document = SignatureDocument.Decode(signatureBytes);
        var signer = document.Signers.FirstOrDefault(entry =>
            entry.KeyId == verifier.KeyId &&
            entry.Algorithm == verifier.Algorithm);

        if (signer is null)
        {
            errors.Add($"{BundleDiagnostics.MissingSigner}: Signature document does not contain the requested key.");
            return errors;
        }

        if (!verifier.Verify(document.Encode(includeSignatures: false), signer.Signature))
        {
            errors.Add($"{BundleDiagnostics.SignatureInvalid}: Signature verification failed.");
        }

        var payloadEntries = archive.Blocks
            .Where(entry => entry.Kind != SlbBlockKind.Signature)
            .OrderBy(entry => entry.BlockId)
            .ToArray();

        var payloadTableBytes = new byte[SlbFormat.BlockEntrySize * payloadEntries.Length];

        for (var index = 0; index < payloadEntries.Length; index++)
        {
            Buffer.BlockCopy(
                archive.BlockTableBytes,
                index * SlbFormat.BlockEntrySize,
                payloadTableBytes,
                index * SlbFormat.BlockEntrySize,
                SlbFormat.BlockEntrySize);
        }

        var blockTableDigest = HashAlgorithms.Compute(HashAlgorithmKind.Blake3, payloadTableBytes);

        if (document.BlockTableDigest is not { } expectedTable ||
            !expectedTable.Value.AsSpan().SequenceEqual(blockTableDigest))
        {
            errors.Add($"{BundleDiagnostics.BlockTableDigestMismatch}: Block table digest mismatch.");
        }

        var indexDigest = HashAlgorithms.Compute(HashAlgorithmKind.Blake3, archive.IndexBytes);

        if (document.IndexDigest is not { } expectedIndex ||
            !expectedIndex.Value.AsSpan().SequenceEqual(indexDigest))
        {
            errors.Add($"{BundleDiagnostics.IndexDigestMismatch}: Index digest mismatch.");
        }

        var contents = payloadEntries
            .Select(entry => archive.RawBytes
                .AsSpan((int)entry.Offset, (int)entry.StoredSize)
                .ToArray())
            .ToArray();
        var merkleRoot = SlbMerkle.Compute(contents);

        if (document.MerkleRoot is null ||
            !document.MerkleRoot.AsSpan().SequenceEqual(merkleRoot))
        {
            errors.Add($"{BundleDiagnostics.MerkleRootMismatch}: Merkle root mismatch.");
        }

        return errors;
    }
}
