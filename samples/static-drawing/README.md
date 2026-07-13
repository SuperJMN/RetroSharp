# Static Drawing Sample

Sample Layer: `target-intrinsic`

Build the same static drawing source for either declared target:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out drawing.gb drawing.rs
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out drawing.nes drawing.rs
```

Preview the emitted ROMs with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=drawing.gb-retroarch.png \
  drawing.gb

flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/fceumm_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=drawing.nes-retroarch.png \
  drawing.nes
```

The source exercises the static calls shared by the two target lowerers:

- `Video.Init()`
- `Palette.Set(index, color)`
- `Tilemap.Set(x, y, tile)`
- `Tilemap.Fill(x, y, width, height, tile)`
- `Video.Present()`

Private `[target("gb")]` and `[target("nes")]` helper variants retain the exact
palette values, coordinates, and map dimensions of the original target fixtures
inside one physical source file. Target selection removes the non-matching
variant at compile time, so the sample adds no runtime dispatch or public
hardware-specific API. The manifest remains the source of truth for which
targets the neutral sample identity supports.

Parameterless helper functions group the calls. Top-level `const` values and a
tile enum name the static data without changing the emitted setup work.

For the first runtime sprite/scroll loop sample, see `../runner`.
