import 'dart:ffi';
import 'dart:typed_data';

import 'package:ffi/ffi.dart';
import 'package:sodium/sodium.ffi.dart' as sodium;

import 'sloom_aead.dart';

/// Native AEAD backend using the exported `package:sodium` bindings.
///
/// The native library and build hooks come from the pub package; this file
/// does not load or bundle libsodium itself.

typedef _DetachedDecrypt = int Function(
  Pointer<UnsignedChar> output,
  Pointer<UnsignedChar> ciphertext,
  int ciphertextLength,
  Pointer<UnsignedChar> tag,
  Pointer<UnsignedChar> nonce,
  Pointer<UnsignedChar> key,
);

final SloomAeadBackend? sloomAeadBackend = _createBackend();

SloomAeadBackend? _createBackend() {
  try {
    sodium.SodiumInit.init();
    final library = const sodium.LibSodiumFFI();
    return _SodiumAeadBackend(
      library,
      aesGcmAvailable: library.crypto_aead_aes256gcm_is_available() == 1,
    );
  } on Object {
    return null;
  }
}

final class _SodiumAeadBackend implements SloomAeadBackend {
  _SodiumAeadBackend(this._library, {required this.aesGcmAvailable});

  final sodium.LibSodiumFFI _library;

  @override
  final bool aesGcmAvailable;

  @override
  Uint8List decryptAesGcm({
    required Uint8List ciphertext,
    required List<int> key,
    required List<int> noncePrefix,
    required int chunkSize,
    required int chunkCount,
    required int plaintextLength,
  }) {
    if (!aesGcmAvailable) {
      throw UnsupportedError('AES-256-GCM is not available in libsodium.');
    }

    return _decryptChunks(
      ciphertext: ciphertext,
      key: key,
      noncePrefix: noncePrefix,
      nonceLength: 12,
      chunkSize: chunkSize,
      chunkCount: chunkCount,
      plaintextLength: plaintextLength,
      decrypt: (output, source, sourceLength, tag, nonce, blockKey) =>
          _library.crypto_aead_aes256gcm_decrypt_detached(
            output,
            nullptr.cast(),
            source,
            sourceLength,
            tag,
            nullptr.cast(),
            0,
            nonce,
            blockKey,
          ),
    );
  }

  @override
  Uint8List decryptXChaCha20Poly1305({
    required Uint8List ciphertext,
    required List<int> key,
    required List<int> noncePrefix,
    required int chunkSize,
    required int chunkCount,
    required int plaintextLength,
  }) => _decryptChunks(
    ciphertext: ciphertext,
    key: key,
    noncePrefix: noncePrefix,
    nonceLength: 24,
    chunkSize: chunkSize,
    chunkCount: chunkCount,
    plaintextLength: plaintextLength,
    decrypt: (output, source, sourceLength, tag, nonce, blockKey) =>
        _library.crypto_aead_xchacha20poly1305_ietf_decrypt_detached(
          output,
          nullptr.cast(),
          source,
          sourceLength,
          tag,
          nullptr.cast(),
          0,
          nonce,
          blockKey,
        ),
  );

  Uint8List _decryptChunks({
    required Uint8List ciphertext,
    required List<int> key,
    required List<int> noncePrefix,
    required int nonceLength,
    required int chunkSize,
    required int chunkCount,
    required int plaintextLength,
    required _DetachedDecrypt decrypt,
  }) {
    if (key.length != 32) {
      throw ArgumentError.value(
        key.length,
        'key',
        'AEAD keys must be 32 bytes.',
      );
    }
    if (noncePrefix.length + 8 != nonceLength) {
      throw ArgumentError.value(
        noncePrefix.length,
        'noncePrefix',
        'Expected ${nonceLength - 8} nonce prefix bytes.',
      );
    }
    if (chunkCount <= 0 || chunkSize <= 0 || chunkSize > 1024 * 1024) {
      throw const FormatException('Invalid AEAD chunk configuration.');
    }

    final expected = plaintextLength + chunkCount * 16;
    if (ciphertext.length < expected) {
      throw const FormatException('Encrypted payload is truncated.');
    }

    final keyBytes = key is Uint8List ? key : Uint8List.fromList(key);
    return sodium.sodiumScope(_library, (scope) {
      // The key stays in libsodium's guarded memory. Large payload buffers use
      // regular heap allocation because sodium_malloc is not intended for them.
      final blockKey = scope.copyList<UnsignedChar>(keyBytes);
      final output = calloc<Uint8>(plaintextLength == 0 ? 1 : plaintextLength);
      final chunk = calloc<Uint8>(chunkSize + 16);
      final nonce = calloc<Uint8>(nonceLength);
      try {
        final nonceBytes = nonce.asTypedList(nonceLength);
        nonceBytes.setRange(0, noncePrefix.length, noncePrefix);
        final nonceData = ByteData.sublistView(nonceBytes);
        final chunkBytes = chunk.asTypedList(chunkSize + 16);

        var inputOffset = 0;
        var outputOffset = 0;
        var remaining = plaintextLength;

        for (var index = 0; index < chunkCount; index++) {
          final currentPlaintext = remaining < chunkSize
              ? remaining
              : chunkSize;
          final currentCiphertext = currentPlaintext + 16;
          chunkBytes.setRange(0, currentCiphertext, ciphertext, inputOffset);
          nonceData.setUint64(noncePrefix.length, index, Endian.little);

          final result = decrypt(
            (output + outputOffset).cast<UnsignedChar>(),
            chunk.cast<UnsignedChar>(),
            currentPlaintext,
            (chunk + currentPlaintext).cast<UnsignedChar>(),
            nonce.cast<UnsignedChar>(),
            blockKey.ptr,
          );
          if (result != 0) {
            throw const FormatException('SL5045: AEAD authentication failed.');
          }

          inputOffset += currentCiphertext;
          outputOffset += currentPlaintext;
          remaining -= currentPlaintext;
        }

        final result = Uint8List(plaintextLength);
        if (plaintextLength > 0) {
          result.setAll(0, output.asTypedList(plaintextLength));
        }
        return result;
      } finally {
        calloc.free(output);
        calloc.free(chunk);
        calloc.free(nonce);
      }
    });
  }
}
