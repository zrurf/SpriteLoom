namespace SpriteLoom.Repository.Tests;

internal static class LayerRules
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Allowed { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["SpriteLoom.Foundation"] = Set(),
            ["SpriteLoom.Model"] = Set("SpriteLoom.Foundation"),
            ["SpriteLoom.Abstractions"] = Set("SpriteLoom.Foundation", "SpriteLoom.Model"),
            ["SpriteLoom.Imaging"] = Set("SpriteLoom.Foundation", "SpriteLoom.Model", "SpriteLoom.Abstractions"),
            ["SpriteLoom.Packing"] = Set("SpriteLoom.Foundation", "SpriteLoom.Model", "SpriteLoom.Imaging"),
            ["SpriteLoom.Codecs"] = Set("SpriteLoom.Foundation", "SpriteLoom.Abstractions", "SpriteLoom.Imaging"),
            ["SpriteLoom.Textures"] = Set("SpriteLoom.Foundation", "SpriteLoom.Abstractions", "SpriteLoom.Imaging"),
            ["SpriteLoom.Compression"] = Set("SpriteLoom.Foundation"),
            ["SpriteLoom.Security"] = Set("SpriteLoom.Foundation"),
            ["SpriteLoom.Bundle"] = Set(
                "SpriteLoom.Foundation",
                "SpriteLoom.Model",
                "SpriteLoom.Compression",
                "SpriteLoom.Security"),
            ["SpriteLoom.Pipeline"] = Set(
                "SpriteLoom.Foundation",
                "SpriteLoom.Abstractions",
                "SpriteLoom.Imaging",
                "SpriteLoom.Packing",
                "SpriteLoom.Codecs",
                "SpriteLoom.Textures",
                "SpriteLoom.Compression",
                "SpriteLoom.Security",
                "SpriteLoom.Bundle"),
            ["SpriteLoom.Hosting"] = Set(
                "SpriteLoom.Abstractions",
                "SpriteLoom.Codecs",
                "SpriteLoom.Textures",
                "SpriteLoom.Compression",
                "SpriteLoom.Security",
                "SpriteLoom.Pipeline"),
            ["SpriteLoom.PluginHost"] = Set("SpriteLoom.Abstractions", "SpriteLoom.Pipeline"),
            ["SpriteLoom.Commands"] = Set(
                "SpriteLoom.Foundation",
                "SpriteLoom.Model",
                "SpriteLoom.Hosting",
                "SpriteLoom.Security",
                "SpriteLoom.Bundle",
                "SpriteLoom.Codecs",
                "SpriteLoom.Textures"),
            ["SpriteLoom.Cli.Jit"] = Set(
                "SpriteLoom.Commands",
                "SpriteLoom.Hosting",
                "SpriteLoom.PluginHost"),
            ["SpriteLoom.Gui"] = Set(
                "SpriteLoom.Foundation",
                "SpriteLoom.Model",
                "SpriteLoom.Bundle",
                "SpriteLoom.Hosting",
                "SpriteLoom.Pipeline",
                "SpriteLoom.Textures")
        };

    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}
