namespace SpriteLoom.Abstractions;

public sealed record PassDescriptor(
    string Id,
    string Version,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    PassDeterminism Determinism)
{
    public bool Cacheable { get; init; } = true;
}
