# Contributing

## Prerequisites

- .NET SDK 11.0.100
- Node.js 24+ with npm (required by the Nx wrapper)
- CMake 3.28+ and Ninja (native encoders)
- pnpm 10+ (TypeScript runtime)
- Flutter 3.27 / Dart 3.6 (Dart runtime)

## Bootstrap

```bash
./nx --version
```

The wrapper installs the pinned Nx version into `.nx/installation` on first use.

## Build and test

```bash
./nx run-many -t build
./nx run-many -t test
./nx affected -t build test --base=origin/main --head=HEAD
```

Direct commands, used when debugging the toolchain:

```bash
dotnet build SpriteLoom.slnx -c Release
dotnet test SpriteLoom.slnx -c Release
```

## Code style

- Source, comments, logs, and diagnostics are English only.
- Comments state contracts, invariants, and non-obvious constraints. Do not narrate the implementation.
- One public type per file; the file name matches the type name.
- Organize by feature. Avoid `Utils`, `Helpers`, and `Common` folders.
- Keep project dependencies one-directional; the architecture tests enforce the allowed edges.
- Versioned text must not reference local planning material.

## Commits

Use Conventional Commits.
