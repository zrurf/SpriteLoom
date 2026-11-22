using System.Security.Cryptography;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle.Tests;

public sealed class SymmetricCryptoTests
{
    private static readonly SymmetricKey Key = SymmetricKey.FromBytes(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());

    [Theory]
    [InlineData(SymmetricAlgorithmKind.Aes256Gcm)]
    [InlineData(SymmetricAlgorithmKind.XChaCha20Poly1305)]
    public void Chunked_round_trip_handles_boundaries(SymmetricAlgorithmKind algorithm)
    {
        var prefixLength = SymmetricAlgorithms.NoncePrefixSize(algorithm);
        var prefix = Enumerable.Range(1, prefixLength).Select(value => (byte)value).ToArray();

        foreach (var length in new[] { 0, 1, 1023, 1024, 1025, 4096 })
        {
            var plaintext = Enumerable.Range(0, length).Select(value => (byte)(value * 31)).ToArray();
            var ciphertext = AeadChunker.Encrypt(
                algorithm,
                Key.Bytes.Span,
                prefix,
                plaintext,
                1024,
                out var chunkCount);
            var decrypted = AeadChunker.Decrypt(
                algorithm,
                Key.Bytes.Span,
                prefix,
                ciphertext,
                1024,
                chunkCount,
                plaintext.Length);

            Assert.Equal(plaintext, decrypted);
        }
    }

    [Theory]
    [InlineData(SymmetricAlgorithmKind.Aes256Gcm)]
    [InlineData(SymmetricAlgorithmKind.XChaCha20Poly1305)]
    public void Chunk_tampering_is_rejected(SymmetricAlgorithmKind algorithm)
    {
        var prefix = new byte[SymmetricAlgorithms.NoncePrefixSize(algorithm)];
        var plaintext = Enumerable.Range(0, 2000).Select(value => (byte)value).ToArray();
        var ciphertext = AeadChunker.Encrypt(
            algorithm,
            Key.Bytes.Span,
            prefix,
            plaintext,
            512,
            out var chunkCount);
        ciphertext[^1] ^= 0x01;

        Assert.Throws<CryptographicException>(() => AeadChunker.Decrypt(
            algorithm,
            Key.Bytes.Span,
            prefix,
            ciphertext,
            512,
            chunkCount,
            plaintext.Length));
    }

    [Theory]
    [InlineData(SymmetricAlgorithmKind.Aes256Gcm)]
    [InlineData(SymmetricAlgorithmKind.XChaCha20Poly1305)]
    public void Senc_round_trip(SymmetricAlgorithmKind algorithm)
    {
        var plaintext = Enumerable.Range(0, 70_000).Select(value => (byte)(value * 13)).ToArray();
        var encrypted = SencFile.Encrypt(plaintext, Key, algorithm, 16 * 1024);
        var decrypted = SencFile.Decrypt(encrypted, Key);

        Assert.Equal(plaintext, decrypted);
        Assert.True(SencFile.HasMagic(encrypted));
        Assert.Equal(0, encrypted[32]);
    }

    [Fact]
    public void Senc_rejects_wrong_key_and_tampering()
    {
        var encrypted = SencFile.Encrypt([1, 2, 3, 4], Key, SymmetricAlgorithmKind.Aes256Gcm);
        var other = SymmetricKey.FromBytes(new byte[32]);

        Assert.Throws<InvalidDataException>(() => SencFile.Decrypt(encrypted, other));

        encrypted[^1] ^= 0x01;
        Assert.Throws<CryptographicException>(() => SencFile.Decrypt(encrypted, Key));
    }

    [Fact]
    public void Key_file_accepts_hex_and_raw_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var hexPath = Path.Combine(root, "hex.key");
            Key.Save(hexPath);
            Assert.Equal(Key.KeyId, SymmetricKey.Load(hexPath).KeyId);

            var rawPath = Path.Combine(root, "raw.key");
            File.WriteAllBytes(rawPath, Key.Bytes.ToArray());
            Assert.Equal(Key.KeyId, SymmetricKey.Load(rawPath).KeyId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class SlbEncryptionTests
{
    [Theory]
    [InlineData(SymmetricAlgorithmKind.Aes256Gcm)]
    [InlineData(SymmetricAlgorithmKind.XChaCha20Poly1305)]
    public void Slb_round_trips_encrypted_blocks(SymmetricAlgorithmKind algorithm)
    {
        var key = SymmetricKey.FromBytes(Enumerable.Range(10, 32).Select(value => (byte)value).ToArray());
        var files = new[]
        {
            new SlbFile("manifest.cbor", SlbBlockKind.Manifest, Enumerable.Range(0, 100).Select(value => (byte)value).ToArray()),
            new SlbFile("page/0000.slatlas/container.cbor", SlbBlockKind.Container, Enumerable.Range(1, 200).Select(value => (byte)(value * 3)).ToArray()),
            new SlbFile("page/0000.slatlas/v0.png", SlbBlockKind.Image, Enumerable.Range(2, 300).Select(value => (byte)(value * 7)).ToArray())
        };
        var bytes = SlbWriter.Write(files, new SlbWriteOptions
        {
            EncryptionKey = key,
            EncryptionAlgorithm = algorithm,
            EncryptionChunkSize = 64
        });

        Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes));

        var archive = SlbReader.Read(bytes, decryptionKey: key);

        Assert.Equal(3, archive.Files.Count);
        Assert.All(archive.Blocks.Take(3), entry =>
        {
            Assert.True(entry.Flags.HasFlag(SlbBlockFlags.Encrypted));
            Assert.NotEqual(0u, entry.ChunkCount);
            Assert.Equal(key.KeyId, entry.KeyId);
        });
        Assert.Single(archive.Crypto);
        Assert.Equal(algorithm, archive.Crypto[0].Algorithm);
        Assert.Equal("manifest.cbor", archive.IndexEntries[0].Path);
        Assert.Equal(files[0].Content, archive.Files["manifest.cbor"]);
        Assert.Equal(files[2].Content, archive.Files["page/0000.slatlas/v0.png"]);
    }

    [Fact]
    public void Slb_rejects_wrong_key_and_ciphertext_tampering()
    {
        var key = SymmetricKey.FromBytes(Enumerable.Range(20, 32).Select(value => (byte)value).ToArray());
        var bytes = SlbWriter.Write(
            [new SlbFile("manifest.cbor", SlbBlockKind.Manifest, [1, 2, 3, 4])],
            new SlbWriteOptions { EncryptionKey = key });

        var wrongKey = SymmetricKey.FromBytes(Enumerable.Range(40, 32).Select(value => (byte)value).ToArray());
        var wrong = Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes, decryptionKey: wrongKey));
        Assert.StartsWith("SL5043:", wrong.Message, StringComparison.Ordinal);

        var archive = SlbReader.Read(bytes, decryptionKey: key);
        bytes[(int)archive.Blocks[0].Offset + 1] ^= 0x01;
        var tampered = Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes, decryptionKey: key));
        Assert.StartsWith("SL5045:", tampered.Message, StringComparison.Ordinal);
    }
}
