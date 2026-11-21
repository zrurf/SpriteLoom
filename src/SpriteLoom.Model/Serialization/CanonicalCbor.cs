using System.Formats.Cbor;

namespace SpriteLoom.Model.Serialization;

internal static class CanonicalCbor
{
    private static readonly ByteArrayComparer Comparer = new();

    public static void WriteMap(CborWriter writer, params (string Key, Action<CborWriter> Write)[] entries)
    {
        var ordered = entries
            .Select(entry => (entry.Key, Bytes: EncodeKey(entry.Key), entry.Write))
            .OrderBy(entry => entry.Bytes, Comparer)
            .ToArray();

        writer.WriteStartMap(ordered.Length);

        foreach (var entry in ordered)
        {
            writer.WriteTextString(entry.Key);
            entry.Write(writer);
        }

        writer.WriteEndMap();
    }

    private static byte[] EncodeKey(string key)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteTextString(key);
        return writer.Encode();
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public int Compare(byte[]? x, byte[]? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return x.AsSpan().SequenceCompareTo(y);
        }
    }
}
