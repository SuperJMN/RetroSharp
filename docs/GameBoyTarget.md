# Game Boy Target

Status: experimental, intentionally narrow.

The Game Boy target is the first playable target. It currently compiles a constrained RetroSharp subset directly to a 32 KiB DMG ROM. This is not yet the shared IR backend path; it is a focused proving ground for the video/runtime API.

## Supported Runtime Subset

- `void main()`
- Local byte-backed variables declared as `i8`, `u8`, `i16`, `u16`, or `bool`
- Constant initializers
- Assignment to local variables
- `while`
- `if`
- `+` between byte-backed runtime expressions
- `-` when one operand is constant
- `==`, `!=`, `<`, `<=`, `>`, and `>=` conditions when one side is constant
- `map_tile_at(...)` as a value expression for runtime map queries
- `button_pressed(...)` as a value expression for joypad queries
- `true` and `false`

Current numeric locals are stored as one byte in WRAM. Types wider than one byte are accepted only as source-level convenience for this prototype.

## Supported Video API

Static setup calls:

- `video_init()`
- `palette_set(index, color)`
- `object_palette_set(index, color)`
- `sprite_asset(name, path[, frameWidth, frameHeight])`
- `map_column(index, tile0, tile1, ...)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Runtime calls:

- `video_wait_vblank()`
- `scroll_set(x, y)`
- `sprite_set(id, x, y, tile, flags)`
- `sprite_draw(name, x, y, frame)`
- `tilemap_fill_column(column, y, height, tile)`
- `map_stream_column(targetColumn, sourceColumn, y, height)`
- `map_tile_at(sourceColumn, row)`
- `button_pressed(button)`

`scroll_set(x, y)` writes `x` to `SCX` and `y` to `SCY`. On Game Boy this gives hardware background scroll over the 256x256 background map.

`tilemap_fill_column(column, y, height, tile)` writes a vertical run into the background tilemap at runtime. It is the current primitive for streaming new map columns as the camera advances. The `column` and `tile` arguments can be simple runtime expressions; `y` and `height` are compile-time constants in this prototype.

`map_column(index, ...)` defines a source-level map column. The compiler stores map rows in ROM tables. `map_stream_column(targetColumn, sourceColumn, y, height)` reads one source column from those ROM tables and writes it into the circular Game Boy background map at runtime.

`map_tile_at(sourceColumn, row)` reads one tile id from the source-level map column data and returns it as a byte expression. The current prototype expects `row` to be a compile-time constant and leaves column wrapping to the source program. This is enough for simple terrain collision, for example `if (map_tile_at(column, 2) != 0) { ... }`.

`button_pressed(button)` reads the Game Boy joypad and returns `1` when the named button is currently pressed or `0` otherwise. Supported names are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

`sprite_asset(name, path, frameWidth, frameHeight)` loads an editable PNG sprite sheet relative to the `.rs` file. Frames are laid out horizontally, which maps directly to a simple Aseprite export. Transparent pixels become Game Boy sprite color `0`; up to three opaque colors become sprite colors `1`, `2`, and `3`. The sample palette maps `#E0F8D0` to `1`, `#88C070` to `2`, and `#346856` to `3`; grayscale exports also map white to `1`, gray to `2`, and black to `3`.

For example, a two-frame 16x16 runner can be referenced as:

```c
sprite_asset(player_run, "assets/player-run.gb.png", 16, 16);
```

In Aseprite, edit at 1x and keep the transparent background. Export with a horizontal sheet:

```bash
aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
```

PNG frame dimensions do not need to be hardware-sized. The compiler pads each frame to Game Boy 8x16 hardware cells internally, so a 16x27 logical sprite is emitted as a 16x32 metasprite.

`sprite_asset(name, path)` is still supported for the experimental JSON asset format. That format uses a `platforms.gb.frames` array. Each frame is a list of rows, and each character is a Game Boy color index from `0` to `3`.

```json
{
  "platforms": {
    "gb": {
      "frames": [
        [
          "0022222022200000",
          "0222222222220000"
        ]
      ]
    }
  }
}
```

`sprite_draw(name, x, y, frame)` draws a logical sprite. The compiler splits the selected Game Boy variant into 8x16 hardware sprites, generates tile data, assigns OAM entries, and treats `frame` as a logical animation frame index. Logical sizes like 16x27 are valid; the emitted hardware footprint is rounded up to 8x16 cells.

## Short-Term Checklist

- [x] Parse `while`.
- [x] Generate a real Game Boy runtime loop.
- [x] Move sprites by writing OAM during the loop.
- [x] Add `scroll_set(x, y)` over Game Boy `SCX`/`SCY`.
- [x] Build a runner sample with a fixed actor and scrolling background.
- [x] Stream new background columns every 8 pixels.
- [x] Represent maps as source data instead of ad hoc `tilemap_set` calls.
- [x] Load an editable logical sprite asset and lower it to Game Boy metasprites.
- [x] Add collision against a simple tile row.
- [x] Add input-driven jump from the Game Boy joypad.
- [ ] Evaluate whether the same `scroll_set` API maps cleanly to NES.
- [ ] Define a portable video/input API contract shared by Game Boy and NES.
- [ ] Add a NES parity spike for logical sprites, input, scroll, and tile collision.
- [ ] Move the Game Boy runtime intrinsics toward a shared lowering surface instead of direct target-only calls.
- [ ] Add a cross-target runner sample that can compile for both Game Boy and NES.

## Progress Snapshot

Landed on 2026-06-01:

- The Game Boy runner can draw an editable 16x27 Aseprite/PNG logical sprite and lower it to 8x16 hardware sprites.
- The runner scrolls the background with `scroll_set(...)` and streams source-level `map_column(...)` data through `map_stream_column(...)`.
- `map_tile_at(...)` lets RetroSharp source query map data for simple tile collision.
- `button_pressed(...)` lets RetroSharp source query the Game Boy joypad.
- The sample now has a small gameplay loop: gravity, simple ground collision, running animation, and A-button jump.
- The compiler subset grew just enough for that loop: runtime-local addition, relational conditions against constants, value-returning runtime intrinsics, and byte-backed state.
- Generated runner screenshots are not tracked as source artifacts; regenerate them with RetroArch when needed.

## Next Milestones

1. Define the portable runtime surface before adding more target-specific APIs. The current candidates are `video_wait_vblank`, `scroll_set`, `sprite_asset`, `sprite_draw`, `map_column`, `map_stream_column`, `map_tile_at`, and `button_pressed`.
2. Decide how target capability differences are reported: hard compiler error, documented degradation, or platform-specific variant.
3. Spike the same runner contract on NES, starting with `scroll_set` and `button_pressed`, then logical sprites and tile collision.
4. Extract shared concepts from the Game Boy direct compiler so the ROM targets stop growing as isolated one-off backends.
5. Keep the GB runner as the acceptance sample: if it cannot still compile and run, the portable runtime surface is not stable enough.
