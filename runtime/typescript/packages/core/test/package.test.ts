import { describe, expect, it } from "vitest";
import { SloomPackage } from "../src/index";
import type { SloomContainerJson, SloomManifestJson } from "../src/index";

const digest = { alg: "xxh3-64", value: "0000000000000000" };

const manifest: SloomManifestJson = {
  format: "sprite-loom",
  "format-major": 1,
  "format-minor": 0,
  bundle: { id: "heroes", requires: [] },
  package: { id: "heroes", "display-name": "Heroes" },
  generator: { name: "SpriteLoom", version: "0.1.0" },
  pages: [
    {
      uri: 4,
      group: "default",
      order: 0,
      required: true,
      container: "page/0000.slatlas/container.json",
      "container-digest": digest,
      size: [8, 8],
      "pixel-format": "rgba8888",
      variants: [
        {
          id: "png",
          codec: "png",
          file: "page/0000.slatlas/v0.png",
          digest,
        },
      ],
    },
  ],
  uris: [
    "anim://heroes/run",
    "frame://heroes/run/a1",
    "frame://heroes/run/a10",
    "frame://heroes/run/a2",
    "page://heroes/0000",
  ],
  frames: {
    "to-record": [0xffffffff, 0, 1, 2, 0xffffffff],
    records: [
      {
        page: 0,
        stored: 0,
        origin: [16, 16],
        offset: [2, 2],
        pivot: [128, 128],
        flags: 1,
      },
      {
        page: 0,
        stored: 0,
        origin: [16, 16],
        offset: [2, 2],
        pivot: [128, 128],
        flags: 1,
      },
      {
        page: 0,
        stored: 1,
        origin: [16, 16],
        offset: [1, 1],
        pivot: [128, 128],
        flags: 1,
      },
    ],
  },
  animations: [
    {
      uri: 0,
      file: "anim/run.anim.json",
      digest,
      "frame-count": 3,
    },
  ],
  blobs: [],
};

const container: SloomContainerJson = {
  format: "sprite-loom/container",
  page: 0,
  size: [8, 8],
  "pixel-format": "rgba8888",
  alpha: "straight",
  variants: [
    {
      id: "png",
      codec: "png",
      file: "page/0000.slatlas/v0.png",
      digest,
    },
  ],
  stored: [
    { rect: [0, 0, 4, 4], rotation: 0, digest },
    { rect: [4, 0, 6, 6], rotation: 0, digest },
  ],
};

describe("SloomPackage", () => {
  const pkg = SloomPackage.fromManifest(manifest, {
    "page://heroes/0000": container,
  });

  it("exposes the URI table and pages", () => {
    expect(pkg.uris).toHaveLength(5);
    expect(pkg.pages[0]?.container).toBe("page/0000.slatlas/container.json");
    expect(pkg.animations).toHaveLength(1);
  });

  it("resolves a frame record", () => {
    const frame = pkg.frame("frame://heroes/run/a1");

    expect(frame.rect).toEqual({ x: 0, y: 0, width: 4, height: 4 });
    expect(frame.origin).toEqual({ width: 16, height: 16 });
    expect(frame.offset).toEqual({ x: 2, y: 2 });
    expect(frame.pivot).toEqual({ x: 8, y: 8 });
    expect(frame.rotated).toBe(false);
    expect(frame.variantUrls).toEqual(["page/0000.slatlas/v0.png"]);
  });

  it("shares stored frames for deduplicated URIs", () => {
    expect(pkg.frame("frame://heroes/run/a1").rect).toEqual(
      pkg.frame("frame://heroes/run/a10").rect,
    );
    expect(pkg.frame("frame://heroes/run/a2").rect).toEqual({
      x: 4,
      y: 0,
      width: 6,
      height: 6,
    });
  });

  it("rejects non-frame URIs", () => {
    expect(() => pkg.frame("page://heroes/0000")).toThrow();
  });
});
