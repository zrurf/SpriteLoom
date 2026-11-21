using System.Formats.Cbor;

namespace SpriteLoom.Security;

public readonly record struct DigestValue(string Algorithm, byte[] Value);

public sealed record SignedFileEntry(string Path, long Size, DigestValue Digest);

public sealed record SignerEntry(string KeyId, string Algorithm, byte[] PublicKey, byte[] Signature);

public sealed class SignatureDocument
{
    public string Layer { get; init; } = "file";

    public byte[]? MerkleRoot { get; init; }

    public DigestValue? BlockTableDigest { get; init; }

    public DigestValue? IndexDigest { get; init; }

    public IReadOnlyList<SignedFileEntry> Files { get; init; } = [];

    public IReadOnlyList<SignerEntry> Signers { get; init; } = [];

    public byte[] Encode(bool includeSignatures)
    {
        var entries = new List<(string Key, Action<CborWriter> Write)>
        {
            ("format", writer => writer.WriteTextString("sprite-loom/signature")),
            ("format-major", writer => writer.WriteUInt32(1)),
            ("layer", writer => writer.WriteTextString(Layer))
        };

        if (MerkleRoot is not null)
        {
            entries.Add(("merkle-root", writer => writer.WriteByteString(MerkleRoot)));
        }

        if (BlockTableDigest is { } blockTableDigest)
        {
            entries.Add(("block-table-digest", writer => WriteDigest(writer, blockTableDigest)));
        }

        if (IndexDigest is { } indexDigest)
        {
            entries.Add(("index-digest", writer => WriteDigest(writer, indexDigest)));
        }

        if (Files.Count > 0)
        {
            entries.Add(("files", writer => WriteFiles(writer, Files)));
        }

        entries.Add(("signers", writer => WriteSigners(writer, Signers, includeSignatures)));

        var cborWriter = new CborWriter(CborConformanceMode.Canonical);
        SignatureCbor.WriteMap(cborWriter, entries.ToArray());
        return cborWriter.Encode();
    }

    public SignatureDocument WithSignature(byte[] signature) =>
        new()
        {
            Layer = Layer,
            MerkleRoot = MerkleRoot,
            BlockTableDigest = BlockTableDigest,
            IndexDigest = IndexDigest,
            Files = Files,
            Signers = Signers
                .Select(signer => signer with { Signature = signature })
                .ToArray()
        };

    public static SignatureDocument Decode(ReadOnlySpan<byte> data)
    {
        var reader = new CborReader(data.ToArray(), CborConformanceMode.Lax, false);
        var mapLength = reader.ReadStartMap() ?? 0;
        var layer = "file";
        byte[]? merkleRoot = null;
        DigestValue? blockTableDigest = null;
        DigestValue? indexDigest = null;
        IReadOnlyList<SignedFileEntry> files = [];
        IReadOnlyList<SignerEntry> signers = [];

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "layer":
                    layer = reader.ReadTextString();
                    break;
                case "merkle-root":
                    merkleRoot = reader.ReadByteString();
                    break;
                case "block-table-digest":
                    blockTableDigest = ReadDigest(reader);
                    break;
                case "index-digest":
                    indexDigest = ReadDigest(reader);
                    break;
                case "files":
                    files = ReadFiles(reader);
                    break;
                case "signers":
                    signers = ReadSigners(reader);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();

        return new SignatureDocument
        {
            Layer = layer,
            MerkleRoot = merkleRoot,
            BlockTableDigest = blockTableDigest,
            IndexDigest = indexDigest,
            Files = files,
            Signers = signers
        };
    }

    private static void WriteDigest(CborWriter writer, DigestValue digest) =>
        SignatureCbor.WriteMap(
            writer,
            ("alg", w => w.WriteTextString(digest.Algorithm)),
            ("value", w => w.WriteByteString(digest.Value)));

    private static void WriteFiles(CborWriter writer, IReadOnlyList<SignedFileEntry> files)
    {
        writer.WriteStartArray(files.Count);

        foreach (var file in files)
        {
            SignatureCbor.WriteMap(
                writer,
                ("path", w => w.WriteTextString(file.Path)),
                ("size", w => w.WriteUInt64((ulong)file.Size)),
                ("digest", w => WriteDigest(w, file.Digest)));
        }

        writer.WriteEndArray();
    }

    private static void WriteSigners(
        CborWriter writer,
        IReadOnlyList<SignerEntry> signers,
        bool includeSignatures)
    {
        writer.WriteStartArray(signers.Count);

        foreach (var signer in signers)
        {
            var entries = new List<(string Key, Action<CborWriter> Write)>
            {
                ("key-id", w => w.WriteTextString(signer.KeyId)),
                ("alg", w => w.WriteTextString(signer.Algorithm)),
                ("public-key", w => w.WriteByteString(signer.PublicKey))
            };

            if (includeSignatures && signer.Signature.Length > 0)
            {
                entries.Add(("signature", w => w.WriteByteString(signer.Signature)));
            }

            SignatureCbor.WriteMap(writer, entries.ToArray());
        }

        writer.WriteEndArray();
    }

    private static DigestValue ReadDigest(CborReader reader)
    {
        var mapLength = reader.ReadStartMap() ?? 0;
        var algorithm = string.Empty;
        byte[] value = [];

        for (var index = 0; index < mapLength; index++)
        {
            var key = reader.ReadTextString();

            switch (key)
            {
                case "alg":
                    algorithm = reader.ReadTextString();
                    break;
                case "value":
                    value = reader.ReadByteString();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }

        reader.ReadEndMap();
        return new DigestValue(algorithm, value);
    }

    private static IReadOnlyList<SignedFileEntry> ReadFiles(CborReader reader)
    {
        var count = reader.ReadStartArray() ?? 0;
        var files = new List<SignedFileEntry>(count);

        for (var index = 0; index < count; index++)
        {
            var mapLength = reader.ReadStartMap() ?? 0;
            var path = string.Empty;
            long size = 0;
            var digest = new DigestValue(string.Empty, []);

            for (var field = 0; field < mapLength; field++)
            {
                var key = reader.ReadTextString();

                switch (key)
                {
                    case "path":
                        path = reader.ReadTextString();
                        break;
                    case "size":
                        size = (long)reader.ReadUInt64();
                        break;
                    case "digest":
                        digest = ReadDigest(reader);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            files.Add(new SignedFileEntry(path, size, digest));
        }

        reader.ReadEndArray();
        return files;
    }

    private static IReadOnlyList<SignerEntry> ReadSigners(CborReader reader)
    {
        var count = reader.ReadStartArray() ?? 0;
        var signers = new List<SignerEntry>(count);

        for (var index = 0; index < count; index++)
        {
            var mapLength = reader.ReadStartMap() ?? 0;
            var keyId = string.Empty;
            var algorithm = string.Empty;
            byte[] publicKey = [];
            byte[] signature = [];

            for (var field = 0; field < mapLength; field++)
            {
                var key = reader.ReadTextString();

                switch (key)
                {
                    case "key-id":
                        keyId = reader.ReadTextString();
                        break;
                    case "alg":
                        algorithm = reader.ReadTextString();
                        break;
                    case "public-key":
                        publicKey = reader.ReadByteString();
                        break;
                    case "signature":
                        signature = reader.ReadByteString();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();
            signers.Add(new SignerEntry(keyId, algorithm, publicKey, signature));
        }

        reader.ReadEndArray();
        return signers;
    }
}
