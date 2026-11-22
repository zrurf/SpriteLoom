using System.Security.Cryptography;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public static class SlbReader
{
    public static SlbArchive Read(
        ReadOnlySpan<byte> file,
        bool verifyDigests = true,
        SymmetricKey? decryptionKey = null)
    {
        if (file.Length < SlbFormat.HeaderSize)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FileTruncated, "SLB file is truncated.");
        }

        var header = SlbHeader.Read(file[..SlbFormat.HeaderSize]);

        if (header.FormatMajor != SlbFormat.FormatMajor)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.UnsupportedVersion,
                $"Unsupported SLB major version: {header.FormatMajor}");
        }

        if (header.HeaderSize != SlbFormat.HeaderSize)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.InvalidHeaderSize,
                $"Unexpected SLB header size: {header.HeaderSize}");
        }

        if (header.HeaderCrc32 != SlbChecksums.ComputeHeaderCrc(file[..SlbFormat.HeaderSize]))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.HeaderCrcMismatch, "SLB header CRC check failed.");
        }

        if (header.FileSize != (ulong)file.Length)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FileSizeMismatch, "SLB file size does not match the header.");
        }

        if (header.Flags.HasFlag(SlbHeaderFlags.HasFooter))
        {
            var footer = SlbFooter.Read(file[^SlbFormat.FooterSize..]);

            if (footer.IndexOffset != header.IndexOffset || footer.IndexSize != header.IndexSize)
            {
                throw BundleDiagnostics.Error(BundleDiagnostics.FooterMismatch, "SLB footer does not match the header.");
            }
        }

        if (header.BlockTableOffset != SlbFormat.HeaderSize ||
            header.BlockCount > (uint)((file.Length - SlbFormat.HeaderSize) / SlbFormat.BlockEntrySize))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.BlockTableOutsideFile, "SLB block table is outside the file.");
        }

        var blockCount = checked((int)header.BlockCount);
        var tableBytes = file.Slice(
            SlbFormat.HeaderSize,
            blockCount * SlbFormat.BlockEntrySize).ToArray();
        var entries = new SlbBlockEntry[blockCount];

        for (var index = 0; index < blockCount; index++)
        {
            var entry = SlbBlockEntry.Read(tableBytes.AsSpan(index * SlbFormat.BlockEntrySize, SlbFormat.BlockEntrySize));

            if (entry.BlockId != (uint)index)
            {
                throw BundleDiagnostics.Error(BundleDiagnostics.BlockIdsNotContiguous, "SLB block ids are not contiguous.");
            }

            entries[index] = entry;
        }

        if (header.IndexOffset + header.IndexSize > (ulong)file.Length)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.IndexOutsideFile, "SLB index is outside the file.");
        }

        var indexBytes = file.Slice((int)header.IndexOffset, (int)header.IndexSize).ToArray();

        if (header.IndexCrc32 != SlbChecksums.ComputeCrc32(indexBytes))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.IndexCrcMismatch, "SLB index CRC check failed.");
        }

        var indexDocument = SlbIndex.ReadDocument(indexBytes);
        var indexEntries = indexDocument.Files;
        var blocks = entries.ToDictionary(entry => entry.BlockId);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var digestRegionOffset = SlbChecksums.Align8(SlbFormat.HeaderSize + blockCount * SlbFormat.BlockEntrySize);

        foreach (var indexEntry in indexEntries)
        {
            if (!blocks.TryGetValue(indexEntry.Block, out var entry))
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.MissingBlock,
                    $"SLB index references missing block {indexEntry.Block}.");
            }

            if (entry.Offset + entry.StoredSize > (ulong)file.Length)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.BlockOutsideFile,
                    $"SLB block {entry.BlockId} is outside the file.");
            }

            var stored = file.Slice((int)entry.Offset, (int)entry.StoredSize).ToArray();
            var content = entry.Flags.HasFlag(SlbBlockFlags.Encrypted)
                ? DecryptBlock(stored, entry, indexDocument.Crypto, decryptionKey)
                : stored;

            if (verifyDigests)
            {
                SlbChecksums.VerifyDigests(file, digestRegionOffset, entry, content);
            }

            files[indexEntry.Path] = content;
        }

        return new SlbArchive
        {
            Header = header,
            Blocks = entries,
            Files = files,
            IndexEntries = indexEntries,
            Crypto = indexDocument.Crypto,
            IndexBytes = indexBytes,
            BlockTableBytes = tableBytes,
            RawBytes = file.ToArray()
        };
    }

    public static SlbArchive ReadFile(
        string path,
        bool verifyDigests = true,
        SymmetricKey? decryptionKey = null) =>
        Read(File.ReadAllBytes(path), verifyDigests, decryptionKey);

    private static byte[] DecryptBlock(
        byte[] stored,
        SlbBlockEntry entry,
        IReadOnlyList<SlbCryptoEntry> cryptoEntries,
        SymmetricKey? key)
    {
        if (key is null)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.KeyMissing,
                $"SLB block {entry.BlockId} is encrypted; a symmetric key is required.");
        }

        var crypto = cryptoEntries.FirstOrDefault(candidate => candidate.KeyId == entry.KeyId)
            ?? throw BundleDiagnostics.Error(
                BundleDiagnostics.InvalidCryptoEntry,
                $"No crypto metadata for SLB block {entry.BlockId}.");

        if (crypto.KeyId != key.KeyId)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.CryptoKeyMismatch,
                $"SLB block {entry.BlockId} uses key {crypto.KeyId:X4}, not {key.KeyId:X4}.");
        }

        if (entry.ChunkCount == 0 || entry.RawSize > int.MaxValue)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.EncryptionFailed,
                $"SLB block {entry.BlockId} has invalid encrypted size metadata.");
        }

        var blockKey = key.DeriveBlockKey(crypto.KeyId, entry.BlockId, crypto.Salt);

        try
        {
            return AeadChunker.Decrypt(
                crypto.Algorithm,
                blockKey,
                crypto.NoncePrefix,
                stored,
                crypto.ChunkSize,
                entry.ChunkCount,
                (int)entry.RawSize);
        }
        catch (Exception exception) when (
            exception is CryptographicException or InvalidDataException or ArgumentException)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.EncryptionFailed,
                $"SLB block {entry.BlockId} failed authenticated decryption: {exception.Message}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blockKey);
        }
    }
}
