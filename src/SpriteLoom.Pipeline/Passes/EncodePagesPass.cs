using System.Globalization;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class EncodePagesPass : IPass
{
    private readonly IImageCodec _codec;

    public EncodePagesPass(IImageCodec codec)
    {
        _codec = codec;
    }

    public PassDescriptor Descriptor { get; } = new(
        "encode.page",
        "1.0",
        [new PortSpec("in", ArtifactKind.PageSet)],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var pages = (ComposedPageSet)inputs["in"].Payload;
        var formats = PassParameters.GetStringList(context.Parameters, "formats");
        var quality = PassParameters.GetInt(context.Parameters, "quality", 90);

        foreach (var page in pages.Pages)
        {
            page.EncodedPng = _codec.EncodePng(page.Pixels);
            page.Variants.Clear();
            var variantIndex = 1;

            foreach (var format in formats)
            {
                if (string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_codec.CanEncode(format))
                {
                    context.Diagnostics.Report(Diagnostic.Warning(
                        "SL3001",
                        $"Raster format '{format}' is unavailable; the encoder is missing."));
                    continue;
                }

                try
                {
                    var data = _codec.Encode(page.Pixels, format, quality);
                    var extension = string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
                        ? "jpg"
                        : format;
                    var file = string.Create(
                        CultureInfo.InvariantCulture,
                        $"page/{page.Index:D4}.slatlas/v{variantIndex}.{extension}");
                    page.Variants.Add(new EncodedPageVariant(
                        format,
                        format,
                        file,
                        data,
                        "rgba8888"));
                    variantIndex++;
                }
                catch (PixelFormatException exception)
                {
                    context.Diagnostics.Report(Diagnostic.Error(exception.Code, exception.Message));
                }
                catch (Exception exception)
                {
                    context.Diagnostics.Report(Diagnostic.Warning(
                        "SL3002",
                        $"Raster format '{format}' failed: {exception.Message}"));
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, new EncodedPageSet(pages.Pages))
            });
    }
}
