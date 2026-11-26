import 'dart:typed_data';

/// Native AEAD deceleration for SLB and SENC chunks.
abstract interface class SloomAeadBackend {
  bool get aesGcmAvailable;

  Uint8List decryptAesGcm({
    required Uint8List ciphertext,
    required List<int> key,
    required List<int> noncePrefix,
    required int chunkSize,
    required int chunkCount,
    required int plaintextLength,
  });

  Uint8List decryptXChaCha20Poly1305({
    required Uint8List ciphertext,
    required List<int> key,
    required List<int> noncePrefix,
    required int chunkSize,
    required int chunkCount,
    required int plaintextLength,
  });
}
