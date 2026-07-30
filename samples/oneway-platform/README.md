# One-Way Platform

Sample Layer: `target-acceptance`

This is the focused cross-target one-way-platform mechanics rung. One shared
source and one compact Tiled map exercise the three observable behaviors of a
one-way (drop-through) platform on a freshly compiled ROM:

- **Pass through from below.** A rising jump crosses the platform top without
  landing, because landing resolves only while the actor is non-rising.
- **Land on top when descending.** The same jump's descent straddles the
  platform top and the actor lands on it.
- **Walk off the edge and fall.** Walking past the platform edge drops support
  and the actor falls to the solid floor below.

Landing and support queries use the combined `Landable = Solid | Platform`
mask, while wall queries request only `Solid`, so the platform is solid from
above yet passable from below and from the side. The platform sits directly
above the authored start, so the mechanic is reachable with a single jump
without deep, timing-fragile navigation into the full runner stage.

Build both tracked cartridges from the shared project:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- oneway-platform.retrosharp.json
```

The sample deliberately omits music, enemies, projectiles, camera-vertical
follow, and full-stage content. Platformer response remains source policy over
the portable input, camera, sprite, Tiled-world, and collision APIs; packing,
banking, mapper selection, VRAM/PPU writes, and OAM publication remain
target-owned. The one-way collision authoring reuses the runner tileset's
`retrosharpCollision=platform` ledge tile.
