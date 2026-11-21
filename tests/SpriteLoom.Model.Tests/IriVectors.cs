using System.Text.Json;

namespace SpriteLoom.Model.Tests;

internal sealed record IriVector(
    string Name,
    string Kind,
    string Input,
    string? ExpectedIri,
    string? ExpectedCode);

internal static class IriVectors
{
    public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "vectors", "iri");

    public static IEnumerable<IriVector> ReadAll()
    {
        if (!Directory.Exists(Root))
        {
            yield break;
        }

        var files = Directory
            .EnumerateFiles(Root, "case.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var path in files)
        {
            yield return Read(path);
        }
    }

    private static IriVector Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var expected = root.GetProperty("expected");

        return new IriVector(
            root.GetProperty("name").GetString()!,
            root.GetProperty("kind").GetString()!,
            root.GetProperty("input").GetProperty("iri").GetString()!,
            expected.TryGetProperty("iri", out var iri) ? iri.GetString() : null,
            expected.TryGetProperty("code", out var code) ? code.GetString() : null);
    }
}
