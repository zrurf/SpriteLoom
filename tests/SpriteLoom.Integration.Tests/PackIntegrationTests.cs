using System.Text.Json;
using SpriteLoom.Foundation;
using SpriteLoom.Hosting;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;

namespace SpriteLoom.Integration.Tests;

public sealed class PackIntegrationTests
{
    [Fact]
    public async Task Pack_produces_manifest_pages_and_animation()
    {
        using var project = TestProject.Create();
        var application = SloomApplication.Load(project.ConfigPath);
        var result = await application.PackAsync();

        Assert.Empty(result.Diagnostics);

        var manifestPath = Path.Combine(result.OutputPath, "manifest.json");
        var containerPath = Path.Combine(result.OutputPath, "page", "0000.slatlas", "container.json");
        var imagePath = Path.Combine(result.OutputPath, "page", "0000.slatlas", "v0.png");
        var animationPath = Path.Combine(result.OutputPath, "anim", "run.anim.json");

        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(containerPath));
        Assert.True(File.Exists(imagePath));
        Assert.True(File.Exists(animationPath));

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var rootElement = manifest.RootElement;

        Assert.Equal(5, rootElement.GetProperty("uris").GetArrayLength());
        Assert.Equal(3, rootElement.GetProperty("frames").GetProperty("records").GetArrayLength());
        Assert.Equal(1, rootElement.GetProperty("animations").GetArrayLength());

        using var container = JsonDocument.Parse(File.ReadAllText(containerPath));
        var storedCount = container.RootElement.GetProperty("stored").GetArrayLength();
        Assert.True(
            storedCount == 2,
            $"stored={storedCount}{Environment.NewLine}{container.RootElement.GetRawText()}");

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var imageBytes = File.ReadAllBytes(imagePath);

        var second = await application.PackAsync();
        var secondManifest = File.ReadAllBytes(manifestPath);
        var secondImage = File.ReadAllBytes(imagePath);

        Assert.True(manifestBytes.AsSpan().SequenceEqual(secondManifest));
        Assert.True(imageBytes.AsSpan().SequenceEqual(secondImage));
        Assert.DoesNotContain(second.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Directory_and_bundle_manifests_match()
    {
        using var directory = TestProject.Create();
        using var bundle = TestProject.Create(
            outputKind: "bundle",
            outputPath: "build/heroes.slb");

        var directoryApplication = SloomApplication.Load(directory.ConfigPath);
        await directoryApplication.PackAsync();

        var bundleApplication = SloomApplication.Load(bundle.ConfigPath);
        await bundleApplication.PackAsync();

        var directoryManifest = File.ReadAllBytes(
            Path.Combine(directory.OutputPath, "manifest.json"));
        var archive = SpriteLoom.Bundle.SlbReader.ReadFile(bundle.OutputPath);

        Assert.True(directoryManifest.AsSpan().SequenceEqual(archive.Files["manifest.json"]));
        Assert.True(
            File.ReadAllBytes(Path.Combine(directory.OutputPath, "page", "0000.slatlas", "v0.png"))
                .AsSpan()
                .SequenceEqual(archive.Files["page/0000.slatlas/v0.png"]));
    }

    [Fact]
    public async Task Pack_supports_cbor_manifest()
    {
        using var project = TestProject.Create("cbor");
        var application = SloomApplication.Load(project.ConfigPath);
        var result = await application.PackAsync();

        var manifestPath = Path.Combine(result.OutputPath, "manifest.cbor");
        Assert.True(File.Exists(manifestPath));

        using var stream = File.OpenRead(manifestPath);
        var summary = ManifestReader.ReadSummary(stream, ManifestFormat.Cbor);

        Assert.Equal(5, summary.UriCount);
        Assert.Equal(3, summary.FrameCount);
        Assert.Equal(1, summary.AnimationCount);
    }
}
