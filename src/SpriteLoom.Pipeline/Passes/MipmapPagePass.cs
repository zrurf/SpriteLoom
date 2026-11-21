using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class MipmapPagePass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "mipmap.page",
        "1.0",
        [new PortSpec("in", ArtifactKind.PageSet)],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        if (inputs["in"].Payload is MipmapPageSet existing)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
                new Dictionary<string, Artifact>(StringComparer.Ordinal)
                {
                    ["out"] = new Artifact(ArtifactKind.PageSet, 0, existing)
                });
        }

        var pages = (ComposedPageSet)inputs["in"].Payload;
        var mipmapped = pages.Pages
            .Select(page => new MipmapPage(
                page.Index,
                page.Layout,
                MipmapGenerator.Generate(page.Pixels)))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, new MipmapPageSet(mipmapped))
            });
    }
}
