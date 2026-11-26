import 'dart:async';
import 'dart:isolate';
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
}) =>
    _DecryptionWorker.instance.slb(
      stored: stored,
      rawSize: rawSize,
      chunkCount: chunkCount,
      keyBytes: keyBytes,
      algorithmName: algorithmName,
      salt: salt,
      noncePrefix: noncePrefix,
      chunkSize: chunkSize,
      keyId: keyId,
      blockId: blockId,
    );

Future<Uint8List> decryptSencInBackground(
  Uint8List file,
  Uint8List keyBytes,
) =>
    _DecryptionWorker.instance.senc(file, keyBytes);

/// Reuses one background isolate so page loads do not pay isolate startup.
class _DecryptionWorker {
  _DecryptionWorker._();

  static final _DecryptionWorker instance = _DecryptionWorker._();

  final Map<int, Completer<Uint8List>> _pending = {};
  SendPort? _send;
  Future<void>? _starting;
  int _nextId = 0;

  Future<Uint8List> slb({
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
  }) =>
      _request({
        'kind': 'slb',
        'stored': stored,
        'rawSize': rawSize,
        'chunkCount': chunkCount,
        'keyBytes': keyBytes,
        'algorithmName': algorithmName,
        'salt': salt,
        'noncePrefix': noncePrefix,
        'chunkSize': chunkSize,
        'keyId': keyId,
        'blockId': blockId,
      });

  Future<Uint8List> senc(Uint8List file, Uint8List keyBytes) => _request({
        'kind': 'senc',
        'file': file,
        'keyBytes': keyBytes,
      });

  Future<Uint8List> _request(Map<String, Object> request) async {
    await _ensureStarted();
    final id = _nextId++;
    final completer = Completer<Uint8List>();
    _pending[id] = completer;
    _send!.send(<String, Object>{'id': id, ...request});
    return completer.future;
  }

  Future<void> _ensureStarted() => _starting ??= _start();

  Future<void> _start() async {
    final receive = ReceivePort();
    final ready = Completer<void>();
    receive.listen((message) {
      if (message is SendPort) {
        _send = message;
        if (!ready.isCompleted) {
          ready.complete();
        }
        return;
      }

      if (message is! Map) {
        return;
      }

      final id = message['id'];
      if (id is! int) {
        return;
      }

      final completer = _pending.remove(id);
      if (completer == null) {
        return;
      }

      final error = message['error'];
      if (error is String) {
        completer.completeError(StateError('SLB decryption failed: $error'));
        return;
      }

      final value = message['value'];
      if (value is Uint8List) {
        completer.complete(value);
      } else {
        completer.completeError(
          const FormatException('Decryption worker returned no data.'),
        );
      }
    });

    try {
      await Isolate.spawn(
        _workerMain,
        receive.sendPort,
        debugName: 'sprite-loom-decrypt',
        errorsAreFatal: true,
      );
      await ready.future;
    } catch (_) {
      receive.close();
      _starting = null;
      rethrow;
    }
  }
}

Future<void> _workerMain(SendPort outbound) async {
  final inbox = ReceivePort();
  outbound.send(inbox.sendPort);

  await for (final message in inbox) {
    if (message is! Map) {
      continue;
    }

    final id = message['id'];
    try {
      final value = switch (message['kind']) {
        'slb' => await decryptSlbBlock(
            stored: message['stored'] as Uint8List,
            rawSize: message['rawSize'] as int,
            chunkCount: message['chunkCount'] as int,
            key: SloomSymmetricKey.fromBytes(message['keyBytes'] as Uint8List),
            algorithmName: message['algorithmName'] as String,
            salt: message['salt'] as Uint8List,
            noncePrefix: message['noncePrefix'] as Uint8List,
            chunkSize: message['chunkSize'] as int,
            keyId: message['keyId'] as int,
            blockId: message['blockId'] as int,
          ),
        'senc' => await decryptSenc(
            message['file'] as Uint8List,
            SloomSymmetricKey.fromBytes(message['keyBytes'] as Uint8List),
          ),
        _ => throw const FormatException('Unknown decryption request.'),
      };
      outbound.send(<String, Object>{'id': id, 'value': value});
    } catch (error) {
      outbound.send(<String, Object>{'id': id, 'error': '$error'});
    }
  }
}
