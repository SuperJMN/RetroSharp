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
- `+` and `-` when one operand is constant
- `==` and `!=` conditions when one side is constant
- `true` and `false`

Current numeric locals are stored as one byte in WRAM. Types wider than one byte are accepted only as source-level convenience for this prototype.

## Supported Video API

Static setup calls:

- `video_init()`
- `palette_set(index, color)`
- `object_palette_set(index, color)`
- `map_column(index, tile0, tile1, ...)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Runtime calls:

- `video_wait_vblank()`
- `scroll_set(x, y)`
- `sprite_set(id, x, y, tile, flags)`
- `tilemap_fill_column(column, y, height, tile)`
- `map_stream_column(targetColumn, sourceColumn, y, height)`

`scroll_set(x, y)` writes `x` to `SCX` and `y` to `SCY`. On Game Boy this gives hardware background scroll over the 256x256 background map.

`tilemap_fill_column(column, y, height, tile)` writes a vertical run into the background tilemap at runtime. It is the current primitive for streaming new map columns as the camera advances. The `column` and `tile` arguments can be simple runtime expressions; `y` and `height` are compile-time constants in this prototype.

`map_column(index, ...)` defines a source-level map column. The compiler stores map rows in ROM tables. `map_stream_column(targetColumn, sourceColumn, y, height)` reads one source column from those ROM tables and writes it into the circular Game Boy background map at runtime.

## Short-Term Checklist

- [x] Parse `while`.
- [x] Generate a real Game Boy runtime loop.
- [x] Move sprites by writing OAM during the loop.
- [x] Add `scroll_set(x, y)` over Game Boy `SCX`/`SCY`.
- [x] Build a runner sample with a fixed actor and scrolling background.
- [x] Stream new background columns every 8 pixels.
- [x] Represent maps as source data instead of ad hoc `tilemap_set` calls.
- [ ] Add collision against a simple tile row.
- [ ] Evaluate whether the same `scroll_set` API maps cleanly to NES.

The next meaningful milestone is collision against a simple tile row. The runner sample can now stream source-level map columns, but the actor still has no gameplay interaction with those tiles.
