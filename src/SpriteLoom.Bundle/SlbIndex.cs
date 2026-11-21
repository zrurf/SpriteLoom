using System.Formats.Cbor;

namespace SpriteLoom.Bundle;

public static class SlbIndex
{
    public static byte[] Write(IReadOnlyList<SlbIndexEntry> entries)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        CborMaps.WriteMap(
            writer,
            ("format", w => w.WriteTextString("sprite-loom/index")),
            ("format-major", w => w.WriteUInt32(SlbFormat.FormatMajor)),
            ("format-minor", w => w.WriteUInt32(SlbFormat.FormatMinor)),
            ("files", w => WriteFiles(w, entries)),
            ("parts", WriteEmptyArray),
            ("features", WriteEmptyArray));

        return writer.Encode();
    }

    public static IReadOnlyList<SlbIndexEntry> Read(ReadOnlySpan<byte> bytes)
    {
        var reader = new CborReader(bytes.ToArray(), CborConformanceMode.Lax, false);
        var mapLength = reader.ReadStartMap() ?? 0;
        var entries = new List<SlbIndexEntry>();

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();

            if (key != "files")
            {
                reader.SkipValue();
                continue;
            }

            var count = reader.ReadStartArray() ?? 0;

            for (var fileIndex = 0; fileIndex < count; fileIndex++)
            {
                var fileMapLength = reader.ReadStartMap() ?? 0;
                string? path = null;
                uint block = 0;
                string? kind = null;

                for (var field = 0; field < fileMapLength; field++)
                {
                    var fieldName = reader.ReadTextString();

                    switch (fieldName)
                    {
                        case "path":
                            path = reader.ReadTextString();
                            break;
                        case "block":
                            block = reader.ReadUInt32();
                            break;
                        case "kind":
                            kind = reader.ReadTextString();
                            break;
                        default:
                            reader.SkipValue();
                            break;
                    }
                }

                reader.ReadEndMap();

                if (path is null || kind is null)
                {
                    throw BundleDiagnostics.Error(
                        BundleDiagnostics.InvalidIndexEntry,
                        "SLB index entry is missing a path or kind.");
                }

                entries.Add(new SlbIndexEntry(path, block, SlbKinds.Parse(kind)));
            }

            reader.ReadEndArray();
        }

        reader.ReadEndMap();
        return entries;
    }

    private static void WriteFiles(CborWriter writer, IReadOnlyList<SlbIndexEntry> entries)
    {
        writer.WriteStartArray(entries.Count);

        foreach (var entry in entries)
        {
            CborMaps.WriteMap(
                writer,
                ("path", w => w.WriteTextString(entry.Path)),
                ("block", w => w.WriteUInt32(entry.Block)),
                ("kind", w => w.WriteTextString(SlbKinds.GetName(entry.Kind))));
        }

        writer.WriteEndArray();
    }

    private static void WriteEmptyArray(CborWriter writer)
    {
        writer.WriteStartArray(0);
        writer.WriteEndArray();
    }
}
