namespace SpriteLoom.Abstractions;

public sealed record PageLayout(int Width, int Height, IReadOnlyList<Placement> Placements);
