import { readFileSync } from "node:fs";
import { createServer } from "node:http";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { SloomPackage, SloomSymmetricKey } from "../src/index";
import { decryptSenc } from "../src/crypto";
import { decodeDocument } from "../src/slb";
import type { SloomContainerJson, SloomManifestJson } from "../src/types";

const fixture = (path: string) =>
  new Uint8Array(readFileSync(fileURLToPath(new URL(`./fixtures/${path}`, import.meta.url))));
const key = SloomSymmetricKey.fromHex(
  new TextDecoder().decode(fixture("key.hex")),
);

describe("SLB and encrypted packages", () => {
  it("reads a plain SLB bundle", async () => {
    const pkg = SloomPackage.fromSlb(fixture("plain.slb"));
    await expectPackage(pkg);
  });

  it("reads AES-GCM and XChaCha20 encrypted SLB bundles", async () => {
    for (const name of ["aes.slb", "x.slb"]) {
      const pkg = SloomPackage.fromSlb(fixture(name), key);
      await expectPackage(pkg);
    }
  });

  it("rejects an encrypted bundle without a key", () => {
    expect(() => SloomPackage.fromSlb(fixture("aes.slb"))).toThrow(/SL5022/);
  });

  it("reads a plain CBOR directory package", () => {
    const manifest = decodeDocument(fixture("plain-dir/manifest.cbor"), "manifest.cbor") as SloomManifestJson;
    const page = manifest.pages[0];
    const container = decodeDocument(
      fixture(`plain-dir/${page.container}`),
      page.container,
    ) as SloomContainerJson;
    const pkg = SloomPackage.fromManifest(manifest, {
      [manifest.uris[page.uri]]: container,
    });

    expect(pkg.frame("frame://demo/run/a1").origin.width).toBe(8);
  });

  it("loads and decrypts a directory package over fetch", async () => {
    const root = fileURLToPath(new URL("./fixtures/aes-dir/", import.meta.url));
    const server = createServer((request, response) => {
      const path = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname);
      const candidates = [path, `${path}.senc`];
      for (const candidate of candidates) {
        try {
          const bytes = readFileSync(`${root}${candidate.replace(/^\//, "")}`);
          response.writeHead(200);
          response.end(Buffer.from(bytes));
          return;
        } catch {
          // Try the encrypted sidecar next.
        }
      }
      response.writeHead(404);
      response.end();
    });

    await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
    try {
      const address = server.address();
      if (!address || typeof address === "string") {
        throw new Error("HTTP server did not bind.");
      }
      const pkg = await SloomPackage.open(
        `http://127.0.0.1:${address.port}/manifest.cbor`,
        { key },
      );
      await expectPackage(pkg);
    } finally {
      await new Promise<void>((resolve, reject) =>
        server.close((error) => (error ? reject(error) : resolve())),
      );
    }
  });

  it("decrypts a directory package", () => {
    const manifest = decodeDocument(
      decryptSenc(fixture("aes-dir/manifest.cbor.senc"), key),
      "manifest.cbor",
    ) as SloomManifestJson;
    const page = manifest.pages[0];
    const container = decodeDocument(
      decryptSenc(fixture(`aes-dir/${page.container}.senc`), key),
      page.container,
    ) as SloomContainerJson;
    const pkg = SloomPackage.fromManifest(manifest, {
      [manifest.uris[page.uri]]: container,
    });

    expect(pkg.frame("frame://demo/run/a1").origin.width).toBe(8);
    expect(decryptSenc(fixture("aes-dir/page/0000.slatlas/v0.png.senc"), key)[0]).toBe(0x89);
  });
});

async function expectPackage(pkg: SloomPackage): Promise<void> {
  expect(pkg.uris).toContain("frame://demo/run/a1");
  const frame = pkg.frame("frame://demo/run/a1");
  expect(frame.origin.width).toBe(8);
  expect(frame.origin.height).toBe(8);

  const animation = await pkg.loadAnimation("anim://demo/run");
  expect(animation.frameUris).toHaveLength(2);
  expect(animation.durationsMs).toEqual([100, 100]);

  const png = await pkg.readFile("page/0000.slatlas/v0.png");
  expect(Array.from(png.slice(0, 8))).toEqual([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
}
