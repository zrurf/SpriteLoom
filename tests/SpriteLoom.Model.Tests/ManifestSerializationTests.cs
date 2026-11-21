using SpriteLoom.Foundation;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;

namespace SpriteLoom.Model.Tests;

public sealed class ManifestSerializationTests
{
    [Fact]
    public void Json_manifest_contains_expected_structure()
    {
        var manifest = CreateManifest();
        using var stream = new MemoryStream();

        ManifestJsonWriter.WriteManifest(manifest, stream);
        using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
        var root = document.RootElement;

        Assert.Equal("sprite-loom", root.GetProperty("format").GetString());
        Assert.Equal(3, root.GetProperty("uris").GetArrayLength());
        Assert.Equal(1, root.GetProperty("pages").GetArrayLength());
        Assert.Equal(1, root.GetProperty("frames").GetProperty("records").GetArrayLength());
        Assert.Equal(1, root.GetProperty("animations").GetArrayLength());
    }

    [Fact]
    public void Cbor_summary_round_trips_counts()
    {
        var manifest = CreateManifest();
        using var stream = new MemoryStream();

        ManifestCborWriter.WriteManifest(manifest, stream);
        stream.Position = 0;

        var summary = ManifestReader.ReadSummary(stream, ManifestFormat.Cbor);

        Assert.Equal("sprite-loom", summary.Format);
        Assert.Equal(3, summary.UriCount);
        Assert.Equal(1, summary.PageCount);
        Assert.Equal(1, summary.FrameCount);
        Assert.Equal(1, summary.AnimationCount);
    }

    [Fact]
    public void Json_summary_round_trips_counts()
    {
        var manifest = CreateManifest();
        using var stream = new MemoryStream();

        ManifestJsonWriter.WriteManifest(manifest, stream);
        stream.Position = 0;

        var summary = ManifestReader.ReadSummary(stream, ManifestFormat.Json);

        Assert.Equal(3, summary.UriCount);
        Assert.Equal(1, summary.PageCount);
        Assert.Equal(1, summary.FrameCount);
        Assert.Equal(1, summary.AnimationCount);
    }

    [Fact]
    public void Writers_are_deterministic()
    {
        var manifest = CreateManifest();
        var container = CreateContainer();
        var animation = CreateAnimation();

        Assert.Equal(Write(manifest, ManifestJsonWriter.WriteManifest), Write(manifest, ManifestJsonWriter.WriteManifest));
        Assert.Equal(Write(manifest, ManifestCborWriter.WriteManifest), Write(manifest, ManifestCborWriter.WriteManifest));
        Assert.Equal(Write(container, ManifestJsonWriter.WriteContainer), Write(container, ManifestJsonWriter.WriteContainer));
        Assert.Equal(Write(container, ManifestCborWriter.WriteContainer), Write(container, ManifestCborWriter.WriteContainer));
        Assert.Equal(Write(animation, ManifestJsonWriter.WriteAnimation), Write(animation, ManifestJsonWriter.WriteAnimation));
        Assert.Equal(Write(animation, ManifestCborWriter.WriteAnimation), Write(animation, ManifestCborWriter.WriteAnimation));
    }

    private static byte[] Write<T>(T value, Action<T, Stream> write)
    {
        using var stream = new MemoryStream();
        write(value, stream);
        return stream.ToArray();
    }

    private static AtlasManifest CreateManifest() =>
        new()
        {
            BundleId = "heroes",
            PackageId = "heroes",
            PackageDisplayName = "Heroes",
            GeneratorName = "SpriteLoom",
            GeneratorVersion = "0.1.0",
            Uris = ["anim://heroes/run", "frame://heroes/run/0001", "page://heroes/0000"],
            Pages =
            [
                new AtlasPage(
                    0,
                    2,
                    "run",
                    0,
                    true,
                    "page/0000.slatlas/container.json",
                    Digest.XxHash64([1]),
                    64,
                    64,
                    "rgba8888",
                    [new AtlasVariant("png", "png", "page/0000.slatlas/v0.png", Digest.XxHash64([2]))])
            ],
            ToRecord = [0xFFFFFFFF, 0, 0xFFFFFFFF],
            Records = [new AtlasFrameRecord(0, 0, 16, 16, 2, 2, 128, 128, 1)],
            Animations = [new AtlasAnimationRef(0, "anim/run.anim.json", Digest.XxHash64([3]), 1)]
        };

    private static AtlasContainer CreateContainer() =>
        new(
            0,
            64,
            64,
            "rgba8888",
            "straight",
            [new AtlasVariant("png", "png", "v0.png", Digest.XxHash64([2]))],
            [new AtlasStoredFrame(new IntRect(2, 2, 12, 12), 0, Digest.XxHash64([4]))]);

    private static AnimationDocument CreateAnimation() =>
        new()
        {
            Uri = "anim://heroes/run",
            Frames = [1],
            DurationsMs = [100],
            Loop = true,
            Playback = "forward"
        };
}
