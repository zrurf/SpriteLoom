# TypeScript runtime

`@sprite-loom/core` reads SpriteLoom packages in browsers and Node. It supports:

- JSON and CBOR directory packages over `fetch`.
- `.slb` bundles via `SloomPackage.openSlb` or `SloomPackage.fromSlb`.
- AES-256-GCM and XChaCha20-Poly1305 `.senc` payloads and SLB blocks.

```ts
import { SloomPackage, SloomSymmetricKey } from "@sprite-loom/core";

const pkg = await SloomPackage.open("assets/heroes/manifest.cbor");
const frame = pkg.frame("frame://heroes/run/a1");
const animation = await pkg.loadAnimation("anim://heroes/run");
const png = await pkg.readFile("page/0000.slatlas/v0.png");

const key = SloomSymmetricKey.fromHex(await (await fetch("keys/asset.key")).text());
const bundle = await SloomPackage.openSlb("assets/heroes.slb", { key });
```

`SloomPackage.fromSlb(bytes, key)` is available for environments where the archive is already in memory. Private key material stays outside the package; the runtime only holds the 32-byte symmetric key supplied by the application.

Run `pnpm --filter @sprite-loom/core build` and `pnpm --filter @sprite-loom/core test` from `runtime/typescript`.
