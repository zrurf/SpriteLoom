using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SpriteLoom.Security;

public static class AeadChunker
{
    public const int DefaultChunkSize = 64 * 1024;

    public static byte[] Encrypt(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> noncePrefix,
        ReadOnlySpan<byte> plaintext,
        int chunkSize,
        out uint chunkCount)
    {
        ValidateChunkSize(chunkSize);
        ValidateNoncePrefix(algorithm, noncePrefix);
        chunkCount = (uint)Math.Max(1, (plaintext.Length + chunkSize - 1) / chunkSize);
        var output = new byte[plaintext.Length + (int)chunkCount * SymmetricAlgorithms.TagSize];
        var inputOffset = 0;
        var outputOffset = 0;

        for (var index = 0; index < chunkCount; index++)
        {
            var length = Math.Min(chunkSize, plaintext.Length - inputOffset);
            var ciphertext = AeadCipher.Encrypt(
                algorithm,
                key,
                BuildNonce(algorithm, noncePrefix, (ulong)index),
                plaintext.Slice(inputOffset, length));
            ciphertext.CopyTo(output, outputOffset);
            inputOffset += length;
            outputOffset += ciphertext.Length;
        }

        return output;
    }

    public static byte[] Decrypt(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> noncePrefix,
        ReadOnlySpan<byte> ciphertext,
        int chunkSize,
        uint chunkCount,
        int plaintextLength)
    {
        ValidateChunkSize(chunkSize);
        ValidateNoncePrefix(algorithm, noncePrefix);

        if (chunkCount == 0 || plaintextLength < 0)
        {
            throw new InvalidDataException("Encrypted payload has invalid chunk metadata.");
        }

        var expectedCiphertext = (long)plaintextLength + (long)chunkCount * SymmetricAlgorithms.TagSize;

        if (ciphertext.Length < expectedCiphertext)
        {
            throw new InvalidDataException("Encrypted payload is truncated.");
        }

        var output = new byte[plaintextLength];
        var inputOffset = 0;
        var outputOffset = 0;
        var plaintextRemaining = plaintextLength;

        for (var index = 0; index < chunkCount; index++)
        {
            var currentPlaintext = Math.Min(chunkSize, plaintextRemaining);
            var currentCiphertext = currentPlaintext + SymmetricAlgorithms.TagSize;
            var plaintext = AeadCipher.Decrypt(
                algorithm,
                key,
                BuildNonce(algorithm, noncePrefix, (ulong)index),
                ciphertext.Slice(inputOffset, currentCiphertext));
            plaintext.CopyTo(output, outputOffset);
            inputOffset += currentCiphertext;
            outputOffset += currentPlaintext;
            plaintextRemaining -= currentPlaintext;
        }

        return output;
    }

    public static uint CountChunks(int plaintextLength, int chunkSize)
    {
        ValidateChunkSize(chunkSize);
        return (uint)Math.Max(1, (plaintextLength + chunkSize - 1) / chunkSize);
    }

    public static byte[] BuildNonce(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> noncePrefix,
        ulong chunkIndex)
    {
        ValidateNoncePrefix(algorithm, noncePrefix);
        var nonce = new byte[SymmetricAlgorithms.NonceSize(algorithm)];
        noncePrefix.CopyTo(nonce.AsSpan());
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(noncePrefix.Length), chunkIndex);
        return nonce;
    }

    public static void ValidateChunkSize(int chunkSize)
    {
        if (chunkSize is < 1 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be between 1 and 1048576 bytes.");
        }
    }

    private static void ValidateNoncePrefix(SymmetricAlgorithmKind algorithm, ReadOnlySpan<byte> prefix)
    {
        var expected = SymmetricAlgorithms.NoncePrefixSize(algorithm);

        if (prefix.Length != expected)
        {
            throw new ArgumentException(
                $"{SymmetricAlgorithms.GetName(algorithm)} requires a {expected}-byte nonce prefix.",
                nameof(prefix));
        }
    }
}
