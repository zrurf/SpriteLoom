using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Integration.Tests;

public sealed class ProjectConfigWriterTests
{
    [Fact]
    public void Writes_and_reloads_project_config()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var config = new ProjectConfig
            {
                BaseDirectory = root,
                PackageId = "heroes",
                DisplayName = "Heroes \"A\"",
                Sources =
                [
                    new SourceSpec(
                        "run",
                        "texturepacker",
                        "art/run.plist",
                        "art/run.png",
                        "lexicographic",
                        80)
                ],
                Animations = [new AnimationSpec("anim://heroes/run", "run", false, "reverse", 80)],
                FramePattern = "{stem}",
                Packer = "pack.basic",
                PackOptions = new PackOptions(4096, 2048, 4, true, 4, true),
                TrimThreshold = 3,
                RasterFormats = ["png", "webp"],
                RasterQuality = 75,
                BleedOptions = new BleedOptions(2, false),
                TextureTargets = ["ktx2-astc6x6"],
                TexturePixelFormat = PixelFormat.Rgba4444,
                TextureDither = DitherMode.Ordered8,
                Mipmaps = true,
                TextureQuality = TextureQuality.High,
                OutputPath = "build/heroes.slb",
                OutputKind = "bundle",
                SigningKeyPath = "keys/private.key",
                ManifestFormat = ManifestFormat.Cbor
            };
            var path = Path.Combine(root, "sloom.toml");

            ProjectConfigWriter.Write(config, path);
            var loaded = ProjectConfigLoader.Load(path);

            Assert.Equal("heroes", loaded.PackageId);
            Assert.Equal("Heroes \"A\"", loaded.DisplayName);
            Assert.Equal("{stem}", loaded.FramePattern);
            Assert.Equal("pack.basic", loaded.Packer);
            Assert.Equal(4096, loaded.PackOptions.MaxWidth);
            Assert.Equal(2048, loaded.PackOptions.MaxHeight);
            Assert.Equal(4, loaded.PackOptions.Padding);
            Assert.True(loaded.PackOptions.Pow2);
            Assert.True(loaded.PackOptions.AllowRotation);
            Assert.Equal(12, loaded.PackOptions.Align);
            Assert.Equal(3, loaded.TrimThreshold);
            Assert.Equal(["png", "webp"], loaded.RasterFormats);
            Assert.Equal(75, loaded.RasterQuality);
            Assert.Equal(2, loaded.BleedOptions.Extrude);
            Assert.False(loaded.BleedOptions.AlphaDilate);
            Assert.Equal(["ktx2-astc6x6"], loaded.TextureTargets);
            Assert.Equal(PixelFormat.Rgba4444, loaded.TexturePixelFormat);
            Assert.Equal(DitherMode.Ordered8, loaded.TextureDither);
            Assert.True(loaded.Mipmaps);
            Assert.Equal(TextureQuality.High, loaded.TextureQuality);
            Assert.Equal("build/heroes.slb", loaded.OutputPath);
            Assert.Equal("bundle", loaded.OutputKind);
            Assert.Equal("keys/private.key", loaded.SigningKeyPath);
            Assert.Equal(ManifestFormat.Cbor, loaded.ManifestFormat);
            Assert.Single(loaded.Sources);
            var source = loaded.Sources[0];
            Assert.Equal("run", source.Id);
            Assert.Equal("texturepacker", source.Kind);
            Assert.Equal("art/run.plist", source.Path);
            Assert.Equal("art/run.png", source.ImagePath);
            Assert.Equal("lexicographic", source.Order);
            Assert.Equal(80, source.FrameDurationMs);
            Assert.Single(loaded.Animations);
            Assert.Equal("anim://heroes/run", loaded.Animations[0].Uri);
            Assert.False(loaded.Animations[0].Loop);
            Assert.Equal("reverse", loaded.Animations[0].Playback);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
