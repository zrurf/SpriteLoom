using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Gui.ViewModels;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Gui.Tests;

public sealed class ProjectSettingsTests
{
    [Fact]
    public void Round_trips_config_through_editable_settings()
    {
        var config = CreateConfig();
        var settings = ProjectSettings.FromConfig(config);

        settings.MaxWidth = 1024;
        settings.Padding = 6;
        settings.RasterFormats = "png, webp, avif";
        settings.TextureTargets = "ktx2-astc6x6, ktx2-bc7";
        settings.Mipmaps = true;

        var result = settings.ToConfig();

        Assert.Equal("heroes", result.PackageId);
        Assert.Equal("Heroes", result.DisplayName);
        Assert.Equal(1024, result.PackOptions.MaxWidth);
        Assert.Equal(6, result.PackOptions.Padding);
        Assert.Equal(12, result.PackOptions.Align);
        Assert.Equal(["png", "webp", "avif"], result.RasterFormats);
        Assert.Equal(["ktx2-astc6x6", "ktx2-bc7"], result.TextureTargets);
        Assert.True(result.Mipmaps);
        Assert.Single(result.Sources);
        Assert.Single(result.Animations);
    }

    [Fact]
    public void Source_duration_updates_animation_duration()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        settings.Sources[0].FrameDurationMs = 150;

        var result = settings.ToConfig();

        Assert.Equal(150, result.Sources[0].FrameDurationMs);
        Assert.Equal(150, result.Animations[0].DurationMs);
    }

    [Fact]
    public void Raises_changed_for_properties_and_sources()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        var changes = 0;
        settings.Changed += (_, _) => changes++;

        settings.Padding = 8;
        settings.Sources[0].FrameDurationMs = 120;
        settings.AddSource();

        Assert.Equal(3, changes);
    }

    [Fact]
    public void Moves_and_removes_sources()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        var second = settings.AddSource();
        second.Id = "walk";

        Assert.True(settings.MoveSource(second, -1));
        Assert.Equal("walk", settings.Sources[0].Id);
        Assert.False(settings.MoveSource(settings.Sources[0], -1));

        settings.Sources.Remove(second);
        Assert.Single(settings.Sources);
    }

    [Fact]
    public void Pipeline_graph_tracks_optional_texture_passes()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        settings.TextureTargets = "ktx2-astc6x6";
        settings.Mipmaps = true;

        var withTextures = PipelineGraphBuilder.Build(settings);

        Assert.Contains(withTextures, node => node.Id == "mipmap");
        Assert.Contains(withTextures, node => node.Id == "texture");
        Assert.Equal("package", withTextures[^1].Id);
        Assert.True(withTextures[^1].IsLast);

        settings.TextureTargets = string.Empty;
        var withoutTextures = PipelineGraphBuilder.Build(settings);

        Assert.DoesNotContain(withoutTextures, node => node.Id == "mipmap");
        Assert.DoesNotContain(withoutTextures, node => node.Id == "texture");
    }

    [Fact]
    public void Snapshot_history_supports_undo_and_redo()
    {
        var history = new SnapshotHistory<string>();
        history.Push("a");
        history.Push("b");

        Assert.True(history.TryUndo("c", out var undo));
        Assert.Equal("b", undo);
        Assert.True(history.CanRedo);

        Assert.True(history.TryRedo("b", out var redo));
        Assert.Equal("c", redo);
        Assert.True(history.CanUndo);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Rejects_animation_uris_from_another_package()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        settings.PackageId = "other";

        var exception = Assert.Throws<InvalidDataException>(() => settings.ToConfig());
        Assert.Contains("anim://heroes/run", exception.Message);
    }

    [Fact]
    public void Rejects_invalid_texture_targets()
    {
        var settings = ProjectSettings.FromConfig(CreateConfig());
        settings.TextureTargets = "ktx2-missing";

        var exception = Assert.Throws<InvalidDataException>(() => settings.ToConfig());
        Assert.Contains("ktx2-missing", exception.Message);
    }

    private static ProjectConfig CreateConfig() =>
        new()
        {
            BaseDirectory = Path.GetTempPath(),
            PackageId = "heroes",
            DisplayName = "Heroes",
            Sources = [new SourceSpec("run", "image-sequence", "art/run", null, "natural", 100)],
            Animations = [new AnimationSpec("anim://heroes/run", "run", true, "forward", 100)],
            FramePattern = "{relpath}",
            Packer = "pack.maxrects",
            PackOptions = new PackOptions(2048, 2048, 2, false, 4, false),
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
