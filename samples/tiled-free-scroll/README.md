# Tiled Free Scroll Sample

Sample Layer: `target-acceptance`

This sample exercises diagonal camera movement from a 50x60 Tiled map loaded with `World.Load(...)`. The map fits inside the NES four-screen 64x60 surface while still crossing both nametable axes; Game Boy builds the same source and uses its staggered diagonal streaming path.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out free-scroll.gb free-scroll.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out free-scroll.nes free-scroll.rs
```
