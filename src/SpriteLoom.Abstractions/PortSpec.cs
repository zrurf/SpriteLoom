namespace SpriteLoom.Abstractions;

public sealed record PortSpec(string Name, ArtifactKind Kind, bool Required = true);
