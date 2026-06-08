# NES Target

Status: experimental, intentionally narrow.

The NES target currently compiles a constrained static drawing subset directly to an iNES ROM and supports the first tick-based input and logical sprite runtime helpers. It does not yet support camera movement, full Game Boy runner parity, or frame-by-frame background streaming.

See `ArchitectureRoadmap.md` for the persistent architecture roadmap that separates the RetroSharp language, portable 2D SDK, and target intrinsics.

## Target Capabilities

The NES target exposes `NesTarget.Capabilities` for portable 2D capability checks.

| Capability | Value |
| --- | --- |
| Target name | `nes` |
| Screen pixels | 256x240 |
| Visible tile grid | 32x30 |
| Tile size | 8x8 |
| Background buffer | 32x30 tiles for the current static drawing path |
| Fine scroll | Not declared supported by the current backend |
| Background tile write budget | 0 runtime tile writes per frame |
| Attribute write budget | 0 runtime attribute writes per frame |
| Hardware sprites | 64 total, 8 per scanline |
| Sprite size modes | 8x8 and 8x16 hardware modes |
| Sprite palettes | 4 object palette slots |
| Background palettes | 4 background palette slots |
| Sprite transforms | Flip X and Flip Y hardware flags |
| HUD modes | None declared portable support yet |

The descriptor records NES sprite and palette hardware limits. Runtime sprite lowering is implemented only for the current JSON logical sprite spike. Portable SDK operations that need scroll axes, per-frame nametable writes, attribute writes, or HUD support must fail capability checks before reaching NES backend code.

## Supported Video API

Static setup calls:

- `video_init()`
- `palette_set(index, color)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Parameterless helper functions can group those calls.

The current target stores one visible 32x30 nametable shape and uploads it during startup. `tilemap_fill(...)` rejects rectangles outside that visible area.

## Supported Runtime API

The NES runtime spike supports byte-backed local variables, assignment, `while (true)`, `video_wait_vblank()`, `input_poll()`, `sprite_asset(...)`, `sprite_draw(...)`, and these tick-based button helpers:

- `button_down(button)`
- `button_just_pressed(button)`
- `button_just_released(button)`
- `button_hold_ticks(button)`

`input_poll()` snapshots the previous controller state, strobes controller port `$4016`, reads the current serial button state, and updates per-button hold counters. The logical button names match the portable Game Boy input surface: `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

`sprite_asset(name, path)` currently loads a JSON asset with a `platforms.nes.frames` variant. Each frame is an array of rows using NES color indexes `0`, `1`, `2`, and `3`. The compiler pads frames to 8x8 hardware cells, writes their tiles into CHR ROM starting at tile `6`, and rejects assets that need more than 64 hardware sprites or exceed the one-byte pattern-table tile index range.

`sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite through the NES OAM shadow page and performs OAM DMA. In this spike, `frame`, `flipX`, and `paletteSlot` are compile-time values. `flipX` is portable boolean data, not raw OAM flags. `paletteSlot` must fit the NES sprite palette slots `0..3`.
