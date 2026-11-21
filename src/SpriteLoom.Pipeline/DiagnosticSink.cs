using SpriteLoom.Abstractions;
using SpriteLoom.Foundation;

namespace SpriteLoom.Pipeline;

public sealed class DiagnosticSink : IDiagnosticsSink
{
    private readonly List<Diagnostic> _diagnostics = [];

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public int ErrorCount => _diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public void Report(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);
}
