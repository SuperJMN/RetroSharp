# Dead-Zone Follow Sample

Sample Layer: `target-acceptance`

This sample exercises 2-axis camera following over a 64x60 Tiled map loaded with `World.Load(...)`. A scripted player point moves one pixel per frame in both axes and the camera remains still while the point stays inside the central dead-zone. When the point crosses a dead-zone edge, the camera advances by at most one pixel per frame to keep it on that edge.

The camera clamps to byte-backed common cross-target bounds: X remains in `0..248`, and Y remains in `0..240` so the sample does not wrap the byte Y position while still covering the NES four-screen vertical range.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out deadzone.gb deadzone.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out deadzone.nes deadzone.rs
```
