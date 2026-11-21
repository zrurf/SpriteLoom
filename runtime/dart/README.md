# SpriteLoom Flutter runtime

SpriteLoom ships a small Flutter loader for directory packages.

## Packages

- `sprite_loom` parses `manifest.json`, page containers, frame records, and animation documents.
- `sprite_loom_flutter` decodes page images and draws frames with `Canvas.drawImageRect`.

The loader prefers `png` and `webp` page variants. KTX2/Basis variants are ignored until a GPU texture adapter is added; a package should therefore keep at least one raster variant.

## Usage

```dart
import 'dart:typed_data';

import 'package:flutter/services.dart';
import 'package:sprite_loom/sprite_loom.dart';
import 'package:sprite_loom_flutter/sprite_loom_flutter.dart';

Future<Uint8List> loadAsset(String path) async =>
    (await rootBundle.load('assets/atlas/$path')).buffer.asUint8List();

final package = await SloomPackage.open(
  manifestPath: 'manifest.json',
  load: loadAsset,
);
final atlas = SloomAtlas(package, load: loadAsset);

SloomPlayer(
  atlas: atlas,
  animationUri: 'anim://sample/idle',
  scale: 0.5,
);
```

The package directory must be listed in `pubspec.yaml`:

```yaml
flutter:
  assets:
    - assets/atlas/
```

Frames are drawn in their original frame coordinates: the stored rectangle is placed at `offset` inside a canvas of `origin` size. This preserves trimming offsets and lets different frames align without extra code.

## Large clips

Full-body clips can use many pages. Packing one state per package keeps app assets small and lets a Flutter app load only the pages for the active state. The current renderer loads every page used by the selected animation before playback starts.

## Layout

- [`packages/sprite_loom`](packages/sprite_loom): package model and animation reader.
- [`packages/sprite_loom_flutter`](packages/sprite_loom_flutter): image decoding and `SloomPlayer`.
