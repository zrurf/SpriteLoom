using System.Text.Encodings.Web;
using System.Text.Json;
using SpriteLoom.Model.Manifest;

namespace SpriteLoom.Model.Serialization;

public static class ManifestJsonWriter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void WriteManifest(AtlasManifest manifest, Stream output)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(output);

        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteString("format", "sprite-loom");
        writer.WriteNumber("format-major", AtlasManifest.CurrentFormatMajor);
        writer.WriteNumber("format-minor", AtlasManifest.CurrentFormatMinor);

        writer.WriteStartObject("bundle");
        writer.WriteString("id", manifest.BundleId);
        writer.WriteStartArray("requires");
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartObject("package");
        writer.WriteString("id", manifest.PackageId);
        if (manifest.PackageDisplayName is not null)
        {
            writer.WriteString("display-name", manifest.PackageDisplayName);
        }

        writer.WriteEndObject();

        writer.WriteStartObject("generator");
        writer.WriteString("name", manifest.GeneratorName);
        writer.WriteString("version", manifest.GeneratorVersion);
        writer.WriteEndObject();

        writer.WriteStartArray("pages");
        foreach (var page in manifest.Pages)
        {
            WritePage(writer, page);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("uris");
        foreach (var uri in manifest.Uris)
        {
            writer.WriteStringValue(uri);
        }

        writer.WriteEndArray();

        writer.WriteStartObject("frames");
        writer.WriteStartArray("to-record");
        foreach (var value in manifest.ToRecord)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("records");
        foreach (var record in manifest.Records)
        {
            WriteRecord(writer, record);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("animations");
        foreach (var animation in manifest.Animations)
        {
            WriteAnimationRef(writer, animation);
        }

        writer.WriteEndArray();

        writer.WriteStartArray("blobs");
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    public static void WriteContainer(AtlasContainer container, Stream output)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(output);

        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteString("format", "sprite-loom/container");
        writer.WriteNumber("format-major", AtlasManifest.CurrentFormatMajor);
        writer.WriteNumber("format-minor", AtlasManifest.CurrentFormatMinor);
        writer.WriteNumber("page", container.Page);
        WriteSize(writer, "size", container.Width, container.Height);
        writer.WriteString("pixel-format", container.PixelFormat);
        writer.WriteString("alpha", container.Alpha);
        WriteVariants(writer, container.Variants);
        writer.WriteStartArray("stored");
        foreach (var stored in container.Stored)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rect");
            writer.WriteNumberValue(stored.Rect.X);
            writer.WriteNumberValue(stored.Rect.Y);
            writer.WriteNumberValue(stored.Rect.Width);
            writer.WriteNumberValue(stored.Rect.Height);
            writer.WriteEndArray();
            writer.WriteNumber("rotation", stored.Rotation);
            WriteDigest(writer, "digest", stored.Digest);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    public static void WriteAnimation(AnimationDocument animation, Stream output)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(output);

        using var writer = new Utf8JsonWriter(output, Options);
        writer.WriteStartObject();
        writer.WriteString("format", "sprite-loom/animation");
        writer.WriteNumber("format-major", AnimationDocument.CurrentFormatMajor);
        writer.WriteNumber("format-minor", AnimationDocument.CurrentFormatMinor);
        writer.WriteString("uri", animation.Uri);
        writer.WriteStartArray("frames");
        foreach (var frame in animation.Frames)
        {
            writer.WriteNumberValue(frame);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("durations-ms");
        foreach (var duration in animation.DurationsMs)
        {
            writer.WriteNumberValue(duration);
        }

        writer.WriteEndArray();
        writer.WriteBoolean("loop", animation.Loop);
        writer.WriteString("playback", animation.Playback);
        writer.WriteStartArray("events");
        foreach (var animationEvent in animation.Events)
        {
            writer.WriteStartObject();
            writer.WriteNumber("frame", animationEvent.Frame);
            writer.WriteString("kind", animationEvent.Kind);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WritePage(Utf8JsonWriter writer, AtlasPage page)
    {
        writer.WriteStartObject();
        writer.WriteNumber("uri", page.UriIndex);
        writer.WriteString("group", page.Group);
        writer.WriteNumber("order", page.Order);
        writer.WriteBoolean("required", page.Required);
        writer.WriteString("container", page.ContainerPath);
        WriteDigest(writer, "container-digest", page.ContainerDigest);
        WriteSize(writer, "size", page.Width, page.Height);
        writer.WriteString("pixel-format", page.PixelFormat);
        WriteVariants(writer, page.Variants);
        writer.WriteEndObject();
    }

    private static void WriteVariants(Utf8JsonWriter writer, IReadOnlyList<AtlasVariant> variants)
    {
        writer.WriteStartArray("variants");
        foreach (var variant in variants)
        {
            writer.WriteStartObject();
            writer.WriteString("id", variant.Id);
            writer.WriteString("codec", variant.Codec);
            writer.WriteString("file", variant.File);
            WriteDigest(writer, "digest", variant.Digest);

            if (variant.PixelFormat is not null)
            {
                writer.WriteString("pixel-format", variant.PixelFormat);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRecord(Utf8JsonWriter writer, AtlasFrameRecord record)
    {
        writer.WriteStartObject();
        writer.WriteNumber("page", record.Page);
        writer.WriteNumber("stored", record.Stored);
        writer.WriteStartArray("origin");
        writer.WriteNumberValue(record.OriginWidth);
        writer.WriteNumberValue(record.OriginHeight);
        writer.WriteEndArray();
        writer.WriteStartArray("offset");
        writer.WriteNumberValue(record.OffsetX);
        writer.WriteNumberValue(record.OffsetY);
        writer.WriteEndArray();
        writer.WriteStartArray("pivot");
        writer.WriteNumberValue(record.PivotX);
        writer.WriteNumberValue(record.PivotY);
        writer.WriteEndArray();
        writer.WriteNumber("flags", record.Flags);
        writer.WriteEndObject();
    }

    private static void WriteAnimationRef(Utf8JsonWriter writer, AtlasAnimationRef animation)
    {
        writer.WriteStartObject();
        writer.WriteNumber("uri", animation.UriIndex);
        writer.WriteString("file", animation.File);
        WriteDigest(writer, "digest", animation.Digest);
        writer.WriteNumber("frame-count", animation.FrameCount);
        writer.WriteEndObject();
    }

    private static void WriteDigest(Utf8JsonWriter writer, string name, Digest digest)
    {
        writer.WriteStartObject(name);
        writer.WriteString("alg", digest.Algorithm);
        writer.WriteString("value", digest.ToHex());
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, string name, int width, int height)
    {
        writer.WriteStartArray(name);
        writer.WriteNumberValue(width);
        writer.WriteNumberValue(height);
        writer.WriteEndArray();
    }
}
