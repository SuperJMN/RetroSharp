# Platformer Landing

Sample Layer: `target-acceptance`

This is the focused cross-target platformer mechanics rung. One shared source
and one compact Tiled map exercise idle support, a solid wall, one complete
jump and landing, horizontal camera follow, collision on the floor at world
Y=304, packed-world boundaries, bidirectional traversal, and an authored hole
that is the only gameplay-reset path.

Build both tracked cartridges from the shared project:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- platformer-landing.retrosharp.json
```

The sample deliberately omits music, enemies, projectiles, and full-stage
content. Platformer response remains source policy over the portable input,
camera, sprite, Tiled-world, and collision APIs; packing, banking, mapper
selection, VRAM/PPU writes, and OAM publication remain target-owned.
