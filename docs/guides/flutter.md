# Flutter player

The Flutter runtime lives in `runtime/dart` and consists of two packages:

- `sprite_loom` parses JSON/CBOR directory manifests, `.slb` bundles, page containers, frame records, animation documents, and encrypted `.senc` payloads.
- `sprite_loom_flutter` decodes page images and draws frames with `Canvas.drawImageRect`.

It targets Dart 3.6+ and Flutter 3.27+, and is tested with Flutter 3.50.

## Asset setup

Copy a directory package into the app assets, for example `assets/atlas/`, and register it:

```yaml
flutter:
  assets:
    - assets/atlas/
```

## Playback

```dart
import 'dart:typed_data';

import 'package:flutter/services.dart';
import 'package:sprite_loom/sprite_loom.dart';
import 'package:sprite_loom_flutter/sprite_loom_flutter.dart';

Future<Uint8List> loadAsset(String path) async =>
    (await rootBundle.load('assets/atlas/$path')).buffer.asUint8List();

Future<SloomAtlas> openAtlas() async {
  final package = await SloomPackage.open(
    manifestPath: 'manifest.cbor', // or manifest.json
    load: loadAsset,
  );
  return SloomAtlas(package, load: loadAsset);
}
```

```dart
FutureBuilder<SloomAtlas>(
  future: openAtlas(),
  builder: (context, snapshot) {
    final atlas = snapshot.data;
    if (atlas == null) {
      return const SizedBox.shrink();
    }

    return SloomPlayer(
      atlas: atlas,
      animationUri: 'anim://sample/idle',
      scale: 0.5,
    );
  },
)
```

For a bundle, use `SloomPackage.openSlb(path: 'heroes.slb', load: loadAsset)`. Encrypted directory or bundle packages accept `key: SloomSymmetricKey.fromHex(...)`; `SloomAtlas` reads page variants through `SloomPackage.readFile`, so decryption happens before image decoding.

`SloomPlayer` waits only for the first page used by the animation, then starts with an `AnimationController` and decodes up to `prefetchAhead` upcoming pages in the background. The default is two pages; pass `prefetchAhead: 0` to decode only the page that is currently drawn, or call `SloomAtlas.preload` yourself for custom loading screens. `loop`, reverse, and ping-pong playback are handled from the animation document.

## Coordinate model

Frames are trimmed. A frame record stores the original frame size (`origin`), the trim offset (`offset`), and the stored rectangle. The renderer reconstructs the original frame by drawing the stored rectangle at `offset` inside a canvas of `origin` size. This keeps frames aligned without per-animation compensation.

## Variants

The renderer prefers `png` and `webp` variants and ignores `ktx2` variants. Keep at least one raster variant in the package until the GPU texture adapter is available.

## Large clips

Full-body clips can span many pages. Pack one state per package when app size matters; the loader then downloads or bundles only the pages for the active state. An encrypted `.slb` archive is decrypted on demand: manifest and page containers are read up front, while image blocks are decrypted through a persistent background isolate only when a page is decoded.
