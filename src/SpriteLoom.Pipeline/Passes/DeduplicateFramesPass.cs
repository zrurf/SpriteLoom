using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class DeduplicateFramesPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "store.deduplicate",
        "1.0",
        [new PortSpec("in", ArtifactKind.FrameSet)],
        [new PortSpec("out", ArtifactKind.FrameSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var frames = (FrameSet)inputs["in"].Payload;
        var deduplicated = FrameOperations.Deduplicate(frames);

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.FrameSet, 0, deduplicated)
            });
    }
}
