# Game Boy Drawing Sample

Build a static `.gb` ROM from the RetroSharp video API subset:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out drawing.gb drawing.rs
```

Preview with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=drawing-retroarch.png \
  drawing.gb
```

Supported static calls:

- `video_init()`
- `palette_set(index, color)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Parameterless helper functions can group those calls.

For the first runtime sprite/scroll loop sample, see `../gameboy-runner`.
