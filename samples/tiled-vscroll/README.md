# Wide Tall Tiled Scroll Sample

Sample Layer: `target-acceptance`

This sample exercises vertical camera movement from a 40x60 Tiled map loaded with `world.Load(...)`. NES uses four-screen nametables, so the initial upload fills all four logical nametables and vertical scrolling crosses the 240-pixel coarse-Y wrap. Game Boy builds the same source and uses its row-streamed wrapped background buffer.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out vscroll.gb vscroll.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out vscroll.nes vscroll.rs
```
