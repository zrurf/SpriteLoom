import 'dart:convert';
import 'dart:typed_data';

import 'cbor.dart';
import 'crypto.dart';

class SloomSlbArchive {
  const SloomSlbArchive({
    required this.manifest,
    required this.files,
    required this.manifestPath,
  });

  final Map<String, dynamic> manifest;
  final Map<String, Uint8List> files;
  final String manifestPath;
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
  const headerSize = 64;
  const blockEntrySize = 64;
  if (bytes.length < headerSize ||
      bytes[0] != 0x53 ||
      bytes[1] != 0x4c ||
      bytes[2] != 0x42 ||
      bytes[3] != 0x1a) {
    throw const FormatException('SLB magic is missing.');
  }

  final data = ByteData.sublistView(bytes);
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

  if (headerSize + blockCount * blockEntrySize > bytes.length) {
    throw const FormatException('SLB block table is outside the file.');
  }
  if (indexOffset + indexSize > bytes.length) {
    throw const FormatException('SLB index is outside the file.');
  }

  final blocks = <int, _SlbBlock>{};
  for (var index = 0; index < blockCount; index++) {
    final offset = headerSize + index * blockEntrySize;
    final block = _SlbBlock(
      offset: data.getUint64(offset, Endian.little),
      storedSize: data.getUint64(offset + 8, Endian.little),
      rawSize: data.getUint64(offset + 16, Endian.little),
      blockId: data.getUint32(offset + 24, Endian.little),
      codec: bytes[offset + 29],
      flags: data.getUint16(offset + 30, Endian.little),
      chunkCount: data.getUint32(offset + 32, Endian.little),
      keyId: data.getUint16(offset + 36, Endian.little),
    );

    if (block.blockId != index) {
      throw const FormatException('SLB block ids are not contiguous.');
    }
    if (block.offset + block.storedSize > bytes.length) {
      throw FormatException('SLB block ${block.blockId} is outside the file.');
    }
    if (block.codec != 0) {
      throw FormatException(
          'SLB block ${block.blockId} uses unsupported codec ${block.codec}.');
    }

    blocks[block.blockId] = block;
  }

  final indexBytes = Uint8List.sublistView(
    bytes,
    indexOffset,
    indexOffset + indexSize,
  );
  final index = _asMap(decodeCbor(indexBytes), 'SLB index');
  final fileEntries = _asMapList(index['files'], 'SLB index files');
  final cryptoEntries =
      _asMapList(index['crypto'] ?? const [], 'SLB index crypto');
  final crypto = <int, _SlbCrypto>{};

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

  final files = <String, Uint8List>{};
  for (final entry in fileEntries) {
    final path = _asString(entry['path'], 'file path');
    final blockId = _asInt(entry['block'], 'file block');
    final block = blocks[blockId];
    if (block == null) {
      throw FormatException('SLB index references missing block $blockId.');
    }

    final stored = Uint8List.sublistView(
      bytes,
      block.offset,
      block.offset + block.storedSize,
    );

    if ((block.flags & 2) == 0) {
      files[path] = Uint8List.fromList(stored);
      continue;
    }

    if (key == null) {
      throw StateError(
          'SL5022: SLB block ${block.blockId} is encrypted; a key is required.');
    }

    final metadata = crypto[block.keyId];
    if (metadata == null) {
      throw FormatException(
          'SLB block ${block.blockId} has no crypto metadata.');
    }
    if (metadata.kdf != 'hkdf-sha256') {
      throw FormatException('Unsupported SLB KDF: ${metadata.kdf}');
    }

    files[path] = await decryptSlbBlock(
      stored: stored,
      rawSize: block.rawSize,
      chunkCount: block.chunkCount,
      key: key,
      algorithmName: metadata.algorithm,
      salt: metadata.salt,
      noncePrefix: metadata.noncePrefix,
      chunkSize: metadata.chunkSize,
      keyId: metadata.keyId,
      blockId: block.blockId,
    );
  }

  final manifestPath = files.containsKey('manifest.cbor')
      ? 'manifest.cbor'
      : files.containsKey('manifest.json')
          ? 'manifest.json'
          : null;
  if (manifestPath == null) {
    throw const FormatException('SLB does not contain a manifest.');
  }

  return SloomSlbArchive(
    manifest:
        _asMap(_decodeDocument(files[manifestPath]!, manifestPath), 'manifest'),
    files: files,
    manifestPath: manifestPath,
  );
}

Object? _decodeDocument(Uint8List bytes, String path) =>
    path.toLowerCase().endsWith('.cbor')
        ? decodeCbor(bytes)
        : jsonDecode(utf8.decode(bytes));

Map<String, dynamic> _asMap(Object? value, String name) {
  if (value is! Map<String, dynamic>) {
    throw FormatException('$name must be an object.');
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
