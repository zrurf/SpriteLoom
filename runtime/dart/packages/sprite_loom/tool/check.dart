import 'dart:io';
import 'dart:typed_data';

import 'package:sprite_loom/sprite_loom.dart';

Future<void> main(List<String> args) async {
  if (args.isEmpty) {
    stderr.writeln(
      'usage: dart run tool/check.dart <package-dir|package.slb> [animation-uri] [--key key.hex]',
    );
    exitCode = 2;
    return;
  }

  var target = args[0];
  String? animationUri;
  String? keyPath;

  for (var index = 1; index < args.length; index++) {
    if (args[index] == '--key') {
      if (index + 1 >= args.length) {
        stderr.writeln('--key requires a path.');
        exitCode = 2;
        return;
      }
      keyPath = args[++index];
    } else if (animationUri == null) {
      animationUri = args[index];
    }
  }

  final key = keyPath == null
      ? null
      : SloomSymmetricKey.fromHex(File(keyPath).readAsStringSync());

  final SloomPackage package;
  if (target.toLowerCase().endsWith('.slb')) {
    Future<Uint8List> load(String path) => File(path).readAsBytes();
    package = await SloomPackage.openSlb(path: target, load: load, key: key);
  } else {
    Future<Uint8List> load(String path) => File('$target/$path').readAsBytes();
    final manifestPath = File('$target/manifest.cbor').existsSync()
        ? 'manifest.cbor'
        : File('$target/manifest.json').existsSync()
            ? 'manifest.json'
            : 'manifest.cbor';
    package = await SloomPackage.open(
      manifestPath: manifestPath,
      load: load,
      key: key,
    );
  }

  stdout.writeln('URIs: ${package.uris.length}');
  stdout.writeln('Pages: ${package.pageCount}');
  stdout.writeln('Animations: ${package.animationRefs.length}');

  final uri = animationUri ??
      (package.animationRefs.isEmpty ? null : package.animationRefs.first.uri);

  if (uri == null) {
    return;
  }

  final animation = await package.animation(uri);
  final frames = animation.frameUris.map(package.frame).toList();
  stdout.writeln('Animation: ${animation.uri}');
  stdout.writeln('Frames: ${frames.length}');
  stdout.writeln('Duration: ${animation.totalDurationMs} ms');
  stdout.writeln('Pages used: ${frames.map((frame) => frame.page).toSet()}');
}
