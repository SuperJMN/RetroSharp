# NES Target

Status: experimental, intentionally narrow.

The NES target currently compiles a constrained static drawing subset directly to an iNES ROM and supports the first tick-based input, logical sprite, and horizontal camera runtime helpers. It does not yet support vertical camera movement, full Game Boy runner parity, or frame-by-frame background streaming.

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
| Fine scroll | Horizontal only |
| Background tile write budget | 0 runtime tile writes per frame |
| Attribute write budget | 0 runtime attribute writes per frame |
| Hardware sprites | 64 total, 8 per scanline |
| Sprite size modes | 8x8 and 8x16 hardware modes |
| Sprite palettes | 4 object palette slots |
| Background palettes | 4 background palette slots |
| Sprite transforms | Flip X and Flip Y hardware flags |
| HUD modes | None declared portable support yet |

The descriptor records NES sprite, palette, and horizontal fine-scroll support. Runtime sprite lowering is implemented only for the current JSON logical sprite spike. The current camera path can update horizontal scroll but cannot stream new nametable columns yet, so portable SDK operations that need vertical scroll, per-frame nametable writes, attribute writes, or HUD support must fail capability checks before reaching NES backend code.

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

The NES runtime spike supports byte-backed local variables, assignment, `while (true)`, `video_wait_vblank()`, `input_poll()`, `world_column(...)`, `world_flags(...)`, `world_map(...)`, `camera_init(...)`, `camera_set_position(...)`, `camera_apply()`, `sprite_asset(...)`, `sprite_draw(...)`, and these tick-based button helpers:

- `button_down(button)`
- `button_just_pressed(button)`
- `button_just_released(button)`
- `button_hold_ticks(button)`

`input_poll()` snapshots the previous controller state, strobes controller port `$4016`, reads the current serial button state, and updates per-button hold counters. The logical button names match the portable Game Boy input surface: `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

`sprite_asset(name, path)` currently loads a JSON asset with a `platforms.nes.frames` variant. Each frame is an array of rows using NES color indexes `0`, `1`, `2`, and `3`. The compiler pads frames to 8x8 hardware cells, writes their tiles into CHR ROM starting at tile `6`, and rejects assets that need more than 64 hardware sprites or exceed the one-byte pattern-table tile index range.

`sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite through the NES OAM shadow page and performs OAM DMA. In this spike, `frame`, `flipX`, and `paletteSlot` are compile-time values. `flipX` is portable boolean data, not raw OAM flags. `paletteSlot` must fit the NES sprite palette slots `0..3`.

`world_column(...)`, `world_flags(...)`, and `world_map(width, streamY, height)` build the initial visible nametable from unified world resources. In this spike, `width` must fit the visible 32-column nametable because runtime column streaming is not implemented.

`camera_init(mapWidth, streamY, streamHeight)` enables the horizontal camera path for the current world map. `camera_set_position(x, 0)` stores the horizontal scroll byte, and `camera_apply()` writes horizontal scroll followed by zero vertical scroll to `$2005`. Any non-zero or runtime Y position is rejected with a NES capability error until vertical movement and streaming have a budgeted lowering.

## HUD Decision

NES HUD support is intentionally undeclared in the current descriptor. `hud_set_tile(window, x, y, tile)` is parsed far enough to fail through `TargetCapabilityChecks.RequireHudMode(...)` with a NES capability error, while `hud_set_tile(none, ...)` is treated as disabled HUD and compiles as a no-op.

Split-scroll HUD needs a timed scroll-change path that the current NES spike does not have. A reserved nametable band would still share the current horizontal scroll and would not behave as a stable HUD. Sprite HUD needs a separate tile-as-sprite contract and sprite-budget policy. Until one of those paths is implemented deliberately, `NesTarget.Capabilities.HudModes` remains `HudMode.None`.

## Cross-Target Sample

`samples/cross-target-camera/camera.rs` is the first shared source sample that builds for both Game Boy and NES. It uses unified world data, tick input, horizontal camera positioning, and JSON logical sprite variants under `platforms.gb` and `platforms.nes`.

The sample intentionally avoids raw target calls such as `sprite_set(...)`, `scroll_set(...)`, `tilemap_set(...)`, `tilemap_fill(...)`, `map_stream_column(...)`, and `object_palette_set(...)`. It does not imply support for NES vertical camera movement, runtime nametable streaming, collision queries, runtime animation, or HUD APIs yet.

Sample portability is tracked in `samples/manifest.json`. The NES drawing sample is classified as `target-intrinsic`, not as a portable SDK sample, because it demonstrates raw static `tilemap_*` setup calls for this target.
