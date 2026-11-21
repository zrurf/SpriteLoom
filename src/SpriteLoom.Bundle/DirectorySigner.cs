using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public static class DirectorySigner
{
    public static string Sign(string root, ISigner signer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(signer);

        var files = DirectoryFiles.Enumerate(root)
            .Select(relativePath => CreateEntry(root, relativePath))
            .ToArray();

        var document = new SignatureDocument
        {
            Layer = "file",
            Files = files,
            Signers = [new SignerEntry(signer.KeyId, signer.Algorithm, signer.PublicKey, [])]
        };

        var signature = signer.Sign(document.Encode(includeSignatures: false));
        var bytes = document.WithSignature(signature).Encode(includeSignatures: true);
        var path = Path.Combine(root, "signatures", $"{signer.KeyId}.sig.cbor");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    private static SignedFileEntry CreateEntry(string root, string relativePath)
    {
        var bytes = File.ReadAllBytes(Path.Combine(root, relativePath));
        return new SignedFileEntry(
            relativePath,
            bytes.Length,
            new DigestValue("sha256", HashAlgorithms.Compute(HashAlgorithmKind.Sha256, bytes)));
    }
}
