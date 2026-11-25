using System.Globalization;
using System.Text;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Packing;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Pipeline.Passes;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline;

public static class PipelineBuilder
{
    public static IReadOnlyList<PassNode> Build(
        ProjectConfig config,
        IImageCodec codec,
        TextureEncoderRegistry? textureEncoders = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(codec);

        var nodes = new List<PassNode>
        {
            new()
            {
                Id = "import",
                Pass = new ImportFramesPass(codec),
                Inputs = NoInputs(),
                Parameters = Parameters(
                    ("packageId", config.PackageId),
                    ("sources", config.Sources),
                    ("framePattern", config.FramePattern),
                    ("cacheSalt", ComputeSourceSalt(config)))
            },
            new()
            {
                Id = "trim",
                Pass = new TrimFramesPass(),
                Inputs = Inputs(("in", "import.out")),
                Parameters = Parameters(("threshold", config.TrimThreshold))
            },
            new()
            {
                Id = "dedup",
                Pass = new DeduplicateFramesPass(),
                Inputs = Inputs(("in", "trim.out")),
                Parameters = Parameters()
            },
            new()
            {
                Id = "pack",
                Pass = new PackPagesPass(CreatePacker(config.Packer)),
                Inputs = Inputs(("in", "dedup.out")),
                Parameters = Parameters(
                    ("maxWidth", config.PackOptions.MaxWidth),
                    ("maxHeight", config.PackOptions.MaxHeight),
                    ("padding", config.PackOptions.Padding),
                    ("pow2", config.PackOptions.Pow2),
                    ("align", config.PackOptions.Align),
                    ("allowRotation", config.PackOptions.AllowRotation))
            },
            new()
            {
                Id = "compose",
                Pass = new ComposePagesPass(),
                Inputs = Inputs(
                    ("frames", "dedup.out"),
                    ("layouts", "pack.out")),
                Parameters = Parameters()
            },
            new()
            {
                Id = "bleed",
                Pass = new BleedPagesPass(),
                Inputs = Inputs(
                    ("pages", "compose.out"),
                    ("frames", "dedup.out")),
                Parameters = Parameters(
                    ("extrude", config.BleedOptions.Extrude),
                    ("alphaDilate", config.BleedOptions.AlphaDilate))
            }
        };

        if (config.Mipmaps && config.TextureTargets.Count > 0)
        {
            nodes.Add(new PassNode
            {
                Id = "mipmap",
                Pass = new MipmapPagePass(),
                Inputs = Inputs(("in", "bleed.out")),
                Parameters = Parameters()
            });
        }

        nodes.Add(new PassNode
        {
            Id = "encode",
            Pass = new EncodePagesPass(codec),
            Inputs = Inputs(("in", "bleed.out")),
            Parameters = Parameters(
                ("formats", config.RasterFormats),
                ("quality", config.RasterQuality),
                ("pngFallback", config.PngFallback))
        });

        if (config.TextureTargets.Count > 0)
        {
            nodes.Add(new PassNode
            {
                Id = "texture",
                Pass = new TextureEncodePass(textureEncoders),
                Inputs = Inputs(("in", config.Mipmaps ? "mipmap.out" : "bleed.out")),
                Parameters = Parameters(
                    ("targets", config.TextureTargets),
                    ("pixelFormat", PixelFormats.GetName(config.TexturePixelFormat)),
                    ("dither", PixelFormats.GetDitherName(config.TextureDither)),
                    ("mipmaps", config.Mipmaps),
                    ("quality", TextureQualities.GetName(config.TextureQuality)))
            });
        }

        nodes.Add(new PassNode
        {
            Id = "animations",
            Pass = new BuildAnimationsPass(),
            Inputs = Inputs(("frames", "dedup.out")),
            Parameters = Parameters(("animations", config.Animations))
        });

        var manifestInputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["frames"] = "dedup.out",
            ["pages"] = "encode.out",
            ["animations"] = "animations.out"
        };

        if (config.TextureTargets.Count > 0)
        {
            manifestInputs["textures"] = "texture.out";
        }

        nodes.Add(new PassNode
        {
            Id = "manifest",
            Pass = new BuildManifestPass(),
            Inputs = manifestInputs,
            Parameters = Parameters(
                ("packageId", config.PackageId),
                ("displayName", config.DisplayName ?? string.Empty),
                ("format", config.ManifestFormat == ManifestFormat.Cbor ? "cbor" : "json"))
        });

        nodes.Add(new PassNode
        {
            Id = "package",
            Pass = new WriteDirectoryPass(),
            Inputs = Inputs(("in", "manifest.out")),
            Parameters = Parameters(
                ("outputPath", config.OutputPath),
                ("outputKind", config.OutputKind),
                ("signingKeyPath", config.SigningKeyPath ?? string.Empty),
                ("encryptionAlgorithm", config.Encryption?.Algorithm ?? string.Empty),
                ("encryptionKeyPath", config.Encryption?.KeyPath ?? string.Empty),
                ("encryptionChunkSize", config.Encryption?.ChunkSize ?? 0))
        });

        return nodes;
    }

    private static string ComputeSourceSalt(ProjectConfig config)
    {
        var builder = new StringBuilder();

        foreach (var source in config.Sources)
        {
            var path = Path.GetFullPath(Path.Combine(config.BaseDirectory, source.Path));

            if (File.Exists(path))
            {
                AppendFileState(builder, path);
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                AppendFileState(builder, file);
            }
        }

        return builder.ToString();
    }

    private static void AppendFileState(StringBuilder builder, string path)
    {
        var info = new FileInfo(path);
        builder.Append(path)
            .Append('|')
            .Append(info.Length.ToString(CultureInfo.InvariantCulture))
            .Append('|')
            .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static IPacker CreatePacker(string id) => id switch
    {
        "pack.basic" => new BasicPacker(),
        "pack.maxrects" => new MaxRectsPacker(),
        _ => throw new InvalidOperationException($"Unknown packer '{id}'.")
    };

    private static IReadOnlyDictionary<string, string> NoInputs() =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> Inputs(params (string Port, string Reference)[] values)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (port, reference) in values)
        {
            inputs[port] = reference;
        }

        return inputs;
    }

    private static IReadOnlyDictionary<string, object?> Parameters(params (string Key, object? Value)[] values)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            parameters[key] = value;
        }

        return parameters;
    }
}
