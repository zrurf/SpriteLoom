using System.Formats.Cbor;
using System.Text.Json;
using SpriteLoom.Model.Manifest;

namespace SpriteLoom.Model.Serialization;

public static class ManifestReader
{
    public static ManifestSummary ReadSummary(Stream stream, ManifestFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return format switch
        {
            ManifestFormat.Json => ReadJsonSummary(stream),
            ManifestFormat.Cbor => ReadCborSummary(stream),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static ManifestSummary ReadJsonSummary(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        return new ManifestSummary(
            root.GetProperty("format").GetString() ?? string.Empty,
            root.GetProperty("format-major").GetInt32(),
            root.GetProperty("format-minor").GetInt32(),
            root.GetProperty("uris").GetArrayLength(),
            root.GetProperty("pages").GetArrayLength(),
            root.GetProperty("frames").GetProperty("records").GetArrayLength(),
            root.GetProperty("animations").GetArrayLength());
    }

    private static ManifestSummary ReadCborSummary(Stream stream)
    {
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        var reader = new CborReader(memory.ToArray(), CborConformanceMode.Lax, false);
        var format = string.Empty;
        var major = 0;
        var minor = 0;
        var uris = 0;
        var pages = 0;
        var frames = 0;
        var animations = 0;

        var mapLength = reader.ReadStartMap() ?? 0;

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "format":
                    format = reader.ReadTextString();
                    break;
                case "format-major":
                    major = (int)reader.ReadUInt32();
                    break;
                case "format-minor":
                    minor = (int)reader.ReadUInt32();
                    break;
                case "uris":
                    uris = ReadArrayLength(reader);
                    break;
                case "pages":
                    pages = ReadArrayLength(reader);
                    break;
                case "animations":
                    animations = ReadArrayLength(reader);
                    break;
                case "frames":
                    frames = ReadFrameCount(reader);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();

        return new ManifestSummary(format, major, minor, uris, pages, frames, animations);
    }

    private static int ReadFrameCount(CborReader reader)
    {
        var mapLength = reader.ReadStartMap() ?? 0;
        var count = 0;

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();
            if (key == "records")
            {
                count = ReadArrayLength(reader);
            }
            else
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndMap();
        return count;
    }

    private static int ReadArrayLength(CborReader reader)
    {
        var length = reader.ReadStartArray();

        if (length is null)
        {
            var count = 0;
            while (reader.PeekState() != CborReaderState.EndArray)
            {
                reader.SkipValue();
                count++;
            }

            reader.ReadEndArray();
            return count;
        }

        for (var index = 0; index < length.Value; index++)
        {
            reader.SkipValue();
        }

        reader.ReadEndArray();
        return length.Value;
    }
}
