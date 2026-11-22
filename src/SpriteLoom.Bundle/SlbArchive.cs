namespace SpriteLoom.Bundle;

public sealed class SlbArchive
{
    public required SlbHeader Header { get; init; }

    public required IReadOnlyList<SlbBlockEntry> Blocks { get; init; }

    public required IReadOnlyDictionary<string, byte[]> Files { get; init; }

    public required IReadOnlyList<SlbIndexEntry> IndexEntries { get; init; }

    public required IReadOnlyList<SlbCryptoEntry> Crypto { get; init; }

    public required byte[] IndexBytes { get; init; }

    public required byte[] BlockTableBytes { get; init; }

    public required byte[] RawBytes { get; init; }
}
