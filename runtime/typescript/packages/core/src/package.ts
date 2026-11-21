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
}

const NotRecorded = 0xffffffff;

export class SloomPackage {
  private readonly manifest: SloomManifestJson;
  private readonly containers: Map<string, SloomContainerJson>;
  private readonly manifestUrl?: URL;
  private readonly uriIndices: Map<string, number>;

  private constructor(
    manifest: SloomManifestJson,
    containers: Map<string, SloomContainerJson>,
    manifestUrl?: URL,
  ) {
    this.manifest = manifest;
    this.containers = containers;
    this.manifestUrl = manifestUrl;
    this.uriIndices = new Map(manifest.uris.map((uri, index) => [uri, index]));
  }

  static async open(
    manifestUrl: string | URL,
    _options: SloomOpenOptions = {},
  ): Promise<SloomPackage> {
    const base =
      typeof globalThis.location !== "undefined" ? globalThis.location.href : undefined;
    const url = new URL(manifestUrl, base);
    const manifest = await fetchJson<SloomManifestJson>(url);
    const containers = new Map<string, SloomContainerJson>();

    for (const page of manifest.pages) {
      const pageUri = manifest.uris[page.uri];
      const containerUrl = new URL(page.container, url);
      containers.set(pageUri, await fetchJson<SloomContainerJson>(containerUrl));
    }

    return new SloomPackage(manifest, containers, url);
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

  async loadAnimation(uri: string): Promise<AnimationView> {
    if (!this.manifestUrl) {
      throw new Error("Animation loading requires a package opened from a URL.");
    }

    const reference = this.manifest.animations.find(
      (candidate) => this.manifest.uris[candidate.uri] === uri,
    );

    if (!reference) {
      throw new Error(`Unknown animation: ${uri}`);
    }

    const document = await fetchJson<SloomAnimationJson>(
      new URL(reference.file, this.manifestUrl),
    );

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

async function fetchJson<T>(url: URL): Promise<T> {
  const response = await fetch(url);

  if (!response.ok) {
    throw new Error(`Failed to load ${url.toString()}: ${response.status}`);
  }

  return (await response.json()) as T;
}
