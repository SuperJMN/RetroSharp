# Game Boy Window HUD Sample

Sample Layer: `target-capability-spike`

Build a Game Boy ROM with the first Window HUD prototype:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out hud.gb hud.rs
```

This sample uses `hud.SetTile(window, x, y, tile)` to place static HUD tiles in the Game Boy Window tilemap. Tile ids are named with a small enum, which folds to the same static bytes before lowering.

Current restrictions:

- The Window HUD is fixed at the top-left screen position with `WY=0` and `WX=7`.
- HUD tiles are copied during startup; runtime HUD writes are not implemented yet.
- The HUD tilemap is separate from the scrolling background/camera tilemap.
- `split_scroll` is not a declared Game Boy HUD mode and fails through the target capability check.
