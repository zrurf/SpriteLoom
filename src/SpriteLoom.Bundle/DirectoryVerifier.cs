using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public static class DirectoryVerifier
{
    public static VerificationResult Verify(
        string root,
        IVerifier verifier,
        bool strict = true) =>
        Verify(
            root,
            verifier,
            new VerificationOptions
            {
                Level = strict ? VerificationLevel.Strict : VerificationLevel.Permissive
            });

    public static VerificationResult Verify(
        string root,
        IVerifier verifier,
        VerificationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var warnings = new List<string>();
        var signaturePath = Path.Combine(root, "signatures", $"{verifier.KeyId}.sig.cbor");

        if (!File.Exists(signaturePath))
        {
            errors.Add($"{BundleDiagnostics.SignatureFileMissing}: Signature file not found: {signaturePath}");
            return new VerificationResult(errors, warnings);
        }

        var document = SignatureDocument.Decode(File.ReadAllBytes(signaturePath));
        var signer = document.Signers.FirstOrDefault(entry =>
            entry.KeyId == verifier.KeyId &&
            entry.Algorithm == verifier.Algorithm);

        if (signer is null)
        {
            errors.Add($"{BundleDiagnostics.MissingSigner}: Signature document does not contain the requested key.");
            return new VerificationResult(errors, warnings);
        }

        if (!verifier.Verify(document.Encode(includeSignatures: false), signer.Signature))
        {
            errors.Add($"{BundleDiagnostics.SignatureInvalid}: Signature verification failed.");
        }

        var expected = document.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var actual = DirectoryFiles.Enumerate(root).ToHashSet(StringComparer.Ordinal);

        foreach (var expectedFile in document.Files)
        {
            if (!actual.Contains(expectedFile.Path))
            {
                errors.Add($"{BundleDiagnostics.MissingSignedFile}: Missing signed file: {expectedFile.Path}");
                continue;
            }

            var bytes = File.ReadAllBytes(Path.Combine(root, expectedFile.Path));

            if (bytes.Length != expectedFile.Size)
            {
                errors.Add($"{BundleDiagnostics.SignedFileSizeMismatch}: Signed file size mismatch: {expectedFile.Path}");
                continue;
            }

            var digest = HashAlgorithms.Compute(HashAlgorithmKind.Sha256, bytes);

            if (!digest.AsSpan().SequenceEqual(expectedFile.Digest.Value))
            {
                errors.Add($"{BundleDiagnostics.SignedFileDigestMismatch}: Signed file digest mismatch: {expectedFile.Path}");
            }
        }

        foreach (var path in actual)
        {
            if (expected.ContainsKey(path) || options.IsAllowed(path))
            {
                continue;
            }

            if (options.Level == VerificationLevel.Strict)
            {
                errors.Add($"{BundleDiagnostics.UnsignedFilePresent}: Unsigned file present: {path}");
            }
            else
            {
                warnings.Add($"{BundleDiagnostics.UnsignedFilePresent}: Unsigned file present: {path}");
            }
        }

        return new VerificationResult(errors, warnings);
    }
}
