# Source Library Package Sample

Sample Layer: `portable-sdk`

This sample demonstrates a project loading an external source-only library
package from a local `libraryPaths` entry. The game source lists only its own
`src/main.rs`; the reusable `Acme.Timing` facade comes from
`lib/acme-timing/retrosharp-library.json`.

The package uses `namespaceMode: "physical"` and exposes a root facade class
that wraps the target `wait_frame` intrinsic for both Game Boy and NES. This is
the supported pluggable-library shape today: source packages can add facades and
helpers over known intrinsics, while new compiler operations and backend
lowering remain part of RetroSharp itself.

Build it directly with:

```sh
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --lib-path samples/source-library-package/lib \
  --out /tmp/source-library.gb \
  samples/source-library-package/source-library.retrosharp.json
```
