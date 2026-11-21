using SpriteLoom.Foundation;

namespace SpriteLoom.Abstractions;

public interface IDiagnosticsSink
{
    void Report(Diagnostic diagnostic);
}
