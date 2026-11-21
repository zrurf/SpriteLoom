namespace SpriteLoom.Bundle;

public enum VerificationLevel
{
    Strict,
    Permissive
}

public sealed class VerificationOptions
{
    public VerificationLevel Level { get; init; } = VerificationLevel.Strict;

    public IReadOnlyList<string> AllowedExtraPaths { get; init; } = [];

    public bool IsAllowed(string path) =>
        AllowedExtraPaths.Any(allowed =>
            path.Equals(allowed, StringComparison.Ordinal) ||
            (allowed.EndsWith('/') && path.StartsWith(allowed, StringComparison.Ordinal)));
}
