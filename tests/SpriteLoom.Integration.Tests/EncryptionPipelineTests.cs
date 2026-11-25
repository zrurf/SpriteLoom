using SpriteLoom.Bundle;
using SpriteLoom.Codecs;
using SpriteLoom.Foundation;
using SpriteLoom.Hosting;
using SpriteLoom.Security;

namespace SpriteLoom.Integration.Tests;

public sealed class EncryptionPipelineTests
{
    [Theory]
    [InlineData("aes-256-gcm")]
    [InlineData("xchacha20-poly1305")]
    public async Task Packs_signed_and_encrypted_bundle(string algorithm)
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-enc-" + Guid.NewGuid().ToString("N"));
        var frames = Path.Combine(root, "art", "run");
        var keys = Path.Combine(root, "keys");
        Directory.CreateDirectory(frames);
        Directory.CreateDirectory(keys);

        try
        {
            var codec = new SkiaImageCodec();
            WriteFrame(codec, Path.Combine(frames, "a1.png"), 1);
            WriteFrame(codec, Path.Combine(frames, "a2.png"), 2);

            var symmetricKey = SymmetricKey.Generate();
            symmetricKey.Save(Path.Combine(keys, "asset.key"));
            var keyPair = Ed25519Keys.Generate();
            Ed25519Keys.SavePrivate(keyPair.PrivateKey, Path.Combine(keys, "publisher.private.pem"));
            Ed25519Keys.SavePublic(keyPair.PublicKey, Path.Combine(keys, "publisher.public.pem"));

            var configPath = Path.Combine(root, "sloom.toml");
            File.WriteAllText(configPath, $$"""
                schema = 1

                [project]
                id = "heroes"
                name = "Heroes"

                [[sources]]
                id = "run"
                path = "art/run"
                order = "natural"
                frame-duration-ms = 100

                [[animations]]
                uri = "anim://heroes/run"
                source = "run"

                [[passes]]
                id = "pack"
                use = "pack.maxrects"
                params = { max-size = [64, 64], padding = 1, align = 4 }

                [signing]
                key = "keys/publisher.private.pem"

                [encryption]
                algorithm = "{{algorithm}}"
                key = "keys/asset.key"
                chunk-size = 64

                [output]
                kind = "bundle"
                path = "build/heroes.slb"
                manifest = "cbor"
                """);

            var result = await SloomApplication.Load(configPath).PackAsync();
            var verifier = Ed25519Keys.CreateVerifier(keyPair.PublicKey);

            var missing = Assert.Throws<InvalidDataException>(() => SlbReader.ReadFile(result.OutputPath));
            Assert.StartsWith("SL5022:", missing.Message, StringComparison.Ordinal);

            var archive = SlbReader.ReadFile(result.OutputPath, decryptionKey: symmetricKey);
            Assert.Single(archive.Crypto);
            Assert.Contains(archive.Files.Keys, path => path == "manifest.cbor");
            Assert.All(archive.Blocks.Where(block => block.Kind != SlbBlockKind.Signature), block =>
                Assert.True(block.Flags.HasFlag(SlbBlockFlags.Encrypted)));
            Assert.Empty(SlbVerifier.Verify(archive, verifier));

            var wrongKey = SymmetricKey.Generate();
            var wrong = Assert.Throws<InvalidDataException>(
                () => SlbReader.ReadFile(result.OutputPath, decryptionKey: wrongKey));
            Assert.StartsWith("SL5043:", wrong.Message, StringComparison.Ordinal);

            var inspectOutput = new StringWriter();
            var inspectExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                ["inspect", result.OutputPath, "--decrypt-key", Path.Combine(keys, "asset.key")],
                inspectOutput,
                new StringWriter());
            Assert.Equal(0, inspectExit);
            Assert.Contains("Frames: 2", inspectOutput.ToString(), StringComparison.Ordinal);

            var verifyExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                [
                    "verify",
                    result.OutputPath,
                    "--key",
                    Path.Combine(keys, "publisher.public.pem"),
                    "--decrypt-key",
                    Path.Combine(keys, "asset.key")
                ],
                new StringWriter(),
                new StringWriter());
            Assert.Equal(0, verifyExit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task Packs_encrypted_directory_and_inspects_with_key()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-enc-dir-" + Guid.NewGuid().ToString("N"));
        var frames = Path.Combine(root, "art", "run");
        var keys = Path.Combine(root, "keys");
        Directory.CreateDirectory(frames);
        Directory.CreateDirectory(keys);

        try
        {
            var codec = new SkiaImageCodec();
            WriteFrame(codec, Path.Combine(frames, "a1.png"), 1);
            WriteFrame(codec, Path.Combine(frames, "a2.png"), 2);

            var symmetricKey = SymmetricKey.Generate();
            symmetricKey.Save(Path.Combine(keys, "asset.key"));
            var keyPair = Ed25519Keys.Generate();
            Ed25519Keys.SavePrivate(keyPair.PrivateKey, Path.Combine(keys, "publisher.private.pem"));
            Ed25519Keys.SavePublic(keyPair.PublicKey, Path.Combine(keys, "publisher.public.pem"));

            var configPath = Path.Combine(root, "sloom.toml");
            File.WriteAllText(configPath, """
                schema = 1

                [project]
                id = "heroes"

                [[sources]]
                id = "run"
                path = "art/run"

                [signing]
                key = "keys/publisher.private.pem"

                [encryption]
                algorithm = "aes-256-gcm"
                key = "keys/asset.key"

                [output]
                kind = "directory"
                path = "build/heroes"
                manifest = "cbor"
                """);

            var result = await SloomApplication.Load(configPath).PackAsync();
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "manifest.cbor.senc")));
            Assert.NotEmpty(Directory.EnumerateFiles(result.OutputPath, "*.senc", SearchOption.AllDirectories));

            var output = new StringWriter();
            var error = new StringWriter();
            var missingExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                ["inspect", result.OutputPath],
                output,
                error);
            Assert.Equal(4, missingExit);

            output = new StringWriter();
            error = new StringWriter();
            var inspectExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                ["inspect", result.OutputPath, "--decrypt-key", Path.Combine(keys, "asset.key")],
                output,
                error);
            Assert.Equal(0, inspectExit);
            Assert.Contains("Frames: 2", output.ToString(), StringComparison.Ordinal);

            var manifestOutput = new StringWriter();
            var manifestExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                [
                    "inspect",
                    Path.Combine(result.OutputPath, "manifest.cbor"),
                    "--decrypt-key",
                    Path.Combine(keys, "asset.key")
                ],
                manifestOutput,
                new StringWriter());
            Assert.Equal(0, manifestExit);

            var verifyExit = await SpriteLoom.Commands.SloomCommandLine.RunAsync(
                ["verify", result.OutputPath, "--key", Path.Combine(keys, "publisher.public.pem")],
                new StringWriter(),
                new StringWriter());
            Assert.Equal(0, verifyExit);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteFrame(SkiaImageCodec codec, string path, int seed)
    {
        var image = RgbaImage.CreateTransparent(8, 8);

        for (var row = seed; row < 6 + seed; row++)
        {
            for (var column = 1; column < 7; column++)
            {
                image.SetPixel(column, row, 180, 100, 40, 255);
            }
        }

        File.WriteAllBytes(path, codec.EncodePng(image));
    }
}
