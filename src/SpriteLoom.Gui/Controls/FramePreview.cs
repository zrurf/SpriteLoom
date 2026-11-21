using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SpriteLoom.Gui.Controls;

public sealed class FramePreview : Control
{
    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<FramePreview, Bitmap?>(nameof(Bitmap));

    public static readonly StyledProperty<Rect> SourceProperty =
        AvaloniaProperty.Register<FramePreview, Rect>(nameof(Source));

    public static readonly StyledProperty<Rect> DestinationProperty =
        AvaloniaProperty.Register<FramePreview, Rect>(nameof(Destination));

    public static readonly StyledProperty<int> OriginWidthProperty =
        AvaloniaProperty.Register<FramePreview, int>(nameof(OriginWidth), 1);

    public static readonly StyledProperty<int> OriginHeightProperty =
        AvaloniaProperty.Register<FramePreview, int>(nameof(OriginHeight), 1);

    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public Rect Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Rect Destination
    {
        get => GetValue(DestinationProperty);
        set => SetValue(DestinationProperty, value);
    }

    public int OriginWidth
    {
        get => GetValue(OriginWidthProperty);
        set => SetValue(OriginWidthProperty, value);
    }

    public int OriginHeight
    {
        get => GetValue(OriginHeightProperty);
        set => SetValue(OriginHeightProperty, value);
    }

    static FramePreview()
    {
        AffectsRender<FramePreview>(
            BitmapProperty,
            SourceProperty,
            DestinationProperty,
            OriginWidthProperty,
            OriginHeightProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bitmap is not { } bitmap || Source.Width <= 0 || Source.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / OriginWidth, Bounds.Height / OriginHeight);
        var origin = new Rect(
            (Bounds.Width - OriginWidth * scale) / 2,
            (Bounds.Height - OriginHeight * scale) / 2,
            OriginWidth * scale,
            OriginHeight * scale);

        context.DrawRectangle(new Pen(Brushes.Gray, 1, new DashStyle([2, 2], 0)), origin);
        var destination = new Rect(
            origin.X + Destination.X * scale,
            origin.Y + Destination.Y * scale,
            Destination.Width * scale,
            Destination.Height * scale);
        context.DrawImage(bitmap, Source, destination);
    }
}
