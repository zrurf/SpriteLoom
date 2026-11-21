using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;

namespace SpriteLoom.Foundation;

public readonly record struct ContentHash(ulong Low, ulong High) : IComparable<ContentHash>
{
    public static ContentHash Compute(ReadOnlySpan<byte> data)
    {
        var hash = XxHash128.Hash(data);
        return new ContentHash(
            BinaryPrimitives.ReadUInt64LittleEndian(hash),
            BinaryPrimitives.ReadUInt64LittleEndian(hash.AsSpan(8)));
    }

    public string ToHex() =>
        Low.ToString("x16", CultureInfo.InvariantCulture) +
        High.ToString("x16", CultureInfo.InvariantCulture);

    public int CompareTo(ContentHash other)
    {
        var comparison = High.CompareTo(other.High);
        return comparison != 0 ? comparison : Low.CompareTo(other.Low);
    }

    public override string ToString() => ToHex();
}
