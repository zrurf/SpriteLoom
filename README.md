# SpriteLoom

SpriteLoom is an open-source sprite atlas compiler and runtime format for Flutter, Web, and PixiJS.

Status: M4 in progress. M0–M3 are complete. The compiler supports pixel-format conversion and ordered dithering, mipmaps, KTX2 output, ASTC/BC/ETC2/Basis encoding through an optional native library, WebP/JPEG XL/AVIF variants, directory and `.slb` packages, signing, and caching. A TypeScript loader and a basic Dart/Flutter player are available. The Avalonia GUI now provides project packing, package loading, atlas/frame preview, and animation playback.

Documentation: [docs/README.md](docs/README.md).

## Repository layout

- `src/` .NET compiler and hosts
- `native/` native texture encoders
- `runtime/` .NET, TypeScript, and Dart loaders
- `spec/` CDDL, JSON Schema, and conformance vectors
- `docs/` English documentation

## Build

Prerequisites: .NET SDK 11, Node.js with npm, CMake, Ninja.

```bash
./nx run-many -t build
```

Direct build:

```bash
dotnet build SpriteLoom.slnx -c Release
```

## License

MIT. See `LICENSE` and `THIRD-PARTY-NOTICES.md`.
