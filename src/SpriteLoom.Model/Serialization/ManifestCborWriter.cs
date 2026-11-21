using System.Formats.Cbor;
using SpriteLoom.Model.Manifest;

namespace SpriteLoom.Model.Serialization;

public static class ManifestCborWriter
{
    public static void WriteManifest(AtlasManifest manifest, Stream output)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        CanonicalCbor.WriteMap(
            writer,
            ("format", w => w.WriteTextString("sprite-loom")),
            ("format-major", w => w.WriteUInt32(AtlasManifest.CurrentFormatMajor)),
            ("format-minor", w => w.WriteUInt32(AtlasManifest.CurrentFormatMinor)),
            ("bundle", w => WriteBundle(w, manifest)),
            ("package", w => WritePackage(w, manifest)),
            ("generator", w => WriteGenerator(w, manifest)),
            ("pages", w => WritePages(w, manifest.Pages)),
            ("uris", w => WriteUris(w, manifest.Uris)),
            ("frames", w => WriteFrames(w, manifest)),
            ("animations", w => WriteAnimations(w, manifest.Animations)),
            ("blobs", w => WriteEmptyArray(w)));
        output.Write(writer.Encode());
    }

    public static void WriteContainer(AtlasContainer container, Stream output)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        CanonicalCbor.WriteMap(
            writer,
            ("format", w => w.WriteTextString("sprite-loom/container")),
            ("format-major", w => w.WriteUInt32(AtlasManifest.CurrentFormatMajor)),
            ("format-minor", w => w.WriteUInt32(AtlasManifest.CurrentFormatMinor)),
            ("page", w => w.WriteUInt32((uint)container.Page)),
            ("size", w => WriteSize(w, container.Width, container.Height)),
            ("pixel-format", w => w.WriteTextString(container.PixelFormat)),
            ("alpha", w => w.WriteTextString(container.Alpha)),
            ("variants", w => WriteVariants(w, container.Variants)),
            ("stored", w => WriteStored(w, container.Stored)));
        output.Write(writer.Encode());
    }

    public static void WriteAnimation(AnimationDocument animation, Stream output)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new CborWriter(CborConformanceMode.Canonical);
        CanonicalCbor.WriteMap(
            writer,
            ("format", w => w.WriteTextString("sprite-loom/animation")),
            ("format-major", w => w.WriteUInt32(AnimationDocument.CurrentFormatMajor)),
            ("format-minor", w => w.WriteUInt32(AnimationDocument.CurrentFormatMinor)),
            ("uri", w => w.WriteTextString(animation.Uri)),
            ("frames", w => WriteUIntArray(w, animation.Frames)),
            ("durations-ms", w => WriteIntArray(w, animation.DurationsMs)),
            ("loop", w => w.WriteBoolean(animation.Loop)),
            ("playback", w => w.WriteTextString(animation.Playback)),
            ("events", w => WriteEvents(w, animation.Events)));
        output.Write(writer.Encode());
    }

    private static void WriteBundle(CborWriter writer, AtlasManifest manifest) =>
        CanonicalCbor.WriteMap(
            writer,
            ("id", w => w.WriteTextString(manifest.BundleId)),
            ("requires", WriteEmptyArray));

    private static void WritePackage(CborWriter writer, AtlasManifest manifest)
    {
        if (manifest.PackageDisplayName is null)
        {
            CanonicalCbor.WriteMap(
                writer,
                ("id", w => w.WriteTextString(manifest.PackageId)));
            return;
        }

        CanonicalCbor.WriteMap(
            writer,
            ("id", w => w.WriteTextString(manifest.PackageId)),
            ("display-name", w => w.WriteTextString(manifest.PackageDisplayName)));
    }

    private static void WriteGenerator(CborWriter writer, AtlasManifest manifest) =>
        CanonicalCbor.WriteMap(
            writer,
            ("name", w => w.WriteTextString(manifest.GeneratorName)),
            ("version", w => w.WriteTextString(manifest.GeneratorVersion)));

    private static void WritePages(CborWriter writer, IReadOnlyList<AtlasPage> pages)
    {
        writer.WriteStartArray(pages.Count);
        foreach (var page in pages)
        {
            CanonicalCbor.WriteMap(
                writer,
                ("uri", w => w.WriteUInt32((uint)page.UriIndex)),
                ("group", w => w.WriteTextString(page.Group)),
                ("order", w => w.WriteUInt32((uint)page.Order)),
                ("required", w => w.WriteBoolean(page.Required)),
                ("container", w => w.WriteTextString(page.ContainerPath)),
                ("container-digest", w => WriteDigest(w, page.ContainerDigest)),
                ("size", w => WriteSize(w, page.Width, page.Height)),
                ("pixel-format", w => w.WriteTextString(page.PixelFormat)),
                ("variants", w => WriteVariants(w, page.Variants)));
        }

        writer.WriteEndArray();
    }

    private static void WriteUris(CborWriter writer, IReadOnlyList<string> uris)
    {
        writer.WriteStartArray(uris.Count);
        foreach (var uri in uris)
        {
            writer.WriteTextString(uri);
        }

        writer.WriteEndArray();
    }

    private static void WriteFrames(CborWriter writer, AtlasManifest manifest) =>
        CanonicalCbor.WriteMap(
            writer,
            ("to-record", w => WriteUIntArray(w, manifest.ToRecord)),
            ("records", w =>
            {
                w.WriteStartArray(manifest.Records.Count);
                foreach (var record in manifest.Records)
                {
                    CanonicalCbor.WriteMap(
                        w,
                        ("page", x => x.WriteUInt32((uint)record.Page)),
                        ("stored", x => x.WriteUInt32((uint)record.Stored)),
                        ("origin", x => WriteSize(x, record.OriginWidth, record.OriginHeight)),
                        ("offset", x => WriteSize(x, record.OffsetX, record.OffsetY)),
                        ("pivot", x => WriteSize(x, (int)record.PivotX, (int)record.PivotY)),
                        ("flags", x => x.WriteUInt32(record.Flags)));
                }

                w.WriteEndArray();
            }));

    private static void WriteAnimations(CborWriter writer, IReadOnlyList<AtlasAnimationRef> animations)
    {
        writer.WriteStartArray(animations.Count);
        foreach (var animation in animations)
        {
            CanonicalCbor.WriteMap(
                writer,
                ("uri", w => w.WriteUInt32((uint)animation.UriIndex)),
                ("file", w => w.WriteTextString(animation.File)),
                ("digest", w => WriteDigest(w, animation.Digest)),
                ("frame-count", w => w.WriteUInt32((uint)animation.FrameCount)));
        }

        writer.WriteEndArray();
    }

    private static void WriteVariants(CborWriter writer, IReadOnlyList<AtlasVariant> variants)
    {
        writer.WriteStartArray(variants.Count);
        foreach (var variant in variants)
        {
            var entries = new List<(string Key, Action<CborWriter> Write)>
            {
                ("id", w => w.WriteTextString(variant.Id)),
                ("codec", w => w.WriteTextString(variant.Codec)),
                ("file", w => w.WriteTextString(variant.File)),
                ("digest", w => WriteDigest(w, variant.Digest))
            };

            if (variant.PixelFormat is not null)
            {
                entries.Add(("pixel-format", w => w.WriteTextString(variant.PixelFormat)));
            }

            CanonicalCbor.WriteMap(writer, entries.ToArray());
        }

        writer.WriteEndArray();
    }

    private static void WriteStored(CborWriter writer, IReadOnlyList<AtlasStoredFrame> stored)
    {
        writer.WriteStartArray(stored.Count);
        foreach (var frame in stored)
        {
            CanonicalCbor.WriteMap(
                writer,
                ("rect", w => WriteSize(w, frame.Rect.X, frame.Rect.Y, frame.Rect.Width, frame.Rect.Height)),
                ("rotation", w => w.WriteUInt32((uint)frame.Rotation)),
                ("digest", w => WriteDigest(w, frame.Digest)));
        }

        writer.WriteEndArray();
    }

    private static void WriteEvents(CborWriter writer, IReadOnlyList<AnimationEvent> events)
    {
        writer.WriteStartArray(events.Count);
        foreach (var animationEvent in events)
        {
            CanonicalCbor.WriteMap(
                writer,
                ("frame", w => w.WriteUInt32((uint)animationEvent.Frame)),
                ("kind", w => w.WriteTextString(animationEvent.Kind)));
        }

        writer.WriteEndArray();
    }

    private static void WriteDigest(CborWriter writer, Digest digest) =>
        CanonicalCbor.WriteMap(
            writer,
            ("alg", w => w.WriteTextString(digest.Algorithm)),
            ("value", w => w.WriteByteString(digest.Value)));

    private static void WriteSize(CborWriter writer, int width, int height) =>
        WriteSize(writer, width, height, null, null);

    private static void WriteSize(CborWriter writer, int width, int height, int? third, int? fourth)
    {
        var count = third.HasValue ? 4 : 2;
        writer.WriteStartArray(count);
        writer.WriteUInt32((uint)width);
        writer.WriteUInt32((uint)height);
        if (third.HasValue)
        {
            writer.WriteUInt32((uint)third.Value);
            writer.WriteUInt32((uint)fourth!.Value);
        }

        writer.WriteEndArray();
    }

    private static void WriteUIntArray(CborWriter writer, IReadOnlyList<uint> values)
    {
        writer.WriteStartArray(values.Count);
        foreach (var value in values)
        {
            writer.WriteUInt32(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteIntArray(CborWriter writer, IReadOnlyList<int> values)
    {
        writer.WriteStartArray(values.Count);
        foreach (var value in values)
        {
            writer.WriteUInt32((uint)value);
        }

        writer.WriteEndArray();
    }

    private static void WriteEmptyArray(CborWriter writer)
    {
        writer.WriteStartArray(0);
        writer.WriteEndArray();
    }
}
