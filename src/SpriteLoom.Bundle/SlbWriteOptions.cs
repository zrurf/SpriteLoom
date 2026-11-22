using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public sealed class SlbWriteOptions
{
    public HashAlgorithmKind Checksums { get; init; } = HashAlgorithmKind.XxHash64;

    public ISigner? Signer { get; init; }

    public bool WriteFooter { get; init; }

    public SymmetricKey? EncryptionKey { get; init; }

    public SymmetricAlgorithmKind EncryptionAlgorithm { get; init; } = SymmetricAlgorithmKind.Aes256Gcm;

    public int EncryptionChunkSize { get; init; } = AeadChunker.DefaultChunkSize;
}
