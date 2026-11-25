import 'dart:io';
import 'dart:typed_data';

import 'package:sprite_loom/sprite_loom.dart';
import 'package:test/test.dart';

void main() {
  final key = SloomSymmetricKey.fromHex(
    File('test/fixtures/key.hex').readAsStringSync(),
  );

  test('reads a plain CBOR directory package', () async {
    final package = await SloomPackage.open(
      manifestPath: 'manifest.cbor',
      load: _directoryLoader('plain-dir'),
    );

    await _expectPackage(package);
  });

  test('reads and decrypts an AES-GCM directory package', () async {
    final package = await SloomPackage.open(
      manifestPath: 'manifest.cbor',
      load: _directoryLoader('aes-dir'),
      key: key,
    );

    await _expectPackage(package);
  });

  test('reads a plain SLB bundle', () async {
    final package = await SloomPackage.openSlb(
      path: 'plain.slb',
      load: _fileLoader,
    );

    await _expectPackage(package);
  });

  test('reads and decrypts AES-GCM and XChaCha20 SLB bundles', () async {
    for (final name in ['aes.slb', 'x.slb']) {
      final package = await SloomPackage.openSlb(
        path: name,
        load: _fileLoader,
        key: key,
      );

      await _expectPackage(package);
    }
  });

  test('rejects an encrypted package without a key', () async {
    await expectLater(
      SloomPackage.openSlb(path: 'aes.slb', load: _fileLoader),
      throwsA(isA<StateError>()),
    );
  });
}

Future<void> _expectPackage(SloomPackage package) async {
  expect(package.uris, contains('anim://demo/run'));
  expect(package.uris, contains('frame://demo/run/a1'));

  final frame = package.frame('frame://demo/run/a1');
  expect(frame.originWidth, 8);
  expect(frame.originHeight, 8);

  final animation = await package.animation('anim://demo/run');
  expect(animation.frameUris.length, 2);
  expect(animation.totalDurationMs, 200);

  final png = await package.readFile('page/0000.slatlas/v0.png');
  expect(png.sublist(0, 8), [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
}

Future<Uint8List> _fileLoader(String path) =>
    File('test/fixtures/$path').readAsBytes();

SloomFileLoader _directoryLoader(String name) =>
    (path) => File('test/fixtures/$name/$path').readAsBytes();
