# Texture encoding guide

## Configuration

```toml
[[passes]]
id = "encode"
use = "encode.page"
params = { formats = ["png", "webp", "jxl", "avif"], quality = 90 }

[[passes]]
id = "texture"
use = "texture.encode"
params = {
  targets = ["ktx2-astc4x4", "ktx2-bc7", "ktx2-basis-uastc"],
  pixel-format = "rgba8888",
  dither = "ordered4",
  mipmaps = true,
  quality = "balanced"
}
```

The manifest lists variants in output order. PNG is the first variant by default; raster formats follow; GPU targets follow after that. Set `png-fallback = false` in the encode pass to omit the PNG variant when every target runtime can decode another listed format:

```toml
[[passes]]
id = "encode"
use = "encode.page"
params = { formats = ["webp"], quality = 85, png-fallback = false }
```

A loader should try variants in order and fall back to the next one when a codec or GPU format is unsupported.

## Native encoder

The optional native library provides ASTC, BC1–BC7, ETC2, and Basis Universal (UASTC/ETC1S) encoding:

```bash
CC=clang CXX=clang++ ./nx run native:native-build
```

The output is written to `.build/native/host-release/sloom_native.dll` (or the platform equivalent) and is copied next to the compiler output when present. The runtime also honors `SLOOM_NATIVE_PATH` for an explicit library location.

When the library is missing, already available variants are kept and each unavailable target emits `SL3001`. A build never fails solely because an optional encoder is missing.

## Alignment and mipmaps

The effective page alignment is the least common multiple of the configured block sizes, with a minimum of 4:

- ASTC 4x4 and BC targets: 4
- ASTC 4x4 + ASTC 12x12: 12
- ASTC 8x8 + ASTC 12x12: 24

With `mipmaps = true`, a box-filtered chain is generated down to 1x1. Mip level dimensions follow `max(1, previous / 2)`; encoders pad partial blocks at small levels.

## Determinism

Pixel format conversion, dithering, mipmap generation, and KTX2 writing use integer math and fixed iteration order. Native encoders run single-threaded to keep output stable.
