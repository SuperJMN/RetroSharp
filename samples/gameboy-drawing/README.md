# Game Boy Drawing Sample

Sample Layer: `target-intrinsic`

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

Supported target-intrinsic static calls:

- `video.Init()`
- `palette.Set(index, color)`
- `tilemap.Set(x, y, tile)`
- `tilemap.Fill(x, y, width, height, tile)`
- `video.Present()`

Parameterless helper functions can group those calls. This sample uses top-level `const` values and a tile enum so static target-intrinsic data has names without changing the emitted setup work.

For the first runtime sprite/scroll loop sample, see `../gameboy-runner`.
