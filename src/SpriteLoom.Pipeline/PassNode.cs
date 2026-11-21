using SpriteLoom.Abstractions;

namespace SpriteLoom.Pipeline;

public sealed class PassNode
{
    public required string Id { get; init; }

    public required IPass Pass { get; init; }

    public required IReadOnlyDictionary<string, string> Inputs { get; init; }

    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
}
