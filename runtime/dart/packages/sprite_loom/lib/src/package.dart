import 'dart:convert';
import 'dart:typed_data';

import 'cbor.dart';
import 'crypto.dart';
import 'decrypt_runner.dart';
import 'slb.dart';
import 'slb_file_source.dart';

typedef SloomFileLoader = Future<Uint8List> Function(String path);

class SloomRect {
  const SloomRect(this.x, this.y, this.width, this.height);

  factory SloomRect.fromList(Object? value) {
    final list = _intList(value, 4, 'rect');
    return SloomRect(
      list[0].toDouble(),
      list[1].toDouble(),
      list[2].toDouble(),
      list[3].toDouble(),
    );
  }

  final double x;
  final double y;
  final double width;
  final double height;

  double get right => x + width;
  double get bottom => y + height;

  @override
  String toString() => 'SloomRect($x, $y, $width, $height)';
}

class SloomSize {
  const SloomSize(this.width, this.height);

  factory SloomSize.fromList(Object? value) {
    final list = _intList(value, 2, 'size');
    return SloomSize(list[0].toDouble(), list[1].toDouble());
  }

  final double width;
  final double height;

  @override
  String toString() => 'SloomSize($width, $height)';
}

class SloomDigest {
  const SloomDigest(this.algorithm, this.value);

  final String algorithm;
  final String value;
}

class SloomVariant {
  const SloomVariant(this.id, this.codec, this.file, this.digest);

  final String id;
  final String codec;
  final String file;
  final SloomDigest digest;
}

class SloomStored {
  const SloomStored(this.rect, this.rotation, this.digest);

  final SloomRect rect;
  final int rotation;
  final SloomDigest digest;
}

class SloomPage {
  const SloomPage({
    required this.index,
    required this.uri,
    required this.container,
    required this.size,
    required this.pixelFormat,
    required this.variants,
    required this.stored,
  });

  final int index;
  final String uri;
  final String container;
  final SloomSize size;
  final String pixelFormat;
  final List<SloomVariant> variants;
  final List<SloomStored> stored;
}

class SloomFrame {
  const SloomFrame({
    required this.uri,
    required this.page,
    required this.pageUri,
    required this.rect,
    required this.originWidth,
    required this.originHeight,
    required this.offsetX,
    required this.offsetY,
    required this.pivotX,
    required this.pivotY,
    required this.rotated,
  });

  final String uri;
  final int page;
  final String pageUri;
  final SloomRect rect;
  final int originWidth;
  final int originHeight;
  final int offsetX;
  final int offsetY;
  final double pivotX;
  final double pivotY;
  final bool rotated;
}

class SloomAnimation {
  const SloomAnimation({
    required this.uri,
    required this.frameUris,
    required this.durationsMs,
    required this.loop,
    required this.playback,
  });

  final String uri;
  final List<String> frameUris;
  final List<int> durationsMs;
  final bool loop;
  final String playback;

  int get totalDurationMs => durationsMs.fold(0, (sum, value) => sum + value);
}

class SloomAnimationRef {
  const SloomAnimationRef(this.uri, this.file, this.frameCount);

  final String uri;
  final String file;
  final int frameCount;
}

class SloomPackage {
  SloomPackage._(
    this._manifest,
    this._containers,
    this._loader, {
    SloomSlbArchive? slb,
    SloomSymmetricKey? key,
  }) : _slb = slb,
       _key = key;

  static const int _notRecorded = 0xffffffff;

  final Map<String, dynamic> _manifest;
  final Map<String, Map<String, dynamic>> _containers;
  final SloomFileLoader? _loader;
  final SloomSlbArchive? _slb;
  final SloomSymmetricKey? _key;
  final Map<String, int> _uriIndices = {};
  final Map<int, SloomPage> _pageCache = {};

  bool get hasFileSource => _loader != null || _slb != null;

  /// `memory`, `file`, or `mmap` for SLB packages; null for directory packages.
  String? get storageKind => _slb?.storageKind;

  /// Releases a file-backed SLB source. Directory packages are unaffected.
  Future<void> close() async {
    await _slb?.close();
  }

  static Future<SloomPackage> open({
    required String manifestPath,
    required SloomFileLoader load,
    SloomSymmetricKey? key,
  }) async {
    final manifest = _decodeObject(
      await _readLogical(load, manifestPath, key),
      manifestPath,
    );
    final containers = <String, Map<String, dynamic>>{};

    for (final value in _mapList(manifest['pages'], 'pages')) {
      final container = _string(value['container'], 'container');
      containers[container] = _decodeObject(
        await _readLogical(load, container, key),
        container,
      );
    }

    final package = SloomPackage._(manifest, containers, load, key: key);
    package._validate();
    return package;
  }

  static Future<SloomPackage> openSlb({
    required String path,
    required SloomFileLoader load,
    SloomSymmetricKey? key,
  }) async {
    final archive = await readSlbLazy(await load(path), key: key);
    return _fromSlbArchive(archive, key);
  }

  /// Opens a local `.slb` file without loading its payload into memory.
  ///
  /// [preferMmap] uses a read-only memory map when available and falls back to
  /// queued random-access reads. Only the header, block table, index,
  /// manifest, page containers, and requested blocks are read.
  static Future<SloomPackage> openSlbFile(
    String path, {
    SloomSymmetricKey? key,
    bool preferMmap = true,
  }) async {
    final source = await openSlbFileSource(path, preferMmap: preferMmap);
    try {
      final archive = await readSlbSource(source, key: key);
      return await _fromSlbArchive(archive, key);
    } catch (_) {
      await source.close();
      rethrow;
    }
  }

  static Future<SloomPackage> _fromSlbArchive(
    SloomSlbArchive archive,
    SloomSymmetricKey? key,
  ) async {
    try {
      final containers = <String, Map<String, dynamic>>{};

      for (final value in _mapList(archive.manifest['pages'], 'pages')) {
        final container = _string(value['container'], 'container');
        if (!archive.contains(container)) {
          throw StateError('Container not found in SLB: $container');
        }
        containers[container] = _decodeObject(
          await archive.readFile(container, cache: false),
          container,
        );
      }

      final package = SloomPackage._(
        archive.manifest,
        containers,
        null,
        slb: archive,
        key: key,
      );
      package._validate();
      return package;
    } catch (_) {
      await archive.close();
      rethrow;
    }
  }

  static SloomPackage fromJson(
    Map<String, dynamic> manifest,
    Map<String, Map<String, dynamic>> containers,
  ) {
    final package = SloomPackage._(manifest, containers, null);
    package._validate();
    return package;
  }

  List<String> get uris => _stringList(_manifest['uris'], 'uris');

  int get pageCount => _mapList(_manifest['pages'], 'pages').length;

  List<SloomAnimationRef> get animationRefs {
    final values = _mapList(_manifest['animations'], 'animations');
    return [
      for (final value in values)
        SloomAnimationRef(
          _uriAt(_integer(value['uri'], 'animation uri')),
          _string(value['file'], 'animation file'),
          _integer(value['frame-count'], 'frame-count'),
        ),
    ];
  }

  SloomPage page(int index) {
    final cached = _pageCache[index];
    if (cached != null) {
      return cached;
    }

    final pages = _mapList(_manifest['pages'], 'pages');
    if (index < 0 || index >= pages.length) {
      throw StateError('Page index out of range: $index');
    }

    final value = pages[index];
    final containerPath = _string(value['container'], 'container');
    final container = _containers[containerPath];
    if (container == null) {
      throw StateError('Container not loaded: $containerPath');
    }

    final page = SloomPage(
      index: index,
      uri: _uriAt(_integer(value['uri'], 'page uri')),
      container: containerPath,
      size: SloomSize.fromList(value['size']),
      pixelFormat: _string(value['pixel-format'], 'pixel-format'),
      variants: [
        for (final variant in _mapList(value['variants'], 'variants'))
          SloomVariant(
            _string(variant['id'], 'variant id'),
            _string(variant['codec'], 'variant codec'),
            _string(variant['file'], 'variant file'),
            _digestValue(variant['digest']),
          ),
      ],
      stored: [
        for (final stored in _mapList(container['stored'], 'stored'))
          SloomStored(
            SloomRect.fromList(stored['rect']),
            _integer(stored['rotation'], 'rotation'),
            _digestValue(stored['digest']),
          ),
      ],
    );

    _pageCache[index] = page;
    return page;
  }

  SloomFrame frame(String uri) {
    final uriIndex = _indexOf(uri);
    final frames = _map(_manifest['frames'], 'frames');
    final toRecord = _intList(frames['to-record'], null, 'to-record');
    final recordIndex = toRecord[uriIndex];

    if (recordIndex == _notRecorded) {
      throw StateError('URI is not a frame: $uri');
    }

    final records = _mapList(frames['records'], 'records');
    final record = records[recordIndex];
    final pageIndex = _integer(record['page'], 'page');
    final pageValue = page(pageIndex);
    final storedIndex = _integer(record['stored'], 'stored');

    if (storedIndex < 0 || storedIndex >= pageValue.stored.length) {
      throw StateError('Stored frame not found: $storedIndex');
    }

    final stored = pageValue.stored[storedIndex];
    final origin = _intList(record['origin'], 2, 'origin');
    final offset = _intList(record['offset'], 2, 'offset');
    final pivot = _intList(record['pivot'], 2, 'pivot');

    return SloomFrame(
      uri: uri,
      page: pageIndex,
      pageUri: pageValue.uri,
      rect: stored.rect,
      originWidth: origin[0],
      originHeight: origin[1],
      offsetX: offset[0],
      offsetY: offset[1],
      pivotX: pivot[0] / 16.0,
      pivotY: pivot[1] / 16.0,
      rotated: stored.rotation != 0,
    );
  }

  Future<Uint8List> readFile(String path, {bool cache = true}) async {
    final slb = _slb;
    if (slb != null) {
      return slb.readFile(path, cache: cache);
    }

    final loader = _loader;
    if (loader == null) {
      throw StateError('File loading requires a package opened from a source.');
    }

    return _readLogical(loader, path, _key);
  }

  Future<SloomAnimation> animation(String uri) async {
    if (!hasFileSource) {
      throw StateError('Animation loading requires a package with a loader.');
    }

    SloomAnimationRef? reference;
    for (final candidate in animationRefs) {
      if (candidate.uri == uri) {
        reference = candidate;
        break;
      }
    }

    if (reference == null) {
      throw StateError('Unknown animation: $uri');
    }

    final document = _decodeObject(
      await readFile(reference.file),
      reference.file,
    );
    final frames = <String>[];
    for (final frame in _list(document['frames'], 'frames')) {
      if (frame is num) {
        frames.add(_uriAt(frame.toInt()));
      } else if (frame is String) {
        frames.add(frame);
      } else {
        throw FormatException('Animation frame must be an index or URI.');
      }
    }

    return SloomAnimation(
      uri: _string(document['uri'], 'animation uri'),
      frameUris: frames,
      durationsMs: _intList(document['durations-ms'], null, 'durations-ms'),
      loop: document['loop'] as bool? ?? true,
      playback: _string(document['playback'], 'playback'),
    );
  }

  void _validate() {
    if (_string(_manifest['format'], 'format') != 'sprite-loom') {
      throw const FormatException('Not a SpriteLoom manifest.');
    }

    final major = _integer(_manifest['format-major'], 'format-major');
    if (major != 1) {
      throw FormatException('Unsupported manifest major: $major');
    }

    for (var index = 0; index < uris.length; index++) {
      _uriIndices[uris[index]] = index;
    }
  }

  int _indexOf(String uri) {
    final index = _uriIndices[uri];
    if (index == null) {
      throw StateError('Unknown URI: $uri');
    }
    return index;
  }

  String _uriAt(int index) {
    final values = uris;
    if (index < 0 || index >= values.length) {
      throw FormatException('URI index out of range: $index');
    }
    return values[index];
  }
}

Map<String, dynamic> _decodeObject(Uint8List bytes, String source) {
  final logical = source.toLowerCase().endsWith('.senc')
      ? source.substring(0, source.length - 5)
      : source;
  final value = logical.toLowerCase().endsWith('.cbor')
      ? decodeCbor(bytes)
      : jsonDecode(utf8.decode(bytes));
  if (value is! Map<String, dynamic>) {
    throw FormatException('$source is not an object.');
  }
  return value;
}

Future<Uint8List> _readLogical(
  SloomFileLoader load,
  String path,
  SloomSymmetricKey? key,
) async {
  Uint8List? plain;
  Object? firstError;

  try {
    plain = await load(path);
  } catch (error) {
    firstError = error;
  }

  if (plain != null) {
    if (!_hasSencMagic(plain)) {
      return plain;
    }

    if (key == null) {
      throw StateError('SENC key required for $path.');
    }

    return decryptSencInBackground(plain, key.bytes);
  }

  final encryptedPath = '$path.senc';
  final encrypted = await load(encryptedPath);
  if (key == null) {
    throw StateError('SENC key required for $encryptedPath.');
  }

  if (firstError != null && !_hasSencMagic(encrypted)) {
    throw FormatException('$path and $encryptedPath are not valid SENC files.');
  }

  return decryptSencInBackground(encrypted, key.bytes);
}

bool _hasSencMagic(Uint8List bytes) =>
    bytes.length >= 4 &&
    bytes[0] == 0x53 &&
    bytes[1] == 0x45 &&
    bytes[2] == 0x4e &&
    bytes[3] == 0x43;

SloomDigest _digestValue(Object? value) {
  final map = _map(value, 'digest');
  final raw = map['value'];
  final digestValue = raw is String
      ? raw
      : raw is Uint8List
      ? _hex(raw)
      : throw const FormatException(
          'digest value must be a string or byte string.',
        );
  return SloomDigest(_string(map['alg'], 'digest alg'), digestValue);
}

String _hex(List<int> bytes) =>
    bytes.map((value) => value.toRadixString(16).padLeft(2, '0')).join();

Map<String, dynamic> _map(Object? value, String name) {
  if (value is! Map<String, dynamic>) {
    throw FormatException('$name must be an object.');
  }
  return value;
}

List<Object?> _list(Object? value, String name) {
  if (value is! List) {
    throw FormatException('$name must be an array.');
  }
  return value;
}

List<Map<String, dynamic>> _mapList(Object? value, String name) => [
  for (final item in _list(value, name)) _map(item, name),
];

List<int> _intList(Object? value, int? expected, String name) {
  final list = _list(value, name);
  if (expected != null && list.length != expected) {
    throw FormatException('$name must contain $expected values.');
  }
  return [
    for (final item in list)
      if (item is num)
        item.toInt()
      else
        throw FormatException('$name must contain numbers.'),
  ];
}

List<String> _stringList(Object? value, String name) {
  final list = _list(value, name);
  return [
    for (final item in list)
      if (item is String)
        item
      else
        throw FormatException('$name must contain strings.'),
  ];
}

String _string(Object? value, String name) {
  if (value is! String) {
    throw FormatException('$name must be a string.');
  }
  return value;
}

int _integer(Object? value, String name) {
  if (value is! num) {
    throw FormatException('$name must be a number.');
  }
  return value.toInt();
}
