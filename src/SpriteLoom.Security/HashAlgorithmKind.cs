namespace SpriteLoom.Security;

[Flags]
public enum HashAlgorithmKind
{
    None = 0,
    Crc32 = 1,
    XxHash64 = 2,
    Blake3 = 4,
    Sha1 = 8,
    Sha256 = 16,
    Sha512 = 32
}
