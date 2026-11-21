using SpriteLoom.Pipeline.Caching;

namespace SpriteLoom.Pipeline;

public sealed class PipelineRunOptions
{
    public ArtifactCache? Cache { get; init; }

    public bool Refresh { get; init; }

    public PipelineCacheStats? Stats { get; init; }
}
