using SpriteLoom.Foundation;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;

namespace SpriteLoom.Gui.Tests;

public sealed class LoadedPackageTests
{
    [Fact]
    public void Opens_cbor_manifest_containers_and_animations()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-gui-" + Guid.NewGuid().ToString("N"));
        var containerDirectory = Path.Combine(root, "page", "0000.slatlas");
        var animationDirectory = Path.Combine(root, "anim");
        Directory.CreateDirectory(containerDirectory);
        Directory.CreateDirectory(animationDirectory);

        try
        {
            var pageDigest = Digest.XxHash64([1]);
            var variantDigest = Digest.XxHash64([2]);
            var variants = new[]
            {
                new AtlasVariant("png", "png", "page/0000.slatlas/page.png", variantDigest)
            };
            var manifest = new AtlasManifest
            {
                BundleId = "bundle",
                PackageId = "heroes",
                GeneratorName = "SpriteLoom",
                GeneratorVersion = "0.1.0",
                Uris = ["pkg://heroes", "frame://heroes/run/0001", "anim://heroes/run"],
                Pages =
                [
                    new AtlasPage(
                        0,
                        0,
                        "default",
                        0,
                        true,
                        "page/0000.slatlas/container.cbor",
                        pageDigest,
                        32,
                        32,
                        "rgba8888",
                        variants)
                ],
                ToRecord = [uint.MaxValue, 0, uint.MaxValue],
                Records = [new AtlasFrameRecord(0, 0, 8, 8, 1, 2, 64, 64, 0)],
                Animations = [new AtlasAnimationRef(2, "anim/run.anim.cbor", Digest.XxHash64([3]), 1)]
            };
            var container = new AtlasContainer(
                0,
                32,
                32,
                "rgba8888",
                "opaque",
                variants,
                [new AtlasStoredFrame(new IntRect(4, 6, 8, 8), 0, variantDigest)]);
            var animation = new AnimationDocument
            {
                Uri = "anim://heroes/run",
                Frames = [1],
                DurationsMs = [100],
                Loop = true,
                Playback = "forward",
                Events = [new AnimationEvent(0, "footstep")]
            };

            WriteCbor(
                Path.Combine(root, "manifest.cbor"),
                stream => ManifestCborWriter.WriteManifest(manifest, stream));
            WriteCbor(
                Path.Combine(containerDirectory, "container.cbor"),
                stream => ManifestCborWriter.WriteContainer(container, stream));
            WriteCbor(
                Path.Combine(animationDirectory, "run.anim.cbor"),
                stream => ManifestCborWriter.WriteAnimation(animation, stream));
            File.WriteAllBytes(Path.Combine(containerDirectory, "page.png"), [0x89, 0x50, 0x4E, 0x47]);

            using var package = LoadedPackage.Open(root);

            Assert.Equal("CBOR", package.ManifestFormat);
            Assert.Equal("heroes", package.PackageId);
            Assert.Equal(1, package.PageCount);
            Assert.Equal(1, package.FrameCount);
            Assert.Equal(1, package.AnimationCount);
            Assert.Contains("sprite-loom", package.ManifestText);
            Assert.Equal(new[] { "anim://heroes/run" }, package.AnimationUris);

            var frame = package.GetFrame("frame://heroes/run/0001");
            Assert.Equal(0, frame.Page);
            Assert.Equal(new Avalonia.Rect(4, 6, 8, 8), frame.Rect);
            Assert.Equal(8, frame.OriginWidth);
            Assert.Equal(8, frame.OriginHeight);
            Assert.Equal(1, frame.OffsetX);
            Assert.Equal(2, frame.OffsetY);

            var loadedAnimation = package.LoadAnimation("anim://heroes/run");
            Assert.Equal(new[] { "frame://heroes/run/0001" }, loadedAnimation.FrameUris);
            Assert.Equal(new[] { 100 }, loadedAnimation.DurationsMs);
            Assert.True(loadedAnimation.Loop);
            Assert.Equal("forward", loadedAnimation.Playback);
            Assert.Equal([new AnimationEventInfo(0, "footstep")], loadedAnimation.Events);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteCbor(string path, Action<Stream> write)
    {
        using var stream = File.Create(path);
        write(stream);
    }
}
