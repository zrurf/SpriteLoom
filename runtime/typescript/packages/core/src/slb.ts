import { decodeCbor } from "./cbor.js";
import { decryptSlbBlock, type SloomSymmetricKey } from "./crypto.js";
import type { SloomManifestJson } from "./types.js";

export interface SloomSlbArchive {
  readonly manifest: SloomManifestJson;
  readonly files: Map<string, Uint8Array>;
  readonly manifestPath: string;
}

interface SlbBlock {
  offset: number;
  storedSize: number;
  rawSize: number;
  blockId: number;
  codec: number;
  flags: number;
  chunkCount: number;
  keyId: number;
}

interface SlbCrypto {
  keyId: number;
  algorithm: string;
  kdf: string;
  salt: Uint8Array;
  noncePrefix: Uint8Array;
  chunkSize: number;
}

export function readSlb(
  bytes: Uint8Array,
  key?: SloomSymmetricKey,
): SloomSlbArchive {
  const headerSize = 64;
  const blockEntrySize = 64;
  if (
    bytes.length < headerSize ||
    bytes[0] !== 0x53 ||
    bytes[1] !== 0x4c ||
    bytes[2] !== 0x42 ||
    bytes[3] !== 0x1a
  ) {
    throw new Error("SLB magic is missing.");
  }

  const data = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const major = data.getUint16(4, true);
  if (major !== 1) {
    throw new Error(`Unsupported SLB major version: ${major}`);
  }

  const flags = data.getUint32(28, true);
  if ((flags & 4) !== 0) {
    throw new Error("Encrypted SLB indexes are not supported.");
  }

  const blockCount = data.getUint32(24, true);
  const indexOffset = number64(data, 40);
  const indexSize = data.getUint32(48, true);

  if (headerSize + blockCount * blockEntrySize > bytes.length) {
    throw new Error("SLB block table is outside the file.");
  }
  if (indexOffset + indexSize > bytes.length) {
    throw new Error("SLB index is outside the file.");
  }

  const blocks = new Map<number, SlbBlock>();
  for (let index = 0; index < blockCount; index++) {
    const offset = headerSize + index * blockEntrySize;
    const block: SlbBlock = {
      offset: number64(data, offset),
      storedSize: number64(data, offset + 8),
      rawSize: number64(data, offset + 16),
      blockId: data.getUint32(offset + 24, true),
      codec: bytes[offset + 29],
      flags: data.getUint16(offset + 30, true),
      chunkCount: data.getUint32(offset + 32, true),
      keyId: data.getUint16(offset + 36, true),
    };

    if (block.blockId !== index) {
      throw new Error("SLB block ids are not contiguous.");
    }
    if (block.offset + block.storedSize > bytes.length) {
      throw new Error(`SLB block ${block.blockId} is outside the file.`);
    }
    if (block.codec !== 0) {
      throw new Error(
        `SLB block ${block.blockId} uses unsupported codec ${block.codec}.`,
      );
    }

    blocks.set(block.blockId, block);
  }

  const indexBytes = bytes.slice(indexOffset, indexOffset + indexSize);
  const index = asObject(decodeCbor(indexBytes), "SLB index");
  const fileEntries = asObjectArray(index.files, "SLB index files");
  const cryptoEntries = asObjectArray(
    index.crypto ?? [],
    "SLB index crypto",
  );
  const crypto = new Map<number, SlbCrypto>();

  for (const value of cryptoEntries) {
    const entry: SlbCrypto = {
      keyId: asNumber(value["key-id"], "crypto key-id"),
      algorithm: asString(value.alg, "crypto alg"),
      kdf: value.kdf === undefined ? "hkdf-sha256" : asString(value.kdf, "crypto kdf"),
      salt: asBytes(value.salt, "crypto salt"),
      noncePrefix: asBytes(value["nonce-prefix"], "crypto nonce-prefix"),
      chunkSize: asNumber(value["chunk-size"], "crypto chunk-size"),
    };
    crypto.set(entry.keyId, entry);
  }

  const files = new Map<string, Uint8Array>();
  for (const entry of fileEntries) {
    const path = asString(entry.path, "file path");
    const blockId = asNumber(entry.block, "file block");
    const block = blocks.get(blockId);
    if (!block) {
      throw new Error(`SLB index references missing block ${blockId}.`);
    }

    const stored = bytes.slice(block.offset, block.offset + block.storedSize);

    if ((block.flags & 2) === 0) {
      files.set(path, stored);
      continue;
    }

    if (!key) {
      throw new Error(
        `SL5022: SLB block ${block.blockId} is encrypted; a key is required.`,
      );
    }

    const metadata = crypto.get(block.keyId);
    if (!metadata) {
      throw new Error(`SLB block ${block.blockId} has no crypto metadata.`);
    }
    if (metadata.kdf !== "hkdf-sha256") {
      throw new Error(`Unsupported SLB KDF: ${metadata.kdf}`);
    }

    files.set(
      path,
      decryptSlbBlock({
        stored,
        rawSize: block.rawSize,
        chunkCount: block.chunkCount,
        key,
        algorithm: metadata.algorithm,
        salt: metadata.salt,
        noncePrefix: metadata.noncePrefix,
        chunkSize: metadata.chunkSize,
        keyId: metadata.keyId,
        blockId: block.blockId,
      }),
    );
  }

  const manifestPath = files.has("manifest.cbor")
    ? "manifest.cbor"
    : files.has("manifest.json")
        ? "manifest.json"
        : undefined;
  if (!manifestPath) {
    throw new Error("SLB does not contain a manifest.");
  }

  const manifest = asObject(
    decodeDocument(files.get(manifestPath)!, manifestPath),
    "manifest",
  ) as unknown as SloomManifestJson;

  return { manifest, files, manifestPath };
}

export function decodeDocument(
  bytes: Uint8Array,
  path: string,
): unknown {
  const value = path.toLowerCase().endsWith(".cbor")
    ? decodeCbor(bytes)
    : JSON.parse(new TextDecoder().decode(bytes));
  return normalizeDigests(value);
}

function normalizeDigests(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(normalizeDigests);
  }
  if (typeof value !== "object" || value === null || value instanceof Uint8Array) {
    return value;
  }

  const source = value as Record<string, unknown>;
  const output: Record<string, unknown> = {};
  for (const [key, child] of Object.entries(source)) {
    if (
      (key === "digest" || key === "container-digest") &&
      typeof child === "object" &&
      child !== null
    ) {
      const digest = child as Record<string, unknown>;
      const digestValue = digest.value;
      output[key] = {
        ...digest,
        value: digestValue instanceof Uint8Array
          ? hex(digestValue)
          : digestValue,
      };
    } else {
      output[key] = normalizeDigests(child);
    }
  }
  return output;
}

function hex(bytes: Uint8Array): string {
  return Array.from(bytes, (value) => value.toString(16).padStart(2, "0")).join("");
}

function number64(view: DataView, offset: number): number {
  return Number(view.getBigUint64(offset, true));
}

function asObject(value: unknown, name: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`${name} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function asObjectArray(
  value: unknown,
  name: string,
): Record<string, unknown>[] {
  if (!Array.isArray(value)) {
    throw new Error(`${name} must be an array.`);
  }
  return value.map((item) => asObject(item, name));
}

function asString(value: unknown, name: string): string {
  if (typeof value !== "string") {
    throw new Error(`${name} must be a string.`);
  }
  return value;
}

function asNumber(value: unknown, name: string): number {
  if (typeof value !== "number") {
    throw new Error(`${name} must be a number.`);
  }
  return value;
}

function asBytes(value: unknown, name: string): Uint8Array {
  if (!(value instanceof Uint8Array)) {
    throw new Error(`${name} must be a byte string.`);
  }
  return value;
}
