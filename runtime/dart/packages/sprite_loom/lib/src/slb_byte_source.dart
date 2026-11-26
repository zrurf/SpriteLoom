import 'dart:typed_data';

/// Random-access storage for an SLB archive.
abstract interface class SloomSlbByteSource {
  int get length;

  /// Diagnostic label: `memory`, `file`, or `mmap`.
  String get kind;

  Future<Uint8List> read(int offset, int length);

  Future<void> close();
}

final class SloomSlbMemorySource implements SloomSlbByteSource {
  SloomSlbMemorySource(Uint8List bytes) : _bytes = bytes;

  final Uint8List _bytes;
  bool _closed = false;

  @override
  int get length => _bytes.length;

  @override
  String get kind => 'memory';

  @override
  Future<Uint8List> read(int offset, int length) async {
    if (_closed) {
      throw StateError('SLB byte source is closed.');
    }

    RangeError.checkValidRange(offset, offset + length, _bytes.length);
    return Uint8List.sublistView(_bytes, offset, offset + length);
  }

  @override
  Future<void> close() async {
    _closed = true;
  }
}
