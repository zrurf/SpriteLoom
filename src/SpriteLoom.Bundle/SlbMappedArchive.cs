using System.Buffers;
using System.IO.MemoryMappedFiles;
using SpriteLoom.Security;

namespace SpriteLoom.Bundle;

public sealed class SlbMappedArchive : IDisposable
{
    private readonly MemoryMappedFile _mapped;
    private readonly MemoryMappedViewAccessor _view;
    private readonly long _length;
    private readonly IReadOnlyList<SlbBlockEntry> _blocks;
    private readonly IReadOnlyList<SlbIndexEntry> _indexEntries;
    private readonly IReadOnlyList<SlbCryptoEntry> _crypto;
    private readonly Dictionary<string, SlbIndexEntry> _paths;
    private readonly byte[]? _digestRegion;
    private readonly IReadOnlyDictionary<uint, int> _digestOffsets;
    private readonly SymmetricKey? _key;
    private bool _disposed;

    private SlbMappedArchive(
        MemoryMappedFile mapped,
        MemoryMappedViewAccessor view,
        long length,
        SlbHeader header,
        IReadOnlyList<SlbBlockEntry> blocks,
        IReadOnlyList<SlbIndexEntry> indexEntries,
        IReadOnlyList<SlbCryptoEntry> crypto,
        byte[]? digestRegion,
        IReadOnlyDictionary<uint, int> digestOffsets,
        SymmetricKey? key)
    {
        _mapped = mapped;
        _view = view;
        _length = length;
        Header = header;
        _blocks = blocks;
        _indexEntries = indexEntries;
        _crypto = crypto;
        _digestRegion = digestRegion;
        _digestOffsets = digestOffsets;
        _key = key;
        _paths = new Dictionary<string, SlbIndexEntry>(indexEntries.Count, StringComparer.Ordinal);

        foreach (var entry in indexEntries)
        {
            if (!_paths.TryAdd(entry.Path, entry))
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.InvalidIndexEntry,
                    $"Duplicate SLB index path: {entry.Path}");
            }
        }
    }

    public SlbHeader Header { get; }

    public IReadOnlyList<SlbBlockEntry> Blocks => _blocks;

    public IReadOnlyList<SlbIndexEntry> IndexEntries => _indexEntries;

    public IReadOnlyList<SlbCryptoEntry> Crypto => _crypto;

    public IReadOnlyList<string> Paths => _indexEntries.Select(entry => entry.Path).ToArray();

    public long FileSize => _length;

    public bool Contains(string path) => _paths.ContainsKey(path);

    public long GetFileLength(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return checked((long)GetBlock(path).RawSize);
    }

    public byte[] ReadFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var block = GetBlock(path);
        var stored = new byte[checked((int)block.StoredSize)];
        _view.ReadArray((long)block.Offset, stored, 0, stored.Length);
        var content = block.Flags.HasFlag(SlbBlockFlags.Encrypted)
            ? SlbDecryptor.DecryptBlock(stored, block, _crypto, _key)
            : stored;

        if (_digestRegion is not null)
        {
            var digestOffset = _digestOffsets[block.BlockId];
            var digestLength = SlbChecksums.DigestBytes(block.ChecksumAlgorithms);
            SlbChecksums.VerifyDigests(
                _digestRegion.AsSpan(digestOffset, digestLength),
                block,
                content);
        }

        return content;
    }

    public Stream OpenFile(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var block = GetBlock(path);

        if (block.Flags.HasFlag(SlbBlockFlags.Encrypted) || _digestRegion is not null)
        {
            return new MemoryStream(ReadFile(path), writable: false);
        }

        return new SlbMappedStream(_view, (long)block.Offset, checked((int)block.StoredSize));
    }

    public static SlbMappedArchive Open(
        string path,
        SymmetricKey? key = null,
        bool verifyDigests = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var metadata = ReadMetadata(fullPath, verifyDigests);
        MemoryMappedFile? mapped = null;
        MemoryMappedViewAccessor? view = null;

        try
        {
            mapped = MemoryMappedFile.CreateFromFile(
                fullPath,
                FileMode.Open,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read);
            view = mapped.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return new SlbMappedArchive(
                mapped,
                view,
                metadata.Length,
                metadata.Header,
                metadata.Blocks,
                metadata.IndexEntries,
                metadata.Crypto,
                metadata.DigestRegion,
                metadata.DigestOffsets,
                key);
        }
        catch
        {
            view?.Dispose();
            mapped?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _view.Dispose();
        _mapped.Dispose();
    }

    private SlbBlockEntry GetBlock(string path)
    {
        if (!_paths.TryGetValue(path, out var entry))
        {
            throw new KeyNotFoundException($"SLB path not found: {path}");
        }

        if (entry.Block >= _blocks.Count)
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.MissingBlock,
                $"SLB index references missing block {entry.Block}.");
        }

        return _blocks[(int)entry.Block];
    }

    private static MappedMetadata ReadMetadata(string path, bool verifyDigests)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = stream.Length;

        if (length < SlbFormat.HeaderSize)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FileTruncated, "SLB file is truncated.");
        }

        var headerBytes = ReadAt(stream, 0, SlbFormat.HeaderSize);
        var header = SlbHeader.Read(headerBytes);

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

        if (header.HeaderCrc32 != SlbChecksums.ComputeHeaderCrc(headerBytes))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.HeaderCrcMismatch, "SLB header CRC check failed.");
        }

        if (header.FileSize != (ulong)length)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FileSizeMismatch, "SLB file size does not match the header.");
        }

        if (header.Flags.HasFlag(SlbHeaderFlags.HasFooter))
        {
            var footer = SlbFooter.Read(ReadAt(stream, length - SlbFormat.FooterSize, SlbFormat.FooterSize));

            if (footer.IndexOffset != header.IndexOffset || footer.IndexSize != header.IndexSize)
            {
                throw BundleDiagnostics.Error(BundleDiagnostics.FooterMismatch, "SLB footer does not match the header.");
            }
        }

        if (header.Flags.HasFlag(SlbHeaderFlags.EncryptedIndex))
        {
            throw BundleDiagnostics.Error(
                BundleDiagnostics.UnsupportedEncryption,
                "Encrypted SLB indexes are not supported.");
        }

        if (header.BlockTableOffset != SlbFormat.HeaderSize ||
            header.BlockCount > (uint)((length - SlbFormat.HeaderSize) / SlbFormat.BlockEntrySize))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.BlockTableOutsideFile, "SLB block table is outside the file.");
        }

        var blockCount = checked((int)header.BlockCount);
        var tableBytes = ReadAt(stream, SlbFormat.HeaderSize, blockCount * SlbFormat.BlockEntrySize);
        var blocks = new SlbBlockEntry[blockCount];

        for (var index = 0; index < blockCount; index++)
        {
            var block = SlbBlockEntry.Read(
                tableBytes.AsSpan(index * SlbFormat.BlockEntrySize, SlbFormat.BlockEntrySize));

            if (block.BlockId != (uint)index)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.BlockIdsNotContiguous,
                    "SLB block ids are not contiguous.");
            }

            if (block.Offset + block.StoredSize > (ulong)length)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.BlockOutsideFile,
                    $"SLB block {block.BlockId} is outside the file.");
            }

            if (block.Codec != SlbCodec.None)
            {
                throw BundleDiagnostics.Error(
                    BundleDiagnostics.UnsupportedCodec,
                    $"SLB block {block.BlockId} uses unsupported codec {block.Codec}.");
            }

            blocks[index] = block;
        }

        if (header.IndexOffset + header.IndexSize > (ulong)length)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.IndexOutsideFile, "SLB index is outside the file.");
        }

        var indexBytes = ReadAt(stream, (long)header.IndexOffset, checked((int)header.IndexSize));

        if (header.IndexCrc32 != SlbChecksums.ComputeCrc32(indexBytes))
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.IndexCrcMismatch, "SLB index CRC check failed.");
        }

        var indexDocument = SlbIndex.ReadDocument(indexBytes);
        byte[]? digestRegion = null;
        var digestOffsets = new Dictionary<uint, int>();
        var digestRegionOffset = SlbChecksums.Align8(SlbFormat.HeaderSize + blockCount * SlbFormat.BlockEntrySize);

        if (verifyDigests)
        {
            var total = 0;

            foreach (var block in blocks)
            {
                digestOffsets[block.BlockId] = total;
                total += SlbChecksums.DigestBytes(block.ChecksumAlgorithms);
            }

            digestRegion = ReadAt(stream, digestRegionOffset, total);
        }

        return new MappedMetadata(
            length,
            header,
            blocks,
            indexDocument.Files,
            indexDocument.Crypto,
            digestRegion,
            digestOffsets);
    }

    private static byte[] ReadAt(FileStream stream, long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > stream.Length)
        {
            throw BundleDiagnostics.Error(BundleDiagnostics.FileTruncated, "SLB metadata is outside the file.");
        }

        var buffer = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private sealed record MappedMetadata(
        long Length,
        SlbHeader Header,
        IReadOnlyList<SlbBlockEntry> Blocks,
        IReadOnlyList<SlbIndexEntry> IndexEntries,
        IReadOnlyList<SlbCryptoEntry> Crypto,
        byte[]? DigestRegion,
        IReadOnlyDictionary<uint, int> DigestOffsets);

    private sealed class SlbMappedStream(
        MemoryMappedViewAccessor view,
        long offset,
        int length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int bufferOffset, int count) =>
            Read(buffer.AsSpan(bufferOffset, count));

        public override int Read(Span<byte> buffer)
        {
            var available = length - (int)_position;
            var count = Math.Min(buffer.Length, available);

            if (count <= 0)
            {
                return 0;
            }

            var rented = ArrayPool<byte>.Shared.Rent(count);

            try
            {
                view.ReadArray(offset + _position, rented, 0, count);
                rented.AsSpan(0, count).CopyTo(buffer);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            _position += count;
            return count;
        }

        public override long Seek(long seekOffset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => seekOffset,
                SeekOrigin.Current => _position + seekOffset,
                SeekOrigin.End => length + seekOffset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            _position = Math.Clamp(_position, 0, length);
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int bufferOffset, int count) =>
            throw new NotSupportedException();
    }
}
