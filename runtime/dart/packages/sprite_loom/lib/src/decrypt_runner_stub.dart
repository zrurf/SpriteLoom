import 'dart:typed_data';

import 'crypto.dart';

Future<Uint8List> decryptSlbBlockInBackground({
  required Uint8List stored,
  required int rawSize,
  required int chunkCount,
  required Uint8List keyBytes,
  required String algorithmName,
  required Uint8List salt,
  required Uint8List noncePrefix,
  required int chunkSize,
  required int keyId,
  required int blockId,
}) => decryptSlbBlock(
  stored: stored,
  rawSize: rawSize,
  chunkCount: chunkCount,
  key: SloomSymmetricKey.fromBytes(keyBytes),
  algorithmName: algorithmName,
  salt: salt,
  noncePrefix: noncePrefix,
  chunkSize: chunkSize,
  keyId: keyId,
  blockId: blockId,
);

Future<Uint8List> decryptSencInBackground(Uint8List file, Uint8List keyBytes) =>
    decryptSenc(file, SloomSymmetricKey.fromBytes(keyBytes));
