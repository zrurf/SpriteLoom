# SpriteLoom documentation

SpriteLoom compiles sprite sources into a package that a runtime can load directly. This directory documents the package contract and the tools that produce it.

## Specifications

- [Identifiers](spec/iri.md): the `pkg`, `frame`, `anim`, `page`, and `blob` schemes.
- [Package format](spec/package.md): directory packages and the `.slb` bundle.
- [Signatures](spec/signatures.md): Ed25519 signing and verification for both package forms.
- [Encryption](guides/encryption.md): AES-256-GCM and XChaCha20-Poly1305 for `.senc` files and `.slb` bundles.
- [Textures](spec/textures.md): pixel formats, dithering, KTX2, and GPU targets.

## Guides

- [Command line](guides/cli.md): commands, options, and exit codes.
- [Pipeline](guides/pipeline.md): pass order, caching, watch mode, and MSBuild integration.
- [Texture encoding](guides/textures.md): raster and GPU variant configuration.
- [Flutter player](guides/flutter.md): loading and playing a package in Flutter.
- [TypeScript runtime](../runtime/typescript/README.md): browser/Node package loading and decryption.
- [GUI editor](guides/gui.md): project editing, pipeline inspector, undo/redo, and package preview.
