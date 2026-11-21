namespace SpriteLoom.Bundle;

public sealed record SlbFile(string Path, SlbBlockKind Kind, byte[] Content);
