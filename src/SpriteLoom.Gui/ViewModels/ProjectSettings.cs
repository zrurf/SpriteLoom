using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;
using SpriteLoom.Imaging;
using SpriteLoom.Model.Iri;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Pipeline;
using SpriteLoom.Pipeline.Configuration;
using SpriteLoom.Textures;

namespace SpriteLoom.Gui.ViewModels;

public sealed class ProjectSettings : ObservableObject
{
    private readonly List<SourceSettings> _trackedSources = [];
    private IReadOnlyList<AnimationSpec> _animations = [];
    private string _packageId = string.Empty;
    private string _displayName = string.Empty;
    private string _framePattern = "{relpath}";
    private string _packer = "pack.maxrects";
    private int _maxWidth = 2048;
    private int _maxHeight = 2048;
    private int _padding = 2;
    private bool _pow2;
    private bool _rotate;
    private int _packAlign = 4;
    private int _trimThreshold = 1;
    private int _extrude = 1;
    private bool _alphaDilate = true;
    private string _rasterFormats = "png";
    private int _rasterQuality = 90;
    private string _textureTargets = string.Empty;
    private string _texturePixelFormat = "rgba8888";
    private string _textureDither = "ordered4";
    private bool _mipmaps;
    private string _textureQuality = "balanced";
    private string _outputPath = "build/package";
    private string _outputKind = "directory";
    private string _manifestFormatName = "json";
    private string _signingKeyPath = string.Empty;
    private PackageEncryptionOptions? _encryption;

    public ProjectSettings(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        Sources.CollectionChanged += OnSourcesChanged;
    }

    public event EventHandler? Changed;

    public string BaseDirectory { get; }

    public ObservableCollection<SourceSettings> Sources { get; } = [];

    public IReadOnlyList<AnimationSpec> Animations
    {
        get => _animations;
        private set
        {
            _animations = value;
            OnPropertyChanged();
        }
    }

    public string PackageId
    {
        get => _packageId;
        set => Set(ref _packageId, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public string FramePattern
    {
        get => _framePattern;
        set => Set(ref _framePattern, value);
    }

    public string Packer
    {
        get => _packer;
        set => Set(ref _packer, value);
    }

    public int MaxWidth
    {
        get => _maxWidth;
        set => Set(ref _maxWidth, value);
    }

    public int MaxHeight
    {
        get => _maxHeight;
        set => Set(ref _maxHeight, value);
    }

    public int Padding
    {
        get => _padding;
        set => Set(ref _padding, value);
    }

    public bool Pow2
    {
        get => _pow2;
        set => Set(ref _pow2, value);
    }

    public bool Rotate
    {
        get => _rotate;
        set => Set(ref _rotate, value);
    }

    public int PackAlign
    {
        get => _packAlign;
        set => Set(ref _packAlign, value);
    }

    public int EffectivePackAlignment
    {
        get
        {
            var alignment = Math.Max(4, PackAlign);

            foreach (var target in SplitList(TextureTargets))
            {
                var format = TextureFormats.Find(target);

                if (format is not null)
                {
                    alignment = Lcm(alignment, format.BlockWidth);
                    alignment = Lcm(alignment, format.BlockHeight);
                }
            }

            return alignment;
        }
    }

    public int TrimThreshold
    {
        get => _trimThreshold;
        set => Set(ref _trimThreshold, value);
    }

    public int Extrude
    {
        get => _extrude;
        set => Set(ref _extrude, value);
    }

    public bool AlphaDilate
    {
        get => _alphaDilate;
        set => Set(ref _alphaDilate, value);
    }

    public string RasterFormats
    {
        get => _rasterFormats;
        set => Set(ref _rasterFormats, value);
    }

    public int RasterQuality
    {
        get => _rasterQuality;
        set => Set(ref _rasterQuality, value);
    }

    public string TextureTargets
    {
        get => _textureTargets;
        set => Set(ref _textureTargets, value);
    }

    public string TexturePixelFormat
    {
        get => _texturePixelFormat;
        set => Set(ref _texturePixelFormat, value);
    }

    public string TextureDither
    {
        get => _textureDither;
        set => Set(ref _textureDither, value);
    }

    public bool Mipmaps
    {
        get => _mipmaps;
        set => Set(ref _mipmaps, value);
    }

    public string TextureQuality
    {
        get => _textureQuality;
        set => Set(ref _textureQuality, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => Set(ref _outputPath, value);
    }

    public string OutputKind
    {
        get => _outputKind;
        set => Set(ref _outputKind, value);
    }

    public string ManifestFormatName
    {
        get => _manifestFormatName;
        set => Set(ref _manifestFormatName, value);
    }

    public string SigningKeyPath
    {
        get => _signingKeyPath;
        set => Set(ref _signingKeyPath, value);
    }

    public PackageEncryptionOptions? Encryption
    {
        get => _encryption;
        private set => Set(ref _encryption, value);
    }

    public static ProjectSettings FromConfig(ProjectConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var settings = new ProjectSettings(config.BaseDirectory)
        {
            PackageId = config.PackageId,
            DisplayName = config.DisplayName ?? string.Empty,
            FramePattern = config.FramePattern,
            Packer = config.Packer,
            MaxWidth = config.PackOptions.MaxWidth,
            MaxHeight = config.PackOptions.MaxHeight,
            Padding = config.PackOptions.Padding,
            Pow2 = config.PackOptions.Pow2,
            Rotate = config.PackOptions.AllowRotation,
            PackAlign = config.PackOptions.Align,
            TrimThreshold = config.TrimThreshold,
            Extrude = config.BleedOptions.Extrude,
            AlphaDilate = config.BleedOptions.AlphaDilate,
            RasterFormats = string.Join(", ", config.RasterFormats),
            RasterQuality = config.RasterQuality,
            TextureTargets = string.Join(", ", config.TextureTargets),
            TexturePixelFormat = PixelFormats.GetName(config.TexturePixelFormat),
            TextureDither = PixelFormats.GetDitherName(config.TextureDither),
            Mipmaps = config.Mipmaps,
            TextureQuality = TextureQualities.GetName(config.TextureQuality),
            OutputPath = config.OutputPath,
            OutputKind = config.OutputKind,
            ManifestFormatName = config.ManifestFormat == ManifestFormat.Cbor ? "cbor" : "json",
            SigningKeyPath = config.SigningKeyPath ?? string.Empty,
            Encryption = config.Encryption
        };

        foreach (var source in config.Sources)
        {
            settings.Sources.Add(new SourceSettings
            {
                Id = source.Id,
                Kind = source.Kind,
                Path = source.Path,
                Image = source.ImagePath ?? string.Empty,
                Order = source.Order,
                FrameDurationMs = source.FrameDurationMs
            });
        }

        settings.Animations = config.Animations;
        return settings;
    }

    public SourceSettings AddSource()
    {
        var index = 1;

        while (Sources.Any(source => string.Equals(source.Id, $"source{index}", StringComparison.Ordinal)))
        {
            index++;
        }

        var created = new SourceSettings { Id = $"source{index}" };
        Sources.Add(created);
        return created;
    }

    public bool MoveSource(SourceSettings source, int offset)
    {
        var index = Sources.IndexOf(source);
        var target = index + offset;

        if (index < 0 || target < 0 || target >= Sources.Count)
        {
            return false;
        }

        Sources.Move(index, target);
        return true;
    }

    public ProjectConfig ToConfig()
    {
        if (string.IsNullOrWhiteSpace(BaseDirectory))
        {
            throw new InvalidDataException("Project directory is required.");
        }

        if (string.IsNullOrWhiteSpace(PackageId))
        {
            throw new InvalidDataException("project.id is required.");
        }

        _ = SloomIri.Create(SloomIriKind.Package, PackageId.Trim(), string.Empty);

        if (Sources.Count == 0)
        {
            throw new InvalidDataException("At least one source is required.");
        }

        var sources = new List<SourceSpec>(Sources.Count);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id))
            {
                throw new InvalidDataException("Every source needs an id.");
            }

            if (string.IsNullOrWhiteSpace(source.Path))
            {
                throw new InvalidDataException($"Source '{source.Id}' needs a path.");
            }

            if (!sourceIds.Add(source.Id))
            {
                throw new InvalidDataException($"Duplicate source id: {source.Id}");
            }

            sources.Add(new SourceSpec(
                source.Id,
                string.IsNullOrWhiteSpace(source.Kind) ? "image-sequence" : source.Kind,
                source.Path,
                string.IsNullOrWhiteSpace(source.Image) ? null : source.Image,
                string.IsNullOrWhiteSpace(source.Order) ? "natural" : source.Order,
                Math.Max(1, source.FrameDurationMs)));
        }

        foreach (var animation in Animations)
        {
            if (!sourceIds.Contains(animation.Source))
            {
                throw new InvalidDataException(
                    $"Animation '{animation.Uri}' references unknown source '{animation.Source}'.");
            }

            var animationIri = SloomIri.Parse(animation.Uri);

            if (!string.Equals(animationIri.PackageId, PackageId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Animation '{animation.Uri}' belongs to package '{animationIri.PackageId}'.");
            }
        }

        var sourceDurations = sources.ToDictionary(
            source => source.Id,
            source => source.FrameDurationMs,
            StringComparer.Ordinal);
        var animations = Animations
            .Select(animation => animation with { DurationMs = sourceDurations[animation.Source] })
            .ToArray();

        if (Packer is not ("pack.basic" or "pack.maxrects"))
        {
            throw new InvalidDataException($"Unknown packer: {Packer}");
        }

        if (MaxWidth <= 0 || MaxHeight <= 0)
        {
            throw new InvalidDataException("Packing size must be positive.");
        }

        if (Padding < 0)
        {
            throw new InvalidDataException("Padding cannot be negative.");
        }

        if (TrimThreshold < 0)
        {
            throw new InvalidDataException("Trim threshold cannot be negative.");
        }

        if (Extrude < 0)
        {
            throw new InvalidDataException("Extrude cannot be negative.");
        }

        var rasterFormats = SplitList(RasterFormats);

        if (rasterFormats.Count == 0)
        {
            throw new InvalidDataException("At least one raster format is required.");
        }

        if (RasterQuality is < 1 or > 100)
        {
            throw new InvalidDataException("Raster quality must be between 1 and 100.");
        }

        var textureTargets = SplitList(TextureTargets);

        foreach (var target in textureTargets)
        {
            if (TextureFormats.Find(target) is null)
            {
                throw new InvalidDataException($"Unknown texture target: {target}");
            }
        }

        var pixelFormat = PixelFormats.Parse(TexturePixelFormat);
        var dither = PixelFormats.ParseDither(TextureDither);
        var textureQuality = TextureQualities.Parse(TextureQuality);

        if (OutputKind is not ("directory" or "bundle"))
        {
            throw new InvalidDataException($"Unsupported output kind: {OutputKind}");
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            throw new InvalidDataException("output.path is required.");
        }

        var manifestFormat = string.Equals(ManifestFormatName, "cbor", StringComparison.OrdinalIgnoreCase)
            ? ManifestFormat.Cbor
            : ManifestFormat.Json;

        return new ProjectConfig
        {
            BaseDirectory = BaseDirectory,
            PackageId = PackageId.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName,
            Sources = sources,
            Animations = animations,
            FramePattern = string.IsNullOrWhiteSpace(FramePattern) ? "{relpath}" : FramePattern,
            Packer = Packer,
            PackOptions = new PackOptions(
                MaxWidth,
                MaxHeight,
                Padding,
                Pow2,
                EffectivePackAlignment,
                Rotate),
            TrimThreshold = TrimThreshold,
            RasterFormats = rasterFormats,
            RasterQuality = RasterQuality,
            BleedOptions = new BleedOptions(Extrude, AlphaDilate),
            TextureTargets = textureTargets,
            TexturePixelFormat = pixelFormat,
            TextureDither = dither,
            Mipmaps = Mipmaps,
            TextureQuality = textureQuality,
            OutputPath = OutputPath.Trim(),
            OutputKind = OutputKind,
            SigningKeyPath = string.IsNullOrWhiteSpace(SigningKeyPath) ? null : SigningKeyPath,
            Encryption = Encryption,
            ManifestFormat = manifestFormat
        };
    }

    internal ProjectSnapshot Capture() =>
        new(
            BaseDirectory,
            PackageId,
            DisplayName,
            FramePattern,
            Packer,
            MaxWidth,
            MaxHeight,
            Padding,
            Pow2,
            Rotate,
            PackAlign,
            TrimThreshold,
            Extrude,
            AlphaDilate,
            RasterFormats,
            RasterQuality,
            TextureTargets,
            TexturePixelFormat,
            TextureDither,
            Mipmaps,
            TextureQuality,
            OutputPath,
            OutputKind,
            ManifestFormatName,
            SigningKeyPath,
            Encryption,
            Sources.Select(source => source.Capture()).ToArray(),
            Animations);

    internal static ProjectSettings FromSnapshot(ProjectSnapshot snapshot)
    {
        var settings = new ProjectSettings(snapshot.BaseDirectory)
        {
            PackageId = snapshot.PackageId,
            DisplayName = snapshot.DisplayName,
            FramePattern = snapshot.FramePattern,
            Packer = snapshot.Packer,
            MaxWidth = snapshot.MaxWidth,
            MaxHeight = snapshot.MaxHeight,
            Padding = snapshot.Padding,
            Pow2 = snapshot.Pow2,
            Rotate = snapshot.Rotate,
            PackAlign = snapshot.PackAlign,
            TrimThreshold = snapshot.TrimThreshold,
            Extrude = snapshot.Extrude,
            AlphaDilate = snapshot.AlphaDilate,
            RasterFormats = snapshot.RasterFormats,
            RasterQuality = snapshot.RasterQuality,
            TextureTargets = snapshot.TextureTargets,
            TexturePixelFormat = snapshot.TexturePixelFormat,
            TextureDither = snapshot.TextureDither,
            Mipmaps = snapshot.Mipmaps,
            TextureQuality = snapshot.TextureQuality,
            OutputPath = snapshot.OutputPath,
            OutputKind = snapshot.OutputKind,
            ManifestFormatName = snapshot.ManifestFormat,
            SigningKeyPath = snapshot.SigningKeyPath,
            Encryption = snapshot.Encryption
        };

        foreach (var source in snapshot.Sources)
        {
            settings.Sources.Add(SourceSettings.FromSnapshot(source));
        }

        settings.Animations = snapshot.Animations;
        return settings;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        MarkChanged();
        return true;
    }

    private void MarkChanged()
    {
        OnPropertyChanged(nameof(EffectivePackAlignment));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var source in _trackedSources)
            {
                source.PropertyChanged -= OnSourceChanged;
            }

            _trackedSources.Clear();
        }

        if (args.OldItems is not null)
        {
            foreach (var item in args.OldItems.OfType<SourceSettings>())
            {
                item.PropertyChanged -= OnSourceChanged;
                _trackedSources.Remove(item);
            }
        }

        foreach (var source in Sources)
        {
            if (_trackedSources.Contains(source))
            {
                continue;
            }

            source.PropertyChanged += OnSourceChanged;
            _trackedSources.Add(source);
        }

        MarkChanged();
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs args) => MarkChanged();

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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
}

internal sealed record ProjectSnapshot(
    string BaseDirectory,
    string PackageId,
    string DisplayName,
    string FramePattern,
    string Packer,
    int MaxWidth,
    int MaxHeight,
    int Padding,
    bool Pow2,
    bool Rotate,
    int PackAlign,
    int TrimThreshold,
    int Extrude,
    bool AlphaDilate,
    string RasterFormats,
    int RasterQuality,
    string TextureTargets,
    string TexturePixelFormat,
    string TextureDither,
    bool Mipmaps,
    string TextureQuality,
    string OutputPath,
    string OutputKind,
    string ManifestFormat,
    string SigningKeyPath,
    PackageEncryptionOptions? Encryption,
    IReadOnlyList<SourceSnapshot> Sources,
    IReadOnlyList<AnimationSpec> Animations);
