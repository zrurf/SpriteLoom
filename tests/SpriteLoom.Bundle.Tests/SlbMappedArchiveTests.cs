using SpriteLoom.Bundle;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle.Tests;

public sealed class SlbMappedArchiveTests
{
    [Fact]
    public void Reads_files_and_streams_without_loading_the_archive()
    {
        var path = WriteBundle(CreateFiles(), new SlbWriteOptions());
        try
        {
            using var archive = SlbMappedArchive.Open(path, verifyDigests: true);

            Assert.Equal(3, archive.Paths.Count);
            Assert.True(archive.Contains("manifest.cbor"));
            Assert.Equal(3, archive.GetFileLength("manifest.cbor"));
            Assert.Equal([1, 2, 3], archive.ReadFile("manifest.cbor"));

            using var stream = archive.OpenFile("page/0000.slatlas/v0.png");
            var buffer = new byte[4];
            var read = stream.Read(buffer, 0, buffer.Length);
            Assert.Equal(4, read);
            Assert.Equal([4, 5, 6, 7], buffer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(SymmetricAlgorithmKind.Aes256Gcm)]
    [InlineData(SymmetricAlgorithmKind.XChaCha20Poly1305)]
    public void Decrypts_files_lazily(SymmetricAlgorithmKind algorithm)
    {
        var key = SymmetricKey.FromBytes(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var path = WriteBundle(
            CreateFiles(),
            new SlbWriteOptions
            {
                EncryptionKey = key,
                EncryptionAlgorithm = algorithm,
                EncryptionChunkSize = 4
            });

        try
        {
            using var archive = SlbMappedArchive.Open(path, key, verifyDigests: true);
            Assert.Equal([1, 2, 3], archive.ReadFile("manifest.cbor"));
            Assert.Equal([4, 5, 6, 7], ReadAll(archive.OpenFile("page/0000.slatlas/v0.png")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Requires_the_key_only_when_an_encrypted_file_is_read()
    {
        var key = SymmetricKey.FromBytes(Enumerable.Range(10, 32).Select(value => (byte)value).ToArray());
        var path = WriteBundle(
            CreateFiles(),
            new SlbWriteOptions { EncryptionKey = key });

        try
        {
            using var archive = SlbMappedArchive.Open(path);
            Assert.Contains("manifest.cbor", archive.Paths);
            Assert.Throws<InvalidDataException>(() => archive.ReadFile("manifest.cbor"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using (stream)
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static string WriteBundle(IReadOnlyList<SlbFile> files, SlbWriteOptions options)
    {
        var path = Path.Combine(Path.GetTempPath(), "sloom-mapped-" + Guid.NewGuid().ToString("N") + ".slb");
        SlbWriter.WriteFile(files, path, options);
        return path;
    }

    private static SlbFile[] CreateFiles() =>
    [
        new SlbFile("manifest.cbor", SlbBlockKind.Manifest, [1, 2, 3]),
        new SlbFile("page/0000.slatlas/container.cbor", SlbBlockKind.Container, [8, 9, 10, 11]),
        new SlbFile("page/0000.slatlas/v0.png", SlbBlockKind.Image, [4, 5, 6, 7])
    ];
}
