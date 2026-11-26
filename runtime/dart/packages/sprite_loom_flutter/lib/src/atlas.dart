import 'dart:ui' as ui;

import 'package:flutter/services.dart';
import 'package:sprite_loom/sprite_loom.dart';

class SloomAtlas {
  SloomAtlas(this.package, {SloomFileLoader? load})
    : _load = load ?? _assetLoader;

  final SloomPackage package;
  final SloomFileLoader _load;
  final Map<int, Future<ui.Image>> _images = {};
  final Map<int, ui.Image> _loaded = {};
  bool _disposed = false;

  /// Decodes one page once; concurrent callers share the same future.
  Future<ui.Image> image(int page) {
    if (_disposed) {
      throw StateError('SloomAtlas has been disposed.');
    }

    return _images.putIfAbsent(page, () => _loadAndStore(page));
  }

  bool isLoaded(int page) => _loaded.containsKey(page);

  ui.Image? imageIfLoaded(int page) => _loaded[page];

  /// Loads only the requested pages. Playback does not call this for a whole
  /// animation; [SloomPlayer] starts with the first page and prefetches ahead.
  Future<void> preload(Iterable<int> pages) async {
    for (final page in pages.toSet()) {
      await image(page);
    }
  }

  void dispose() {
    _disposed = true;
    for (final image in _loaded.values) {
      image.dispose();
    }
    _loaded.clear();
    _images.clear();
  }

  Future<ui.Image> _loadAndStore(int page) async {
    final decoded = await _loadPage(page);
    if (_disposed) {
      decoded.dispose();
      throw StateError('SloomAtlas has been disposed.');
    }

    _loaded[page] = decoded;
    return decoded;
  }

  Future<ui.Image> _loadPage(int page) async {
    final value = package.page(page);
    final variant = _pickVariant(value);

    if (variant == null) {
      throw StateError('Page $page has no raster variant.');
    }

    final bytes = package.hasFileSource
        ? await package.readFile(variant.file, cache: false)
        : await _load(variant.file);
    final codec = await ui.instantiateImageCodec(bytes);
    try {
      final frame = await codec.getNextFrame();
      return frame.image;
    } finally {
      codec.dispose();
    }
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
