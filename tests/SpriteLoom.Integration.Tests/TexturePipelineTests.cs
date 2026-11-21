using System.Buffers.Binary;
using System.Text.Json;
using SpriteLoom.Foundation;
using SpriteLoom.Hosting;

namespace SpriteLoom.Integration.Tests;

public sealed class TexturePipelineTests
{
    [Fact]
    public async Task Raw_ktx2_variant_is_written_with_mips()
    {
        using var project = TestProject.Create(
            textureTargets: ["ktx2-rgba8888"],
            mipmaps: true);
        var application = SloomApplication.Load(project.ConfigPath);
        Assert.Equal(4, application.Config.PackOptions.Align);
        var result = await application.PackAsync();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
        var page = manifest.RootElement.GetProperty("pages")[0];
        var variants = page.GetProperty("variants").EnumerateArray().ToArray();

        Assert.Equal(2, variants.Length);
        Assert.Equal("png", variants[0].GetProperty("id").GetString());
        Assert.Equal("ktx2-rgba8888", variants[1].GetProperty("id").GetString());
        Assert.Equal("rgba8888", variants[1].GetProperty("pixel-format").GetString());

        var texturePath = Path.Combine(
            result.OutputPath,
            variants[1].GetProperty("file").GetString()!.Replace('/', Path.DirectorySeparatorChar));
        var texture = File.ReadAllBytes(texturePath);
        Assert.Equal(
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A },
            texture[..12]);
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(texture.AsSpan(12 + 28)) > 1);

        var size = page.GetProperty("size");
        Assert.True(size[0].GetInt32() % 4 == 0, $"size={size.GetRawText()}");
        Assert.True(size[1].GetInt32() % 4 == 0, $"size={size.GetRawText()}");
    }

    [Fact]
    public async Task Native_encoder_produces_astc_and_bc_variants()
    {
        if (SpriteLoom.Textures.NativeTextureEncoder.TryCreate() is null)
        {
            return;
        }

        using var project = TestProject.Create(
            textureTargets:
            [
                "ktx2-astc4x4",
                "ktx2-bc1",
                "ktx2-bc7",
                "ktx2-etc2-rgba",
                "ktx2-basis-uastc",
                "ktx2-basis-etc1s"
            ]);
        var application = SloomApplication.Load(project.ConfigPath);
        var result = await application.PackAsync();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
        var variants = manifest.RootElement.GetProperty("pages")[0]
            .GetProperty("variants")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(7, variants.Length);
        Assert.Equal("ktx2-astc4x4", variants[1].GetProperty("id").GetString());
        Assert.Equal("ktx2-bc1", variants[2].GetProperty("id").GetString());
        Assert.Equal("ktx2-bc7", variants[3].GetProperty("id").GetString());
        Assert.Equal("ktx2-etc2-rgba", variants[4].GetProperty("id").GetString());
        Assert.Equal("ktx2-basis-uastc", variants[5].GetProperty("id").GetString());
        Assert.Equal("ktx2-basis-etc1s", variants[6].GetProperty("id").GetString());

        foreach (var variant in variants.Skip(1))
        {
            var path = Path.Combine(
                result.OutputPath,
                variant.GetProperty("file").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(
                new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A },
                bytes[..12]);
            Assert.True(bytes.Length > 100);
        }
    }

    [Fact]
    public async Task Raster_variants_are_written_to_the_manifest()
    {
        using var project = TestProject.Create(rasterFormats: ["png", "webp", "jxl", "avif"]);
        var application = SloomApplication.Load(project.ConfigPath);
        var result = await application.PackAsync();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
        var variants = manifest.RootElement.GetProperty("pages")[0]
            .GetProperty("variants")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(4, variants.Length);
        Assert.Equal("png", variants[0].GetProperty("id").GetString());
        Assert.Equal("webp", variants[1].GetProperty("id").GetString());
        Assert.Equal("jxl", variants[2].GetProperty("id").GetString());
        Assert.Equal("avif", variants[3].GetProperty("id").GetString());

        foreach (var variant in variants)
        {
            var path = Path.Combine(
                result.OutputPath,
                variant.GetProperty("file").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }

        var firstManifest = File.ReadAllBytes(Path.Combine(result.OutputPath, "manifest.json"));
        await application.PackAsync();
        Assert.True(firstManifest.AsSpan().SequenceEqual(
            File.ReadAllBytes(Path.Combine(result.OutputPath, "manifest.json"))));
    }

    [Fact]
    public async Task Missing_native_encoder_degrades_to_png()
    {
        Environment.SetEnvironmentVariable("SLOOM_DISABLE_NATIVE", "1");

        try
        {
            using var project = TestProject.Create(textureTargets: ["ktx2-astc4x4"]);
            var application = SloomApplication.Load(project.ConfigPath);
            var result = await application.PackAsync();

            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code == "SL3001" && diagnostic.Severity == DiagnosticSeverity.Warning);

            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
            var variants = manifest.RootElement.GetProperty("pages")[0]
                .GetProperty("variants")
                .EnumerateArray()
                .ToArray();

            Assert.Single(variants);
            Assert.Equal("png", variants[0].GetProperty("id").GetString());
            Assert.False(File.Exists(Path.Combine(
                result.OutputPath,
                "page",
                "0000.slatlas",
                "v1.ktx2")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SLOOM_DISABLE_NATIVE", null);
        }
    }

    [Fact]
    public async Task Astc_alignment_is_applied_to_page_size()
    {
        using var project = TestProject.Create(textureTargets: ["ktx2-astc6x6"]);
        var application = SloomApplication.Load(project.ConfigPath);
        var result = await application.PackAsync();

        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(result.OutputPath, "manifest.json")));
        var size = manifest.RootElement.GetProperty("pages")[0].GetProperty("size");

        Assert.Equal(0, size[0].GetInt32() % 12);
        Assert.Equal(0, size[1].GetInt32() % 12);
    }
}
