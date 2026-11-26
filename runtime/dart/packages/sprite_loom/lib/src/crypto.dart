import 'dart:convert';
import 'dart:typed_data';

import 'package:cryptography/cryptography.dart';
import 'package:cryptography/dart.dart' show DartAesGcm;

import 'sloom_aead_backend.dart';

class SloomSymmetricKey {
  SloomSymmetricKey.fromBytes(List<int> bytes)
    : bytes = Uint8List.fromList(bytes) {
    if (this.bytes.length != 32) {
      throw ArgumentError.value(
        this.bytes.length,
        'bytes',
        'Symmetric keys must be 32 bytes.',
      );
    }
  }

  factory SloomSymmetricKey.fromHex(String value) {
    final text = value.trim();
    final normalized = text.startsWith('0x') || text.startsWith('0X')
        ? text.substring(2)
        : text;
    if (normalized.length != 64) {
      throw const FormatException(
        'Symmetric key must contain 64 hex characters.',
      );
    }
    return SloomSymmetricKey.fromBytes(
      List<int>.generate(
        32,
        (index) => int.parse(
          normalized.substring(index * 2, index * 2 + 2),
          radix: 16,
        ),
      ),
    );
  }

  final Uint8List bytes;
}

enum _SloomAead { aes256Gcm, xchacha20Poly1305 }

/// Native AEAD capabilities of the current process.
///
/// AES-256-GCM is hardware accelerated only when libsodium reports AES-NI or
/// ARM Crypto support; otherwise the pure-Dart implementation is used.
abstract final class SloomCryptoCapabilities {
  static bool get nativeAesGcm =>
      (sloomAeadBackend?.aesGcmAvailable ?? false) ||
      AesGcm.with256bits() is! DartAesGcm;

  static bool get nativeXChaCha20Poly1305 => sloomAeadBackend != null;
}

Future<Uint8List> decryptSenc(Uint8List file, SloomSymmetricKey key) async {
  if (file.length < 36 || !_hasMagic(file, const [0x53, 0x45, 0x4e, 0x43])) {
    throw const FormatException('SENC header is missing or truncated.');
  }

  final data = ByteData.sublistView(file);
  if (file[4] != 1) {
    throw FormatException('Unsupported SENC version: ${file[4]}');
  }

  final algorithm = _parseAlgorithm(file[5]);
  if (data.getUint32(28, Endian.little) == 0) {
    throw const FormatException('SENC file contains no chunks.');
  }

  final chunkSize = data.getUint32(8, Endian.little);
  final chunkCount = data.getUint32(28, Endian.little);
  final prefixLength = algorithm == _SloomAead.aes256Gcm ? 4 : 16;
  final prefix = file.sublist(12, 12 + prefixLength);
  final body = Uint8List.sublistView(file, 36);
  final fullChunks = chunkCount - 1;
  final fullBytes = fullChunks * (chunkSize + 16);
  if (body.length < fullBytes + 16) {
    throw const FormatException('SENC payload is truncated.');
  }

  final lastPlaintext = body.length - fullBytes - 16;
  if (lastPlaintext < 0 || lastPlaintext > chunkSize) {
    throw const FormatException('SENC payload has an invalid final chunk.');
  }

  return _decryptChunks(
    body,
    key.bytes,
    algorithm,
    prefix,
    chunkSize,
    chunkCount,
    fullChunks * chunkSize + lastPlaintext,
  );
}

Future<Uint8List> decryptSlbBlock({
  required Uint8List stored,
  required int rawSize,
  required int chunkCount,
  required SloomSymmetricKey key,
  required String algorithmName,
  required Uint8List salt,
  required Uint8List noncePrefix,
  required int chunkSize,
  required int keyId,
  required int blockId,
}) async {
  if (chunkCount <= 0) {
    throw const FormatException('Encrypted SLB block has no chunks.');
  }

  final algorithm = _parseAlgorithmName(algorithmName);
  final info = Uint8List(15);
  info.setRange(0, 9, utf8.encode('slb-block'));
  final infoData = ByteData.sublistView(info);
  infoData.setUint16(9, keyId, Endian.little);
  infoData.setUint32(11, blockId, Endian.little);
  final keyMaterial = await Hkdf(
    hmac: Hmac.sha256(),
    outputLength: 32,
  ).deriveKey(secretKey: SecretKey(key.bytes), nonce: salt, info: info);
  final blockKey = await keyMaterial.extractBytes();

  return _decryptChunks(
    stored,
    blockKey,
    algorithm,
    noncePrefix,
    chunkSize,
    chunkCount,
    rawSize,
  );
}

Future<Uint8List> _decryptChunks(
  Uint8List ciphertext,
  List<int> key,
  _SloomAead algorithm,
  List<int> noncePrefix,
  int chunkSize,
  int chunkCount,
  int plaintextLength,
) async {
  if (chunkSize <= 0 || chunkSize > 1024 * 1024) {
    throw const FormatException('Invalid AEAD chunk size.');
  }

  final expected = plaintextLength + chunkCount * 16;
  if (ciphertext.length < expected) {
    throw const FormatException('Encrypted payload is truncated.');
  }

  final backend = sloomAeadBackend;
  if (backend != null) {
    if (algorithm == _SloomAead.aes256Gcm &&
        backend.aesGcmAvailable &&
        noncePrefix.length == 4) {
      return backend.decryptAesGcm(
        ciphertext: ciphertext,
        key: key,
        noncePrefix: noncePrefix,
        chunkSize: chunkSize,
        chunkCount: chunkCount,
        plaintextLength: plaintextLength,
      );
    }
    if (algorithm == _SloomAead.xchacha20Poly1305 && noncePrefix.length == 16) {
      return backend.decryptXChaCha20Poly1305(
        ciphertext: ciphertext,
        key: key,
        noncePrefix: noncePrefix,
        chunkSize: chunkSize,
        chunkCount: chunkCount,
        plaintextLength: plaintextLength,
      );
    }
  }

  final output = Uint8List(plaintextLength);
  final algorithmImpl = algorithm == _SloomAead.aes256Gcm
      ? AesGcm.with256bits()
      : Xchacha20.poly1305Aead();
  final secretKey = SecretKey(key);
  final nonce = Uint8List(noncePrefix.length + 8)
    ..setRange(0, noncePrefix.length, noncePrefix);
  final nonceData = ByteData.sublistView(nonce);
  var inputOffset = 0;
  var outputOffset = 0;
  var remaining = plaintextLength;

  for (var index = 0; index < chunkCount; index++) {
    final currentPlaintext = remaining < chunkSize ? remaining : chunkSize;
    final currentCiphertext = currentPlaintext + 16;
    nonceData.setUint64(noncePrefix.length, index, Endian.little);
    final chunk = Uint8List.sublistView(
      ciphertext,
      inputOffset,
      inputOffset + currentCiphertext,
    );
    final secretBox = SecretBox(
      Uint8List.sublistView(chunk, 0, currentPlaintext),
      nonce: nonce,
      mac: Mac(Uint8List.sublistView(chunk, currentPlaintext)),
    );
    final plain = await algorithmImpl.decrypt(secretBox, secretKey: secretKey);
    output.setRange(outputOffset, outputOffset + currentPlaintext, plain);
    inputOffset += currentCiphertext;
    outputOffset += currentPlaintext;
    remaining -= currentPlaintext;
  }

  return output;
}

_SloomAead _parseAlgorithm(int value) {
  switch (value) {
    case 1:
      return _SloomAead.aes256Gcm;
    case 2:
      return _SloomAead.xchacha20Poly1305;
    default:
      throw FormatException('Unsupported SENC algorithm: $value');
  }
}

_SloomAead _parseAlgorithmName(String name) {
  switch (name) {
    case 'aes-256-gcm':
      return _SloomAead.aes256Gcm;
    case 'xchacha20-poly1305':
      return _SloomAead.xchacha20Poly1305;
    default:
      throw FormatException('Unsupported SLB encryption algorithm: $name');
  }
}

bool _hasMagic(List<int> bytes, List<int> magic) {
  if (bytes.length < magic.length) {
    return false;
  }
  for (var index = 0; index < magic.length; index++) {
    if (bytes[index] != magic[index]) {
      return false;
    }
  }
  return true;
}
