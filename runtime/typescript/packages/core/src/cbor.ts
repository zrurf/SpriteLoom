export function decodeCbor(bytes: Uint8Array): unknown {
  const reader = new CborReader(bytes);
  const value = reader.readValue(0);
  if (!reader.atEnd) {
    throw new Error("CBOR payload has trailing data.");
  }
  return value;
}

class CborReader {
  private offset = 0;

  constructor(private readonly bytes: Uint8Array) {}

  get atEnd(): boolean {
    return this.offset >= this.bytes.length;
  }

  readValue(depth: number): unknown {
    if (depth > 64) {
      throw new Error("CBOR payload is nested too deeply.");
    }

    const initial = this.readByte();
    const major = initial >> 5;
    const additional = initial & 0x1f;

    switch (major) {
      case 0:
        return this.readLength(additional);
      case 1:
        return -1 - this.readLength(additional);
      case 2:
        return this.readBytes(additional);
      case 3:
        return this.readText(additional);
      case 4:
        return this.readArray(additional, depth);
      case 5:
        return this.readMap(additional, depth);
      case 6:
        this.readLength(additional);
        return this.readValue(depth + 1);
      case 7:
        return this.readSimple(additional);
      default:
        throw new Error(`Unsupported CBOR major type: ${major}`);
    }
  }

  private readLength(additional: number): number {
    if (additional < 24) {
      return additional;
    }

    switch (additional) {
      case 24:
        return this.readByte();
      case 25:
        return this.readUint(2);
      case 26:
        return this.readUint(4);
      case 27:
        return this.readUint(8);
      case 31:
        throw new Error("Indefinite length is not valid here.");
      default:
        throw new Error(`Unsupported CBOR additional information: ${additional}`);
    }
  }

  private readBytes(additional: number): Uint8Array {
    if (additional === 31) {
      const chunks: Uint8Array[] = [];
      while (this.peekByte() !== 0xff) {
        const chunk = this.readValue(0);
        if (!(chunk instanceof Uint8Array)) {
          throw new Error("Indefinite byte string contains a non-byte chunk.");
        }
        chunks.push(chunk);
      }
      this.offset++;
      return concatBytes(chunks);
    }

    const length = this.readLength(additional);
    this.require(length);
    const value = this.bytes.slice(this.offset, this.offset + length);
    this.offset += length;
    return value;
  }

  private readText(additional: number): string {
    if (additional === 31) {
      let value = "";
      while (this.peekByte() !== 0xff) {
        const chunk = this.readValue(0);
        if (typeof chunk !== "string") {
          throw new Error("Indefinite text string contains a non-text chunk.");
        }
        value += chunk;
      }
      this.offset++;
      return value;
    }

    const length = this.readLength(additional);
    this.require(length);
    const value = new TextDecoder().decode(
      this.bytes.subarray(this.offset, this.offset + length),
    );
    this.offset += length;
    return value;
  }

  private readArray(additional: number, depth: number): unknown[] {
    const values: unknown[] = [];
    if (additional === 31) {
      while (this.peekByte() !== 0xff) {
        values.push(this.readValue(depth + 1));
      }
      this.offset++;
      return values;
    }

    const length = this.readLength(additional);
    for (let index = 0; index < length; index++) {
      values.push(this.readValue(depth + 1));
    }
    return values;
  }

  private readMap(additional: number, depth: number): Record<string, unknown> {
    const values: Record<string, unknown> = {};
    const readEntry = () => {
      const key = this.readValue(depth + 1);
      if (typeof key !== "string") {
        throw new Error("CBOR map keys must be text strings.");
      }
      values[key] = this.readValue(depth + 1);
    };

    if (additional === 31) {
      while (this.peekByte() !== 0xff) {
        readEntry();
      }
      this.offset++;
      return values;
    }

    const length = this.readLength(additional);
    for (let index = 0; index < length; index++) {
      readEntry();
    }
    return values;
  }

  private readSimple(additional: number): unknown {
    switch (additional) {
      case 20:
        return false;
      case 21:
        return true;
      case 22:
        return null;
      case 25:
        return readFloat16(this.readUint(2));
      case 26:
        return this.readFloat(4);
      case 27:
        return this.readFloat(8);
      default:
        throw new Error(`Unsupported CBOR simple value: ${additional}`);
    }
  }

  private readFloat(length: number): number {
    this.require(length);
    const view = new DataView(
      this.bytes.buffer,
      this.bytes.byteOffset + this.offset,
      length,
    );
    this.offset += length;
    return length === 4 ? view.getFloat32(0) : view.getFloat64(0);
  }

  private readUint(length: number): number {
    this.require(length);
    let value = 0;
    for (let index = 0; index < length; index++) {
      value = value * 256 + this.bytes[this.offset++];
    }
    return value;
  }

  private readByte(): number {
    this.require(1);
    return this.bytes[this.offset++];
  }

  private peekByte(): number {
    this.require(1);
    return this.bytes[this.offset];
  }

  private require(length: number): void {
    if (this.offset + length > this.bytes.length) {
      throw new Error("CBOR payload is truncated.");
    }
  }
}

function readFloat16(value: number): number {
  const sign = (value & 0x8000) !== 0 ? -1 : 1;
  const exponent = (value >> 10) & 0x1f;
  const mantissa = value & 0x3ff;
  if (exponent === 0) {
    return sign * mantissa * 2 ** -24;
  }
  if (exponent === 31) {
    return mantissa === 0 ? sign * Infinity : Number.NaN;
  }
  return sign * (1 + mantissa / 1024) * 2 ** (exponent - 15);
}

function concatBytes(chunks: Uint8Array[]): Uint8Array {
  const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
  const output = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    output.set(chunk, offset);
    offset += chunk.length;
  }
  return output;
}
