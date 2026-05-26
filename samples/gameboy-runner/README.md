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

This sample uses declarations, assignment, `while`, `if`, `video_wait_vblank()`, `scroll_set(...)`, `sprite_set(...)`, `map_column(...)`, and `map_stream_column(...)`. The actor stays fixed while the background moves through the Game Boy `SCX` register, then the sample streams a new map column from ROM every 8 pixels.
