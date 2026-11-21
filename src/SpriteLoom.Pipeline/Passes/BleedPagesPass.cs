using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class BleedPagesPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "bleed.page",
        "1.0",
        [
            new PortSpec("pages", ArtifactKind.PageSet),
            new PortSpec("frames", ArtifactKind.FrameSet)
        ],
        [new PortSpec("out", ArtifactKind.PageSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var pages = (ComposedPageSet)inputs["pages"].Payload;
        var frames = (FrameSet)inputs["frames"].Payload;
        var options = new BleedOptions(
            PassParameters.GetInt(context.Parameters, "extrude", 1),
            PassParameters.GetBool(context.Parameters, "alphaDilate", true));

        FrameOperations.Bleed(pages.Pages, frames, options);

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.PageSet, 0, pages)
            });
    }
}
