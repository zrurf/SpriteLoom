namespace SpriteLoom.Bundle;

public enum SlbBlockKind : byte
{
    Manifest = 1,
    Container = 2,
    Image = 3,
    Animation = 4,
    Blob = 5,
    Mesh = 6,
    Signature = 7,
    Checksum = 8,
    Padding = 9,
    ExternalPart = 10,
    Crypto = 11
}
