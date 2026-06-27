# Actor Framework Sample

Sample Layer: `target-acceptance`

This sample is the focused acceptance case for the actor framework roadmap. The
same `actors.rs` source builds for Game Boy and NES and exercises a fixed actor
pool, three declarative enemy definitions, Tiled object-layer spawn data
activated through a literal camera window, shared `enemies.Update()`,
animation-backed `enemies.Draw()`, and camera-relative tile queries through the
actor pool helpers.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out actors.gb actors.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out actors.nes actors.rs
```

The sample intentionally uses a literal startup window for activation. Runtime
camera-window paging remains a later expansion point.
