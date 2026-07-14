# Tiled Horizontal Scroll Sample

Sample Layer: `target-acceptance`

This sample family isolates packed Tiled horizontal scrolling. It has no player,
sprites, audio, input, gravity, or collision queries: each loop applies the
prepared column, advances the camera at its declared cadence, and reverses only
at the shared Game Boy/NES horizontal limit.

Three stable sample identities use the same visual-only tileset:

- `tiled-hscroll-short` preserves the first 64 columns of `stage1`, expanded to
  128 hardware-tile columns, with a maximum shared camera X of 768 pixels.
- `tiled-hscroll-full` preserves all 156 `stage1` columns, expanded to 312
  hardware-tile columns, with a maximum shared camera X of 2240 pixels. It moves
  one pixel per gameplay tick.
- `tiled-hscroll-offset` uses that same full fixture and one-pixel cadence, but
  expands the camera height from 30 to 40 hardware rows so horizontal streaming
  runs at a non-zero vertical camera offset without any runner gameplay systems.

The two 20-cell-high fixtures move the bottom 15 authored `stage1` rows into
the 30-hardware-row camera window and retain five empty staging rows below it.
This keeps the packed-map geometry stable while `Camera.VerticalScrollMax()`
selects the same bottom-focused picture on both targets: visible Y 96 on Game
Boy and logical Y 0 on NES. The NES target recognizes this fixed 30-row camera
window even though the backing Tiled map is 40 rows tall, and applies its
render-only 8 px bottom-overscan inset; the five empty staging rows provide the
clean wrapped strip below the shifted scene. The offset sample selects all 40
rows instead, producing Y 176 on Game Boy and Y 80 on NES. The first 64 gameplay
ticks hold X
at zero so that framing settles before horizontal acceptance begins. The dense
scenery and floor make stale columns, corrupt palettes, or vertical row-placement
errors conspicuous, and each horizontal edge is held for one tick before
reversal.

The local `stage1-visual.tsx` deliberately omits the collision object groups
from the runner tileset. This keeps the sample focused on column preparation,
publication cadence, and visible tile integrity. The functional scenarios run
the exact tracked ROMs, retain every visible frame, compare tile and palette
identity with the authored Tiled map, reject unsafe video writes, and run past
the right edge far enough to prove the reversal and return path. The short
windows retain 1024 frames, the zero-offset complete-map windows retain 2584
frames, and the focused non-zero-Y window retains 420 frames. The latter detects
the NES failure where floor rows 18/19 are projected into rows 8/9 as soon as
horizontal streaming begins.

Build the short Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out hscroll-short.gb hscroll-short.rs
```

Build the short NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out hscroll-short.nes hscroll-short.rs
```

Build the full Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out hscroll-full.gb hscroll-full.rs
```

Build the full NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out hscroll-full.nes hscroll-full.rs
```

Build the offset Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out hscroll-offset.gb hscroll-offset.rs
```

Build the offset NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out hscroll-offset.nes hscroll-offset.rs
```
