import { gcm } from "@noble/ciphers/aes.js";
import { xchacha20poly1305 } from "@noble/ciphers/chacha.js";
import { hkdf } from "@noble/hashes/hkdf.js";
import { sha256 } from "@noble/hashes/sha2.js";

export class SloomSymmetricKey {
  readonly bytes: Uint8Array;

  constructor(bytes: Uint8Array | number[]) {
    this.bytes = new Uint8Array(bytes);
    if (this.bytes.length !== 32) {
      throw new Error("Symmetric keys must be 32 bytes.");
    }
  }

  static fromHex(value: string): SloomSymmetricKey {
    const text = value.trim();
    const normalized = text.startsWith("0x") || text.startsWith("0X")
      ? text.slice(2)
      : text;
    if (normalized.length !== 64) {
      throw new Error("Symmetric key must contain 64 hex characters.");
    }

    const bytes = new Uint8Array(32);
    for (let index = 0; index < bytes.length; index++) {
      const byte = Number.parseInt(
        normalized.slice(index * 2, index * 2 + 2),
        16,
      );
      if (!Number.isFinite(byte)) {
        throw new Error("Symmetric key contains invalid hex.");
      }
      bytes[index] = byte;
    }
    return new SloomSymmetricKey(bytes);
  }
}

type AeadAlgorithm = "aes-256-gcm" | "xchacha20-poly1305";

const tagSize = 16;

export function decryptSenc(file: Uint8Array, key: SloomSymmetricKey): Uint8Array {
  if (file.length < 36 || !hasMagic(file, [0x53, 0x45, 0x4e, 0x43])) {
    throw new Error("SENC header is missing or truncated.");
  }

  const data = new DataView(file.buffer, file.byteOffset, file.byteLength);
  if (file[4] !== 1) {
    throw new Error(`Unsupported SENC version: ${file[4]}`);
  }

  const algorithm = parseAlgorithm(file[5]);
  const chunkSize = data.getUint32(8, true);
  const chunkCount = data.getUint32(28, true);
  if (chunkCount === 0) {
    throw new Error("SENC file contains no chunks.");
  }

  const prefixLength = algorithm === "aes-256-gcm" ? 4 : 16;
  const prefix = file.slice(12, 12 + prefixLength);
  const body = file.subarray(36);
  const fullChunks = chunkCount - 1;
  const fullBytes = fullChunks * (chunkSize + tagSize);
  if (body.length < fullBytes + tagSize) {
    throw new Error("SENC payload is truncated.");
  }

  const lastPlaintext = body.length - fullBytes - tagSize;
  if (lastPlaintext < 0 || lastPlaintext > chunkSize) {
    throw new Error("SENC payload has an invalid final chunk.");
  }

  return decryptChunks(
    body,
    key.bytes,
    algorithm,
    prefix,
    chunkSize,
    chunkCount,
    fullChunks * chunkSize + lastPlaintext,
  );
}

export function decryptSlbBlock(options: {
  stored: Uint8Array;
  rawSize: number;
  chunkCount: number;
  key: SloomSymmetricKey;
  algorithm: string;
  salt: Uint8Array;
  noncePrefix: Uint8Array;
  chunkSize: number;
  keyId: number;
  blockId: number;
}): Uint8Array {
  const algorithm = parseAlgorithmName(options.algorithm);
  const info = new Uint8Array(15);
  info.set(new TextEncoder().encode("slb-block"), 0);
  const infoView = new DataView(info.buffer);
  infoView.setUint16(9, options.keyId, true);
  infoView.setUint32(11, options.blockId, true);
  const blockKey = hkdf(
    sha256,
    options.key.bytes,
    options.salt,
    info,
    32,
  );

  return decryptChunks(
    options.stored,
    blockKey,
    algorithm,
    options.noncePrefix,
    options.chunkSize,
    options.chunkCount,
    options.rawSize,
  );
}

function decryptChunks(
  ciphertext: Uint8Array,
  key: Uint8Array,
  algorithm: AeadAlgorithm,
  noncePrefix: Uint8Array,
  chunkSize: number,
  chunkCount: number,
  plaintextLength: number,
): Uint8Array {
  if (chunkSize <= 0 || chunkSize > 1024 * 1024) {
    throw new Error("Invalid AEAD chunk size.");
  }
  if (ciphertext.length < plaintextLength + chunkCount * tagSize) {
    throw new Error("Encrypted payload is truncated.");
  }

  const output = new Uint8Array(plaintextLength);
  let inputOffset = 0;
  let outputOffset = 0;
  let remaining = plaintextLength;

  for (let index = 0; index < chunkCount; index++) {
    const currentPlaintext = Math.min(chunkSize, remaining);
    const currentCiphertext = currentPlaintext + tagSize;
    const nonce = new Uint8Array(noncePrefix.length + 8);
    nonce.set(noncePrefix);
    new DataView(nonce.buffer).setBigUint64(
      noncePrefix.length,
      BigInt(index),
      true,
    );
    const chunk = ciphertext.slice(inputOffset, inputOffset + currentCiphertext);
    const cipher = algorithm === "aes-256-gcm"
      ? gcm(key, nonce)
      : xchacha20poly1305(key, nonce);
    const plain = cipher.decrypt(chunk);
    output.set(plain, outputOffset);
    inputOffset += currentCiphertext;
    outputOffset += currentPlaintext;
    remaining -= currentPlaintext;
  }

  return output;
}

function parseAlgorithm(value: number): AeadAlgorithm {
  switch (value) {
    case 1:
      return "aes-256-gcm";
    case 2:
      return "xchacha20-poly1305";
    default:
      throw new Error(`Unsupported SENC algorithm: ${value}`);
  }
}

function parseAlgorithmName(value: string): AeadAlgorithm {
  if (value === "aes-256-gcm" || value === "xchacha20-poly1305") {
    return value;
  }
  throw new Error(`Unsupported SLB encryption algorithm: ${value}`);
}

function hasMagic(bytes: Uint8Array, magic: number[]): boolean {
  return magic.every((value, index) => bytes[index] === value);
}
