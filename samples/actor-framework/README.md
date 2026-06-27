# Actor Framework Sample

Sample Layer: `target-acceptance`

This sample is the focused acceptance case for the actor framework roadmap. The
same `actors.rs` source builds for Game Boy and NES and exercises a fixed actor
pool, three declarative enemy definitions, Tiled object-layer spawn data
loaded into fixed slots, shared `enemies.Update()`, tile helpers, and
animation-backed `enemies.Draw()` while the camera scrolls horizontally. Actor X
positions are world-space bytes split as low `x` plus high `xHi`; draw,
`enemies.TouchTiles(...)`, and `enemies.LandOnTiles(...)` project each actor to
`screenX = worldX - cameraX` and cull actors outside the visible camera window,
including the Koopa spawned beyond X=255.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out actors.gb actors.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out actors.nes actors.rs
```

The collision layer keeps the second tile row solid, while the authored spawns
sit at different world X columns on that row. The sample therefore exercises two
visible actors colliding against different tile columns without a global
enemy-kind switch in `main`. Runtime camera-window paging remains a later
expansion point; all authored spawns stay active from startup.
