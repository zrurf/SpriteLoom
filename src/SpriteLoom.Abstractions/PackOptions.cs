namespace SpriteLoom.Abstractions;

public sealed record PackOptions(
    int MaxWidth,
    int MaxHeight,
    int Padding,
    bool Pow2,
    int Align,
    bool AllowRotation);
