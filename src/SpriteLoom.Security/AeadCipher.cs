using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace SpriteLoom.Security;

public static class AeadCipher
{
    public static byte[] Encrypt(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        var cipher = CreateCipher(algorithm);
        var parameters = CreateParameters(algorithm, key, nonce, associatedData);
        cipher.Init(forEncryption: true, parameters);
        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var length = cipher.ProcessBytes(plaintext, output);
        length += cipher.DoFinal(output, length);
        return output[..length];
    }

    public static byte[] Decrypt(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> associatedData = default)
    {
        var cipher = CreateCipher(algorithm);
        var parameters = CreateParameters(algorithm, key, nonce, associatedData);
        cipher.Init(forEncryption: false, parameters);
        var output = new byte[cipher.GetOutputSize(ciphertext.Length)];

        try
        {
            var length = cipher.ProcessBytes(ciphertext, output);
            length += cipher.DoFinal(output, length);
            return output[..length];
        }
        catch (InvalidCipherTextException exception)
        {
            throw new CryptographicException("Authenticated decryption failed.", exception);
        }
    }

    private static IAeadCipher CreateCipher(SymmetricAlgorithmKind algorithm) => algorithm switch
    {
        SymmetricAlgorithmKind.Aes256Gcm => new GcmBlockCipher(new AesEngine()),
        SymmetricAlgorithmKind.XChaCha20Poly1305 => new XChaCha20Poly1305(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown symmetric algorithm.")
    };

    private static AeadParameters CreateParameters(
        SymmetricAlgorithmKind algorithm,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> associatedData)
    {
        if (key.Length != SymmetricKey.KeySize)
        {
            throw new ArgumentException($"AEAD keys must be {SymmetricKey.KeySize} bytes.", nameof(key));
        }

        if (nonce.Length != SymmetricAlgorithms.NonceSize(algorithm))
        {
            throw new ArgumentException(
                $"{SymmetricAlgorithms.GetName(algorithm)} requires a {SymmetricAlgorithms.NonceSize(algorithm)}-byte nonce.",
                nameof(nonce));
        }

        return new AeadParameters(
            new KeyParameter(key.ToArray()),
            SymmetricAlgorithms.TagSize * 8,
            nonce.ToArray(),
            associatedData.IsEmpty ? null : associatedData.ToArray());
    }
}
