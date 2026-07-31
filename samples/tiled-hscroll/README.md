# Tiled Stage1 Scroll Samples

Sample Layer: `target-acceptance`

This sample family isolates packed Tiled scrolling. It has no player, sprites,
audio, input, gravity, or collision queries: each loop applies the prepared
edge, advances the camera at its declared cadence, and stays inside its declared
Game Boy/NES limits.

Four stable sample identities use the same visual-only tileset:

- `tiled-hscroll-short` preserves the first 64 columns of `stage1`, expanded to
  128 hardware-tile columns, with a maximum shared camera X of 768 pixels.
- `tiled-hscroll-full` preserves all 156 `stage1` columns, expanded to 312
  hardware-tile columns, with a maximum shared camera X of 2240 pixels. It moves
  one pixel per gameplay tick.
- `tiled-hscroll-offset` uses that same full fixture and one-pixel cadence, but
  expands the camera height from 30 to 40 hardware rows so horizontal streaming
  runs at a non-zero vertical camera offset without any runner gameplay systems.
  Its focused maximum X is 96 pixels, which covers rightward streaming, reversal,
  and return inside the 420-frame acceptance window.
- `tiled-stage1-diagonal-speed-sweep` traverses the unshifted stable
  `samples/shared/platformer-assets/maps/stage1.tmx` from left to right three
  times while bouncing vertically between zero and the target's real
  `Camera.VerticalScrollMax()`. Its target-private right limit is X 2336 on
  Game Boy and X 2240 on NES, so the final viewport reaches the map edge on each
  screen width. The slow pass advances one pixel every two VBlanks, the medium
  pass advances one pixel per VBlank, and the fast pass advances two pixels per
  VBlank.
  Two-second holds at each endpoint and a two-pixel-per-VBlank rewind separate
  each pass, so every measured pass starts at `(0, 0)` without a discontinuous
  camera jump. After the fast pass and rewind, the sequence repeats from slow.

The two 20-cell-high fixtures move the bottom 15 authored `stage1` rows into
the 30-hardware-row camera window and retain five empty staging rows below it.
This keeps the packed-map geometry stable while `Camera.VerticalScrollMax()`
selects the same bottom-focused picture on both targets: visible Y 96 on Game
Boy and logical Y 0 on NES. The NES target recognizes this fixed 30-row camera
window even though the backing Tiled map is 40 rows tall, and applies its
render-only 8 px bottom-overscan inset; the five empty staging rows provide the
clean wrapped strip below the shifted scene. The offset sample selects all 40
shifted rows instead. The speed sweep also initializes a 40-row camera, but over
the unshifted shared map; its valid Y interval is 0..176 on Game Boy and 0..80
on NES. The three horizontal identities hold X at zero for their first 64
gameplay ticks; the speed sweep holds each endpoint for 120 VBlanks. The dense
scenery and floor make stale columns, corrupt palettes, or vertical
row-placement errors conspicuous.

The three horizontal identities use the local `stage1-visual.tsx`, which
deliberately omits collision object groups. The speed sweep uses the stable
shared stage and tileset instead, but still performs no collision queries. The
automated functional scenarios for the horizontal identities run their exact
tracked ROMs, retain every visible frame, compare tile and palette identity with
the authored Tiled map, and reject unsafe video writes. The speed sweep is
deliberately a visual diagnostic ROM: its terminal tile-integrity verdict comes
from watching the three complete passes on the affected target.

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

Build the diagonal speed-sweep Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out stage1-diagonal-speed-sweep.gb stage1-diagonal-speed-sweep.rs
```

Build the diagonal speed-sweep NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out stage1-diagonal-speed-sweep.nes stage1-diagonal-speed-sweep.rs
```
