namespace SpriteLoom.Gui.ViewModels;

internal static class PipelineGraphBuilder
{
    public static IReadOnlyList<PipelineNodeViewModel> Build(ProjectSettings settings)
    {
        var nodes = new List<PipelineNodeViewModel>
        {
            new("import", "Import", $"{settings.Sources.Count} sources", "sources"),
            new("trim", "Trim", $"threshold {settings.TrimThreshold}", "import.out"),
            new("dedup", "Deduplicate", "content hash", "trim.out"),
            new(
                "pack",
                "Pack",
                $"{settings.Packer} · {settings.MaxWidth}×{settings.MaxHeight}",
                "dedup.out"),
            new("compose", "Compose", "page surfaces", "dedup.out, pack.out"),
            new("bleed", "Bleed", $"extrude {settings.Extrude}", "compose.out, dedup.out")
        };

        var targets = SplitList(settings.TextureTargets);

        if (settings.Mipmaps && targets.Count > 0)
        {
            nodes.Add(new PipelineNodeViewModel("mipmap", "Mipmaps", "box filter chain", "bleed.out"));
        }

        nodes.Add(new PipelineNodeViewModel(
            "encode",
            "Encode",
            $"{settings.RasterFormats} · q{settings.RasterQuality}",
            settings.Mipmaps && targets.Count > 0 ? "mipmap.out" : "bleed.out"));

        if (targets.Count > 0)
        {
            var alignment = settings.EffectivePackAlignment;
            nodes.Add(new PipelineNodeViewModel(
                "texture",
                "Textures",
                $"{string.Join(", ", targets)} · align {alignment}",
                settings.Mipmaps ? "mipmap.out" : "bleed.out"));
        }

        nodes.Add(new PipelineNodeViewModel(
            "animations",
            "Animations",
            $"{settings.Animations.Count} documents",
            "dedup.out"));

        nodes.Add(new PipelineNodeViewModel(
            "manifest",
            "Manifest",
            settings.ManifestFormatName.ToUpperInvariant(),
            "dedup.out, encode.out, animations.out"));

        nodes.Add(new PipelineNodeViewModel(
            "package",
            "Package",
            $"{settings.OutputKind} → {settings.OutputPath}",
            "manifest.out"));

        for (var index = 0; index < nodes.Count; index++)
        {
            nodes[index].IsLast = index == nodes.Count - 1;
        }

        return nodes;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public sealed class PipelineNodeViewModel(
    string id,
    string title,
    string detail,
    string inputs)
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string Inputs { get; } = inputs;

    public bool IsLast { get; set; }
}
