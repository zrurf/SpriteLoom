using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpriteLoom.Hosting;

namespace SpriteLoom.Gui.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private LoadedPackage? _package;
    private IReadOnlyList<FrameInfo> _frames = [];
    private readonly DispatcherTimer _timer;
    private AnimationInfo? _selectedAnimation;
    private int _frameIndex;
    private bool _updatingFrame;
    private bool _isPlaying;
    private bool _isBusy;
    private string _status = "Open a package or pack a project.";
    private string _projectPath = "sloom.toml";
    private string _packagePath = string.Empty;
    private Bitmap? _atlasBitmap;
    private Bitmap? _frameBitmap;
    private Rect _highlight;
    private Rect _frameSource;
    private Rect _frameDestination;
    private int _originWidth = 1;
    private int _originHeight = 1;
    private CancellationTokenSource? _cancellation;

    public MainWindowViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Advance();
    }

    public ObservableCollection<AnimationInfo> Animations { get; } = [];

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

    public AnimationInfo? SelectedAnimation
    {
        get => _selectedAnimation;
        set
        {
            if (SetProperty(ref _selectedAnimation, value))
            {
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
    private void OpenPackage()
    {
        try
        {
            LoadPackage(PackagePath);
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
            var projectPath = Directory.Exists(ProjectPath)
                ? Path.Combine(ProjectPath, "sloom.toml")
                : ProjectPath;
            var application = SloomApplication.Load(projectPath);
            var result = await Task.Run(
                () => application.PackAsync(_cancellation.Token),
                _cancellation.Token);
            Status = $"Packed {result.OutputPath}";
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
    private void Cancel()
    {
        _cancellation?.Cancel();
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
        _package?.Dispose();
    }

    private void LoadPackage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Package path is required.");
        }

        var package = LoadedPackage.Open(path);
        var previous = _package;
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

        SelectedAnimation = Animations.FirstOrDefault();
        Status = $"{Animations.Count} animations loaded from {Path.GetFileName(path)}.";
    }

    private void LoadSelectedAnimation()
    {
        _timer.Stop();
        IsPlaying = false;
        _frames = [];
        FrameIndex = 0;

        if (_package is null || SelectedAnimation is null)
        {
            ApplyFrame();
            return;
        }

        _frames = SelectedAnimation.FrameUris
            .Select(uri => _package.GetFrame(uri))
            .ToArray();

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
        var bitmap = _package.GetPageBitmap(frame.Page);
        AtlasBitmap = bitmap;
        FrameBitmap = bitmap;
        Highlight = frame.Rect;
        FrameSource = frame.Rect;
        FrameDestination = new Rect(frame.OffsetX, frame.OffsetY, frame.Rect.Width, frame.Rect.Height);
        OriginWidth = frame.OriginWidth;
        OriginHeight = frame.OriginHeight;
        OnPropertyChanged(nameof(FrameLabel));
        UpdateTimerInterval();
    }

    private void Advance()
    {
        if (_frames.Count == 0)
        {
            return;
        }

        if (FrameIndex + 1 >= _frames.Count && SelectedAnimation?.Loop == false)
        {
            _timer.Stop();
            IsPlaying = false;
            return;
        }

        FrameIndex = (FrameIndex + 1) % _frames.Count;
    }

    private void UpdateTimerInterval()
    {
        var duration = SelectedAnimation is { } animation &&
                       FrameIndex < animation.DurationsMs.Count
            ? animation.DurationsMs[FrameIndex]
            : 33;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, duration));
    }
}
