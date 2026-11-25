import 'dart:convert';
import 'dart:typed_data';

Object? decodeCbor(Uint8List bytes) {
  final reader = _CborReader(bytes);
  final value = reader.readValue(0);
  if (!reader.isAtEnd) {
    throw const FormatException('CBOR payload has trailing data.');
  }
  return value;
}

class _CborReader {
  _CborReader(this._bytes);

  final Uint8List _bytes;
  int _offset = 0;

  bool get isAtEnd => _offset >= _bytes.length;

  Object? readValue(int depth) {
    if (depth > 64) {
      throw const FormatException('CBOR payload is nested too deeply.');
    }

    final initial = _readByte();
    final major = initial >> 5;
    final additional = initial & 0x1f;

    switch (major) {
      case 0:
        return _readLength(additional);
      case 1:
        return -1 - _readLength(additional);
      case 2:
        return _readBytes(additional);
      case 3:
        return _readText(additional);
      case 4:
        return _readArray(additional, depth);
      case 5:
        return _readMap(additional, depth);
      case 6:
        _readLength(additional);
        return readValue(depth + 1);
      case 7:
        return _readSimple(additional);
      default:
        throw FormatException('Unsupported CBOR major type: $major');
    }
  }

  int _readLength(int additional) {
    if (additional < 24) {
      return additional;
    }

    switch (additional) {
      case 24:
        return _readByte();
      case 25:
        return _readUint(2);
      case 26:
        return _readUint(4);
      case 27:
        return _readUint(8);
      case 31:
        throw const FormatException('Indefinite length is not valid here.');
      default:
        throw FormatException(
            'Unsupported CBOR additional information: $additional');
    }
  }

  Uint8List _readBytes(int additional) {
    if (additional == 31) {
      final builder = BytesBuilder(copy: false);
      while (_peekByte() != 0xff) {
        final chunk = readValue(0);
        if (chunk is! Uint8List) {
          throw const FormatException(
              'Indefinite byte string contains a non-byte chunk.');
        }
        builder.add(chunk);
      }
      _offset++;
      return builder.takeBytes();
    }

    final length = _readLength(additional);
    _require(length);
    final value = Uint8List.fromList(_bytes.sublist(_offset, _offset + length));
    _offset += length;
    return value;
  }

  String _readText(int additional) {
    if (additional == 31) {
      final buffer = StringBuffer();
      while (_peekByte() != 0xff) {
        final chunk = readValue(0);
        if (chunk is! String) {
          throw const FormatException(
              'Indefinite text string contains a non-text chunk.');
        }
        buffer.write(chunk);
      }
      _offset++;
      return buffer.toString();
    }

    final length = _readLength(additional);
    _require(length);
    final value = utf8.decode(_bytes.sublist(_offset, _offset + length));
    _offset += length;
    return value;
  }

  List<Object?> _readArray(int additional, int depth) {
    final values = <Object?>[];
    if (additional == 31) {
      while (_peekByte() != 0xff) {
        values.add(readValue(depth + 1));
      }
      _offset++;
      return values;
    }

    final length = _readLength(additional);
    for (var index = 0; index < length; index++) {
      values.add(readValue(depth + 1));
    }
    return values;
  }

  Map<String, dynamic> _readMap(int additional, int depth) {
    final values = <String, dynamic>{};
    if (additional == 31) {
      while (_peekByte() != 0xff) {
        final key = readValue(depth + 1);
        if (key is! String) {
          throw const FormatException('CBOR map keys must be text strings.');
        }
        values[key] = readValue(depth + 1);
      }
      _offset++;
      return values;
    }

    final length = _readLength(additional);
    for (var index = 0; index < length; index++) {
      final key = readValue(depth + 1);
      if (key is! String) {
        throw const FormatException('CBOR map keys must be text strings.');
      }
      values[key] = readValue(depth + 1);
    }
    return values;
  }

  Object? _readSimple(int additional) {
    switch (additional) {
      case 20:
        return false;
      case 21:
        return true;
      case 22:
        return null;
      case 25:
        return _readFloat16();
      case 26:
        final data = ByteData.sublistView(_take(4));
        return data.getFloat32(0, Endian.big);
      case 27:
        final data = ByteData.sublistView(_take(8));
        return data.getFloat64(0, Endian.big);
      default:
        throw FormatException('Unsupported CBOR simple value: $additional');
    }
  }

  double _readFloat16() {
    final value = _readUint(2);
    final sign = (value & 0x8000) != 0 ? -1.0 : 1.0;
    final exponent = (value >> 10) & 0x1f;
    final mantissa = value & 0x3ff;

    if (exponent == 0) {
      return sign * mantissa * 0.00006103515625;
    }

    if (exponent == 31) {
      return mantissa == 0 ? sign * double.infinity : double.nan;
    }

    return sign * (1 + mantissa / 1024) * _pow2(exponent - 15);
  }

  double _pow2(int exponent) {
    var value = 1.0;
    if (exponent > 0) {
      for (var index = 0; index < exponent; index++) {
        value *= 2;
      }
    } else {
      for (var index = 0; index < -exponent; index++) {
        value /= 2;
      }
    }
    return value;
  }

  int _readUint(int length) {
    _require(length);
    var value = 0;
    for (var index = 0; index < length; index++) {
      value = (value << 8) | _bytes[_offset++];
    }
    return value;
  }

  int _readByte() {
    _require(1);
    return _bytes[_offset++];
  }

  int _peekByte() {
    _require(1);
    return _bytes[_offset];
  }

  Uint8List _take(int length) {
    _require(length);
    final value = Uint8List.sublistView(_bytes, _offset, _offset + length);
    _offset += length;
    return value;
  }

  void _require(int length) {
    if (_offset + length > _bytes.length) {
      throw const FormatException('CBOR payload is truncated.');
    }
  }
}
