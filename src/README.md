# Source Layout

The assemblies form a one-directional layered graph. The table groups assemblies by responsibility; the exact allowed edges are enforced by `SpriteLoom.Repository.Tests`. A project may reference only projects permitted by those rules.

| Ring | Assemblies |
|---|---|
| 0 | `SpriteLoom.Foundation` |
| 1 | `SpriteLoom.Model` |
| 2 | `SpriteLoom.Abstractions` |
| 3 | `SpriteLoom.Imaging`, `SpriteLoom.Packing`, `SpriteLoom.Codecs`, `SpriteLoom.Textures`, `SpriteLoom.Compression`, `SpriteLoom.Security`, `SpriteLoom.Bundle` |
| 4 | `SpriteLoom.Pipeline` |
| 5 | `SpriteLoom.Hosting`, `SpriteLoom.PluginHost` |
| 6 | `SpriteLoom.Commands`, `SpriteLoom.Cli.Jit` |

Rules:

- `Foundation` has no project references.
- `Model` contains data only. It does not reference implementations.
- `Abstractions` contains contracts only.
- `Bundle` treats payloads as opaque bytes. It does not reference `Codecs`.
- `Textures` does not reference `Codecs`.
- `Commands` does not reference `PluginHost`. Plugin loading happens in the JIT host.
- No project references `Cli.*`, `Gui`, or `Mcp`.
- `Hosting` is the only composition root.

The repository tests enforce these edges. Run `dotnet test` after adding a project reference.
