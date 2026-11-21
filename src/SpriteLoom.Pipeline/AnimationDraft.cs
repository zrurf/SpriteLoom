using SpriteLoom.Model.Iri;

namespace SpriteLoom.Pipeline;

public sealed record AnimationSpec(
    string Uri,
    string Source,
    bool Loop,
    string Playback,
    int DurationMs);

public sealed record AnimationDraft(
    string Uri,
    IReadOnlyList<SloomIri> FrameUris,
    int DurationMs,
    bool Loop,
    string Playback);

public sealed record AnimationDraftSet(IReadOnlyList<AnimationDraft> Animations);
