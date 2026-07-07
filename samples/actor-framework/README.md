# Actor Framework Sample

Sample Layer: `target-acceptance`

This sample is the focused acceptance case for the actor framework roadmap. The
same `actors.rs` source builds for Game Boy and NES and exercises a fixed actor
pool, three declarative enemy definitions, Tiled object-layer spawn data
kept as generated ROM tables, runtime camera-window activation into two fixed
slots, shared `enemies.Update()`, tile helpers, and animation-backed
`enemies.Draw()` while the camera scrolls horizontally. Actor world positions
use split byte storage (`x`/`xHi` and `y`/`yHi`); draw,
`enemies.TouchTiles(...)`, and `enemies.LandOnTiles(...)` project actors to
`screenX = worldX - cameraX` and `screenY = worldY - cameraY` before culling or
collision. Spawn activation still uses the camera X window, including the Koopa
spawned beyond X=255.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out actors.gb actors.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out actors.nes actors.rs
```

The collision layer keeps the second tile row solid, while the authored spawns
sit at different world X columns on that row. The sample starts with the distant
Koopa outside the camera window and no slot assigned to it; each frame calls
`Actors.SpawnLayer(...)` after `Camera.SetPosition(...)`, so the offscreen slot is
recycled and the Koopa activates when scrolling reaches its world X. The source
still has no global enemy-kind switch in `main`.

See `../../docs/Portable2DSdkV1.md` for the actor API and the hand-authored
low-level equivalent pattern. See `../../docs/ActorFrameworkRoadmap.md` for the
AF-5 follow-ups that remain after the first scrolling platformer slice: spawn
reactivation policy and activation-scan cost.
