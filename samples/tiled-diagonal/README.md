# Diagonal Tiled Game Boy Scroll Sample

Sample Layer: `target-acceptance`

This sample exercises Game Boy diagonal camera streaming from a 40x40 Tiled map loaded with `world.Load(...)`. The source map keeps the full 40-row world slice and is wider than the 32-column Game Boy background buffer, so diagonal camera movement has to stream both fresh columns and fresh rows from the Tiled world rows.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out diag.gb diag.rs
```
