using System.Text.RegularExpressions;

namespace SpriteLoom.Repository.Tests;

public sealed class SourcePolicyTests
{
    private static readonly Regex CjkCharacters = new(
        @"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]",
        RegexOptions.Compiled);

    [Fact]
    public void Source_files_contain_no_cjk_characters()
    {
        foreach (var file in Enumerate("src", "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.False(CjkCharacters.IsMatch(text), $"CJK characters found in {file}.");
        }
    }

    [Fact]
    public void Versioned_text_does_not_reference_local_notes()
    {
        var forbidden = "dev" + "-note";
        var files = Enumerate("src", "*.cs")
            .Concat(Enumerate(".github", "*.yml"))
            .Concat(Enumerate(".github", "*.yaml"))
            .Concat(Enumerate("docs", "*.md"))
            .Concat(
            [
                Path.Combine(RepositoryLayout.Root, "README.md"),
                Path.Combine(RepositoryLayout.Root, "CONTRIBUTING.md")
            ]);

        foreach (var file in files.Where(File.Exists))
        {
            var text = File.ReadAllText(file);
            Assert.False(
                text.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Forbidden reference found in {file}.");
        }
    }

    private static IEnumerable<string> Enumerate(string relativeRoot, string pattern)
    {
        var root = Path.Combine(RepositoryLayout.Root, relativeRoot);
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            : [];
    }
}
