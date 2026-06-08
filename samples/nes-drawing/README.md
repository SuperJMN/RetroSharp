# NES Drawing Sample

Sample Layer: `target-intrinsic`

Build a static iNES ROM from the RetroSharp video API subset:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out drawing.nes drawing.rs
```

Preview with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/fceumm_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=drawing-retroarch.png \
  drawing.nes
```

Supported target-intrinsic calls in this first slice:

- `video_init()`
- `palette_set(index, color)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Parameterless helper functions can group those calls.
