namespace SpriteLoom.Model.Manifest;

public sealed class AnimationDocument
{
    public const int CurrentFormatMajor = 1;
    public const int CurrentFormatMinor = 0;

    public required string Uri { get; init; }

    public required IReadOnlyList<uint> Frames { get; init; }

    public required IReadOnlyList<int> DurationsMs { get; init; }

    public required bool Loop { get; init; }

    public required string Playback { get; init; }

    public IReadOnlyList<AnimationEvent> Events { get; init; } = [];
}

public sealed record AnimationEvent(int Frame, string Kind);
