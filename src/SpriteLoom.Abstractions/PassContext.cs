namespace SpriteLoom.Abstractions;

public sealed class PassContext
{
    public required string BaseDirectory { get; init; }

    public required IReadOnlyDictionary<string, object?> Parameters { get; init; }

    public required IDiagnosticsSink Diagnostics { get; init; }

    public required CancellationToken Cancellation { get; init; }
}
