namespace SpriteLoom.Foundation;

public sealed record Diagnostic(string Code, DiagnosticSeverity Severity, string Message)
{
    public static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);

    public static Diagnostic Warning(string code, string message) =>
        new(code, DiagnosticSeverity.Warning, message);

    public static Diagnostic Info(string code, string message) =>
        new(code, DiagnosticSeverity.Info, message);
}
