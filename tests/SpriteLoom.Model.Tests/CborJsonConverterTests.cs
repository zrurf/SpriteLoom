using System.Text.Json;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;

namespace SpriteLoom.Model.Tests;

public sealed class CborJsonConverterTests
{
    [Fact]
    public void Converts_manifest_cbor_to_json()
    {
        var manifest = new AtlasManifest
        {
            BundleId = "bundle",
            PackageId = "heroes",
            PackageDisplayName = "Heroes",
            GeneratorName = "SpriteLoom",
            GeneratorVersion = "0.1.0",
            Uris = ["pkg://heroes", "frame://heroes/run/0001"],
            Pages =
            [
                new AtlasPage(
                    0,
                    0,
                    "default",
                    0,
                    true,
                    "page/0000.slatlas/container.cbor",
                    Digest.XxHash64([1, 2, 3]),
                    64,
                    32,
                    "rgba8888",
                    [
                        new AtlasVariant(
                            "png",
                            "png",
                            "page/0000.slatlas/page.png",
                            Digest.XxHash64([4, 5]))
                    ])
            ],
            ToRecord = [uint.MaxValue, 0],
            Records = [new AtlasFrameRecord(0, 0, 16, 16, 0, 0, 128, 128, 0)],
            Animations =
            [
                new AtlasAnimationRef(
                    1,
                    "anim/run.anim.cbor",
                    Digest.XxHash64([6, 7]),
                    1)
            ]
        };

        using var stream = new MemoryStream();
        ManifestCborWriter.WriteManifest(manifest, stream);

        var json = CborJsonConverter.ToJson(stream.ToArray());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("sprite-loom", root.GetProperty("format").GetString());
        Assert.Equal("heroes", root.GetProperty("package").GetProperty("id").GetString());
        Assert.Equal(2, root.GetProperty("uris").GetArrayLength());
        Assert.Equal(uint.MaxValue, root.GetProperty("frames").GetProperty("to-record")[0].GetUInt32());
        Assert.Equal(0, root.GetProperty("frames").GetProperty("records")[0].GetProperty("page").GetInt32());
        Assert.Equal(
            Convert.ToBase64String(Digest.XxHash64([1, 2, 3]).Value),
            root.GetProperty("pages")[0].GetProperty("container-digest").GetProperty("value").GetString());
        Assert.Equal(1, root.GetProperty("animations")[0].GetProperty("frame-count").GetInt32());
    }

    [Fact]
    public void Rejects_empty_payload()
    {
        Assert.Throws<InvalidDataException>(() => CborJsonConverter.ToJson([]));
    }
}
