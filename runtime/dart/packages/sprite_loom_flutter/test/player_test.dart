import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sprite_loom/sprite_loom.dart';
import 'package:sprite_loom_flutter/sprite_loom_flutter.dart';

void main() {
  testWidgets('decodes a page and renders an animation', (tester) async {
    final files = <String, List<int>>{
      'manifest.json': utf8.encode(jsonEncode(manifest)),
      'page/0000.slatlas/container.json': utf8.encode(jsonEncode(container)),
      'anim/run.anim.json': utf8.encode(jsonEncode(animation)),
      'page/0000.slatlas/v0.png': base64Decode(pngBase64),
    };

    Future<Uint8List> load(String path) async =>
        Uint8List.fromList(files[path]!);

    final package = await SloomPackage.open(
      manifestPath: 'manifest.json',
      load: load,
    );
    final atlas = SloomAtlas(package, load: load);
    await tester.runAsync(() => atlas.preload([0]));

    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: SloomPlayer(
          atlas: atlas,
          animationUri: 'anim://p/run',
        ),
      ),
    );

    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(tester.takeException(), isNull);
    expect(find.byType(SloomPlayer), findsOneWidget);
    expect(atlas.imageIfLoaded(0), isNotNull);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump();
    atlas.dispose();
  });

  testWidgets('plays without waiting for every animation page', (tester) async {
    final blockedPage = Completer<Uint8List>();
    final files = <String, List<int>>{
      'manifest.json': utf8.encode(jsonEncode(twoPageManifest)),
      'page/0000.slatlas/container.json': utf8.encode(jsonEncode(container)),
      'page/0001.slatlas/container.json': utf8.encode(jsonEncode(container)),
      'anim/run.anim.json': utf8.encode(jsonEncode(twoPageAnimation)),
      'page/0000.slatlas/v0.png': base64Decode(pngBase64),
    };

    Future<Uint8List> load(String path) {
      if (path == 'page/0001.slatlas/v1.png') {
        return blockedPage.future;
      }
      return Future.value(Uint8List.fromList(files[path]!));
    }

    final package = await SloomPackage.open(
      manifestPath: 'manifest.json',
      load: load,
    );
    final atlas = SloomAtlas(package, load: load);
    await tester.runAsync(() => atlas.image(0));

    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: SloomPlayer(
          atlas: atlas,
          animationUri: 'anim://p/run',
        ),
      ),
    );

    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(tester.takeException(), isNull);
    expect(find.byType(CustomPaint), findsOneWidget);
    expect(atlas.isLoaded(0), isTrue);
    expect(atlas.isLoaded(1), isFalse);

    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pump();
    atlas.dispose();
  });
}

const pngBase64 =
    'iVBORw0KGgoAAAANSUhEUgAAAAQAAAAECAYAAACp8Z5+AAAAFUlEQVR4nGM8UaHxnwEJMCFziBMAAJ6EAm8uG2W+AAAAAElFTkSuQmCC';

const manifest = {
  'format': 'sprite-loom',
  'format-major': 1,
  'format-minor': 0,
  'uris': [
    'anim://p/run',
    'page://p/0000',
    'frame://p/run/a',
  ],
  'pages': [
    {
      'uri': 1,
      'container': 'page/0000.slatlas/container.json',
      'size': [4, 4],
      'pixel-format': 'rgba8888',
      'variants': [
        {
          'id': 'png',
          'codec': 'png',
          'file': 'page/0000.slatlas/v0.png',
          'digest': {'alg': 'xxh3-64', 'value': '00'},
        }
      ],
    }
  ],
  'frames': {
    'to-record': [0xffffffff, 0xffffffff, 0],
    'records': [
      {
        'page': 0,
        'stored': 0,
        'origin': [4, 4],
        'offset': [0, 0],
        'pivot': [32, 32],
        'flags': 1,
      },
    ],
  },
  'animations': [
    {
      'uri': 0,
      'file': 'anim/run.anim.json',
      'frame-count': 1,
    }
  ],
};

const container = {
  'format': 'sprite-loom/container',
  'page': 0,
  'size': [4, 4],
  'pixel-format': 'rgba8888',
  'alpha': 'straight',
  'variants': [],
  'stored': [
    {
      'rect': [0, 0, 4, 4],
      'rotation': 0,
      'digest': {'alg': 'xxh3-64', 'value': '00'},
    },
  ],
};

const animation = {
  'format': 'sprite-loom/animation',
  'uri': 'anim://p/run',
  'frames': [2],
  'durations-ms': [42],
  'loop': true,
  'playback': 'forward',
};

const twoPageManifest = {
  'format': 'sprite-loom',
  'format-major': 1,
  'format-minor': 0,
  'uris': [
    'anim://p/run',
    'page://p/0000',
    'frame://p/run/a',
    'page://p/0001',
    'frame://p/run/b',
  ],
  'pages': [
    {
      'uri': 1,
      'container': 'page/0000.slatlas/container.json',
      'size': [4, 4],
      'pixel-format': 'rgba8888',
      'variants': [
        {
          'id': 'png',
          'codec': 'png',
          'file': 'page/0000.slatlas/v0.png',
          'digest': {'alg': 'xxh3-64', 'value': '00'},
        }
      ],
    },
    {
      'uri': 3,
      'container': 'page/0001.slatlas/container.json',
      'size': [4, 4],
      'pixel-format': 'rgba8888',
      'variants': [
        {
          'id': 'png',
          'codec': 'png',
          'file': 'page/0001.slatlas/v1.png',
          'digest': {'alg': 'xxh3-64', 'value': '00'},
        }
      ],
    },
  ],
  'frames': {
    'to-record': [0xffffffff, 0xffffffff, 0, 0xffffffff, 1],
    'records': [
      {
        'page': 0,
        'stored': 0,
        'origin': [4, 4],
        'offset': [0, 0],
        'pivot': [32, 32],
        'flags': 1,
      },
      {
        'page': 1,
        'stored': 0,
        'origin': [4, 4],
        'offset': [0, 0],
        'pivot': [32, 32],
        'flags': 1,
      },
    ],
  },
  'animations': [
    {
      'uri': 0,
      'file': 'anim/run.anim.json',
      'frame-count': 2,
    }
  ],
};

const twoPageAnimation = {
  'format': 'sprite-loom/animation',
  'uri': 'anim://p/run',
  'frames': [2, 4],
  'durations-ms': [500, 500],
  'loop': true,
  'playback': 'forward',
};
