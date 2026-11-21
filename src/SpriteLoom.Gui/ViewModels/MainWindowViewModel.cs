using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpriteLoom.Hosting;
using SpriteLoom.Pipeline.Configuration;

namespace SpriteLoom.Gui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly SnapshotHistory<ProjectSnapshot> _history = new();
    private readonly DispatcherTimer _timer;
    private ProjectSnapshot? _lastSnapshot;
    private LoadedPackage? _package;
    private IReadOnlyList<FrameInfo> _frames = [];
    private AnimationInfo? _selectedAnimation;
    private SourceSettings? _selectedSource;
    private PipelineNodeViewModel? _selectedPass;
    private ProjectSettings? _settings;
    private int _frameIndex;
    private int _pageRequest;
    private int _playbackDirection = 1;
    private bool _updatingFrame;
    private bool _isPlaying;
    private bool _isBusy;
    private bool _isDirty;
    private string _status = "Open a project or a package.";
    private string _projectPath = "sloom.toml";
    private string _projectFile = string.Empty;
    private string _packagePath = string.Empty;
    private string _manifestText = string.Empty;
    private string _manifestFormat = string.Empty;
    private string _packageSummary = string.Empty;
    private Bitmap? _atlasBitmap;
    private Bitmap? _frameBitmap;
    private Rect _highlight;
    private Rect _frameSource;
    private Rect _frameDestination;
    private int _originWidth = 1;
    private int _originHeight = 1;
    private CancellationTokenSource? _cancellation;

    public MainWindowViewModel()
        : this(null)
    {
    }

    public MainWindowViewModel(string? initialProject)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Advance();
        Capabilities = SloomCapabilities.Describe();

        if (!string.IsNullOrWhiteSpace(initialProject))
        {
            ProjectPath = initialProject;
            OpenProject();
        }
    }

    public ObservableCollection<AnimationInfo> Animations { get; } = [];

    public ObservableCollection<TimelineFrameInfo> TimelineFrames { get; } = [];

    public ObservableCollection<PipelineNodeViewModel> PipelineNodes { get; } = [];

    public IReadOnlyList<string> Packers { get; } = ["pack.maxrects", "pack.basic"];

    public IReadOnlyList<string> SourceKinds { get; } = ["image-sequence", "texturepacker"];

    public IReadOnlyList<string> SourceOrders { get; } = ["natural", "lexicographic"];

    public IReadOnlyList<string> OutputKinds { get; } = ["directory", "bundle"];

    public IReadOnlyList<string> ManifestFormats { get; } = ["json", "cbor"];

    public IReadOnlyList<string> PixelFormatNames { get; } =
        ["rgba8888", "bgra8888", "rgb888", "rgb565", "rgba4444", "r8"];

    public IReadOnlyList<string> DitherNames { get; } = ["none", "ordered4", "ordered8"];

    public IReadOnlyList<string> TextureQualityNames { get; } = ["fast", "balanced", "high"];

    public SloomCapabilities Capabilities { get; }

    public string NativeEncoderStatus =>
        Capabilities.NativeEncoderAvailable ? "available" : "unavailable";

    public ProjectSettings? Settings
    {
        get => _settings;
        private set
        {
            if (ReferenceEquals(_settings, value))
            {
                return;
            }

            if (_settings is not null)
            {
                _settings.Changed -= OnSettingsChanged;
            }

            _settings = value;

            if (_settings is not null)
            {
                _settings.Changed += OnSettingsChanged;
            }

            _lastSnapshot = _settings?.Capture();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProjectCaption));
            SelectedSource = _settings?.Sources.FirstOrDefault();
            RefreshPipeline();
        }
    }

    public string ProjectCaption =>
        Settings is null
            ? "No project"
            : $"{Settings.PackageId}{(IsDirty ? " *" : string.Empty)}";

    public string ProjectPath
    {
        get => _projectPath;
        set => SetProperty(ref _projectPath, value);
    }

    public string PackagePath
    {
        get => _packagePath;
        set => SetProperty(ref _packagePath, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string ManifestText
    {
        get => _manifestText;
        private set => SetProperty(ref _manifestText, value);
    }

    public string ManifestFormat
    {
        get => _manifestFormat;
        private set => SetProperty(ref _manifestFormat, value);
    }

    public string PackageSummary
    {
        get => _packageSummary;
        private set => SetProperty(ref _packageSummary, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(ProjectCaption));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public SourceSettings? SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    public PipelineNodeViewModel? SelectedPass
    {
        get => _selectedPass;
        set
        {
            if (SetProperty(ref _selectedPass, value))
            {
                NotifyInspectorVisibility();
            }
        }
    }

    public bool ShowImportInspector => SelectedPass?.Id == "import";

    public bool ShowTrimInspector => SelectedPass?.Id == "trim";

    public bool ShowPackInspector => SelectedPass?.Id == "pack";

    public bool ShowBleedInspector => SelectedPass?.Id == "bleed";

    public bool ShowEncodeInspector => SelectedPass?.Id == "encode";

    public bool ShowTextureInspector => SelectedPass?.Id == "texture";

    public bool ShowAnimationsInspector => SelectedPass?.Id == "animations";

    public bool ShowManifestInspector => SelectedPass?.Id == "manifest";

    public bool ShowPackageInspector => SelectedPass?.Id == "package";

    public bool ShowEmptyInspector =>
        SelectedPass is null or { Id: "dedup" or "compose" or "mipmap" };

    public AnimationInfo? SelectedAnimation
    {
        get => _selectedAnimation;
        set
        {
            if (SetProperty(ref _selectedAnimation, value))
            {
                _playbackDirection = 1;
                LoadSelectedAnimation();
            }
        }
    }

    public int FrameIndex
    {
        get => _frameIndex;
        set
        {
            var max = Math.Max(0, _frames.Count - 1);
            var clamped = Math.Clamp(value, 0, max);

            if (SetProperty(ref _frameIndex, clamped))
            {
                ApplyFrame();
            }
        }
    }

    public int FrameCount => _frames.Count;

    public int MaxFrameIndex => Math.Max(0, FrameCount - 1);

    public string FrameLabel => FrameCount == 0 ? "0 / 0" : $"{FrameIndex + 1} / {FrameCount}";

    public Bitmap? AtlasBitmap
    {
        get => _atlasBitmap;
        private set => SetProperty(ref _atlasBitmap, value);
    }

    public Bitmap? FrameBitmap
    {
        get => _frameBitmap;
        private set => SetProperty(ref _frameBitmap, value);
    }

    public Rect Highlight
    {
        get => _highlight;
        private set => SetProperty(ref _highlight, value);
    }

    public Rect FrameSource
    {
        get => _frameSource;
        private set => SetProperty(ref _frameSource, value);
    }

    public Rect FrameDestination
    {
        get => _frameDestination;
        private set => SetProperty(ref _frameDestination, value);
    }

    public int OriginWidth
    {
        get => _originWidth;
        private set => SetProperty(ref _originWidth, value);
    }

    public int OriginHeight
    {
        get => _originHeight;
        private set => SetProperty(ref _originHeight, value);
    }

    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            var projectFile = ResolveProjectFile(ProjectPath);
            var config = ProjectConfigLoader.Load(projectFile);
            _projectFile = projectFile;
            ProjectPath = projectFile;
            Settings = ProjectSettings.FromConfig(config);
            _history.Clear();
            IsDirty = false;
            PackagePath = Path.GetFullPath(Path.Combine(config.BaseDirectory, config.OutputPath));
            Status = $"Project '{config.PackageId}' loaded from {projectFile}.";

            if (File.Exists(PackagePath) || Directory.Exists(PackagePath))
            {
                try
                {
                    LoadPackage(PackagePath);
                    Status = $"Project '{config.PackageId}' loaded; previewing {Path.GetFileName(PackagePath)}.";
                }
                catch (Exception exception)
                {
                    Status = $"Project '{config.PackageId}' loaded; preview unavailable: {exception.Message}";
                }
            }
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        if (Settings is null || string.IsNullOrWhiteSpace(_projectFile))
        {
            Status = "Open a project before saving.";
            return;
        }

        try
        {
            var config = Settings.ToConfig();
            ProjectConfigWriter.Write(config, _projectFile);
            ProjectPath = _projectFile;
            IsDirty = false;
            Status = $"Saved {_projectFile}.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private void OpenPackage()
    {
        try
        {
            LoadPackage(ResolvePackagePath(PackagePath));
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
    }

    [RelayCommand]
    private async Task PackAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = "Packing...";
        _cancellation = new CancellationTokenSource();

        try
        {
            SloomApplication application;

            if (Settings is not null)
            {
                application = SloomApplication.FromConfig(Settings.ToConfig());
            }
            else
            {
                application = SloomApplication.Load(ResolveProjectFile(ProjectPath));
            }

            var result = await Task.Run(
                () => application.PackAsync(_cancellation.Token),
                _cancellation.Token);
            Status = $"Packed {result.OutputPath} ({result.CacheHits} hits, " +
                     $"{result.CacheMisses} misses, {result.Diagnostics.Count} diagnostics).";
            PackagePath = result.OutputPath;
            LoadPackage(result.OutputPath);
        }
        catch (OperationCanceledException)
        {
            Status = "Packing cancelled.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void Undo()
    {
        if (Settings is null || !_history.TryUndo(Settings.Capture(), out var snapshot))
        {
            return;
        }

        var selectedSourceId = SelectedSource?.Id;
        Settings = ProjectSettings.FromSnapshot(snapshot);
        SelectedSource = Settings.Sources.FirstOrDefault(source => source.Id == selectedSourceId)
            ?? Settings.Sources.FirstOrDefault();
        IsDirty = true;
        Status = "Undo.";
    }

    [RelayCommand]
    private void Redo()
    {
        if (Settings is null || !_history.TryRedo(Settings.Capture(), out var snapshot))
        {
            return;
        }

        var selectedSourceId = SelectedSource?.Id;
        Settings = ProjectSettings.FromSnapshot(snapshot);
        SelectedSource = Settings.Sources.FirstOrDefault(source => source.Id == selectedSourceId)
            ?? Settings.Sources.FirstOrDefault();
        IsDirty = true;
        Status = "Redo.";
    }

    [RelayCommand]
    private void AddSource()
    {
        if (Settings is null)
        {
            return;
        }

        SelectedSource = Settings.AddSource();
        Status = "Source added.";
    }

    [RelayCommand]
    private void RemoveSource()
    {
        if (Settings is null || SelectedSource is null)
        {
            return;
        }

        var index = Settings.Sources.IndexOf(SelectedSource);
        Settings.Sources.Remove(SelectedSource);
        SelectedSource = Settings.Sources.Count == 0
            ? null
            : Settings.Sources[Math.Clamp(index, 0, Settings.Sources.Count - 1)];
        Status = "Source removed.";
    }

    [RelayCommand]
    private void MoveSourceUp()
    {
        if (Settings is not null && SelectedSource is not null &&
            Settings.MoveSource(SelectedSource, -1))
        {
            Status = "Source moved up.";
        }
    }

    [RelayCommand]
    private void MoveSourceDown()
    {
        if (Settings is not null && SelectedSource is not null &&
            Settings.MoveSource(SelectedSource, 1))
        {
            Status = "Source moved down.";
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        if (_timer.IsEnabled)
        {
            _timer.Stop();
            IsPlaying = false;
            return;
        }

        _playbackDirection = 1;
        IsPlaying = true;
        UpdateTimerInterval();
        _timer.Start();
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        FrameIndex = (FrameIndex + 1) % _frames.Count;
    }

    [RelayCommand]
    private void PreviousFrame()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        FrameIndex = (FrameIndex - 1 + _frames.Count) % _frames.Count;
    }

    public void Dispose()
    {
        _timer.Stop();
        _cancellation?.Cancel();
        _cancellation?.Dispose();

        if (_settings is not null)
        {
            _settings.Changed -= OnSettingsChanged;
        }

        _package?.Dispose();
    }

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        if (Settings is null)
        {
            return;
        }

        if (_lastSnapshot is not null)
        {
            _history.Push(_lastSnapshot);
        }

        _lastSnapshot = Settings.Capture();
        IsDirty = true;
        RefreshPipeline();
    }

    private void RefreshPipeline()
    {
        var selectedId = SelectedPass?.Id;
        PipelineNodes.Clear();

        if (Settings is null)
        {
            SelectedPass = null;
            return;
        }

        foreach (var node in PipelineGraphBuilder.Build(Settings))
        {
            PipelineNodes.Add(node);
        }

        SelectedPass = PipelineNodes.FirstOrDefault(node => node.Id == selectedId)
            ?? PipelineNodes.FirstOrDefault();
    }

    private void NotifyInspectorVisibility()
    {
        OnPropertyChanged(nameof(ShowImportInspector));
        OnPropertyChanged(nameof(ShowTrimInspector));
        OnPropertyChanged(nameof(ShowPackInspector));
        OnPropertyChanged(nameof(ShowBleedInspector));
        OnPropertyChanged(nameof(ShowEncodeInspector));
        OnPropertyChanged(nameof(ShowTextureInspector));
        OnPropertyChanged(nameof(ShowAnimationsInspector));
        OnPropertyChanged(nameof(ShowManifestInspector));
        OnPropertyChanged(nameof(ShowPackageInspector));
        OnPropertyChanged(nameof(ShowEmptyInspector));
    }

    private void LoadPackage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Package path is required.");
        }

        var package = LoadedPackage.Open(path);
        var previous = _package;
        _pageRequest++;
        _package = package;
        AtlasBitmap = null;
        FrameBitmap = null;
        previous?.Dispose();
        _timer.Stop();
        IsPlaying = false;
        Animations.Clear();

        foreach (var uri in package.AnimationUris)
        {
            Animations.Add(package.LoadAnimation(uri));
        }

        ManifestText = package.ManifestText;
        ManifestFormat = package.ManifestFormat;
        PackageSummary = $"{package.PackageId} · {package.PageCount} pages · " +
                         $"{package.FrameCount} frames · {package.AnimationCount} animations";
        SelectedAnimation = Animations.FirstOrDefault();
        Status = $"{Animations.Count} animations loaded from {Path.GetFileName(path)}.";
    }

    private void LoadSelectedAnimation()
    {
        _timer.Stop();
        IsPlaying = false;
        _frames = [];
        TimelineFrames.Clear();
        _pageRequest++;
        FrameIndex = 0;

        if (_package is null || SelectedAnimation is null)
        {
            ApplyFrame();
            return;
        }

        _frames = SelectedAnimation.FrameUris
            .Select(uri => _package.GetFrame(uri))
            .ToArray();

        for (var index = 0; index < _frames.Count; index++)
        {
            var duration = index < SelectedAnimation.DurationsMs.Count
                ? SelectedAnimation.DurationsMs[index]
                : 33;
            var events = string.Join(
                ", ",
                SelectedAnimation.Events
                    .Where(animationEvent => animationEvent.Frame == index)
                    .Select(animationEvent => animationEvent.Kind));
            TimelineFrames.Add(new TimelineFrameInfo(index, duration, events));
        }

        OnPropertyChanged(nameof(FrameCount));
        OnPropertyChanged(nameof(MaxFrameIndex));
        _updatingFrame = true;
        FrameIndex = 0;
        _updatingFrame = false;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        if (_updatingFrame || _package is null || _frames.Count == 0)
        {
            OnPropertyChanged(nameof(FrameLabel));
            return;
        }

        var frame = _frames[Math.Clamp(FrameIndex, 0, _frames.Count - 1)];
        Highlight = frame.Rect;
        FrameSource = frame.Rect;
        FrameDestination = new Rect(frame.OffsetX, frame.OffsetY, frame.Rect.Width, frame.Rect.Height);
        OriginWidth = frame.OriginWidth;
        OriginHeight = frame.OriginHeight;
        OnPropertyChanged(nameof(FrameLabel));
        UpdateTimerInterval();
        var request = ++_pageRequest;

        if (_package.TryGetPageBitmap(frame.Page, out var cached))
        {
            AtlasBitmap = cached;
            FrameBitmap = cached;
            return;
        }

        _ = LoadPageBitmapAsync(_package, frame.Page, request);
    }

    private async Task LoadPageBitmapAsync(LoadedPackage package, int page, int request)
    {
        try
        {
            var bitmap = await Task.Run(() => package.GetPageBitmap(page));

            if (request != _pageRequest || !ReferenceEquals(package, _package))
            {
                return;
            }

            AtlasBitmap = bitmap;
            FrameBitmap = bitmap;
        }
        catch (Exception exception)
        {
            if (request == _pageRequest && ReferenceEquals(package, _package))
            {
                Status = exception.Message;
            }
        }
    }

    private void Advance()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        var playback = SelectedAnimation?.Playback ?? "forward";
        var loop = SelectedAnimation?.Loop ?? true;

        if (string.Equals(playback, "reverse", StringComparison.OrdinalIgnoreCase))
        {
            if (FrameIndex == 0)
            {
                if (!loop)
                {
                    StopPlayback();
                    return;
                }

                FrameIndex = _frames.Count - 1;
                return;
            }

            FrameIndex--;
            return;
        }

        if (string.Equals(playback, "pingpong", StringComparison.OrdinalIgnoreCase))
        {
            var next = FrameIndex + _playbackDirection;

            if (next >= _frames.Count || next < 0)
            {
                _playbackDirection = -_playbackDirection;
                next = FrameIndex + _playbackDirection;
            }

            if (next < 0 || next >= _frames.Count)
            {
                StopPlayback();
                return;
            }

            FrameIndex = next;
            return;
        }

        if (FrameIndex + 1 >= _frames.Count)
        {
            if (!loop)
            {
                StopPlayback();
                return;
            }

            FrameIndex = 0;
            return;
        }

        FrameIndex++;
    }

    private void StopPlayback()
    {
        _timer.Stop();
        IsPlaying = false;
    }

    private void UpdateTimerInterval()
    {
        var duration = SelectedAnimation is { } animation &&
                       FrameIndex < animation.DurationsMs.Count
            ? animation.DurationsMs[FrameIndex]
            : 33;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, duration));
    }

    private string ResolveProjectFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Project path is required.");
        }

        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath)
            ? Path.Combine(fullPath, "sloom.toml")
            : fullPath;
    }

    private string ResolvePackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Package path is required.");
        }

        if (Path.IsPathRooted(path) || Settings is null)
        {
            return path;
        }

        return Path.Combine(Settings.BaseDirectory, path);
    }
}
