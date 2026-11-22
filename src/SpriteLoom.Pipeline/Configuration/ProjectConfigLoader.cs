using System.Globalization;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Security;
using SpriteLoom.Textures;
using Tomlyn;
using Tomlyn.Model;

namespace SpriteLoom.Pipeline.Configuration;

public static class ProjectConfigLoader
{
    public static ProjectConfig Load(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Configuration file not found.", fullPath);
        }

        TomlTable model;

        try
        {
            model = TomlDocumentReader.Parse(File.ReadAllText(fullPath));
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new InvalidDataException($"Invalid TOML configuration: {exception.Message}", exception);
        }

        var project = Table(model, "project")
            ?? throw new InvalidDataException("Missing [project] section.");
        var packageId = String(project, "id")
            ?? throw new InvalidDataException("Missing project.id.");
        _ = SloomIri.Create(SloomIriKind.Package, packageId, string.Empty);

        var input = Table(model, "input");
        var framePattern = String(input, "frame-pattern") ?? "{relpath}";

        var sources = ReadSources(model);
        var animations = ReadAnimations(model, sources);
        var passes = ReadPasses(model);

        var packer = "pack.maxrects";
        TomlTable? packParams = null;

        if (passes.TryGetValue("pack", out var packPass))
        {
            packer = String(packPass, "use") ?? packer;
            packParams = Table(packPass, "params");
        }

        var maxSize = ReadMaxSize(packParams);
        var textureParams = passes.TryGetValue("texture", out var texturePass)
            ? Table(texturePass, "params")
            : null;
        var textureTargets = ReadStringArray(textureParams, "targets");

        foreach (var target in textureTargets)
        {
            if (TextureFormats.Find(target) is null)
            {
                throw new InvalidDataException($"Unknown texture target: {target}");
            }
        }

        var texturePixelFormat = PixelFormats.Parse(
            String(textureParams, "pixel-format") ?? "rgba8888");
        var textureDither = PixelFormats.ParseDither(
            String(textureParams, "dither") ?? "ordered4");
        var textureQuality = TextureQualities.Parse(
            String(textureParams, "quality") ?? "balanced");
        var mipmaps = Bool(textureParams, "mipmaps", false);
        var align = Int(packParams, "align", 4);

        if (textureTargets.Length > 0)
        {
            align = Lcm(align, TextureFormats.RequiredAlignment(textureTargets));
        }

        var packOptions = new PackOptions(
            maxSize[0],
            maxSize[1],
            Int(packParams, "padding", 2),
            Bool(packParams, "pow2", false),
            align,
            Bool(packParams, "rotate", false));

        var trimParams = passes.TryGetValue("trim", out var trimPass) ? Table(trimPass, "params") : null;
        var encodeParams = passes.TryGetValue("encode", out var encodePass) ? Table(encodePass, "params") : null;
        var rasterFormats = ReadStringArray(encodeParams, "formats");
        var bleedParams = passes.TryGetValue("bleed", out var bleedPass) ? Table(bleedPass, "params") : null;
        var bleedOptions = new BleedOptions(
            Int(bleedParams, "extrude", 1),
            Bool(bleedParams, "alpha-dilate", true));

        var output = Table(model, "output")
            ?? throw new InvalidDataException("Missing [output] section.");
        var outputPath = String(output, "path")
            ?? throw new InvalidDataException("Missing output.path.");
        var outputKind = (String(output, "kind") ?? "directory").ToLowerInvariant();

        if (outputKind is not ("directory" or "bundle"))
        {
            throw new InvalidDataException($"Unsupported output kind: {outputKind}");
        }

        var signing = Table(model, "signing");
        var signingKeyPath = String(signing, "key");

        if (signingKeyPath is not null && !Bool(signing, "enabled", true))
        {
            signingKeyPath = null;
        }

        var encryptionTable = Table(model, "encryption");
        var encryptionKeyPath = String(encryptionTable, "key");
        PackageEncryptionOptions? encryption = null;

        if (encryptionKeyPath is not null && Bool(encryptionTable, "enabled", true))
        {
            var algorithmName = String(encryptionTable, "algorithm") ?? "aes-256-gcm";

            try
            {
                _ = SymmetricAlgorithms.Parse(algorithmName);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }

            var chunkSize = Int(encryptionTable, "chunk-size", 65536);

            if (chunkSize is < 1 or > 1024 * 1024)
            {
                throw new InvalidDataException("encryption.chunk-size must be between 1 and 1048576.");
            }

            encryption = new PackageEncryptionOptions(algorithmName, encryptionKeyPath, chunkSize);
        }

        var manifest = String(output, "manifest") ?? "json";
        var format = string.Equals(manifest, "cbor", StringComparison.OrdinalIgnoreCase)
            ? ManifestFormat.Cbor
            : ManifestFormat.Json;

        return new ProjectConfig
        {
            BaseDirectory = Path.GetDirectoryName(fullPath)!,
            PackageId = packageId,
            DisplayName = String(project, "name"),
            Sources = sources,
            Animations = animations,
            FramePattern = framePattern,
            Packer = packer,
            PackOptions = packOptions,
            TrimThreshold = Int(trimParams, "threshold", 1),
            RasterFormats = rasterFormats.Length > 0 ? rasterFormats : ["png"],
            RasterQuality = Int(encodeParams, "quality", 90),
            BleedOptions = bleedOptions,
            TextureTargets = textureTargets,
            TexturePixelFormat = texturePixelFormat,
            TextureDither = textureDither,
            Mipmaps = mipmaps,
            TextureQuality = textureQuality,
            OutputPath = outputPath,
            OutputKind = outputKind,
            SigningKeyPath = signingKeyPath,
            Encryption = encryption,
            ManifestFormat = format
        };
    }

    private static IReadOnlyList<SourceSpec> ReadSources(TomlTable model)
    {
        var array = Array(model, "sources")
            ?? throw new InvalidDataException("At least one [[sources]] entry is required.");
        var sources = new List<SourceSpec>(array.Count);

        foreach (var item in array)
        {
            if (item is not TomlTable table)
            {
                throw new InvalidDataException("Each [[sources]] entry must be a table.");
            }

            var id = String(table, "id")
                ?? throw new InvalidDataException("Missing source id.");
            var path = String(table, "path")
                ?? throw new InvalidDataException($"Missing path for source '{id}'.");

            sources.Add(new SourceSpec(
                id,
                String(table, "kind") ?? "image-sequence",
                path,
                String(table, "image"),
                String(table, "order") ?? "natural",
                Int(table, "frame-duration-ms", 100)));
        }

        return sources;
    }

    private static IReadOnlyList<AnimationSpec> ReadAnimations(
        TomlTable model,
        IReadOnlyList<SourceSpec> sources)
    {
        var array = Array(model, "animations");
        if (array is null)
        {
            return [];
        }

        var animations = new List<AnimationSpec>(array.Count);

        foreach (var item in array)
        {
            if (item is not TomlTable table)
            {
                throw new InvalidDataException("Each [[animations]] entry must be a table.");
            }

            var uri = String(table, "uri")
                ?? throw new InvalidDataException("Missing animation uri.");
            var parsed = SloomIri.Parse(uri);

            if (parsed.Kind != SloomIriKind.Animation)
            {
                throw new InvalidDataException($"Animation uri must use the anim scheme: {uri}");
            }

            var sourceId = String(table, "source")
                ?? throw new InvalidDataException($"Missing source for animation '{uri}'.");
            var source = sources.FirstOrDefault(candidate => string.Equals(candidate.Id, sourceId, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Animation '{uri}' references unknown source '{sourceId}'.");

            animations.Add(new AnimationSpec(
                uri,
                sourceId,
                Bool(table, "loop", true),
                String(table, "playback") ?? "forward",
                source.FrameDurationMs));
        }

        return animations;
    }

    private static Dictionary<string, TomlTable> ReadPasses(TomlTable model)
    {
        var passes = new Dictionary<string, TomlTable>(StringComparer.Ordinal);
        var array = Array(model, "passes");

        if (array is null)
        {
            return passes;
        }

        foreach (var item in array)
        {
            if (item is not TomlTable table)
            {
                continue;
            }

            var id = String(table, "id");
            if (id is not null)
            {
                passes[id] = table;
            }
        }

        return passes;
    }

    private static int[] ReadMaxSize(TomlTable? parameters)
    {
        if (parameters is null ||
            !parameters.TryGetValue("max-size", out var value) ||
            value is not TomlArray array ||
            array.Count < 2)
        {
            return [2048, 2048];
        }

        return
        [
            Convert.ToInt32(array[0], CultureInfo.InvariantCulture),
            Convert.ToInt32(array[1], CultureInfo.InvariantCulture)
        ];
    }

    private static string[] ReadStringArray(TomlTable? table, string key)
    {
        if (table is null || !table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return [];
        }

        return array
            .Select(item => item as string)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private static int Lcm(int left, int right) =>
        left / Gcd(left, right) * right;

    private static int Gcd(int left, int right)
    {
        while (right != 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }

    private static TomlTable? Table(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as TomlTable : null;

    private static TomlArray? Array(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? value as TomlArray : null;

    private static string? String(TomlTable? table, string key) =>
        table is not null && table.TryGetValue(key, out var value) ? value as string : null;

    private static int Int(TomlTable? table, string key, int defaultValue) =>
        table is not null && table.TryGetValue(key, out var value) && value is long number
            ? (int)number
            : defaultValue;

    private static bool Bool(TomlTable? table, string key, bool defaultValue) =>
        table is not null && table.TryGetValue(key, out var value) && value is bool flag
            ? flag
            : defaultValue;
}
