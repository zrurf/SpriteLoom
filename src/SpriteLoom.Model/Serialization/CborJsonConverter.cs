using System.Formats.Cbor;
using System.Text.Json;

namespace SpriteLoom.Model.Serialization;

public static class CborJsonConverter
{
    private const int MaxDepth = 64;

    public static byte[] ToJson(ReadOnlySpan<byte> cbor, bool indented = false)
    {
        if (cbor.IsEmpty)
        {
            throw new InvalidDataException("CBOR payload is empty.");
        }

        var reader = new CborReader(cbor.ToArray(), CborConformanceMode.Lax, false);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            WriteValue(reader, writer, 0);
        }

        if (reader.BytesRemaining != 0)
        {
            throw new InvalidDataException("CBOR payload has trailing data.");
        }

        return stream.ToArray();
    }

    private static void WriteValue(CborReader reader, Utf8JsonWriter writer, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException("CBOR payload is nested too deeply.");
        }

        switch (reader.PeekState())
        {
            case CborReaderState.StartMap:
                WriteMap(reader, writer, depth);
                break;
            case CborReaderState.StartArray:
                WriteArray(reader, writer, depth);
                break;
            case CborReaderState.TextString:
                writer.WriteStringValue(reader.ReadTextString());
                break;
            case CborReaderState.ByteString:
                writer.WriteBase64StringValue(reader.ReadByteString());
                break;
            case CborReaderState.UnsignedInteger:
                writer.WriteNumberValue(reader.ReadUInt64());
                break;
            case CborReaderState.NegativeInteger:
                writer.WriteNumberValue(reader.ReadInt64());
                break;
            case CborReaderState.Boolean:
                writer.WriteBooleanValue(reader.ReadBoolean());
                break;
            case CborReaderState.Null:
                reader.ReadNull();
                writer.WriteNullValue();
                break;
            case CborReaderState.HalfPrecisionFloat:
                writer.WriteNumberValue((double)reader.ReadHalf());
                break;
            case CborReaderState.SinglePrecisionFloat:
                writer.WriteNumberValue(reader.ReadSingle());
                break;
            case CborReaderState.DoublePrecisionFloat:
                writer.WriteNumberValue(reader.ReadDouble());
                break;
            case CborReaderState.Tag:
                _ = reader.ReadTag();
                WriteValue(reader, writer, depth);
                break;
            default:
                throw new InvalidDataException($"Unsupported CBOR value: {reader.PeekState()}.");
        }
    }

    private static void WriteMap(CborReader reader, Utf8JsonWriter writer, int depth)
    {
        _ = reader.ReadStartMap();
        writer.WriteStartObject();

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            if (reader.PeekState() != CborReaderState.TextString)
            {
                throw new InvalidDataException("CBOR map keys must be text strings.");
            }

            writer.WritePropertyName(reader.ReadTextString());
            WriteValue(reader, writer, depth + 1);
        }

        reader.ReadEndMap();
        writer.WriteEndObject();
    }

    private static void WriteArray(CborReader reader, Utf8JsonWriter writer, int depth)
    {
        _ = reader.ReadStartArray();
        writer.WriteStartArray();

        while (reader.PeekState() != CborReaderState.EndArray)
        {
            WriteValue(reader, writer, depth + 1);
        }

        reader.ReadEndArray();
        writer.WriteEndArray();
    }
}
