using SpriteLoom.Abstractions;
using SpriteLoom.Codecs;
using SpriteLoom.Foundation;
using SpriteLoom.Gui.ViewModels;
using SpriteLoom.Hosting;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Gui.Tests;

public sealed class GuiWorkflowTests
{
    [Theory]
    [InlineData("directory")]
    [InlineData("bundle")]
    public async Task Edits_settings_and_repacks_a_cbor_package(string outputKind)
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-gui-flow-" + Guid.NewGuid().ToString("N"));
        var frames = Path.Combine(root, "art", "run");
        Directory.CreateDirectory(frames);

        try
        {
            var codec = new SkiaImageCodec();
            WriteFrame(codec, Path.Combine(frames, "a1.png"), 1);
            WriteFrame(codec, Path.Combine(frames, "a2.png"), 2);

            var config = new ProjectConfig
            {
                BaseDirectory = root,
                PackageId = "heroes",
                DisplayName = "Heroes",
                Sources = [new SourceSpec("run", "image-sequence", "art/run", null, "natural", 100)],
                Animations = [new AnimationSpec("anim://heroes/run", "run", true, "forward", 100)],
                FramePattern = "{relpath}",
                Packer = "pack.maxrects",
                PackOptions = new PackOptions(64, 64, 1, false, 4, false),
                TrimThreshold = 1,
                RasterFormats = ["png"],
                RasterQuality = 90,
                BleedOptions = new BleedOptions(1, true),
                TextureTargets = [],
                TexturePixelFormat = PixelFormat.Rgba8888,
                TextureDither = DitherMode.Ordered4,
                Mipmaps = false,
                TextureQuality = TextureQuality.Balanced,
                OutputPath = outputKind == "bundle" ? "build/heroes.slb" : "build/heroes",
                OutputKind = outputKind,
                ManifestFormat = ManifestFormat.Cbor
            };
            var settings = ProjectSettings.FromConfig(config);
            settings.Padding = 2;

            var application = SloomApplication.FromConfig(settings.ToConfig());
            var result = await application.PackAsync();

            using var package = LoadedPackage.Open(result.OutputPath);

            Assert.Equal("CBOR", package.ManifestFormat);
            Assert.Equal("heroes", package.PackageId);
            Assert.Equal(2, package.FrameCount);
            Assert.Equal(1, package.AnimationCount);
            Assert.Contains("heroes", package.ManifestText);
            var animation = package.LoadAnimation("anim://heroes/run");
            Assert.Equal(2, animation.FrameUris.Count);
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
