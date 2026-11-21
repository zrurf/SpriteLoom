using System.IO.Hashing;

namespace SpriteLoom.Model.Manifest;

public readonly record struct Digest(string Algorithm, byte[] Value)
{
    public static Digest XxHash64(ReadOnlySpan<byte> data) =>
        new("xxh3-64", System.IO.Hashing.XxHash64.Hash(data));

    public string ToHex() => Convert.ToHexString(Value).ToLowerInvariant();
}
