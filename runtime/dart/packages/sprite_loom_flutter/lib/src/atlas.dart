import 'dart:ui' as ui;

import 'package:flutter/services.dart';
import 'package:sprite_loom/sprite_loom.dart';

class SloomAtlas {
  SloomAtlas(this.package, {SloomFileLoader? load})
      : _load = load ?? _assetLoader;

  final SloomPackage package;
  final SloomFileLoader _load;
  final Map<int, Future<ui.Image>> _images = {};

  Future<ui.Image> image(int page) =>
      _images.putIfAbsent(page, () => _loadPage(page));

  ui.Image? imageIfLoaded(int page) {
    // Images are loaded through futures; the renderer calls preload before draw.
    return _loaded[page];
  }

  final Map<int, ui.Image> _loaded = {};

  Future<void> preload(Iterable<int> pages) async {
    for (final page in pages.toSet()) {
      final decoded = await image(page);
      _loaded[page] = decoded;
    }
  }

  void dispose() {
    for (final image in _loaded.values) {
      image.dispose();
    }
    _loaded.clear();
    _images.clear();
  }

  Future<ui.Image> _loadPage(int page) async {
    final value = package.page(page);
    final variant = _pickVariant(value);

    if (variant == null) {
      throw StateError('Page $page has no raster variant.');
    }

    final bytes = package.hasFileSource
        ? await package.readFile(variant.file)
        : await _load(variant.file);
    final codec = await ui.instantiateImageCodec(bytes);
    final frame = await codec.getNextFrame();
    return frame.image;
  }

  static SloomVariant? _pickVariant(SloomPage page) {
    for (final id in const ['png', 'webp']) {
      for (final variant in page.variants) {
        if (variant.id == id) {
          return variant;
        }
      }
    }

    for (final variant in page.variants) {
      if (variant.codec != 'ktx2') {
        return variant;
      }
    }

    return null;
  }

  static Future<Uint8List> _assetLoader(String path) async =>
      (await rootBundle.load(path)).buffer.asUint8List();
}
