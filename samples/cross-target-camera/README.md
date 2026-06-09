# Cross-Target Camera Sample

Sample Layer: `portable-sdk`

This sample is the first small portability acceptance case for the 2D SDK surface. The same `camera.rs` source builds for Game Boy and NES by using shared world data, tick input, horizontal camera positioning, and logical sprite drawing.

The source also exercises the language v1 style shared by both cartridge targets: `type` aliases, top-level `const` values, `loop`, and boolean literals for portable sprite draw arguments.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out cross-camera.gb camera.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out cross-camera.nes camera.rs
```

The sample intentionally avoids raw target calls such as `sprite_set(...)`, `scroll_set(...)`, `tilemap_set(...)`, `tilemap_fill(...)`, `map_stream_column(...)`, and `object_palette_set(...)`.

Current unsupported optional features:

- NES accepts only horizontal `camera_set_position(x, 0)` in this spike; vertical camera movement is a capability error.
- NES seeds the visible nametable from `world_map(...)` but does not stream new columns or rows at runtime yet.
- NES `sprite_draw(...)` requires constant frame, flip, and palette-slot arguments in the current spike.
- This sample does not use collision queries, runtime animation, or HUD APIs; those remain separate roadmap items.
