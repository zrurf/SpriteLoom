import 'dart:io';

import 'package:sprite_loom/sprite_loom.dart';
import 'package:sprite_loom/src/slb_file_source.dart';
import 'package:test/test.dart';

void main() {
  final key = SloomSymmetricKey.fromHex(
    File('test/fixtures/key.hex').readAsStringSync(),
  );

  test('file and mmap sources expose random ranges', () async {
    final path = 'test/fixtures/plain.slb';
    final bytes = await File(path).readAsBytes();

    for (final preferMmap in [false, true]) {
      final source = await openSlbFileSource(path, preferMmap: preferMmap);
      expect(source.kind, preferMmap ? 'mmap' : 'file');
      expect(source.length, bytes.length);

      final head = await source.read(0, 4);
      expect(head, [0x53, 0x4c, 0x42, 0x1a]);

      final tail = await source.read(bytes.length - 4, 4);
      expect(tail, bytes.sublist(bytes.length - 4));

      final reads = await Future.wait([
        source.read(0, 8),
        source.read(64, 16),
        source.read(bytes.length - 32, 32),
      ]);
      expect(reads[0], bytes.sublist(0, 8));
      expect(reads[1], bytes.sublist(64, 80));
      expect(reads[2], bytes.sublist(bytes.length - 32));

      await source.close();
      await expectLater(source.read(0, 1), throwsStateError);
    }
  });

  test('opens plain and encrypted bundles from file sources', () async {
    for (final preferMmap in [false, true]) {
      final plain = await SloomPackage.openSlbFile(
        'test/fixtures/plain.slb',
        preferMmap: preferMmap,
      );
      expect(plain.storageKind, preferMmap ? 'mmap' : 'file');
      await _expectPackage(plain);
      await plain.close();

      final encrypted = await SloomPackage.openSlbFile(
        'test/fixtures/aes.slb',
        key: key,
        preferMmap: preferMmap,
      );
      expect(encrypted.storageKind, preferMmap ? 'mmap' : 'file');
      await _expectPackage(encrypted);
      await encrypted.close();

      await expectLater(
        encrypted.readFile('page/0000.slatlas/v0.png'),
        throwsStateError,
      );
    }
  });

  test('maps paths with non-ASCII characters', () async {
    final directory = await Directory.systemTemp.createTemp('sloom-mmap-');
    final path = '${directory.path}${Platform.pathSeparator}atlas-角色.slb';

    try {
      await File('test/fixtures/plain.slb').copy(path);
      final package = await SloomPackage.openSlbFile(path);
      expect(package.storageKind, 'mmap');
      await _expectPackage(package);
      await package.close();
    } finally {
      await directory.delete(recursive: true);
    }
  });

  test('missing files fail without leaking the source', () async {
    await expectLater(
      SloomPackage.openSlbFile('test/fixtures/missing.slb'),
      throwsA(isA<FileSystemException>()),
    );
  });
}

Future<void> _expectPackage(SloomPackage package) async {
  expect(package.uris, contains('anim://demo/run'));
  expect(package.frame('frame://demo/run/a1').originWidth, 8);

  final animation = await package.animation('anim://demo/run');
  expect(animation.frameUris.length, 2);
  expect(animation.totalDurationMs, 200);

  final png = await package.readFile('page/0000.slatlas/v0.png');
  expect(png.sublist(0, 8), [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
}
