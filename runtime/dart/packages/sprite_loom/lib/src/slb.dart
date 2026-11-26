import 'dart:convert';
import 'dart:typed_data';

import 'cbor.dart';
import 'crypto.dart';
import 'decrypt_runner.dart';
import 'slb_byte_source.dart';

class SloomSlbArchive {
  SloomSlbArchive._({
    required SloomSlbByteSource source,
    required String manifestPath,
    required Map<int, _SlbBlock> blocks,
    required Map<String, int> paths,
    required Map<int, _SlbCrypto> crypto,
    required SloomSymmetricKey? key,
  }) : _source = source,
       manifestPath = manifestPath,
       _blocks = blocks,
       _paths = paths,
       _crypto = crypto,
       _key = key;

  final SloomSlbByteSource _source;
  final String manifestPath;
  final Map<int, _SlbBlock> _blocks;
  final Map<String, int> _paths;
  final Map<int, _SlbCrypto> _crypto;
  final SloomSymmetricKey? _key;
  final Map<String, Uint8List> _cache = {};
  bool _closed = false;

  late final Map<String, dynamic> manifest;

  /// Storage backing this archive: `memory`, `file`, or `mmap`.
  String get storageKind => _source.kind;

  bool contains(String path) => _paths.containsKey(path);

  Future<Uint8List> readFile(String path, {bool cache = true}) async {
    if (_closed) {
      throw StateError('SLB archive is closed.');
    }

    final cached = _cache[path];
    if (cached != null) {
      return cached;
    }

    final blockId = _paths[path];
    if (blockId == null) {
      throw StateError('File not found in SLB: $path');
    }

    final block = _blocks[blockId]!;
    final stored = await _source.read(block.offset, block.storedSize);

    Uint8List content;
    if ((block.flags & 2) == 0) {
      content = stored;
    } else {
      final key = _key;
      if (key == null) {
        throw StateError(
          'SL5022: SLB block ${block.blockId} is encrypted; a key is required.',
        );
      }

      final metadata = _crypto[block.keyId];
      if (metadata == null) {
        throw FormatException(
          'SLB block ${block.blockId} has no crypto metadata.',
        );
      }
      if (metadata.kdf != 'hkdf-sha256') {
        throw FormatException('Unsupported SLB KDF: ${metadata.kdf}');
      }

      content = await decryptSlbBlockInBackground(
        stored: stored,
        rawSize: block.rawSize,
        chunkCount: block.chunkCount,
        keyBytes: key.bytes,
        algorithmName: metadata.algorithm,
        salt: metadata.salt,
        noncePrefix: metadata.noncePrefix,
        chunkSize: metadata.chunkSize,
        keyId: metadata.keyId,
        blockId: block.blockId,
      );
    }

    if (cache) {
      _cache[path] = content;
    }
    return content;
  }

  Future<void> preloadAll() async {
    for (final path in _paths.keys) {
      await readFile(path);
    }
  }

  Future<void> close() async {
    if (_closed) {
      return;
    }

    _closed = true;
    _cache.clear();
    await _source.close();
  }
}

class _SlbBlock {
  const _SlbBlock({
    required this.offset,
    required this.storedSize,
    required this.rawSize,
    required this.blockId,
    required this.codec,
    required this.flags,
    required this.chunkCount,
    required this.keyId,
  });

  final int offset;
  final int storedSize;
  final int rawSize;
  final int blockId;
  final int codec;
  final int flags;
  final int chunkCount;
  final int keyId;
}

class _SlbCrypto {
  const _SlbCrypto({
    required this.keyId,
    required this.algorithm,
    required this.kdf,
    required this.salt,
    required this.noncePrefix,
    required this.chunkSize,
  });

  final int keyId;
  final String algorithm;
  final String kdf;
  final Uint8List salt;
  final Uint8List noncePrefix;
  final int chunkSize;
}

Future<SloomSlbArchive> readSlb(
  Uint8List bytes, {
  SloomSymmetricKey? key,
}) async {
  final archive = await readSlbLazy(bytes, key: key);
  await archive.preloadAll();
  return archive;
}

Future<SloomSlbArchive> readSlbLazy(
  Uint8List bytes, {
  SloomSymmetricKey? key,
}) => readSlbSource(SloomSlbMemorySource(bytes), key: key);

/// Parses an archive from a random-access source.
///
/// On failure the source is closed. On success ownership transfers to the
/// returned [SloomSlbArchive].
Future<SloomSlbArchive> readSlbSource(
  SloomSlbByteSource source, {
  SloomSymmetricKey? key,
}) async {
  try {
    return await _readSlbSource(source, key);
  } catch (_) {
    await source.close();
    rethrow;
  }
}

Future<SloomSlbArchive> _readSlbSource(
  SloomSlbByteSource source,
  SloomSymmetricKey? key,
) async {
  const headerSize = 64;
  const blockEntrySize = 64;
  if (source.length < headerSize) {
    throw const FormatException('SLB header is truncated.');
  }

  final header = await source.read(0, headerSize);
  if (header[0] != 0x53 ||
      header[1] != 0x4c ||
      header[2] != 0x42 ||
      header[3] != 0x1a) {
    throw const FormatException('SLB magic is missing.');
  }

  final data = ByteData.sublistView(header);
  final major = data.getUint16(4, Endian.little);
  if (major != 1) {
    throw FormatException('Unsupported SLB major version: $major');
  }

  final flags = data.getUint32(28, Endian.little);
  if ((flags & 4) != 0) {
    throw const FormatException('Encrypted SLB indexes are not supported.');
  }

  final blockCount = data.getUint32(24, Endian.little);
  final indexOffset = data.getUint64(40, Endian.little);
  final indexSize = data.getUint32(48, Endian.little);

  if (headerSize + blockCount * blockEntrySize > source.length) {
    throw const FormatException('SLB block table is outside the file.');
  }
  if (indexOffset + indexSize > source.length) {
    throw const FormatException('SLB index is outside the file.');
  }

  final blocks = <int, _SlbBlock>{};
  if (blockCount > 0) {
    final table = await source.read(headerSize, blockCount * blockEntrySize);
    final tableData = ByteData.sublistView(table);

    for (var index = 0; index < blockCount; index++) {
      final offset = index * blockEntrySize;
      final block = _SlbBlock(
        offset: tableData.getUint64(offset, Endian.little),
        storedSize: tableData.getUint64(offset + 8, Endian.little),
        rawSize: tableData.getUint64(offset + 16, Endian.little),
        blockId: tableData.getUint32(offset + 24, Endian.little),
        codec: table[offset + 29],
        flags: tableData.getUint16(offset + 30, Endian.little),
        chunkCount: tableData.getUint32(offset + 32, Endian.little),
        keyId: tableData.getUint16(offset + 36, Endian.little),
      );

      if (block.blockId != index) {
        throw const FormatException('SLB block ids are not contiguous.');
      }
      if (block.offset + block.storedSize > source.length) {
        throw FormatException(
          'SLB block ${block.blockId} is outside the file.',
        );
      }
      if (block.codec != 0) {
        throw FormatException(
          'SLB block ${block.blockId} uses unsupported codec ${block.codec}.',
        );
      }

      blocks[block.blockId] = block;
    }
  }

  final indexBytes = await source.read(indexOffset, indexSize);
  final index = _asMap(decodeCbor(indexBytes), 'SLB index');
  final fileEntries = _asMapList(index['files'], 'SLB index files');
  final cryptoEntries = _asMapList(
    index['crypto'] ?? const [],
    'SLB index crypto',
  );
  final crypto = <int, _SlbCrypto>{};
  final paths = <String, int>{};

  for (final value in cryptoEntries) {
    final entry = _SlbCrypto(
      keyId: _asInt(value['key-id'], 'crypto key-id'),
      algorithm: _asString(value['alg'], 'crypto alg'),
      kdf: value['kdf'] == null
          ? 'hkdf-sha256'
          : _asString(value['kdf'], 'crypto kdf'),
      salt: _asBytes(value['salt'], 'crypto salt'),
      noncePrefix: _asBytes(value['nonce-prefix'], 'crypto nonce-prefix'),
      chunkSize: _asInt(value['chunk-size'], 'crypto chunk-size'),
    );
    crypto[entry.keyId] = entry;
  }

  for (final entry in fileEntries) {
    final path = _asString(entry['path'], 'file path');
    final blockId = _asInt(entry['block'], 'file block');
    if (!blocks.containsKey(blockId)) {
      throw FormatException('SLB index references missing block $blockId.');
    }
    paths[path] = blockId;
  }

  final manifestPath = paths.containsKey('manifest.cbor')
      ? 'manifest.cbor'
      : paths.containsKey('manifest.json')
      ? 'manifest.json'
      : null;
  if (manifestPath == null) {
    throw const FormatException('SLB does not contain a manifest.');
  }

  final archive = SloomSlbArchive._(
    source: source,
    manifestPath: manifestPath,
    blocks: blocks,
    paths: paths,
    crypto: crypto,
    key: key,
  );

  try {
    archive.manifest = _asMap(
      _decodeDocument(
        await archive.readFile(manifestPath, cache: false),
        manifestPath,
      ),
      'manifest',
    );
    return archive;
  } catch (_) {
    await archive.close();
    rethrow;
  }
}

Object? _decodeDocument(Uint8List bytes, String path) =>
    path.toLowerCase().endsWith('.cbor')
    ? decodeCbor(bytes)
    : jsonDecode(utf8.decode(bytes));

Map<String, dynamic> _asMap(Object? value, String name) {
  if (value == null) {
    throw FormatException('$name');
  }
  if (value is! Map<String, dynamic>) {
    throw const FormatException('Expected an object.');
  }
  return value;
}

List<Map<String, dynamic>> _asMapList(Object? value, String name) {
  if (value is! List) {
    throw FormatException('$name must be an array.');
  }
  return [for (final item in value) _asMap(item, name)];
}

String _asString(Object? value, String name) {
  if (value is! String) {
    throw FormatException('$name must be a string.');
  }
  return value;
}

int _asInt(Object? value, String name) {
  if (value is! num) {
    throw FormatException('$name must be a number.');
  }
  return value.toInt();
}

Uint8List _asBytes(Object? value, String name) {
  if (value is! Uint8List) {
    if (value is List<int>) {
      return Uint8List.fromList(value);
    }
    throw FormatException('$name must be a byte string.');
  }
  return value;
}
