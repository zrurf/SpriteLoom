namespace SpriteLoom.Bundle;

[Flags]
public enum SlbHeaderFlags : uint
{
    None = 0,
    Signed = 1,
    HasFooter = 2,
    EncryptedIndex = 4,
    Multipart = 8,
    SequentialOrder = 16
}
