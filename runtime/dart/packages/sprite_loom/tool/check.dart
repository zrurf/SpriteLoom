import 'dart:io';
import 'dart:typed_data';

import 'package:sprite_loom/sprite_loom.dart';

Future<void> main(List<String> args) async {
  if (args.isEmpty) {
    stderr.writeln('usage: dart run tool/check.dart <package-dir> [animation-uri]');
    exitCode = 2;
    return;
  }

  final root = args[0];
  Future<Uint8List> load(String path) => File('$root/$path').readAsBytes();

  final package = await SloomPackage.open(
    manifestPath: 'manifest.json',
    load: load,
  );

  stdout.writeln('URIs: ${package.uris.length}');
  stdout.writeln('Pages: ${package.pageCount}');
  stdout.writeln('Animations: ${package.animationRefs.length}');

  final uri = args.length > 1
      ? args[1]
      : package.animationRefs.isEmpty
          ? null
          : package.animationRefs.first.uri;

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
