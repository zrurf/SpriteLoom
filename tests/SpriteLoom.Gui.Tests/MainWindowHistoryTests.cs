using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Gui.ViewModels;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Gui.Tests;

public sealed class MainWindowHistoryTests
{
    [Fact]
    public void Undo_and_redo_restore_project_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-gui-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configPath = Path.Combine(root, "sloom.toml");
            ProjectConfigWriter.Write(CreateConfig(root), configPath);

            using var viewModel = new MainWindowViewModel(configPath);

            Assert.NotNull(viewModel.Settings);
            Assert.Equal(2, viewModel.Settings.Padding);

            viewModel.Settings.Padding = 8;
            Assert.Equal(8, viewModel.Settings.Padding);

            viewModel.UndoCommand.Execute(null);
            Assert.Equal(2, viewModel.Settings.Padding);

            viewModel.RedoCommand.Execute(null);
            Assert.Equal(8, viewModel.Settings.Padding);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ProjectConfig CreateConfig(string root) =>
        new()
        {
            BaseDirectory = root,
            PackageId = "heroes",
            DisplayName = "Heroes",
            Sources = [new SourceSpec("run", "image-sequence", "art/run", null, "natural", 100)],
            Animations = [new AnimationSpec("anim://heroes/run", "run", true, "forward", 100)],
            FramePattern = "{relpath}",
            Packer = "pack.maxrects",
            PackOptions = new PackOptions(64, 64, 2, false, 4, false),
            TrimThreshold = 1,
            RasterFormats = ["png"],
            RasterQuality = 90,
            BleedOptions = new BleedOptions(1, true),
            TextureTargets = [],
            TexturePixelFormat = PixelFormat.Rgba8888,
            TextureDither = DitherMode.Ordered4,
            Mipmaps = false,
            TextureQuality = TextureQuality.Balanced,
            OutputPath = "build/heroes",
            OutputKind = "directory",
            ManifestFormat = ManifestFormat.Json
        };
}
