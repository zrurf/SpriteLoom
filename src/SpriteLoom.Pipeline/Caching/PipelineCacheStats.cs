namespace SpriteLoom.Pipeline.Caching;

public sealed class PipelineCacheStats
{
    public int Hits { get; set; }

    public int Misses { get; set; }

    public int Total => Hits + Misses;
}
