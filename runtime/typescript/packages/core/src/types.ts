export interface SloomDigest {
  alg: string;
  value: string;
}

export interface SloomVariant {
  id: string;
  codec: string;
  file: string;
  digest: SloomDigest;
}

export interface SloomPageJson {
  uri: number;
  group: string;
  order: number;
  required: boolean;
  container: string;
  "container-digest": SloomDigest;
  size: [number, number];
  "pixel-format": string;
  variants: SloomVariant[];
}

export interface SloomRecordJson {
  page: number;
  stored: number;
  origin: [number, number];
  offset: [number, number];
  pivot: [number, number];
  flags: number;
}

export interface SloomAnimationRefJson {
  uri: number;
  file: string;
  digest: SloomDigest;
  "frame-count": number;
}

export interface SloomManifestJson {
  format: string;
  "format-major": number;
  "format-minor": number;
  bundle: { id: string; requires: unknown[] };
  package: { id: string; "display-name"?: string };
  generator: { name: string; version: string };
  pages: SloomPageJson[];
  uris: string[];
  frames: {
    "to-record": number[];
    records: SloomRecordJson[];
  };
  animations: SloomAnimationRefJson[];
  blobs: unknown[];
}

export interface SloomStoredJson {
  rect: [number, number, number, number];
  rotation: number;
  digest: SloomDigest;
}

export interface SloomContainerJson {
  format: string;
  page: number;
  size: [number, number];
  "pixel-format": string;
  alpha: string;
  variants: SloomVariant[];
  stored: SloomStoredJson[];
}

export interface SloomAnimationJson {
  format: string;
  uri: string;
  frames: Array<number | string>;
  "durations-ms": number[];
  loop: boolean;
  playback: "forward" | "reverse" | "pingpong";
  events?: unknown[];
}

export interface FrameView {
  uri: string;
  page: number;
  pageUri: string;
  rect: { x: number; y: number; width: number; height: number };
  origin: { width: number; height: number };
  offset: { x: number; y: number };
  pivot: { x: number; y: number };
  rotated: boolean;
  variantUrls: string[];
}

export interface AnimationView {
  uri: string;
  frameUris: string[];
  durationsMs: number[];
  loop: boolean;
  playback: string;
}
