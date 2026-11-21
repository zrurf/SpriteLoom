namespace SpriteLoom.Bundle;

internal static class DirectoryFiles
{
    public static IReadOnlyList<string> Enumerate(string root)
    {
        var files = new List<string>();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');

            if (relativePath.StartsWith("signatures/", StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(relativePath);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
