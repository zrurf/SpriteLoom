namespace SpriteLoom.Abstractions;

public readonly record struct Artifact(ArtifactKind Kind, ulong Hash, object Payload);
