using System.Buffers.Binary;
using System.Text;

namespace SpriteLoom.Security;

public sealed class SymmetricKey
{
    public const int KeySize = 32;

    private readonly byte[] _bytes;

    private SymmetricKey(byte[] bytes)
    {
        if (bytes.Length != KeySize)
        {
            throw new ArgumentException($"Symmetric keys must be {KeySize} bytes.", nameof(bytes));
        }

        _bytes = bytes;
        KeyId = BinaryPrimitives.ReadUInt16LittleEndian(
            HashAlgorithms.Compute(HashAlgorithmKind.XxHash64, bytes));
    }

    public ushort KeyId { get; }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static SymmetricKey Generate()
    {
        var bytes = new byte[KeySize];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return new SymmetricKey(bytes);
    }

    public static SymmetricKey FromBytes(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    public static SymmetricKey Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length == KeySize)
        {
            return new SymmetricKey(bytes);
        }

        var text = Encoding.UTF8.GetString(bytes).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        if (text.Length != KeySize * 2)
        {
            throw new InvalidDataException($"Symmetric key file must contain 32 raw bytes or 64 hex characters: {path}");
        }

        try
        {
            return new SymmetricKey(Convert.FromHexString(text));
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Symmetric key file is not valid hexadecimal: {path}", exception);
        }
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Convert.ToHexString(_bytes).ToLowerInvariant() + "\n", new UTF8Encoding(false));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public byte[] DeriveBlockKey(ushort keyId, uint blockId, ReadOnlySpan<byte> salt)
    {
        var info = new byte[15];
        "slb-block"u8.CopyTo(info);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(9), keyId);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(11), blockId);
        return HkdfSha256.Derive(_bytes, salt, info, KeySize);
    }
}
