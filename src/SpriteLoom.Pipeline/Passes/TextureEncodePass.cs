using System.Globalization;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline.Passes;

public sealed class TextureEncodePass : IPass
{
    private readonly TextureEncoderRegistry _encoders;

    public TextureEncodePass(TextureEncoderRegistry? encoders = null)
    {
        _encoders = encoders ?? TextureEncoderRegistry.Empty;
    }

    public PassDescriptor Descriptor { get; } = new(
        "texture.encode",
        "1.0",
        [new PortSpec("in", ArtifactKind.PageSet)],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var targets = PassParameters.GetStringList(context.Parameters, "targets");
        var dither = PixelFormats.ParseDither(
            PassParameters.GetString(context.Parameters, "dither", "none"));
        var quality = TextureQualities.Parse(
            PassParameters.GetString(context.Parameters, "quality", "balanced"));
        var mipmaps = PassParameters.GetBool(context.Parameters, "mipmaps", false);
        var pages = GetPages(inputs["in"].Payload, mipmaps);
        var encodedPages = new List<TexturePage>(pages.Count);

        foreach (var page in pages)
        {
            var variants = new List<TexturePageVariant>();
            var variantIndex = 1;

            foreach (var target in targets)
            {
                var format = TextureFormats.Find(target);

                if (format is null)
                {
                    context.Diagnostics.Report(Diagnostic.Error(
                        "SL3001",
                        $"Unknown texture target '{target}'."));
                    continue;
                }

                try
                {
                    var data = Encode(format, page.Levels, dither, quality);

                    if (data is null)
                    {
                        context.Diagnostics.Report(Diagnostic.Warning(
                            "SL3001",
                            $"Texture target '{target}' is unavailable; the encoder is missing."));
                        continue;
                    }

                    var file = string.Create(
                        CultureInfo.InvariantCulture,
                        $"page/{page.Index:D4}.slatlas/t{variantIndex}.ktx2");
                    variants.Add(new TexturePageVariant(
                        target,
                        format.Codec,
                        file,
                        format.PixelFormat is { } pixelFormat
                            ? PixelFormats.GetName(pixelFormat)
                            : format.Codec,
                        data));
                    variantIndex++;
                }
                catch (PixelFormatException exception)
                {
                    context.Diagnostics.Report(Diagnostic.Error(exception.Code, exception.Message));
                }
                catch (Ktx2Exception exception)
                {
                    context.Diagnostics.Report(Diagnostic.Error(exception.Code, exception.Message));
                }
                catch (Exception exception)
                {
                    context.Diagnostics.Report(Diagnostic.Warning(
                        "SL3002",
                        $"Texture target '{target}' failed: {exception.Message}"));
                }
            }

            encodedPages.Add(new TexturePage(page.Index, page.Layout, variants));
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, new TexturePageSet(encodedPages))
            });
    }

    private byte[]? Encode(
        TextureFormat format,
        IReadOnlyList<RgbaImage> levels,
        DitherMode dither,
        TextureQuality quality)
    {
        if (format.IsRaw)
        {
            var ktxLevels = new Ktx2Level[levels.Count];

            for (var index = 0; index < levels.Count; index++)
            {
                var level = levels[index];
                ktxLevels[index] = new Ktx2Level(
                    level.Width,
                    level.Height,
                    PixelFormatConverter.Convert(level, format.PixelFormat!.Value, dither));
            }

            return Ktx2Writer.Write(
                format,
                levels[0].Width,
                levels[0].Height,
                ktxLevels);
        }

        return _encoders.Find(format)?.EncodeContainer(levels, format, quality);
    }

    private static IReadOnlyList<MipmapPage> GetPages(object payload, bool mipmaps) =>
        payload switch
        {
            MipmapPageSet mipmapped => mipmapped.Pages,
            ComposedPageSet composed => composed.Pages
                .Select(page => new MipmapPage(
                    page.Index,
                    page.Layout,
                    mipmaps
                        ? MipmapGenerator.Generate(page.Pixels)
                        : [page.Pixels]))
                .ToArray(),
            _ => throw new InvalidOperationException("Texture encoding requires a composed page set.")
        };
}
