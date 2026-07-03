# RetroSharp.Portable2D

`RetroSharp.Portable2D` is the built-in portable 2D SDK package. It is a
source-only RetroSharp library, not a compiled binary dependency.

## Quick Start

Projects normally load it from a project manifest:

```json
{
  "target": "gb",
  "sources": [ "src/main.rs" ],
  "libraries": [ "RetroSharp.Portable2D" ]
}
```

Standalone source can use the transition import form:

```c
import RetroSharp.Portable2D;

void Main() {
    Video.WaitVBlank();
}
```

External source-only packages use the same resolver through `libraryPaths` in a
project manifest or repeated `--lib-path` CLI options.

## Package Shape

This package is declared by `retrosharp-library.json`. The manifest lists the
source files under `src/`, enables physical namespaces, and gates the package to
the target ids it supports.

The public facade source lives in files such as `src/runtime.rs`,
`src/camera.rs`, `src/sprite.rs`, and `src/world.rs`. These files define ordinary
RetroSharp classes and inline helpers over target intrinsics and compiler-known
portable operations.

## Contract

This package:

- Provides the public portable SDK facade used by game source.
- Remains source-only and zero-cost after lowering.
- Can be loaded through `libraries`, source-level `import`, or package paths.
- Uses target intrinsics and resource metadata that the compiler already knows.

This package does not ship a binary ABI, backend plugin, resource importer
plugin, or new target lowerer. Compiler-owned semantics still live in the
internal model described in `docs/SdkArchitecture.md`.

See `docs/SdkArchitecture.md` and `docs/Portable2DSdkV1.md` for the broader
architecture and public API reference.
