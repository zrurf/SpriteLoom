import { decryptSenc, type SloomSymmetricKey } from "./crypto.js";
import { decodeDocument, readSlb } from "./slb.js";
import type {
  AnimationView,
  FrameView,
  SloomAnimationJson,
  SloomAnimationRefJson,
  SloomContainerJson,
  SloomManifestJson,
  SloomPageJson,
} from "./types.js";

export interface SloomOpenOptions {
  verify?: "off" | "header";
  key?: SloomSymmetricKey;
}

const NotRecorded = 0xffffffff;

export class SloomPackage {
  private readonly manifest: SloomManifestJson;
  private readonly containers: Map<string, SloomContainerJson>;
  private readonly manifestUrl?: URL;
  private readonly uriIndices: Map<string, number>;
  private readonly files?: Map<string, Uint8Array>;
  private readonly key?: SloomSymmetricKey;

  private constructor(
    manifest: SloomManifestJson,
    containers: Map<string, SloomContainerJson>,
    manifestUrl?: URL,
    files?: Map<string, Uint8Array>,
    key?: SloomSymmetricKey,
  ) {
    this.manifest = manifest;
    this.containers = containers;
    this.manifestUrl = manifestUrl;
    this.files = files;
    this.key = key;
    this.uriIndices = new Map(manifest.uris.map((uri, index) => [uri, index]));
  }

  static async open(
    manifestUrl: string | URL,
    options: SloomOpenOptions = {},
  ): Promise<SloomPackage> {
    const base =
      typeof globalThis.location !== "undefined" ? globalThis.location.href : undefined;
    const url = new URL(manifestUrl, base);
    const manifest = decodeDocument(
      await fetchLogical(url, options.key),
      url.pathname,
    ) as SloomManifestJson;
    const containers = new Map<string, SloomContainerJson>();

    for (const page of manifest.pages) {
      const pageUri = manifest.uris[page.uri];
      const containerUrl = new URL(page.container, url);
      containers.set(
        pageUri,
        decodeDocument(
          await fetchLogical(containerUrl, options.key),
          containerUrl.pathname,
        ) as SloomContainerJson,
      );
    }

    return new SloomPackage(manifest, containers, url, undefined, options.key);
  }

  static async openSlb(
    path: string | URL,
    options: SloomOpenOptions = {},
  ): Promise<SloomPackage> {
    const base =
      typeof globalThis.location !== "undefined" ? globalThis.location.href : undefined;
    const url = new URL(path, base);
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Failed to load ${url.toString()}: ${response.status}`);
    }

    return SloomPackage.fromSlb(
      new Uint8Array(await response.arrayBuffer()),
      options.key,
    );
  }

  static fromSlb(
    bytes: Uint8Array,
    key?: SloomSymmetricKey,
  ): SloomPackage {
    const archive = readSlb(bytes, key);
    const containers = new Map<string, SloomContainerJson>();
    for (const page of archive.manifest.pages) {
      const path = page.container;
      const pageUri = archive.manifest.uris[page.uri];
      const containerBytes = archive.files.get(path);
      if (!containerBytes) {
        throw new Error(`Container not found in SLB: ${path}`);
      }
      containers.set(
        pageUri,
        decodeDocument(containerBytes, path) as SloomContainerJson,
      );
    }
    return new SloomPackage(
      archive.manifest,
      containers,
      undefined,
      archive.files,
      key,
    );
  }

  static fromManifest(
    manifest: SloomManifestJson,
    containers:
      | Record<string, SloomContainerJson>
      | Map<string, SloomContainerJson>,
  ): SloomPackage {
    const map =
      containers instanceof Map
        ? containers
        : new Map(Object.entries(containers));
    return new SloomPackage(manifest, map);
  }

  get uris(): readonly string[] {
    return this.manifest.uris;
  }

  get pages(): readonly SloomPageJson[] {
    return this.manifest.pages;
  }

  get animations(): readonly SloomAnimationRefJson[] {
    return this.manifest.animations;
  }

  frame(uri: string): FrameView {
    const uriIndex = this.uriIndices.get(uri);

    if (uriIndex === undefined) {
      throw new Error(`Unknown URI: ${uri}`);
    }

    const recordIndex = this.manifest.frames["to-record"][uriIndex];

    if (recordIndex === NotRecorded) {
      throw new Error(`URI is not a frame: ${uri}`);
    }

    const record = this.manifest.frames.records[recordIndex];
    const page = this.manifest.pages[record.page];
    const pageUri = this.manifest.uris[page.uri];
    const container = this.containers.get(pageUri);

    if (!container) {
      throw new Error(`Container not loaded: ${pageUri}`);
    }

    const stored = container.stored[record.stored];

    if (!stored) {
      throw new Error(`Stored frame not found: ${record.stored}`);
    }

    const [x, y, width, height] = stored.rect;

    return {
      uri,
      page: record.page,
      pageUri,
      rect: { x, y, width, height },
      origin: { width: record.origin[0], height: record.origin[1] },
      offset: { x: record.offset[0], y: record.offset[1] },
      pivot: { x: record.pivot[0] / 16, y: record.pivot[1] / 16 },
      rotated: stored.rotation !== 0,
      variantUrls: page.variants.map((variant) => this.resolve(variant.file)),
    };
  }

  async readFile(path: string): Promise<Uint8Array> {
    if (this.files) {
      const bytes = this.files.get(path) ?? this.files.get(`${path}.senc`);
      if (!bytes) {
        throw new Error(`File not found in SLB: ${path}`);
      }
      return bytes;
    }

    if (!this.manifestUrl) {
      throw new Error("File loading requires a package opened from a URL.");
    }

    return fetchLogical(new URL(path, this.manifestUrl), this.key);
  }

  async loadAnimation(uri: string): Promise<AnimationView> {
    if (!this.files && !this.manifestUrl) {
      throw new Error("Animation loading requires a package opened from a URL.");
    }

    const reference = this.manifest.animations.find(
      (candidate) => this.manifest.uris[candidate.uri] === uri,
    );

    if (!reference) {
      throw new Error(`Unknown animation: ${uri}`);
    }

    const document = decodeDocument(
      await this.readFile(reference.file),
      reference.file,
    ) as SloomAnimationJson;

    return {
      uri: document.uri,
      frameUris: document.frames.map((frame) =>
        typeof frame === "number" ? this.manifest.uris[frame] : frame,
      ),
      durationsMs: document["durations-ms"],
      loop: document.loop,
      playback: document.playback,
    };
  }

  private resolve(file: string): string {
    return this.manifestUrl ? new URL(file, this.manifestUrl).toString() : file;
  }
}

async function fetchLogical(
  url: URL,
  key?: SloomSymmetricKey,
): Promise<Uint8Array> {
  const plain = await fetch(url);
  if (plain.ok) {
    const bytes = new Uint8Array(await plain.arrayBuffer());
    return hasSencMagic(bytes)
      ? decryptRequired(bytes, url.toString(), key)
      : bytes;
  }

  const encryptedUrl = new URL(`${url.toString()}.senc`);
  const encrypted = await fetch(encryptedUrl);
  if (!encrypted.ok) {
    throw new Error(
      `Failed to load ${url.toString()}: ${plain.status}; ${encryptedUrl.toString()}: ${encrypted.status}`,
    );
  }

  return decryptRequired(
    new Uint8Array(await encrypted.arrayBuffer()),
    encryptedUrl.toString(),
    key,
  );
}

function decryptRequired(
  bytes: Uint8Array,
  source: string,
  key?: SloomSymmetricKey,
): Uint8Array {
  if (!key) {
    throw new Error(`SENC key required for ${source}.`);
  }
  return decryptSenc(bytes, key);
}

function hasSencMagic(bytes: Uint8Array): boolean {
  return (
    bytes.length >= 4 &&
    bytes[0] === 0x53 &&
    bytes[1] === 0x45 &&
    bytes[2] === 0x4e &&
    bytes[3] === 0x43
  );
}
