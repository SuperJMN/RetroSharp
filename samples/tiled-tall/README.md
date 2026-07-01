# Tall Tiled Game Boy Scroll Sample

Sample Layer: `target-acceptance`

This sample exercises Game Boy vertical camera streaming from a tall Tiled map loaded with `World.Load(...)`. The source map is 16x40 8x8 tiles with `retrosharpWorldY = 0` and `retrosharpWorldHeight = 40`, so the loaded world keeps the full map height and `Camera.Init(...)` is configured with that full height.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out tall.gb tall.rs
```
