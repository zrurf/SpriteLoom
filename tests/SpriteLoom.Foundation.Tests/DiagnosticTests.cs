using SpriteLoom.Foundation;

namespace SpriteLoom.Foundation.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void Error_sets_code_severity_and_message()
    {
        var diagnostic = Diagnostic.Error("SL9999", "boom");

        Assert.Equal("SL9999", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("boom", diagnostic.Message);
    }
}
