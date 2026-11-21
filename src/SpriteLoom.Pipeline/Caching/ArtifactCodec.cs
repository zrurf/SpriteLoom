using System.Text;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline.Caching;

internal static class ArtifactCodec
{
    private const byte PageLayouts = 1;
    private const byte ComposedPages = 2;
    private const byte EncodedPages = 3;
    private const byte MipmappedPages = 4;
    private const byte TexturePages = 5;

    public static void Write(BinaryWriter writer, Artifact artifact)
    {
        writer.Write((byte)artifact.Kind);

        switch (artifact.Payload)
        {
            case FrameSet frames:
                WriteFrameSet(writer, frames);
                break;
            case PageLayoutSet layouts:
                writer.Write(PageLayouts);
                WritePageLayouts(writer, layouts);
                break;
            case ComposedPageSet composed:
                writer.Write(ComposedPages);
                WritePageImages(writer, composed.Pages);
                break;
            case EncodedPageSet encoded:
                writer.Write(EncodedPages);
                WritePageImages(writer, encoded.Pages);
                break;
            case MipmapPageSet mipmapped:
                writer.Write(MipmappedPages);
                WriteMipmapPages(writer, mipmapped.Pages);
                break;
            case TexturePageSet textures:
                writer.Write(TexturePages);
                WriteTexturePages(writer, textures.Pages);
                break;
            case AnimationDraftSet drafts:
                WriteAnimationDrafts(writer, drafts);
                break;
            case BuildPackage package:
                WritePackage(writer, package);
                break;
            case PackageOutput output:
                writer.Write(output.Path);
                break;
            default:
                throw new InvalidOperationException(
                    $"Artifact payload '{artifact.Payload.GetType().Name}' cannot be cached.");
        }
    }

    public static Artifact Read(BinaryReader reader)
    {
        var kind = (ArtifactKind)reader.ReadByte();

        object payload = kind switch
        {
            ArtifactKind.FrameSet => ReadFrameSet(reader),
            ArtifactKind.PageSet => ReadPageSet(reader),
            ArtifactKind.AnimationSet => ReadAnimationDrafts(reader),
            ArtifactKind.Manifest => ReadPackage(reader),
            ArtifactKind.Package => new PackageOutput(reader.ReadString()),
            _ => throw new InvalidDataException($"Artifact kind '{kind}' cannot be restored.")
        };

        return new Artifact(kind, 0, payload);
    }

    private static void WriteFrameSet(BinaryWriter writer, FrameSet frames)
    {
        writer.Write(frames.Frames.Count);

        foreach (var frame in frames.Frames)
        {
            writer.Write(frame.Uri.Value);
            writer.Write(frame.SourceId);
            writer.Write(frame.SourceIndex);
            WriteImage(writer, frame.Pixels);
            writer.Write(frame.OriginWidth);
            writer.Write(frame.OriginHeight);
            writer.Write(frame.OffsetX);
            writer.Write(frame.OffsetY);
            writer.Write(frame.PivotX);
            writer.Write(frame.PivotY);
            writer.Write(frame.ContentHash.Low);
            writer.Write(frame.ContentHash.High);
            writer.Write(frame.Trimmed);
            writer.Write(frame.StoredIndex);
        }

        writer.Write(frames.Stored.Count);

        foreach (var stored in frames.Stored)
        {
            writer.Write(stored.Hash.Low);
            writer.Write(stored.Hash.High);
            WriteImage(writer, stored.Pixels);
        }
    }

    private static FrameSet ReadFrameSet(BinaryReader reader)
    {
        var frameCount = reader.ReadInt32();
        var frames = new List<Frame>(frameCount);

        for (var index = 0; index < frameCount; index++)
        {
            frames.Add(new Frame
            {
                Uri = SloomIri.Parse(reader.ReadString()),
                SourceId = reader.ReadString(),
                SourceIndex = reader.ReadInt32(),
                Pixels = ReadImage(reader),
                OriginWidth = reader.ReadInt32(),
                OriginHeight = reader.ReadInt32(),
                OffsetX = reader.ReadInt32(),
                OffsetY = reader.ReadInt32(),
                PivotX = reader.ReadUInt32(),
                PivotY = reader.ReadUInt32(),
                ContentHash = new ContentHash(reader.ReadUInt64(), reader.ReadUInt64()),
                Trimmed = reader.ReadBoolean(),
                StoredIndex = reader.ReadInt32()
            });
        }

        var storedCount = reader.ReadInt32();
        var stored = new List<StoredFrame>(storedCount);

        for (var index = 0; index < storedCount; index++)
        {
            stored.Add(new StoredFrame
            {
                Hash = new ContentHash(reader.ReadUInt64(), reader.ReadUInt64()),
                Pixels = ReadImage(reader)
            });
        }

        return new FrameSet { Frames = frames, Stored = stored };
    }

    private static void WritePageLayouts(BinaryWriter writer, PageLayoutSet layouts)
    {
        writer.Write(layouts.Pages.Count);

        foreach (var page in layouts.Pages)
        {
            WriteLayout(writer, page);
        }
    }

    private static void WritePageImages(BinaryWriter writer, IReadOnlyList<PageImage> pages)
    {
        writer.Write(pages.Count);

        foreach (var page in pages)
        {
            writer.Write(page.Index);
            WriteImage(writer, page.Pixels);
            WriteLayout(writer, page.Layout);
            writer.Write(page.EncodedPng is not null);

            if (page.EncodedPng is not null)
            {
                writer.Write(page.EncodedPng.Length);
                writer.Write(page.EncodedPng);
            }

            writer.Write(page.Variants.Count);

            foreach (var variant in page.Variants)
            {
                writer.Write(variant.Id);
                writer.Write(variant.Codec);
                writer.Write(variant.File);
                WriteNullableString(writer, variant.PixelFormat);
                writer.Write(variant.Data.Length);
                writer.Write(variant.Data);
            }
        }
    }

    private static object ReadPageSet(BinaryReader reader)
    {
        var subtype = reader.ReadByte();

        return subtype switch
        {
            PageLayouts => ReadPageLayouts(reader),
            ComposedPages => new ComposedPageSet(ReadPageImages(reader)),
            EncodedPages => new EncodedPageSet(ReadPageImages(reader)),
            MipmappedPages => new MipmapPageSet(ReadMipmapPages(reader)),
            TexturePages => new TexturePageSet(ReadTexturePages(reader)),
            _ => throw new InvalidDataException($"Unknown page-set subtype: {subtype}")
        };
    }

    private static PageLayoutSet ReadPageLayouts(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pages = new List<PageLayout>(count);

        for (var index = 0; index < count; index++)
        {
            pages.Add(ReadLayout(reader));
        }

        return new PageLayoutSet(pages);
    }

    private static IReadOnlyList<PageImage> ReadPageImages(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pages = new List<PageImage>(count);

        for (var index = 0; index < count; index++)
        {
            var pageIndex = reader.ReadInt32();
            var pixels = ReadImage(reader);
            var layout = ReadLayout(reader);
            var hasPng = reader.ReadBoolean();
            var page = new PageImage
            {
                Index = pageIndex,
                Pixels = pixels,
                Layout = layout,
                EncodedPng = hasPng ? reader.ReadBytes(reader.ReadInt32()) : null
            };
            var variantCount = reader.ReadInt32();

            for (var variant = 0; variant < variantCount; variant++)
            {
                page.Variants.Add(new EncodedPageVariant(
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadBytes(reader.ReadInt32()),
                    ReadNullableString(reader)));
            }

            pages.Add(page);
        }

        return pages;
    }

    private static void WriteMipmapPages(BinaryWriter writer, IReadOnlyList<MipmapPage> pages)
    {
        writer.Write(pages.Count);

        foreach (var page in pages)
        {
            writer.Write(page.Index);
            WriteLayout(writer, page.Layout);
            writer.Write(page.Levels.Count);

            foreach (var level in page.Levels)
            {
                WriteImage(writer, level);
            }
        }
    }

    private static IReadOnlyList<MipmapPage> ReadMipmapPages(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pages = new List<MipmapPage>(count);

        for (var index = 0; index < count; index++)
        {
            var pageIndex = reader.ReadInt32();
            var layout = ReadLayout(reader);
            var levelCount = reader.ReadInt32();
            var levels = new List<RgbaImage>(levelCount);

            for (var level = 0; level < levelCount; level++)
            {
                levels.Add(ReadImage(reader));
            }

            pages.Add(new MipmapPage(pageIndex, layout, levels));
        }

        return pages;
    }

    private static void WriteTexturePages(BinaryWriter writer, IReadOnlyList<TexturePage> pages)
    {
        writer.Write(pages.Count);

        foreach (var page in pages)
        {
            writer.Write(page.Index);
            WriteLayout(writer, page.Layout);
            writer.Write(page.Variants.Count);

            foreach (var variant in page.Variants)
            {
                writer.Write(variant.Id);
                writer.Write(variant.Codec);
                writer.Write(variant.File);
                writer.Write(variant.PixelFormat);
                writer.Write(variant.Data.Length);
                writer.Write(variant.Data);
            }
        }
    }

    private static IReadOnlyList<TexturePage> ReadTexturePages(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var pages = new List<TexturePage>(count);

        for (var index = 0; index < count; index++)
        {
            var pageIndex = reader.ReadInt32();
            var layout = ReadLayout(reader);
            var variantCount = reader.ReadInt32();
            var variants = new List<TexturePageVariant>(variantCount);

            for (var variant = 0; variant < variantCount; variant++)
            {
                variants.Add(new TexturePageVariant(
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadString(),
                    reader.ReadBytes(reader.ReadInt32())));
            }

            pages.Add(new TexturePage(pageIndex, layout, variants));
        }

        return pages;
    }

    private static void WriteAnimationDrafts(BinaryWriter writer, AnimationDraftSet drafts)    {
        writer.Write(drafts.Animations.Count);

        foreach (var draft in drafts.Animations)
        {
            writer.Write(draft.Uri);
            writer.Write(draft.FrameUris.Count);

            foreach (var uri in draft.FrameUris)
            {
                writer.Write(uri.Value);
            }

            writer.Write(draft.DurationMs);
            writer.Write(draft.Loop);
            writer.Write(draft.Playback);
        }
    }

    private static AnimationDraftSet ReadAnimationDrafts(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var drafts = new List<AnimationDraft>(count);

        for (var index = 0; index < count; index++)
        {
            var uri = reader.ReadString();
            var frameCount = reader.ReadInt32();
            var frames = new List<SloomIri>(frameCount);

            for (var frame = 0; frame < frameCount; frame++)
            {
                frames.Add(SloomIri.Parse(reader.ReadString()));
            }

            drafts.Add(new AnimationDraft(
                uri,
                frames,
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadString()));
        }

        return new AnimationDraftSet(drafts);
    }

    private static void WritePackage(BinaryWriter writer, BuildPackage package)
    {
        writer.Write((byte)package.Format);
        WriteManifest(writer, package.Manifest);
        writer.Write(package.Files.Count);

        foreach (var (path, bytes) in package.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Write(path);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    private static BuildPackage ReadPackage(BinaryReader reader)
    {
        var format = (ManifestFormat)reader.ReadByte();
        var manifest = ReadManifest(reader);
        var count = reader.ReadInt32();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        for (var index = 0; index < count; index++)
        {
            var path = reader.ReadString();
            files[path] = reader.ReadBytes(reader.ReadInt32());
        }

        return new BuildPackage(manifest, files, format);
    }

    private static void WriteManifest(BinaryWriter writer, AtlasManifest manifest)
    {
        writer.Write(manifest.BundleId);
        writer.Write(manifest.PackageId);
        WriteNullableString(writer, manifest.PackageDisplayName);
        writer.Write(manifest.GeneratorName);
        writer.Write(manifest.GeneratorVersion);
        WriteStrings(writer, manifest.Uris);

        writer.Write(manifest.Pages.Count);
        foreach (var page in manifest.Pages)
        {
            writer.Write(page.Index);
            writer.Write(page.UriIndex);
            writer.Write(page.Group);
            writer.Write(page.Order);
            writer.Write(page.Required);
            writer.Write(page.ContainerPath);
            WriteDigest(writer, page.ContainerDigest);
            writer.Write(page.Width);
            writer.Write(page.Height);
            writer.Write(page.PixelFormat);
            WriteVariants(writer, page.Variants);
        }

        writer.Write(manifest.ToRecord.Count);
        foreach (var value in manifest.ToRecord)
        {
            writer.Write(value);
        }

        writer.Write(manifest.Records.Count);
        foreach (var record in manifest.Records)
        {
            writer.Write(record.Page);
            writer.Write(record.Stored);
            writer.Write(record.OriginWidth);
            writer.Write(record.OriginHeight);
            writer.Write(record.OffsetX);
            writer.Write(record.OffsetY);
            writer.Write(record.PivotX);
            writer.Write(record.PivotY);
            writer.Write(record.Flags);
        }

        writer.Write(manifest.Animations.Count);
        foreach (var animation in manifest.Animations)
        {
            writer.Write(animation.UriIndex);
            writer.Write(animation.File);
            WriteDigest(writer, animation.Digest);
            writer.Write(animation.FrameCount);
        }

        writer.Write(manifest.Blobs.Count);
        foreach (var blob in manifest.Blobs)
        {
            writer.Write(blob.UriIndex);
            writer.Write(blob.File);
            WriteDigest(writer, blob.Digest);
            writer.Write(blob.Size);
        }
    }

    private static AtlasManifest ReadManifest(BinaryReader reader)
    {
        var bundleId = reader.ReadString();
        var packageId = reader.ReadString();
        var displayName = ReadNullableString(reader);
        var generatorName = reader.ReadString();
        var generatorVersion = reader.ReadString();
        var uris = ReadStrings(reader);
        var pageCount = reader.ReadInt32();
        var pages = new List<AtlasPage>(pageCount);

        for (var index = 0; index < pageCount; index++)
        {
            pages.Add(new AtlasPage(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadString(),
                ReadDigest(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadString(),
                ReadVariants(reader)));
        }

        var toRecordCount = reader.ReadInt32();
        var toRecord = new List<uint>(toRecordCount);

        for (var index = 0; index < toRecordCount; index++)
        {
            toRecord.Add(reader.ReadUInt32());
        }

        var recordCount = reader.ReadInt32();
        var records = new List<AtlasFrameRecord>(recordCount);

        for (var index = 0; index < recordCount; index++)
        {
            records.Add(new AtlasFrameRecord(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32()));
        }

        var animationCount = reader.ReadInt32();
        var animations = new List<AtlasAnimationRef>(animationCount);

        for (var index = 0; index < animationCount; index++)
        {
            animations.Add(new AtlasAnimationRef(
                reader.ReadInt32(),
                reader.ReadString(),
                ReadDigest(reader),
                reader.ReadInt32()));
        }

        var blobCount = reader.ReadInt32();
        var blobs = new List<AtlasBlobRef>(blobCount);

        for (var index = 0; index < blobCount; index++)
        {
            blobs.Add(new AtlasBlobRef(
                reader.ReadInt32(),
                reader.ReadString(),
                ReadDigest(reader),
                reader.ReadInt64()));
        }

        return new AtlasManifest
        {
            BundleId = bundleId,
            PackageId = packageId,
            PackageDisplayName = displayName,
            GeneratorName = generatorName,
            GeneratorVersion = generatorVersion,
            Uris = uris,
            Pages = pages,
            ToRecord = toRecord,
            Records = records,
            Animations = animations,
            Blobs = blobs
        };
    }

    private static void WriteLayout(BinaryWriter writer, PageLayout layout)
    {
        writer.Write(layout.Width);
        writer.Write(layout.Height);
        writer.Write(layout.Placements.Count);

        foreach (var placement in layout.Placements)
        {
            writer.Write(placement.ItemIndex);
            writer.Write(placement.X);
            writer.Write(placement.Y);
            writer.Write(placement.Rotation);
        }
    }

    private static PageLayout ReadLayout(BinaryReader reader)
    {
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var count = reader.ReadInt32();
        var placements = new List<Placement>(count);

        for (var index = 0; index < count; index++)
        {
            placements.Add(new Placement(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()));
        }

        return new PageLayout(width, height, placements);
    }

    private static void WriteImage(BinaryWriter writer, RgbaImage image)
    {
        writer.Write(image.Width);
        writer.Write(image.Height);
        writer.Write(image.Pixels.Length);
        writer.Write(image.Pixels);
    }

    private static RgbaImage ReadImage(BinaryReader reader)
    {
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        return RgbaImage.FromBytes(width, height, reader.ReadBytes(reader.ReadInt32()));
    }

    private static void WriteVariants(BinaryWriter writer, IReadOnlyList<AtlasVariant> variants)
    {
        writer.Write(variants.Count);

        foreach (var variant in variants)
        {
            writer.Write(variant.Id);
            writer.Write(variant.Codec);
            writer.Write(variant.File);
            WriteDigest(writer, variant.Digest);
            WriteNullableString(writer, variant.PixelFormat);
        }
    }

    private static IReadOnlyList<AtlasVariant> ReadVariants(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var variants = new List<AtlasVariant>(count);

        for (var index = 0; index < count; index++)
        {
            variants.Add(new AtlasVariant(
                reader.ReadString(),
                reader.ReadString(),
                reader.ReadString(),
                ReadDigest(reader))
            {
                PixelFormat = ReadNullableString(reader)
            });
        }

        return variants;
    }

    private static void WriteDigest(BinaryWriter writer, Digest digest)
    {
        writer.Write(digest.Algorithm);
        writer.Write(digest.Value.Length);
        writer.Write(digest.Value);
    }

    private static Digest ReadDigest(BinaryReader reader) =>
        new(reader.ReadString(), reader.ReadBytes(reader.ReadInt32()));

    private static void WriteStrings(BinaryWriter writer, IReadOnlyList<string> values)
    {
        writer.Write(values.Count);

        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static IReadOnlyList<string> ReadStrings(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var values = new List<string>(count);

        for (var index = 0; index < count; index++)
        {
            values.Add(reader.ReadString());
        }

        return values;
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);

        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? ReadNullableString(BinaryReader reader) =>
        reader.ReadBoolean() ? reader.ReadString() : null;

    public static BinaryWriter CreateWriter(Stream stream) =>
        new(stream, new UTF8Encoding(false), leaveOpen: true);
}
