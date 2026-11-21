# Package format

A package is either a directory tree or a single `.slb` bundle. Both carry the same manifest, page containers, encoded images, and animation documents.

## Directory package

```text
build/heroes/
  manifest.json
  page/0000.slatlas/
    container.json
    v0.png
  anim/
    run.anim.json
  signatures/
    <key-id>.sig.cbor
```

- `manifest.json` or `manifest.cbor` indexes URIs, pages, frame records, and animations.
- Each `<page>.slatlas/` directory is a container: one metadata file and one or more content variants.
- Variant order in the manifest is the fallback order. If any frame in a variant cannot be loaded, the whole container falls back to the next variant.
- Animation files are addressed by `anim:` IRIs and contain frame indices, per-frame durations, playback defaults, and optional key-value extras.

The `signatures/` directory is present only in signed packages.

## Bundle

`.slb` is a little-endian container designed for whole-package reads and memory mapping.

```mermaid
flowchart LR
  H[Header 64 B] --> B[Block table 64 B per block]
  B --> D[Digest region]
  D --> I[Canonical CBOR index]
  I --> P[Payload blocks, 64 B aligned]
  P --> S[Optional signature block]
```

Header fields include the magic `53 4C 42 1A`, format version, flags, file size, block count, block-table offset, index offset and size, and an index CRC32. Every block entry records its offset, stored and raw sizes, kind, codec, flags, checksum mask, and digest offset.

A writer may append an optional 64-byte footer. It repeats the index offset and size and carries its own CRC32, allowing sequential readers to locate the index without seeking back to the header.

Block kinds:

| Value | Kind |
| --- | --- |
| 1 | Manifest |
| 2 | Container |
| 3 | Animation |
| 4 | Image |
| 5 | Mesh |
| 6 | Blob |
| 7 | Signature |

The index maps package paths to block ids and records the feature set. Readers reject missing blocks, duplicate paths, out-of-range offsets, and checksum mismatches.

Checksums are combinable. The default is `xxh3-64`; `crc32`, `blake3`, `sha1`, `sha256`, and `sha512` may be enabled together. The digest region stores one digest per enabled algorithm per block.

## Determinism

Given identical inputs and configuration, a package is byte-identical across runs. Passes sort their outputs with ordinal comparisons and avoid timestamps in normative content.
