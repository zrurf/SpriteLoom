using System.Formats.Cbor;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public sealed record SlbIndexDocument(
    IReadOnlyList<SlbIndexEntry> Files,
    IReadOnlyList<SlbCryptoEntry> Crypto);

public static class SlbIndex
{
    public static byte[] Write(IReadOnlyList<SlbIndexEntry> entries) => Write(entries, []);

    public static byte[] Write(
        IReadOnlyList<SlbIndexEntry> entries,
        IReadOnlyList<SlbCryptoEntry> crypto)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        CborMaps.WriteMap(
            writer,
            ("format", w => w.WriteTextString("sprite-loom/index")),
            ("format-major", w => w.WriteUInt32(SlbFormat.FormatMajor)),
            ("format-minor", w => w.WriteUInt32(SlbFormat.FormatMinor)),
            ("files", w => WriteFiles(w, entries)),
            ("parts", WriteEmptyArray),
            ("crypto", w => WriteCrypto(w, crypto)),
            ("features", WriteEmptyArray));

        return writer.Encode();
    }

    public static IReadOnlyList<SlbIndexEntry> Read(ReadOnlySpan<byte> bytes) =>
        ReadDocument(bytes).Files;

    public static SlbIndexDocument ReadDocument(ReadOnlySpan<byte> bytes)
    {
        var reader = new CborReader(bytes.ToArray(), CborConformanceMode.Lax, false);
        var mapLength = reader.ReadStartMap() ?? 0;
        var files = new List<SlbIndexEntry>();
        var crypto = new List<SlbCryptoEntry>();

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "files":
                    ReadFiles(reader, files);
                    break;
                case "crypto":
                    ReadCrypto(reader, crypto);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();
        return new SlbIndexDocument(files, crypto);
    }

    private static void ReadFiles(CborReader reader, List<SlbIndexEntry> entries)
    {
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

    private static void ReadCrypto(CborReader reader, List<SlbCryptoEntry> entries)
    {
        var count = reader.ReadStartArray() ?? 0;

        for (var cryptoIndex = 0; cryptoIndex < count; cryptoIndex++)
        {
            var mapLength = reader.ReadStartMap() ?? 0;
            ushort keyId = 0;
            SymmetricAlgorithmKind algorithm = SymmetricAlgorithmKind.Aes256Gcm;
            var hasAlgorithm = false;
            byte[] salt = [];
            byte[] noncePrefix = [];
            var chunkSize = AeadChunker.DefaultChunkSize;
            var hasSalt = false;
            var hasNonce = false;

            for (var field = 0; field < mapLength; field++)
            {
                var fieldName = reader.ReadTextString();

                switch (fieldName)
                {
                    case "key-id":
                        keyId = checked((ushort)reader.ReadUInt32());
                        break;
                    case "alg":
                        try
                        {
                            algorithm = SymmetricAlgorithms.Parse(reader.ReadTextString());
                            hasAlgorithm = true;
                        }
                        catch (ArgumentException exception)
                        {
                            throw BundleDiagnostics.Error(
                                BundleDiagnostics.UnsupportedEncryption,
                                exception.Message);
                        }

                        break;
                    case "kdf":
                        var kdf = reader.ReadTextString();

                        if (kdf is not ("hkdf-sha256" or "argon2id"))
                        {
                            throw BundleDiagnostics.Error(
                                BundleDiagnostics.UnsupportedEncryption,
                                $"Unsupported SLB key derivation: {kdf}");
                        }

                        break;
                    case "salt":
                        salt = reader.ReadByteString();
                        hasSalt = true;
                        break;
                    case "nonce-prefix":
                        noncePrefix = reader.ReadByteString();
                        hasNonce = true;
                        break;
                    case "chunk-size":
                        chunkSize = checked((int)reader.ReadUInt32());
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();

            if (!hasAlgorithm || !hasSalt || !hasNonce)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.InvalidCryptoEntry,
                    "SLB crypto entry is missing algorithm, salt, or nonce prefix.");
            }

            if (noncePrefix.Length != SymmetricAlgorithms.NoncePrefixSize(algorithm))
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.InvalidCryptoEntry,
                    "SLB crypto entry has an invalid nonce prefix length.");
            }

            try
            {
                AeadChunker.ValidateChunkSize(chunkSize);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.InvalidCryptoEntry,
                    exception.Message);
            }

            entries.Add(new SlbCryptoEntry(keyId, algorithm, salt, noncePrefix, chunkSize));
        }

        reader.ReadEndArray();
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

    private static void WriteCrypto(CborWriter writer, IReadOnlyList<SlbCryptoEntry> entries)
    {
        writer.WriteStartArray(entries.Count);

        foreach (var entry in entries)
        {
            CborMaps.WriteMap(
                writer,
                ("key-id", w => w.WriteUInt32(entry.KeyId)),
                ("alg", w => w.WriteTextString(SymmetricAlgorithms.GetName(entry.Algorithm))),
                ("kdf", w => w.WriteTextString("hkdf-sha256")),
                ("salt", w => w.WriteByteString(entry.Salt)),
                ("nonce-prefix", w => w.WriteByteString(entry.NoncePrefix)),
                ("chunk-size", w => w.WriteUInt32((uint)entry.ChunkSize)));
        }

        writer.WriteEndArray();
    }

    private static void WriteEmptyArray(CborWriter writer)
    {
        writer.WriteStartArray(0);
        writer.WriteEndArray();
    }
}
