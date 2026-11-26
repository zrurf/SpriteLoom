# SpriteLoom Flutter runtime

SpriteLoom ships a small Flutter loader for directory packages and `.slb` bundles, with optional AES-256-GCM or XChaCha20-Poly1305 decryption.

## Packages

- `sprite_loom` parses JSON or CBOR directory packages, `.slb` bundles, page containers, frame records, animation documents, and encrypted payloads.
- `sprite_loom_flutter` decodes page images and draws frames with `Canvas.drawImageRect`.

The loader prefers `png` and `webp` page variants. KTX2/Basis variants are ignored until a GPU texture adapter is added; a package should therefore keep at least one raster variant. `sprite_loom` depends on the pure-Dart `cryptography` package for decryption.

## Usage

```dart
import 'dart:typed_data';

import 'package:flutter/services.dart';
import 'package:sprite_loom/sprite_loom.dart';
import 'package:sprite_loom_flutter/sprite_loom_flutter.dart';

Future<Uint8List> loadAsset(String path) async =>
    (await rootBundle.load('assets/atlas/$path')).buffer.asUint8List();

final package = await SloomPackage.open(
  manifestPath: 'manifest.json', // or manifest.cbor
  load: loadAsset,
);
final atlas = SloomAtlas(package, load: loadAsset);

SloomPlayer(
  atlas: atlas,
  animationUri: 'anim://sample/idle',
  scale: 0.5,
);
```

For a `.slb` bundle, load the whole archive and pass an optional key:

```dart
final package = await SloomPackage.openSlb(
  path: 'heroes.slb',
  load: loadAsset,
);

final key = SloomSymmetricKey.fromHex(
  await rootBundle.loadString('assets/atlas/asset.key'),
);
final encrypted = await SloomPackage.openSlb(
  path: 'heroes.slb',
  load: loadAsset,
  key: key,
);
```

Encrypted directory packages use `.senc` files on disk and the same optional `key` argument to `SloomPackage.open`. `SloomAtlas` uses `package.readFile`, so encrypted page variants are decrypted before image decoding. Encrypted `.slb` image blocks stay encrypted in memory until the page is needed; decryption runs in a shared background isolate instead of blocking the UI isolate.

The package directory must be listed in `pubspec.yaml`:

```yaml
flutter:
  assets:
    - assets/atlas/
```

Frames are drawn in their original frame coordinates: the stored rectangle is placed at `offset` inside a canvas of `origin` size. This preserves trimming offsets and lets different frames align without extra code.

## Large clips

Full-body clips can use many pages. Packing one state per package keeps app assets small and lets a Flutter app load only the pages for the active state. `SloomPlayer` waits for the first page and then prefetches a bounded number of upcoming pages (`prefetchAhead`, default 2); it no longer decodes every page in the animation before playback starts.

## Layout

- [`packages/sprite_loom`](packages/sprite_loom): package model and animation reader.
- [`packages/sprite_loom_flutter`](packages/sprite_loom_flutter): image decoding and `SloomPlayer`.
