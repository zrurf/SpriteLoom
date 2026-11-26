import 'dart:convert';
import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';

import 'slb_byte_source.dart';

/// Opens a local SLB file, preferring a read-only memory map.
///
/// If mmap is unavailable or fails, a queued random-access file source is
/// returned instead.
Future<SloomSlbByteSource> openSlbFileSource(
  String path, {
  bool preferMmap = true,
}) async {
  if (preferMmap) {
    try {
      return await SloomSlbMmapSource.open(path);
    } on Object {
      // Fall through to random-access reads.
    }
  }

  return SloomSlbFileSource.open(path);
}

final class SloomSlbFileSource implements SloomSlbByteSource {
  SloomSlbFileSource._(this._file, this._length);

  final RandomAccessFile _file;
  final int _length;
  Future<void> _tail = Future<void>.value();
  bool _closed = false;

  static Future<SloomSlbFileSource> open(String path) async {
    final file = await File(path).open(mode: FileMode.read);
    try {
      return SloomSlbFileSource._(file, await file.length());
    } catch (_) {
      await file.close();
      rethrow;
    }
  }

  @override
  int get length => _length;

  @override
  String get kind => 'file';

  @override
  Future<Uint8List> read(int offset, int length) {
    if (_closed) {
      return Future.error(StateError('SLB byte source is closed.'));
    }

    final request = _tail.then((_) async {
      RangeError.checkValidRange(offset, offset + length, _length);
      await _file.setPosition(offset);
      final output = Uint8List(length);
      var read = 0;
      while (read < length) {
        final chunk = await _file.read(length - read);
        if (chunk.isEmpty) {
          throw FormatException(
            'SLB file ended at ${offset + read}, expected '
            '${offset + length}.',
          );
        }
        output.setRange(read, read + chunk.length, chunk);
        read += chunk.length;
      }
      return output;
    });

    _tail = request.then<void>((_) {}, onError: (Object _) {});
    return request;
  }

  @override
  Future<void> close() async {
    if (_closed) {
      return;
    }

    _closed = true;
    await _tail;
    await _file.close();
  }
}

final class SloomSlbMmapSource implements SloomSlbByteSource {
  SloomSlbMmapSource._(this._length, this._view, this._region);

  final int _length;
  Uint8List? _view;
  final _MappedRegion _region;
  bool _closed = false;

  static final Finalizer<_MappedRegion> _finalizer = Finalizer<_MappedRegion>(
    (region) => region.close(),
  );

  static Future<SloomSlbMmapSource> open(String path) async {
    final length = await File(path).length();
    if (length <= 0) {
      throw const FormatException('Cannot map an empty SLB file.');
    }

    final region = Platform.isWindows
        ? _WindowsMappedRegion.open(path, length)
        : _PosixMappedRegion.open(path, length);
    final source = SloomSlbMmapSource._(
      length,
      region.address.cast<Uint8>().asTypedList(length),
      region,
    );
    _finalizer.attach(source, region, detach: source);
    return source;
  }

  @override
  int get length => _length;

  @override
  String get kind => 'mmap';

  @override
  Future<Uint8List> read(int offset, int length) async {
    if (_closed) {
      throw StateError('SLB byte source is closed.');
    }

    RangeError.checkValidRange(offset, offset + length, _length);
    // Return owned bytes so a later close cannot unmap data in use.
    return Uint8List.fromList(
      Uint8List.sublistView(_view!, offset, offset + length),
    );
  }

  @override
  Future<void> close() async {
    if (_closed) {
      return;
    }

    _closed = true;
    _view = null;
    _finalizer.detach(this);
    _region.close();
  }
}

abstract interface class _MappedRegion {
  Pointer<Void> get address;

  int get length;

  void close();
}

final class _PosixMappedRegion implements _MappedRegion {
  _PosixMappedRegion(this.address, this.length, this._munmap);

  @override
  final Pointer<Void> address;

  @override
  final int length;

  final int Function(Pointer<Void>, int) _munmap;
  bool _closed = false;

  static final DynamicLibrary _libc = _openLibc();

  static DynamicLibrary _openLibc() {
    if (Platform.isAndroid) {
      return DynamicLibrary.open('libc.so');
    }
    if (Platform.isLinux) {
      return DynamicLibrary.open('libc.so.6');
    }
    return DynamicLibrary.process();
  }

  static _PosixMappedRegion open(String path, int length) {
    final malloc = _libc
        .lookupFunction<
          Pointer<Uint8> Function(IntPtr),
          Pointer<Uint8> Function(int)
        >('malloc');
    final free = _libc
        .lookupFunction<
          Void Function(Pointer<Uint8>),
          void Function(Pointer<Uint8>)
        >('free');
    final fopen = _libc
        .lookupFunction<
          Pointer<Void> Function(Pointer<Uint8>, Pointer<Uint8>),
          Pointer<Void> Function(Pointer<Uint8>, Pointer<Uint8>)
        >('fopen');
    final fileno = _libc
        .lookupFunction<
          Int32 Function(Pointer<Void>),
          int Function(Pointer<Void>)
        >('fileno');
    final fclose = _libc
        .lookupFunction<
          Int32 Function(Pointer<Void>),
          int Function(Pointer<Void>)
        >('fclose');
    final mmap = _libc
        .lookupFunction<
          Pointer<Void> Function(
            Pointer<Void>,
            IntPtr,
            Int32,
            Int32,
            Int32,
            IntPtr,
          ),
          Pointer<Void> Function(Pointer<Void>, int, int, int, int, int)
        >('mmap');
    final munmap = _libc
        .lookupFunction<
          Int32 Function(Pointer<Void>, IntPtr),
          int Function(Pointer<Void>, int)
        >('munmap');

    final pathBytes = utf8.encode(path);
    final pathPointer = malloc(pathBytes.length + 1);
    final modePointer = malloc(3);
    if (pathPointer == nullptr || modePointer == nullptr) {
      free(pathPointer);
      free(modePointer);
      throw StateError('Unable to allocate mmap path.');
    }

    try {
      pathPointer.asTypedList(pathBytes.length + 1)
        ..setAll(0, pathBytes)
        ..[pathBytes.length] = 0;
      modePointer.asTypedList(3).setAll(0, const [0x72, 0x62, 0]);

      final file = fopen(pathPointer, modePointer);
      if (file == nullptr) {
        throw FileSystemException('Unable to open file for mmap.', path);
      }

      try {
        final descriptor = fileno(file);
        if (descriptor < 0) {
          throw FileSystemException('Unable to get file descriptor.', path);
        }

        const protRead = 1;
        const mapPrivate = 2;
        final address = mmap(
          nullptr,
          length,
          protRead,
          mapPrivate,
          descriptor,
          0,
        );
        if (address.address == -1) {
          throw FileSystemException('mmap failed.', path);
        }

        return _PosixMappedRegion(address, length, munmap);
      } finally {
        fclose(file);
      }
    } finally {
      free(pathPointer);
      free(modePointer);
    }
  }

  @override
  void close() {
    if (_closed) {
      return;
    }

    _closed = true;
    _munmap(address, length);
  }
}

final class _WindowsMappedRegion implements _MappedRegion {
  _WindowsMappedRegion(this.address, this.length, this._unmap);

  @override
  final Pointer<Void> address;

  @override
  final int length;

  final int Function(Pointer<Void>) _unmap;
  bool _closed = false;

  static final DynamicLibrary _kernel32 = DynamicLibrary.open('kernel32.dll');

  static _WindowsMappedRegion open(String path, int length) {
    final getProcessHeap = _kernel32
        .lookupFunction<Pointer<Void> Function(), Pointer<Void> Function()>(
          'GetProcessHeap',
        );
    final heapAlloc = _kernel32
        .lookupFunction<
          Pointer<Uint8> Function(Pointer<Void>, Uint32, IntPtr),
          Pointer<Uint8> Function(Pointer<Void>, int, int)
        >('HeapAlloc');
    final heapFree = _kernel32
        .lookupFunction<
          Int32 Function(Pointer<Void>, Uint32, Pointer<Uint8>),
          int Function(Pointer<Void>, int, Pointer<Uint8>)
        >('HeapFree');
    final createFile = _kernel32
        .lookupFunction<
          Pointer<Void> Function(
            Pointer<Uint16>,
            Uint32,
            Uint32,
            Pointer<Void>,
            Uint32,
            Uint32,
            Pointer<Void>,
          ),
          Pointer<Void> Function(
            Pointer<Uint16>,
            int,
            int,
            Pointer<Void>,
            int,
            int,
            Pointer<Void>,
          )
        >('CreateFileW');
    final createMapping = _kernel32
        .lookupFunction<
          Pointer<Void> Function(
            Pointer<Void>,
            Pointer<Void>,
            Uint32,
            Uint32,
            Uint32,
            Pointer<Uint16>,
          ),
          Pointer<Void> Function(
            Pointer<Void>,
            Pointer<Void>,
            int,
            int,
            int,
            Pointer<Uint16>,
          )
        >('CreateFileMappingW');
    final mapView = _kernel32
        .lookupFunction<
          Pointer<Void> Function(Pointer<Void>, Uint32, Uint32, Uint32, IntPtr),
          Pointer<Void> Function(Pointer<Void>, int, int, int, int)
        >('MapViewOfFile');
    final unmapView = _kernel32
        .lookupFunction<
          Int32 Function(Pointer<Void>),
          int Function(Pointer<Void>)
        >('UnmapViewOfFile');
    final closeHandle = _kernel32
        .lookupFunction<
          Int32 Function(Pointer<Void>),
          int Function(Pointer<Void>)
        >('CloseHandle');

    final heap = getProcessHeap();
    final units = path.codeUnits;
    final pathPointer = heapAlloc(heap, 0, (units.length + 1) * 2);
    if (pathPointer == nullptr) {
      throw StateError('Unable to allocate mmap path.');
    }

    try {
      pathPointer.cast<Uint16>().asTypedList(units.length + 1)
        ..setAll(0, units)
        ..[units.length] = 0;

      const genericRead = 0x80000000;
      const shareAll = 0x00000007;
      const openExisting = 3;
      const fileAttributeNormal = 0x80;
      final file = createFile(
        pathPointer.cast<Uint16>(),
        genericRead,
        shareAll,
        nullptr,
        openExisting,
        fileAttributeNormal,
        nullptr,
      );
      if (file.address == -1) {
        throw FileSystemException('CreateFileW failed.', path);
      }

      try {
        const pageReadonly = 0x02;
        final mapping = createMapping(
          file,
          nullptr,
          pageReadonly,
          length >> 32,
          length & 0xffffffff,
          nullptr,
        );
        if (mapping == nullptr) {
          throw FileSystemException('CreateFileMappingW failed.', path);
        }

        try {
          const fileMapRead = 0x0004;
          final address = mapView(mapping, fileMapRead, 0, 0, length);
          if (address == nullptr) {
            throw FileSystemException('MapViewOfFile failed.', path);
          }

          return _WindowsMappedRegion(address, length, unmapView);
        } finally {
          closeHandle(mapping);
        }
      } finally {
        closeHandle(file);
      }
    } finally {
      heapFree(heap, 0, pathPointer);
    }
  }

  @override
  void close() {
    if (_closed) {
      return;
    }

    _closed = true;
    _unmap(address);
  }
}
