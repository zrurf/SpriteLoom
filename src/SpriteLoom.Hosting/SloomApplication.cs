using SpriteLoom.Abstractions;
using SpriteLoom.Codecs;
using SpriteLoom.Foundation;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Caching;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Hosting;

public sealed class SloomApplication
{
    private readonly ProjectConfig _config;
    private readonly IImageCodec _codec;

    private SloomApplication(ProjectConfig config, IImageCodec codec)
    {
        _config = config;
        _codec = codec;
    }

    public ProjectConfig Config => _config;

    public static SloomApplication Load(string configPath) =>
        new(ProjectConfigLoader.Load(configPath), ImageCodecs.CreateDefault());

    public static SloomApplication FromConfig(ProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new SloomApplication(config, ImageCodecs.CreateDefault());
    }

    public IReadOnlyList<Diagnostic> Validate()
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var source in _config.Sources)
        {
            var path = Path.GetFullPath(Path.Combine(_config.BaseDirectory, source.Path));

            if (!Directory.Exists(path))
            {
                diagnostics.Add(Diagnostic.Error("SL2001", $"Source directory not found: {path}"));
                continue;
            }

            if (!Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(_codec.CanDecode))
            {
                diagnostics.Add(Diagnostic.Warning("SL2002", $"Source '{source.Id}' contains no decodable images."));
            }
        }

        foreach (var animation in _config.Animations)
        {
            if (!_config.Sources.Any(source => string.Equals(source.Id, animation.Source, StringComparison.Ordinal)))
            {
                diagnostics.Add(Diagnostic.Error(
                    "SL2003",
                    $"Animation '{animation.Uri}' references unknown source '{animation.Source}'."));
            }
        }

        if (_config.SigningKeyPath is not null)
        {
            var keyPath = Path.GetFullPath(Path.Combine(_config.BaseDirectory, _config.SigningKeyPath));

            if (!File.Exists(keyPath))
            {
                diagnostics.Add(Diagnostic.Error("SL2004", $"Signing key not found: {keyPath}"));
            }
        }

        if (_config.Encryption is not null)
        {
            var keyPath = Path.GetFullPath(Path.Combine(_config.BaseDirectory, _config.Encryption.KeyPath));

            if (!File.Exists(keyPath))
            {
                diagnostics.Add(Diagnostic.Error("SL2005", $"Encryption key not found: {keyPath}"));
            }
        }

        return diagnostics;
    }

    public async Task<PackResult> PackAsync(
        CancellationToken cancellation = default,
        bool refresh = false,
        bool useCache = true)
    {
        var diagnostics = new DiagnosticSink();
        var nativeEncoder = NativeTextureEncoder.TryCreate();
        var encoders = nativeEncoder is null
            ? TextureEncoderRegistry.Empty
            : new TextureEncoderRegistry([nativeEncoder]);
        var nodes = PipelineBuilder.Build(_config, _codec, encoders);
        var cache = useCache
            ? new ArtifactCache(Path.Combine(_config.BaseDirectory, ".sloom-cache"))
            : null;
        var stats = new PipelineCacheStats();
        var artifacts = await new PipelineRunner().RunAsync(
            nodes,
            _config.BaseDirectory,
            diagnostics,
            new PipelineRunOptions { Cache = cache, Refresh = refresh, Stats = stats },
            cancellation);

        if (!artifacts.TryGetValue("package.out", out var artifact) ||
            artifact.Payload is not PackageOutput output)
        {
            throw new InvalidOperationException("Pipeline did not produce a package.");
        }

        return new PackResult(
            output.Path,
            diagnostics.Diagnostics,
            stats.Hits,
            stats.Misses);
    }
}

public sealed record PackResult(
    string OutputPath,
    IReadOnlyList<Diagnostic> Diagnostics,
    int CacheHits = 0,
    int CacheMisses = 0);
