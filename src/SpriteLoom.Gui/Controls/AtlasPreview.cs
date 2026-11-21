using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SpriteLoom.Gui.Controls;

public sealed class AtlasPreview : Control
{
    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<AtlasPreview, Bitmap?>(nameof(Bitmap));

    public static readonly StyledProperty<Rect> HighlightProperty =
        AvaloniaProperty.Register<AtlasPreview, Rect>(nameof(Highlight));

    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public Rect Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    static AtlasPreview()
    {
        AffectsRender<AtlasPreview>(BitmapProperty, HighlightProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bitmap is not { } bitmap)
        {
            return;
        }

        var source = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var scale = Math.Min(Bounds.Width / source.Width, Bounds.Height / source.Height);
        var destination = new Rect(
            (Bounds.Width - source.Width * scale) / 2,
            (Bounds.Height - source.Height * scale) / 2,
            source.Width * scale,
            source.Height * scale);
        context.DrawImage(bitmap, source, destination);

        if (Highlight.Width > 0 && Highlight.Height > 0)
        {
            var highlight = new Rect(
                destination.X + Highlight.X * scale,
                destination.Y + Highlight.Y * scale,
                Highlight.Width * scale,
                Highlight.Height * scale);
            context.DrawRectangle(null, new Pen(Brushes.OrangeRed, 2), highlight);
        }
    }
}
