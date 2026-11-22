namespace SpriteLoom.Security;

public enum SymmetricAlgorithmKind : byte
{
    Aes256Gcm = 1,
    XChaCha20Poly1305 = 2
}

public static class SymmetricAlgorithms
{
    public const int TagSize = 16;

    public static SymmetricAlgorithmKind Parse(string name) => name switch
    {
        "aes-256-gcm" => SymmetricAlgorithmKind.Aes256Gcm,
        "xchacha20-poly1305" => SymmetricAlgorithmKind.XChaCha20Poly1305,
        _ => throw new ArgumentException($"Unknown symmetric algorithm: {name}", nameof(name))
    };

    public static string GetName(SymmetricAlgorithmKind algorithm) => algorithm switch
    {
        SymmetricAlgorithmKind.Aes256Gcm => "aes-256-gcm",
        SymmetricAlgorithmKind.XChaCha20Poly1305 => "xchacha20-poly1305",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown symmetric algorithm.")
    };

    public static int NoncePrefixSize(SymmetricAlgorithmKind algorithm) => algorithm switch
    {
        SymmetricAlgorithmKind.Aes256Gcm => 4,
        SymmetricAlgorithmKind.XChaCha20Poly1305 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown symmetric algorithm.")
    };

    public static int NonceSize(SymmetricAlgorithmKind algorithm) => algorithm switch
    {
        SymmetricAlgorithmKind.Aes256Gcm => 12,
        SymmetricAlgorithmKind.XChaCha20Poly1305 => 24,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown symmetric algorithm.")
    };
}
