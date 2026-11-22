using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SpriteLoom.Security;

public static class SencFile
{
    public const int HeaderSize = 36;
    public const int NoncePrefixStorage = 16;
    public const byte Version = 1;
    public const string Extension = ".senc";

    private static ReadOnlySpan<byte> Magic => [0x53, 0x45, 0x4E, 0x43];

    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext,
        SymmetricKey key,
        SymmetricAlgorithmKind algorithm,
        int chunkSize = AeadChunker.DefaultChunkSize,
        ReadOnlySpan<byte> noncePrefix = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        AeadChunker.ValidateChunkSize(chunkSize);

        var prefixStorage = new byte[NoncePrefixStorage];

        if (noncePrefix.IsEmpty)
        {
            RandomNumberGenerator.Fill(prefixStorage);
        }
        else if (noncePrefix.Length == NoncePrefixStorage)
        {
            noncePrefix.CopyTo(prefixStorage);
        }
        else
        {
            throw new ArgumentException(
                $"The SENC nonce prefix must be {NoncePrefixStorage} bytes.",
                nameof(noncePrefix));
        }

        var prefixLength = SymmetricAlgorithms.NoncePrefixSize(algorithm);
        var payload = AeadChunker.Encrypt(
            algorithm,
            key.Bytes.Span,
            prefixStorage.AsSpan(0, prefixLength),
            plaintext,
            chunkSize,
            out var chunkCount);
        var output = new byte[HeaderSize + payload.Length];
        var header = output.AsSpan(0, HeaderSize);
        Magic.CopyTo(header);
        header[4] = Version;
        header[5] = (byte)algorithm;
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], key.KeyId);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], chunkSize);
        prefixStorage.CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], chunkCount);
        payload.CopyTo(output.AsSpan(HeaderSize));
        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> file, SymmetricKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (file.Length < HeaderSize || !file[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("SENC file header is missing or truncated.");
        }

        if (file[4] != Version)
        {
            throw new InvalidDataException($"Unsupported SENC version: {file[4]}");
        }

        var algorithm = (SymmetricAlgorithmKind)file[5];

        if (algorithm is not (SymmetricAlgorithmKind.Aes256Gcm or SymmetricAlgorithmKind.XChaCha20Poly1305))
        {
            throw new InvalidDataException($"Unsupported SENC algorithm: {file[5]}");
        }

        var keyId = BinaryPrimitives.ReadUInt16LittleEndian(file[6..]);

        if (keyId != key.KeyId)
        {
            throw new InvalidDataException(
                $"SENC key id {keyId:X4} does not match the supplied key {key.KeyId:X4}.");
        }

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(file[8..]);
        AeadChunker.ValidateChunkSize(chunkSize);
        var chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(file[28..]);

        if (chunkCount is 0 or > int.MaxValue)
        {
            throw new InvalidDataException("SENC file contains an invalid chunk count.");
        }

        var prefixLength = SymmetricAlgorithms.NoncePrefixSize(algorithm);
        var prefix = file.Slice(12, prefixLength);
        var body = file[HeaderSize..];
        var fullChunks = (int)chunkCount - 1;
        var fullBytes = (long)fullChunks * (chunkSize + SymmetricAlgorithms.TagSize);

        if (body.Length < fullBytes + SymmetricAlgorithms.TagSize)
        {
            throw new InvalidDataException("SENC payload is truncated.");
        }

        var lastPlaintext = body.Length - fullBytes - SymmetricAlgorithms.TagSize;

        if (lastPlaintext < 0 || lastPlaintext > chunkSize)
        {
            throw new InvalidDataException("SENC payload has an invalid final chunk.");
        }

        var plaintextLength = checked((int)((long)fullChunks * chunkSize + lastPlaintext));
        return AeadChunker.Decrypt(
            algorithm,
            key.Bytes.Span,
            prefix,
            body,
            chunkSize,
            chunkCount,
            plaintextLength);
    }

    public static bool HasMagic(ReadOnlySpan<byte> file) =>
        file.Length >= 4 && file[..4].SequenceEqual(Magic);
}
