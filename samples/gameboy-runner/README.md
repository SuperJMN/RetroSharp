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

This sample uses declarations, assignment, `while`, `if`, `video_wait_vblank()`, `scroll_set(...)`, `sprite_asset(...)`, `sprite_draw(...)`, `map_column(...)`, and `map_stream_column(...)`. The actor stays fixed while the background moves through the Game Boy `SCX` register, then the sample streams a new map column from ROM every 8 pixels.

The runner sprite is editable at `assets/player-run.gb.png`. It is a horizontal PNG sprite sheet with two 16x16 frames, suitable for Aseprite. Open it at 1x, enable a 16x16 grid, keep the background transparent, and add frames by growing the canvas horizontally. The sample colors map `#E0F8D0` to Game Boy color `1`, `#88C070` to `2`, and `#346856` to `3`. The compiler splits each logical frame into 8x16 Game Boy hardware sprites and assigns the generated tiles automatically.
