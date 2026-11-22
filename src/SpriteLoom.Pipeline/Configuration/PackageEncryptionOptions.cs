namespace SpriteLoom.Pipeline.Configuration;

public sealed record PackageEncryptionOptions(
    string Algorithm,
    string KeyPath,
    int ChunkSize);
