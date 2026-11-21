import 'dart:convert';
import 'dart:typed_data';

import 'package:sprite_loom/sprite_loom.dart';
import 'package:test/test.dart';

void main() {
  test('reads frames and animation from a package', () async {
    final files = <String, String>{
      'manifest.json': jsonEncode(manifest),
      'page/0000.slatlas/container.json': jsonEncode(container),
      'anim/run.anim.json': jsonEncode(animation),
    };
    final package = await SloomPackage.open(
      manifestPath: 'manifest.json',
      load: (path) async => Uint8List.fromList(utf8.encode(files[path]!)),
    );

    final frame = package.frame('frame://p/run/a');
    expect(frame.rect.x, 1);
    expect(frame.rect.y, 2);
    expect(frame.originWidth, 4);
    expect(frame.offsetX, 1);
    expect(frame.pivotX, 2);

    final animationValue = await package.animation('anim://p/run');
    expect(animationValue.frameUris, ['frame://p/run/a', 'frame://p/run/b']);
    expect(animationValue.durationsMs, [42, 42]);
    expect(animationValue.totalDurationMs, 84);
  });

  test('rejects a non-frame URI', () async {
    final package = SloomPackage.fromJson(
      manifest,
      {'page/0000.slatlas/container.json': container},
    );

    expect(() => package.frame('page://p/0000'), throwsStateError);
  });
}

const manifest = {
  'format': 'sprite-loom',
  'format-major': 1,
  'format-minor': 0,
  'uris': [
    'anim://p/run',
    'page://p/0000',
    'frame://p/run/a',
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
    }
  ],
  'frames': {
    'to-record': [0xffffffff, 0xffffffff, 0, 1],
    'records': [
      {
        'page': 0,
        'stored': 0,
        'origin': [4, 4],
        'offset': [1, 1],
        'pivot': [32, 32],
        'flags': 1,
      },
      {
        'page': 0,
        'stored': 1,
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

const container = {
  'format': 'sprite-loom/container',
  'page': 0,
  'size': [4, 4],
  'pixel-format': 'rgba8888',
  'alpha': 'straight',
  'variants': [],
  'stored': [
    {
      'rect': [1, 2, 2, 2],
      'rotation': 0,
      'digest': {'alg': 'xxh3-64', 'value': '00'},
    },
    {
      'rect': [0, 0, 4, 4],
      'rotation': 0,
      'digest': {'alg': 'xxh3-64', 'value': '01'},
    },
  ],
};

const animation = {
  'format': 'sprite-loom/animation',
  'uri': 'anim://p/run',
  'frames': [2, 3],
  'durations-ms': [42, 42],
  'loop': true,
  'playback': 'forward',
};
