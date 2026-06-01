# Game Boy Runner Sample

Build a `.gb` ROM from RetroSharp source that executes a real loop on the Game Boy CPU:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out runner.gb runner.rs
```

Preview with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=runner-retroarch.png \
  runner.gb
```

This sample uses declarations, assignment, `while`, `if`, relational conditions, `video_wait_vblank()`, `scroll_set(...)`, `sprite_asset(...)`, `sprite_draw(...)`, `map_column(...)`, `map_stream_column(...)`, `map_tile_at(...)`, and `button_pressed(...)`. The actor keeps a fixed screen X while the background moves through the Game Boy `SCX` register, streams a new map column from ROM every 8 pixels, lands on a simple source-map tile row, and jumps when the Game Boy A button is pressed.

The runner sprite source is `assets/mario-run.aseprite`. Export it to the PNG used by RetroSharp with:

```bash
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
```

The exported `assets/mario-run.gb.png` is a horizontal sprite sheet with three 16x27 frames. The compiler pads that logical size internally to 16x32 so it fits Game Boy 8x16 hardware sprites. Keep the background transparent and use at most three opaque colors.
