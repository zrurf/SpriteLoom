using CommunityToolkit.Mvvm.ComponentModel;

namespace SpriteLoom.Gui.ViewModels;

public sealed class SourceSettings : ObservableObject
{
    private string _id = "source";
    private string _kind = "image-sequence";
    private string _path = string.Empty;
    private string _image = string.Empty;
    private string _order = "natural";
    private int _frameDurationMs = 100;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Kind
    {
        get => _kind;
        set => SetProperty(ref _kind, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    public string Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    public int FrameDurationMs
    {
        get => _frameDurationMs;
        set => SetProperty(ref _frameDurationMs, value);
    }

    internal SourceSnapshot Capture() =>
        new(Id, Kind, Path, string.IsNullOrWhiteSpace(Image) ? null : Image, Order, FrameDurationMs);

    internal static SourceSettings FromSnapshot(SourceSnapshot snapshot) =>
        new()
        {
            Id = snapshot.Id,
            Kind = snapshot.Kind,
            Path = snapshot.Path,
            Image = snapshot.Image ?? string.Empty,
            Order = snapshot.Order,
            FrameDurationMs = snapshot.FrameDurationMs
        };
}

internal sealed record SourceSnapshot(
    string Id,
    string Kind,
    string Path,
    string? Image,
    string Order,
    int FrameDurationMs);
