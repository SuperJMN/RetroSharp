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

- `video.Init()`
- `palette.Set(index, color)`
- `tilemap.Set(x, y, tile)`
- `tilemap.Fill(x, y, width, height, tile)`
- `video.Present()`

Parameterless helper functions can group those calls. This sample uses top-level `const` values and a tile enum so static target-intrinsic data has names without changing the emitted setup work.
