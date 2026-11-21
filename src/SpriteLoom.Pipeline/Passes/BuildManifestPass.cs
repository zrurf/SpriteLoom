using System.Globalization;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline.Passes;

public sealed class BuildManifestPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "manifest.build",
        "1.0",
        [
            new PortSpec("frames", ArtifactKind.FrameSet),
            new PortSpec("pages", ArtifactKind.PageSet),
            new PortSpec("animations", ArtifactKind.AnimationSet),
            new PortSpec("textures", ArtifactKind.PageSet, Required: false)
        ],
        [new PortSpec("out", ArtifactKind.Manifest)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var frameSet = (FrameSet)inputs["frames"].Payload;
        var encodedPages = (EncodedPageSet)inputs["pages"].Payload;
        var drafts = (AnimationDraftSet)inputs["animations"].Payload;
        var texturePages = inputs.TryGetValue("textures", out var textureArtifact)
            ? ((TexturePageSet)textureArtifact.Payload).Pages.ToDictionary(page => page.Index)
            : null;

        var packageId = PassParameters.GetString(context.Parameters, "packageId");
        var displayName = PassParameters.GetString(context.Parameters, "displayName");
        var generatorVersion = PassParameters.GetString(context.Parameters, "generatorVersion", "0.1.0");
        var format = ParseFormat(PassParameters.GetString(context.Parameters, "format", "json"));
        var extension = format == ManifestFormat.Cbor ? "cbor" : "json";

        var pageCount = encodedPages.Pages.Count;
        var placements = new Dictionary<int, (int Page, Placement Placement)>();
        var pageStored = new List<int>[pageCount];
        var localStored = new Dictionary<(int Page, int Stored), int>();

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var ordered = encodedPages.Pages[pageIndex].Layout.Placements
                .OrderBy(placement => placement.ItemIndex)
                .ToArray();

            pageStored[pageIndex] = new List<int>(ordered.Length);

            for (var local = 0; local < ordered.Length; local++)
            {
                var placement = ordered[local];
                placements[placement.ItemIndex] = (pageIndex, placement);
                pageStored[pageIndex].Add(placement.ItemIndex);
                localStored[(pageIndex, placement.ItemIndex)] = local;
            }
        }

        var uriSet = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var frame in frameSet.Frames)
        {
            uriSet.Add(frame.Uri.Value);
        }

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            uriSet.Add(PageUri(packageId, pageIndex));
        }

        foreach (var draft in drafts.Animations)
        {
            uriSet.Add(draft.Uri);
        }

        var uris = uriSet.ToArray();
        var uriIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < uris.Length; index++)
        {
            uriIndex[uris[index]] = index;
        }

        var toRecord = new uint[uris.Length];
        Array.Fill(toRecord, uint.MaxValue);
        var records = new List<AtlasFrameRecord>(frameSet.Frames.Count);

        foreach (var frame in frameSet.Frames.OrderBy(frame => frame.Uri.Value, StringComparer.Ordinal))
        {
            if (!placements.TryGetValue(frame.StoredIndex, out var location))
            {
                throw new InvalidOperationException($"Frame '{frame.Uri}' was not placed on any page.");
            }

            var local = localStored[(location.Page, frame.StoredIndex)];
            var recordIndex = records.Count;

            records.Add(new AtlasFrameRecord(
                location.Page,
                local,
                frame.OriginWidth,
                frame.OriginHeight,
                frame.OffsetX,
                frame.OffsetY,
                frame.PivotX,
                frame.PivotY,
                frame.Trimmed ? 1u : 0u));

            toRecord[uriIndex[frame.Uri.Value]] = (uint)recordIndex;
        }

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var pages = new List<AtlasPage>(pageCount);

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var page = encodedPages.Pages[pageIndex];
            var pageUri = PageUri(packageId, pageIndex);
            var png = page.EncodedPng ?? throw new InvalidOperationException("Page has not been encoded.");
            var pngPath = $"page/{pageIndex.ToString("D4", CultureInfo.InvariantCulture)}.slatlas/v0.png";
            files[pngPath] = png;

            var variants = new List<AtlasVariant>
            {
                new("png", "png", pngPath, Digest.XxHash64(png))
            };

            foreach (var raster in page.Variants)
            {
                files[raster.File] = raster.Data;
                variants.Add(new AtlasVariant(
                    raster.Id,
                    raster.Codec,
                    raster.File,
                    Digest.XxHash64(raster.Data))
                {
                    PixelFormat = raster.PixelFormat
                });
            }

            if (texturePages is not null && texturePages.TryGetValue(pageIndex, out var texturePage))
            {
                foreach (var texture in texturePage.Variants)
                {
                    files[texture.File] = texture.Data;
                    variants.Add(new AtlasVariant(
                        texture.Id,
                        texture.Codec,
                        texture.File,
                        Digest.XxHash64(texture.Data))
                    {
                        PixelFormat = texture.PixelFormat
                    });
                }
            }

            var stored = pageStored[pageIndex].Select(globalIndex =>
            {
                var placement = placements[globalIndex].Placement;
                var storedFrame = frameSet.Stored[globalIndex];

                return new AtlasStoredFrame(
                    new IntRect(
                        placement.X,
                        placement.Y,
                        storedFrame.Pixels.Width,
                        storedFrame.Pixels.Height),
                    0,
                    Digest.XxHash64(storedFrame.Pixels.Pixels));
            }).ToArray();

            var container = new AtlasContainer(
                pageIndex,
                page.Layout.Width,
                page.Layout.Height,
                "rgba8888",
                "straight",
                variants,
                stored);

            var containerBytes = SerializeContainer(container, format);
            var containerPath =
                $"page/{pageIndex.ToString("D4", CultureInfo.InvariantCulture)}.slatlas/container.{extension}";
            files[containerPath] = containerBytes;

            pages.Add(new AtlasPage(
                pageIndex,
                uriIndex[pageUri],
                "default",
                pageIndex,
                true,
                containerPath,
                Digest.XxHash64(containerBytes),
                page.Layout.Width,
                page.Layout.Height,
                "rgba8888",
                variants));
        }

        var animationRefs = new List<AtlasAnimationRef>(drafts.Animations.Count);

        foreach (var draft in drafts.Animations)
        {
            var frameIndices = draft.FrameUris
                .Select(uri => (uint)uriIndex[uri.Value])
                .ToArray();

            var document = new AnimationDocument
            {
                Uri = draft.Uri,
                Frames = frameIndices,
                DurationsMs = Enumerable.Repeat(draft.DurationMs, frameIndices.Length).ToArray(),
                Loop = draft.Loop,
                Playback = draft.Playback
            };

            var bytes = SerializeAnimation(document, format);
            var animationPath = $"anim/{SloomIri.Parse(draft.Uri).Path}.anim.{extension}";
            files[animationPath] = bytes;

            animationRefs.Add(new AtlasAnimationRef(
                uriIndex[draft.Uri],
                animationPath,
                Digest.XxHash64(bytes),
                frameIndices.Length));
        }

        var manifest = new AtlasManifest
        {
            BundleId = packageId,
            PackageId = packageId,
            PackageDisplayName = string.IsNullOrEmpty(displayName) ? null : displayName,
            GeneratorName = "SpriteLoom",
            GeneratorVersion = generatorVersion,
            Uris = uris,
            Pages = pages,
            ToRecord = toRecord,
            Records = records,
            Animations = animationRefs
        };

        files[$"manifest.{extension}"] = SerializeManifest(manifest, format);

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.Manifest, 0, new BuildPackage(manifest, files, format))
            });
    }

    private static string PageUri(string packageId, int index) =>
        $"page://{packageId}/{index.ToString("D4", CultureInfo.InvariantCulture)}";

    private static ManifestFormat ParseFormat(string value) =>
        string.Equals(value, "cbor", StringComparison.OrdinalIgnoreCase)
            ? ManifestFormat.Cbor
            : ManifestFormat.Json;

    private static byte[] SerializeManifest(AtlasManifest manifest, ManifestFormat format)
    {
        using var stream = new MemoryStream();

        if (format == ManifestFormat.Cbor)
        {
            ManifestCborWriter.WriteManifest(manifest, stream);
        }
        else
        {
            ManifestJsonWriter.WriteManifest(manifest, stream);
        }

        return stream.ToArray();
    }

    private static byte[] SerializeContainer(AtlasContainer container, ManifestFormat format)
    {
        using var stream = new MemoryStream();

        if (format == ManifestFormat.Cbor)
        {
            ManifestCborWriter.WriteContainer(container, stream);
        }
        else
        {
            ManifestJsonWriter.WriteContainer(container, stream);
        }

        return stream.ToArray();
    }

    private static byte[] SerializeAnimation(AnimationDocument animation, ManifestFormat format)
    {
        using var stream = new MemoryStream();

        if (format == ManifestFormat.Cbor)
        {
            ManifestCborWriter.WriteAnimation(animation, stream);
        }
        else
        {
            ManifestJsonWriter.WriteAnimation(animation, stream);
        }

        return stream.ToArray();
    }
}
