using System.IO.Hashing;
using System.Security.Cryptography;
using Blake3;

namespace SpriteLoom.Security;

public static class HashAlgorithms
{
    public static byte[] Compute(HashAlgorithmKind kind, ReadOnlySpan<byte> data) => kind switch
    {
        HashAlgorithmKind.Crc32 => Crc32.Hash(data),
        HashAlgorithmKind.XxHash64 => XxHash64.Hash(data),
        HashAlgorithmKind.Blake3 => ComputeBlake3(data),
        HashAlgorithmKind.Sha1 => SHA1.HashData(data),
        HashAlgorithmKind.Sha256 => SHA256.HashData(data),
        HashAlgorithmKind.Sha512 => SHA512.HashData(data),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported hash algorithm.")
    };

    public static int GetLength(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Crc32 => 4,
        HashAlgorithmKind.XxHash64 => 8,
        HashAlgorithmKind.Blake3 => 32,
        HashAlgorithmKind.Sha1 => 20,
        HashAlgorithmKind.Sha256 => 32,
        HashAlgorithmKind.Sha512 => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported hash algorithm.")
    };

    public static string GetName(HashAlgorithmKind kind) => kind switch
    {
        HashAlgorithmKind.Crc32 => "crc32",
        HashAlgorithmKind.XxHash64 => "xxh3-64",
        HashAlgorithmKind.Blake3 => "blake3",
        HashAlgorithmKind.Sha1 => "sha1",
        HashAlgorithmKind.Sha256 => "sha256",
        HashAlgorithmKind.Sha512 => "sha512",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported hash algorithm.")
    };

    public static IReadOnlyList<HashAlgorithmKind> Expand(HashAlgorithmKind mask)
    {
        var result = new List<HashAlgorithmKind>(6);

        foreach (var kind in new[]
                 {
                     HashAlgorithmKind.Crc32,
                     HashAlgorithmKind.XxHash64,
                     HashAlgorithmKind.Blake3,
                     HashAlgorithmKind.Sha1,
                     HashAlgorithmKind.Sha256,
                     HashAlgorithmKind.Sha512
                 })
        {
            if ((mask & kind) != 0)
            {
                result.Add(kind);
            }
        }

        return result;
    }

    public static int GetTotalLength(HashAlgorithmKind mask) =>
        Expand(mask).Sum(GetLength);

    public static string ToHex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(value).ToLowerInvariant();

    private static byte[] ComputeBlake3(ReadOnlySpan<byte> data)
    {
        var hash = Hasher.Hash(data);
        return hash.AsSpan().ToArray();
    }
}
