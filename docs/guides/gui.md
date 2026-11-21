# GUI editor

The Avalonia editor is in `src/SpriteLoom.Gui` and uses the Semi.Avalonia theme. It shares the same compiler host as the CLI.

## Run

```bash
dotnet run --project src/SpriteLoom.Gui
dotnet run --project src/SpriteLoom.Gui -- path/to/sloom.toml
```

A project path argument opens the project at startup. If the configured output already exists, the editor loads it into the preview automatically.

## Project editing

- **Open project** loads a `sloom.toml` file or a project directory and fills the editor with its current values.
- **Sources** lists image-sequence and TexturePacker sources. A source can be added, removed, reordered, or edited in place.
- **Pipeline** shows the active pass graph as a horizontal flow. Optional mipmap and texture passes appear when they are enabled.
- **Inspector** edits the parameters of the selected pass: import, trim, pack, bleed, raster encode, texture encode, manifest, and package output.
- **Save config** writes the current values back to `sloom.toml` atomically.
- **Pack** builds from the in-memory configuration, so unsaved edits are packed without writing the file first. The artifact cache still applies to unchanged passes.
- **Undo/Redo** covers project values, source fields, and source list changes. Shortcuts: `Ctrl+Z` and `Ctrl+Y`. `Ctrl+O` opens a project, `Ctrl+S` saves it, and `F5` packs.

Saving writes a normalized configuration: known sections and passes are emitted in a stable order. Comments and unrecognized pass fields are not preserved, so keep a separate copy if a project depends on hand-written extensions.

## Package preview

- **Package path** accepts a directory package, a manifest file, or an `.slb` bundle.
- **Load package** opens a built package read-only. JSON and CBOR manifests are both supported.
- The animation list shows every animation document and its total duration.
- The atlas view renders the selected page and highlights the current stored rectangle. Page bitmaps decode off the UI thread and are cached per page.
- Opening an `.slb` bundle verifies its header, index, and block digests; opening a directory package checks the manifest and container structure.
- The frame view reconstructs the original frame from `origin`, `offset`, and the stored rectangle.
- Play/Pause, Previous, and Next drive the timeline. Forward, reverse, and ping-pong playback follow the animation durations and loop flag.
- The timeline strip shows one block per frame with its duration and any animation event kinds attached to that frame.
- **Manifest** shows the decoded manifest as formatted JSON and reports the package id, page count, frame count, and animation count. CBOR documents are converted for display.

## Capabilities

The capabilities tab lists the image formats exposed by the configured codecs, whether the native texture encoder is loaded, the available KTX2 texture targets, and the built-in packers. External plugin hosting is not enabled in this build.

## Limits

- Animation documents are displayed read-only; source-to-animation editing is not exposed yet.
- The current animation builder emits documents without events, so event markers only appear for packages that already carry them.
- KTX2-only pages cannot be previewed; the viewer requires a raster page variant.
- Large package manifests are decoded fully in memory.
