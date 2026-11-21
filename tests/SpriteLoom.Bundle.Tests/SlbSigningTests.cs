using System.Text;
using SpriteLoom.Bundle;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle.Tests;

public sealed class SlbSigningTests
{
    [Fact]
    public void Signed_bundle_round_trips_and_verifies()
    {
        var keyPair = Ed25519Keys.Generate();
        var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
        var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);
        var bytes = SlbWriter.Write(
            CreateFiles(),
            new SlbWriteOptions { Signer = signer, WriteFooter = true });
        var archive = SlbReader.Read(bytes);

        Assert.True(archive.Header.Flags.HasFlag(SlbHeaderFlags.Signed));
        Assert.Equal(5u, archive.Header.BlockCount);
        Assert.Contains(archive.Blocks, entry => entry.Kind == SlbBlockKind.Signature);
        Assert.Contains($"signatures/{signer.KeyId}.sig.cbor", archive.Files.Keys);
        Assert.Empty(SlbVerifier.Verify(archive, verifier));
    }

    [Fact]
    public void Payload_tampering_fails_signature_verification()
    {
        var keyPair = Ed25519Keys.Generate();
        var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
        var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);
        var bytes = SlbWriter.Write(CreateFiles(), new SlbWriteOptions { Signer = signer });
        var archive = SlbReader.Read(bytes);
        var payload = archive.Blocks.First(entry => entry.Kind == SlbBlockKind.Image);
        bytes[(int)payload.Offset + 1] ^= 0x7f;

        var tampered = SlbReader.Read(bytes, verifyDigests: false);
        var errors = SlbVerifier.Verify(tampered, verifier);

        Assert.Contains(errors, error => error.StartsWith("SL5006:", StringComparison.Ordinal));
    }

    [Fact]
    public void Signature_tampering_is_detected()
    {
        var keyPair = Ed25519Keys.Generate();
        var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
        var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);
        var bytes = SlbWriter.Write(CreateFiles(), new SlbWriteOptions { Signer = signer });
        var archive = SlbReader.Read(bytes);
        var signature = archive.Blocks.First(entry => entry.Kind == SlbBlockKind.Signature);
        bytes[(int)signature.Offset + (int)signature.StoredSize - 1] ^= 0x7f;

        var tampered = SlbReader.Read(bytes, verifyDigests: false);
        var errors = SlbVerifier.Verify(tampered, verifier);

        Assert.Contains(errors, error => error.Contains("Signature verification", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsigned_bundle_reports_missing_signature()
    {
        var keyPair = Ed25519Keys.Generate();
        var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);
        var archive = SlbReader.Read(SlbWriter.Write(CreateFiles()));

        var errors = SlbVerifier.Verify(archive, verifier);

        Assert.Single(errors);
        Assert.StartsWith("SL5001:", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_signing_round_trips()
    {
        var root = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}", Encoding.UTF8);
            Directory.CreateDirectory(Path.Combine(root, "pages"));
            File.WriteAllBytes(Path.Combine(root, "pages", "v0.png"), [1, 2, 3]);

            var keyPair = Ed25519Keys.Generate();
            var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
            var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);

            var signaturePath = DirectorySigner.Sign(root, signer);
            var result = DirectoryVerifier.Verify(root, verifier);

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(signaturePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Directory_tampering_is_detected()
    {
        var root = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}", Encoding.UTF8);

            var keyPair = Ed25519Keys.Generate();
            var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
            var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);

            DirectorySigner.Sign(root, signer);
            File.WriteAllText(Path.Combine(root, "manifest.json"), "[]", Encoding.UTF8);

            var result = DirectoryVerifier.Verify(root, verifier);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.StartsWith("SL5009:", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Strict_mode_rejects_unsigned_files()
    {
        var root = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}", Encoding.UTF8);

            var keyPair = Ed25519Keys.Generate();
            var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
            var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);

            DirectorySigner.Sign(root, signer);
            File.WriteAllText(Path.Combine(root, "extra.json"), "{}", Encoding.UTF8);

            var strict = DirectoryVerifier.Verify(root, verifier);
            var permissive = DirectoryVerifier.Verify(root, verifier, strict: false);

            Assert.False(strict.Succeeded);
            Assert.True(permissive.Succeeded);
            Assert.Single(permissive.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Whitelist_allows_extra_files()
    {
        var root = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}", Encoding.UTF8);

            var keyPair = Ed25519Keys.Generate();
            var signer = Ed25519Keys.CreateSigner(keyPair.PrivateKey);
            var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);

            DirectorySigner.Sign(root, signer);
            Directory.CreateDirectory(Path.Combine(root, "extra"));
            File.WriteAllText(Path.Combine(root, "extra", "notes.txt"), "ok", Encoding.UTF8);

            var result = DirectoryVerifier.Verify(
                root,
                verifier,
                new VerificationOptions { AllowedExtraPaths = ["extra/"] });

            Assert.True(result.Succeeded);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    private static SlbFile[] CreateFiles() =>
    [
        new("manifest.json", SlbBlockKind.Manifest, Encoding.UTF8.GetBytes("{\"format\":\"sprite-loom\"}")),
        new("page/0000.slatlas/container.json", SlbBlockKind.Container, Encoding.UTF8.GetBytes("{\"page\":0}")),
        new("page/0000.slatlas/v0.png", SlbBlockKind.Image, [1, 2, 3, 4, 5]),
        new("anim/run.anim.json", SlbBlockKind.Animation, Encoding.UTF8.GetBytes("{\"frames\":[]}"))
    ];
}
