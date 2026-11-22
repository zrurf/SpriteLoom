using System.Security.Cryptography;

namespace SpriteLoom.Security;

internal static class HkdfSha256
{
    public static byte[] Derive(
        ReadOnlySpan<byte> inputKey,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info,
        int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Span<byte> defaultSalt = stackalloc byte[32];
        var effectiveSalt = salt.IsEmpty ? defaultSalt : salt;
        var prk = HMACSHA256.HashData(effectiveSalt, inputKey);
        var output = new byte[length];
        var previous = Array.Empty<byte>();
        var offset = 0;

        for (var counter = 1; offset < length; counter++)
        {
            var block = new byte[previous.Length + info.Length + 1];
            previous.CopyTo(block, 0);
            info.CopyTo(block.AsSpan(previous.Length));
            block[^1] = (byte)counter;
            previous = HMACSHA256.HashData(prk, block);
            var copy = Math.Min(previous.Length, length - offset);
            previous.AsSpan(0, copy).CopyTo(output.AsSpan(offset));
            offset += copy;
        }

        CryptographicOperations.ZeroMemory(prk);
        return output;
    }
}
