namespace SpriteLoom.Bundle;

[Flags]
public enum SlbBlockFlags : ushort
{
    None = 0,
    Critical = 1,
    Encrypted = 2,
    External = 4,
    Padding = 8,
    SignedLeaf = 16
}
