using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public sealed record SlbCryptoEntry(
    ushort KeyId,
    SymmetricAlgorithmKind Algorithm,
    byte[] Salt,
    byte[] NoncePrefix,
    int ChunkSize);
