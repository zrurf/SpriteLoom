namespace SpriteLoom.Bundle;

public sealed record VerificationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Errors.Count == 0;
}
