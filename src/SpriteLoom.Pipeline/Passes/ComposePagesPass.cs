using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class ComposePagesPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "compose.page",
        "1.0",
        [
            new PortSpec("frames", ArtifactKind.FrameSet),
            new PortSpec("layouts", ArtifactKind.PageSet)
        ],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var frames = (FrameSet)inputs["frames"].Payload;
        var layouts = (PageLayoutSet)inputs["layouts"].Payload;
        var pages = FrameOperations.Compose(frames, layouts.Pages);

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, new ComposedPageSet(pages))
            });
    }
}
