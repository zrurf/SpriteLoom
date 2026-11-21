using System.Text;
using SpriteLoom.Bundle;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle.Tests;

public sealed class SlbRoundTripTests
{
    [Fact]
    public void Round_trips_files_and_checksums()
    {
        var files = CreateFiles();
        var mask = HashAlgorithmKind.Crc32 |
                   HashAlgorithmKind.XxHash64 |
                   HashAlgorithmKind.Blake3 |
                   HashAlgorithmKind.Sha256;
        var bytes = SlbWriter.Write(files, new SlbWriteOptions { Checksums = mask });
        var archive = SlbReader.Read(bytes);

        Assert.Equal(files.Length, archive.Files.Count);

        foreach (var file in files)
        {
            Assert.True(archive.Files.TryGetValue(file.Path, out var content));
            Assert.True(file.Content.AsSpan().SequenceEqual(content));
        }

        Assert.Equal(SlbBlockKind.Manifest, archive.Blocks[0].Kind);
        Assert.Equal(mask, archive.Blocks[0].ChecksumAlgorithms);
    }

    [Fact]
    public void Payload_tampering_is_detected()
    {
        var bytes = SlbWriter.Write(CreateFiles());
        var archive = SlbReader.Read(bytes);
        bytes[(int)archive.Blocks[1].Offset + 2] ^= 0x7f;

        var exception = Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes));
        Assert.StartsWith("SL5035:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_tampering_is_detected()
    {
        var bytes = SlbWriter.Write(CreateFiles());
        bytes[16] ^= 0x7f;

        Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes));
    }

    [Fact]
    public void Index_tampering_is_detected()
    {
        var bytes = SlbWriter.Write(CreateFiles());
        var archive = SlbReader.Read(bytes);
        bytes[(int)archive.Header.IndexOffset] ^= 0x7f;

        Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes));
    }

    [Fact]
    public void Footer_round_trips_and_validates()
    {
        var bytes = SlbWriter.Write(CreateFiles(), new SlbWriteOptions { WriteFooter = true });
        var archive = SlbReader.Read(bytes);

        Assert.True(archive.Header.Flags.HasFlag(SlbHeaderFlags.HasFooter));
        Assert.Equal((ulong)bytes.Length, archive.Header.FileSize);

        bytes[^1] ^= 0x7f;
        Assert.Throws<InvalidDataException>(() => SlbReader.Read(bytes));
    }

    [Fact]
    public void Empty_bundle_round_trips()
    {
        var bytes = SlbWriter.Write([]);
        var archive = SlbReader.Read(bytes);

        Assert.Empty(archive.Files);
        Assert.Equal(0u, archive.Header.BlockCount);
    }

    private static SlbFile[] CreateFiles() =>
    [
        new("manifest.json", SlbBlockKind.Manifest, Encoding.UTF8.GetBytes("{\"format\":\"sprite-loom\"}")),
        new("page/0000.slatlas/container.json", SlbBlockKind.Container, Encoding.UTF8.GetBytes("{\"page\":0}")),
        new("page/0000.slatlas/v0.png", SlbBlockKind.Image, [1, 2, 3, 4, 5]),
        new("anim/run.anim.json", SlbBlockKind.Animation, Encoding.UTF8.GetBytes("{\"frames\":[]}"))
    ];
}
