namespace SpriteLoom.Foundation;

public static class PixelFormats
{
    public static string GetName(PixelFormat format) => format switch
    {
        PixelFormat.Rgba8888 => "rgba8888",
        PixelFormat.Bgra8888 => "bgra8888",
        PixelFormat.Rgb888 => "rgb888",
        PixelFormat.Rgb565 => "rgb565",
        PixelFormat.Rgba4444 => "rgba4444",
        PixelFormat.R8 => "r8",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
    };

    public static PixelFormat Parse(string name) => name switch
    {
        "rgba8888" => PixelFormat.Rgba8888,
        "bgra8888" => PixelFormat.Bgra8888,
        "rgb888" => PixelFormat.Rgb888,
        "rgb565" => PixelFormat.Rgb565,
        "rgba4444" => PixelFormat.Rgba4444,
        "r8" => PixelFormat.R8,
        _ => throw new ArgumentException($"Unknown pixel format: {name}", nameof(name))
    };

    public static bool HasAlpha(PixelFormat format) =>
        format is PixelFormat.Rgba8888 or PixelFormat.Bgra8888 or PixelFormat.Rgba4444;

    public static bool RequiresOpaque(PixelFormat format) =>
        format is PixelFormat.Rgb888 or PixelFormat.Rgb565;

    public static int GetBytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Rgba8888 or PixelFormat.Bgra8888 => 4,
        PixelFormat.Rgb888 => 3,
        PixelFormat.Rgb565 or PixelFormat.Rgba4444 => 2,
        PixelFormat.R8 => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.")
    };

    public static string GetDitherName(DitherMode mode) => mode switch
    {
        DitherMode.None => "none",
        DitherMode.Ordered4 => "ordered4",
        DitherMode.Ordered8 => "ordered8",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown dither mode.")
    };

    public static DitherMode ParseDither(string name) => name switch
    {
        "none" => DitherMode.None,
        "ordered4" => DitherMode.Ordered4,
        "ordered8" => DitherMode.Ordered8,
        _ => throw new ArgumentException($"Unknown dither mode: {name}", nameof(name))
    };
}
