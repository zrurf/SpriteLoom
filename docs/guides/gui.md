# GUI editor

The Avalonia editor is in `src/SpriteLoom.Gui` and uses the Semi.Avalonia theme. It shares the same compiler host as the CLI.

## Run

```bash
dotnet run --project src/SpriteLoom.Gui
```

## Current workspace

- **Project path**: a `sloom.toml` file or project directory.
- **Pack**: runs the pipeline and writes the configured output.
- **Package path**: a directory package, a manifest file, or an `.slb` bundle.
- **Load package**: opens a built package read-only.

## Preview

- The animation list shows every animation in the package.
- The atlas view renders the selected page and highlights the current stored rectangle.
- The frame view reconstructs the original frame from `origin`, `offset`, and the stored rectangle.
- Play/Pause, Previous, and Next drive the timeline; playback follows the animation's durations and loop flag.

## Current limits

This is the first M4 workspace. The pipeline graph editor, parameter inspector, source management, undo/redo, plugin panel, and CBOR manifest viewer are still pending. Packing already uses the pipeline's staging directory and does not leave a partially written package when cancelled.
