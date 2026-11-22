using System.Globalization;
using System.Text;
using SpriteLoom.Foundation;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Textures;

namespace SpriteLoom.Pipeline.Configuration;

public static class ProjectConfigWriter
{
    public static void Write(ProjectConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Configuration path has no directory.");
        Directory.CreateDirectory(directory);

        var staging = Path.Combine(directory, $".sloom-config-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(staging, Format(config), new UTF8Encoding(false));
            File.Move(staging, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }

            throw;
        }
    }

    public static string Format(ProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var builder = new StringBuilder();
        builder.Append("schema = 1\n\n");
        builder.Append("[project]\n");
        builder.Append("id = ").Append(Quote(config.PackageId)).Append('\n');

        if (!string.IsNullOrWhiteSpace(config.DisplayName))
        {
            builder.Append("name = ").Append(Quote(config.DisplayName)).Append('\n');
        }

        builder.Append("\n[input]\n");
        builder.Append("frame-pattern = ").Append(Quote(config.FramePattern)).Append('\n');

        foreach (var source in config.Sources)
        {
            builder.Append("\n[[sources]]\n");
            builder.Append("id = ").Append(Quote(source.Id)).Append('\n');
            builder.Append("kind = ").Append(Quote(source.Kind)).Append('\n');
            builder.Append("path = ").Append(Quote(source.Path)).Append('\n');

            if (!string.IsNullOrWhiteSpace(source.ImagePath))
            {
                builder.Append("image = ").Append(Quote(source.ImagePath)).Append('\n');
            }

            builder.Append("order = ").Append(Quote(source.Order)).Append('\n');
            builder.Append("frame-duration-ms = ")
                .Append(source.FrameDurationMs.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        foreach (var animation in config.Animations)
        {
            builder.Append("\n[[animations]]\n");
            builder.Append("uri = ").Append(Quote(animation.Uri)).Append('\n');
            builder.Append("source = ").Append(Quote(animation.Source)).Append('\n');
            builder.Append("loop = ").Append(animation.Loop ? "true" : "false").Append('\n');
            builder.Append("playback = ").Append(Quote(animation.Playback)).Append('\n');
        }

        builder.Append("\n[[passes]]\n");
        builder.Append("id = \"trim\"\n");
        builder.Append("use = \"trim.fit-pixel\"\n");
        builder.Append("params = { threshold = ")
            .Append(config.TrimThreshold.ToString(CultureInfo.InvariantCulture))
            .Append(" }\n");

        builder.Append("\n[[passes]]\n");
        builder.Append("id = \"pack\"\n");
        builder.Append("use = ").Append(Quote(config.Packer)).Append('\n');
        builder.Append("params = { max-size = [")
            .Append(config.PackOptions.MaxWidth.ToString(CultureInfo.InvariantCulture))
            .Append(", ")
            .Append(config.PackOptions.MaxHeight.ToString(CultureInfo.InvariantCulture))
            .Append("], padding = ")
            .Append(config.PackOptions.Padding.ToString(CultureInfo.InvariantCulture))
            .Append(", pow2 = ")
            .Append(config.PackOptions.Pow2 ? "true" : "false")
            .Append(", align = ")
            .Append(config.PackOptions.Align.ToString(CultureInfo.InvariantCulture))
            .Append(", rotate = ")
            .Append(config.PackOptions.AllowRotation ? "true" : "false")
            .Append(" }\n");

        builder.Append("\n[[passes]]\n");
        builder.Append("id = \"bleed\"\n");
        builder.Append("use = \"bleed.page\"\n");
        builder.Append("params = { extrude = ")
            .Append(config.BleedOptions.Extrude.ToString(CultureInfo.InvariantCulture))
            .Append(", alpha-dilate = ")
            .Append(config.BleedOptions.AlphaDilate ? "true" : "false")
            .Append(" }\n");

        builder.Append("\n[[passes]]\n");
        builder.Append("id = \"encode\"\n");
        builder.Append("params = { formats = ")
            .Append(FormatArray(config.RasterFormats))
            .Append(", quality = ")
            .Append(config.RasterQuality.ToString(CultureInfo.InvariantCulture))
            .Append(" }\n");

        if (config.TextureTargets.Count > 0 || config.Mipmaps)
        {
            builder.Append("\n[[passes]]\n");
            builder.Append("id = \"texture\"\n");
            builder.Append("params = { targets = ")
                .Append(FormatArray(config.TextureTargets))
                .Append(", pixel-format = ")
                .Append(Quote(PixelFormats.GetName(config.TexturePixelFormat)))
                .Append(", dither = ")
                .Append(Quote(PixelFormats.GetDitherName(config.TextureDither)))
                .Append(", mipmaps = ")
                .Append(config.Mipmaps ? "true" : "false")
                .Append(", quality = ")
                .Append(Quote(TextureQualities.GetName(config.TextureQuality)))
                .Append(" }\n");
        }

        builder.Append("\n[output]\n");
        builder.Append("kind = ").Append(Quote(config.OutputKind)).Append('\n');
        builder.Append("path = ").Append(Quote(config.OutputPath)).Append('\n');
        builder.Append("manifest = ")
            .Append(Quote(config.ManifestFormat == ManifestFormat.Cbor ? "cbor" : "json"))
            .Append('\n');

        if (!string.IsNullOrWhiteSpace(config.SigningKeyPath))
        {
            builder.Append("\n[signing]\n");
            builder.Append("enabled = true\n");
            builder.Append("key = ").Append(Quote(config.SigningKeyPath)).Append('\n');
        }

        if (config.Encryption is not null)
        {
            builder.Append("\n[encryption]\n");
            builder.Append("enabled = true\n");
            builder.Append("algorithm = ").Append(Quote(config.Encryption.Algorithm)).Append('\n');
            builder.Append("key = ").Append(Quote(config.Encryption.KeyPath)).Append('\n');
            builder.Append("chunk-size = ")
                .Append(config.Encryption.ChunkSize.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return builder.ToString();
    }

    private static string FormatArray(IReadOnlyList<string> values) =>
        "[" + string.Join(", ", values.Select(Quote)) + "]";

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (character < ' ' || character == '\u007f')
                    {
                        builder.Append("\\u")
                            .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
