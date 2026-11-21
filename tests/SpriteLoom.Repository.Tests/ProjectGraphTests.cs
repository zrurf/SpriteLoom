using System.Xml.Linq;

namespace SpriteLoom.Repository.Tests;

public sealed class ProjectGraphTests
{
    [Fact]
    public void Source_projects_only_reference_allowed_dependencies()
    {
        var projects = LoadSourceProjects();

        foreach (var (name, references) in projects)
        {
            if (!LayerRules.Allowed.TryGetValue(name, out var allowed))
            {
                Assert.Fail($"Missing layer rule for {name}.");
                continue;
            }

            foreach (var reference in references)
            {
                Assert.Contains(reference, allowed);
            }
        }
    }

    [Fact]
    public void Source_project_graph_is_acyclic()
    {
        var projects = LoadSourceProjects();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in projects.Keys)
        {
            Visit(name, projects, visiting, visited);
        }
    }

    [Fact]
    public void Source_tree_contains_no_build_outputs()
    {
        var sourceRoot = Path.Combine(RepositoryLayout.Root, "src");

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(directory);
            Assert.False(name is "bin" or "obj", $"Build output directory found: {directory}");
        }
    }

    private static void Visit(
        string name,
        IReadOnlyDictionary<string, string[]> projects,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (!visited.Add(name))
        {
            return;
        }

        visiting.Add(name);

        foreach (var reference in projects[name])
        {
            Assert.False(visiting.Contains(reference), $"Cycle detected: {name} -> {reference}.");
            Visit(reference, projects, visiting, visited);
        }

        visiting.Remove(name);
    }

    private static IReadOnlyDictionary<string, string[]> LoadSourceProjects()
    {
        var sourceRoot = Path.Combine(RepositoryLayout.Root, "src");
        var projects = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var projectPath in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);

            var references = document
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFileNameWithoutExtension(
                    Path.GetFullPath(Path.Combine(projectDirectory, value!))))
                .ToArray();

            projects[projectName] = references;
        }

        return projects;
    }
}
