using SpriteLoom.Abstractions;
using SpriteLoom.Imaging;

namespace SpriteLoom.Pipeline.Passes;

public sealed class BuildAnimationsPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "build.animations",
        "1.0",
        [new PortSpec("frames", ArtifactKind.FrameSet)],
        [new PortSpec("out", ArtifactKind.AnimationSet)],
        PassDeterminism.Deterministic);

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var frames = (FrameSet)inputs["frames"].Payload;
        var specs = PassParameters.GetValue<IReadOnlyList<AnimationSpec>>(context.Parameters, "animations")
            ?? [];

        var drafts = new List<AnimationDraft>(specs.Count);

        foreach (var spec in specs)
        {
            var sourceFrames = frames.Frames
                .Where(frame => string.Equals(frame.SourceId, spec.Source, StringComparison.Ordinal))
                .OrderBy(frame => frame.SourceIndex)
                .Select(frame => frame.Uri)
                .ToArray();

            if (sourceFrames.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Animation '{spec.Uri}' references source '{spec.Source}' which produced no frames.");
            }

            drafts.Add(new AnimationDraft(
                spec.Uri,
                sourceFrames,
                spec.DurationMs,
                spec.Loop,
                spec.Playback));
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.AnimationSet, 0, new AnimationDraftSet(drafts))
            });
    }
}
