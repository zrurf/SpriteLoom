using System.Security.Cryptography;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

internal static class SlbDecryptor
{
    public static byte[] DecryptBlock(
        byte[] stored,
        SlbBlockEntry entry,
        IReadOnlyList<SlbCryptoEntry> cryptoEntries,
        SymmetricKey? key)
    {
        if (key is null)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.KeyMissing,
                $"SLB block {entry.BlockId} is encrypted; a symmetric key is required.");
        }

        var crypto = cryptoEntries.FirstOrDefault(candidate => candidate.KeyId == entry.KeyId)
            ?? throw BundleDiagnostics.Error(
                BundleDiagnostics.InvalidCryptoEntry,
                $"No crypto metadata for SLB block {entry.BlockId}.");

        if (crypto.KeyId != key.KeyId)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.CryptoKeyMismatch,
                $"SLB block {entry.BlockId} uses key {crypto.KeyId:X4}, not {key.KeyId:X4}.");
        }

        if (entry.ChunkCount == 0 || entry.RawSize > int.MaxValue)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.EncryptionFailed,
                $"SLB block {entry.BlockId} has invalid encrypted size metadata.");
        }

        var blockKey = key.DeriveBlockKey(crypto.KeyId, entry.BlockId, crypto.Salt);

        try
        {
            return AeadChunker.Decrypt(
                crypto.Algorithm,
                blockKey,
                crypto.NoncePrefix,
                stored,
                crypto.ChunkSize,
                entry.ChunkCount,
                (int)entry.RawSize);
        }
        catch (Exception exception) when (
            exception is CryptographicException or InvalidDataException or ArgumentException)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.EncryptionFailed,
                $"SLB block {entry.BlockId} failed authenticated decryption: {exception.Message}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blockKey);
        }
    }
}
