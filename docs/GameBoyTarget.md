# Game Boy Target

Status: experimental, intentionally narrow.

The Game Boy target is the first playable target. It currently compiles a constrained RetroSharp subset directly to a 32 KiB DMG ROM. This is not yet the shared IR backend path; it is a focused proving ground for the video/runtime API.

See `ArchitectureRoadmap.md` for the persistent architecture roadmap that separates the RetroSharp language, portable 2D SDK, and target intrinsics. This file tracks the current Game Boy target subset and runner milestones.

## Target Capabilities

The Game Boy target exposes `GameBoyTarget.Capabilities` for portable 2D capability checks.

| Capability | Value |
| --- | --- |
| Target name | `gb` |
| Screen pixels | 160x144 |
| Visible tile grid | 20x18 |
| Tile size | 8x8 |
| Background buffer | 32x32 tiles |
| Fine scroll | X and Y |
| Background tile write budget | 20 tile writes per frame |
| Attribute write budget | 0 per frame on the current DMG target |
| Hardware sprites | 40 total, 10 per scanline |
| Sprite size modes | 8x8 and 8x16 |
| Sprite palettes | 2 object palette slots |
| Background palettes | 1 background palette slot |
| Sprite transforms | Flip X and Flip Y |
| HUD modes | Window and sprite HUD; split-scroll HUD is not declared portable support |

## SDK Operation Boundary

`GameBoyRomCompiler.CollectSdkOperations(...)` exposes the first compiler boundary where portable 2D calls become semantic `Sdk2DOperation` records before Game Boy ROM lowering. The current boundary recognizes:

- `video_wait_vblank()` as `Sdk2DOperation.WaitFrame`
- `input_poll()` as `Sdk2DOperation.PollInput`
- `camera_set_position(x, 0)` as `Sdk2DOperation.SetCameraPosition`

`Sdk2DOperation.WaitFrame` now lowers through `GameBoySdkOperationLowerer` to the same VBlank edge wait routine previously emitted directly by `video_wait_vblank()`.

Target intrinsics and transitional helpers such as `sprite_set(...)`, `scroll_set(...)`, raw tilemap writes, and direction-specific camera movement still lower through the direct Game Boy path. Future roadmap tasks should move them only after adding the appropriate portable operation and capability checks.

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
- `map_tile_at(...)` and `map_flags_at(...)` as value expressions for runtime map queries
- `button_pressed(...)` as a value expression for joypad queries
- `button_down(...)`, `button_just_pressed(...)`, `button_just_released(...)`, and `button_hold_ticks(...)` as tick-based input value expressions
- `true` and `false`

Current numeric locals are stored as one byte in WRAM. Types wider than one byte are accepted only as source-level convenience for this prototype.

## Supported Video API

Static setup calls:

- `video_init()`
- `palette_set(index, color)`
- `object_palette_set(index, color)`
- `sprite_asset(name, path[, frameWidth, frameHeight])`
- `world_column(index, tile0, tile1, ...)`
- `world_flags(index, flags0, flags1, ...)`
- `map_column(index, tile0, tile1, ...)`
- `world_map(width, streamY, height)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Runtime calls:

- `video_wait_vblank()`
- `input_poll()`
- `scroll_set(x, y)`
- `camera_init(mapWidth, streamY, streamHeight)`
- `camera_set_position(x, y)`
- `camera_apply()`
- `camera_move_right()`
- `camera_move_left()`
- `camera_tile_column_at(screenColumn)`
- `camera_span_tile_at(screenX, widthPx, row)`
- `camera_span_has_tile(screenX, widthPx, row, tile)`
- `camera_span_has_flags(screenX, widthPx, row, flags)`
- `sprite_width(name)`
- `sprite_set(id, x, y, tile, flags)`
- `sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])`
- `tilemap_fill_column(column, y, height, tile)`
- `map_stream_column(targetColumn, sourceColumn, y, height)`
- `map_tile_at(sourceColumn, row)`
- `map_flags_at(sourceColumn, row)`
- `animation_clip(name, firstFrame, duration...)`
- `animation_frame(name, tick)`
- `button_pressed(button)`
- `button_down(button)`
- `button_just_pressed(button)`
- `button_just_released(button)`
- `button_hold_ticks(button)`

`scroll_set(x, y)` writes `x` to `SCX` and `y` to `SCY`. On Game Boy this gives hardware background scroll over the 256x256 background map.

`camera_init(mapWidth, streamY, streamHeight)` initializes the current world camera. It keeps 16-bit camera X/Y positions in WRAM, tracks sub-tile movement on both axes, tracks the circular Game Boy background map edges for horizontal streaming, tracks top/bottom background and source rows for vertical streaming, and seeds source-map columns from the generated world-map row data. `mapWidth`, `streamY`, and `streamHeight` are compile-time constants. Call it after declaring the source map and before `camera_apply()`, `camera_move_right()`, `camera_move_left()`, or `camera_tile_column_at(...)`.

`camera_set_position(x, y)` is the current position-based camera API candidate. `x` and `y` can be byte-backed expressions such as constants or local variables. The current Game Boy lowering compares the requested position with the current camera state and moves at most one pixel per axis toward it on each call. X tile-boundary crossings reuse the existing column streaming paths. Y tile-boundary crossings stream one visible row up or down from the generated world-map row data, writing 20 background tiles per streamed row to stay within the Game Boy target budget. A camera operation that can move both axes in the same frame is rejected for Game Boy until a scheduler or split-frame policy can keep the combined column-and-row writes within budget.

`camera_apply()` writes the camera X low byte to `SCX` and the camera Y low byte to `SCY`. `camera_move_right()` and `camera_move_left()` move the world camera horizontally by one pixel. When horizontal movement crosses an 8 px tile boundary, the backend streams the next source map column into the circular Game Boy background map. `camera_tile_column_at(screenColumn)` returns the source-map column currently visible at a screen tile column, wrapped by the configured map width.

`camera_span_tile_at(screenX, widthPx, row)` checks every source-map tile column covered by a horizontal pixel span and returns the first non-zero tile id, or `0` when the span is empty. `camera_span_has_tile(screenX, widthPx, row, tile)` returns `1` when any covered source-map tile matches `tile`, or `0` otherwise. `camera_span_has_flags(screenX, widthPx, row, flags)` checks the generated collision flag table for any matching flag bit and returns `1` or `0`. `screenX`, `widthPx`, `row`, `tile`, and `flags` are compile-time values in this prototype; `widthPx` can use `sprite_width(name)` so collision follows the logical width declared by `sprite_asset(...)`.

`tilemap_fill_column(column, y, height, tile)` writes a vertical run into the background tilemap at runtime. It is the current primitive for streaming new map columns as the camera advances. The `column` and `tile` arguments can be simple runtime expressions; `y` and `height` are compile-time constants in this prototype.

`world_column(index, ...)` defines one source-level world tile-id column. `world_flags(index, ...)` defines the matching collision flag column using `0` for `Empty`, `1` for `Solid`, `2` for `Hazard`, and `4` for `Platform`; flag values can be combined. `world_map(width, streamY, height)` builds the current portable `WorldMap2D` resource from those columns, fills the initial visible Game Boy background rows from that resource, and generates the source-map ROM row tables used by camera streaming and collision flag reads. The Game Boy runner uses this path so the starting scene, streamed terrain, and collision flags share one source.

`map_column(index, ...)` remains supported as a transitional compatibility call. New runner-level world data should use `world_column(...)` and `world_flags(...)` so visual setup, streaming data, and collision flags can share the same world resource.

`map_tile_at(sourceColumn, row)` reads one tile id from the source-level map column data and returns it as a byte expression. `map_flags_at(sourceColumn, row)` reads the generated collision flag byte for the same source coordinate. The current prototype expects `row` to be a compile-time constant and leaves column wrapping to the source program. This is enough for simple terrain collision, for example `if (map_flags_at(column, 2) != 0) { ... }`.

`input_poll()` snapshots the joypad for the current game tick. Call it once after `video_wait_vblank()` before using the tick-based input helpers. The Game Boy backend reads each selected `JOYP` row several times before latching it and deselects both rows afterward, which avoids stale row reads on original DMG hardware. `button_down(button)` returns `1` while the button is down in the current snapshot, `button_just_pressed(button)` returns `1` only on the up-to-down transition, `button_just_released(button)` returns `1` only on the down-to-up transition, and `button_hold_ticks(button)` returns the number of consecutive polls the button has been held, saturating at `255` and resetting to `0` when released. This supports variable-height jumps without introducing real-time clocks.

`button_pressed(button)` remains supported as a compatibility direct joypad read and returns `1` when the named button is currently pressed or `0` otherwise. New gameplay code should prefer `input_poll()` with the tick-based helpers. Supported names are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

`sprite_asset(name, path, frameWidth, frameHeight)` loads an editable PNG sprite sheet relative to the `.rs` file. Frames are laid out horizontally, which maps directly to a simple Aseprite export. Transparent pixels become Game Boy sprite color `0`; up to three opaque colors become sprite colors `1`, `2`, and `3`. The sample palette maps `#E0F8D0` to `1`, `#88C070` to `2`, and `#346856` to `3`; grayscale exports also map white to `1`, gray to `2`, and black to `3`.

For example, a two-frame 16x16 runner can be referenced as:

```c
sprite_asset(player_run, "assets/player-run.gb.png", 16, 16);
```

In Aseprite, edit at 1x and keep the transparent background. Export with a horizontal sheet:

```bash
aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
```

PNG frame dimensions do not need to be hardware-sized. The compiler pads each frame to Game Boy 8x16 hardware cells internally, so a 16x27 logical sprite is emitted as a 16x32 metasprite. Compiled assets now expose portable sprite metadata: logical width and height, a default origin at `(0, 0)`, a default full-size hitbox, palette slot count, and a default animation clip spanning the loaded frames. Animation clips carry frame indices, per-frame durations, frame-start ticks, and total duration; the default loaded-asset clip assigns one tick to each frame until authored animation data exists. Target lowering still uses the existing Game Boy metasprite pieces and tile data.

`sprite_asset(name, path)` is still supported as a transitional legacy path for the experimental JSON asset format. That format uses a `platforms.gb.frames` array. Each frame is a list of rows, and each character is a Game Boy color index from `0` to `3`.

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

`sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite. The compiler splits the selected Game Boy variant into 8x16 hardware sprites, generates tile data, assigns OAM entries, and treats `frame` as a logical animation frame index. Logical sizes like 16x27 are valid; the emitted hardware footprint is rounded up to 8x16 cells. The optional `flipX` argument is a portable boolean: any non-zero local value mirrors the logical metasprite horizontally, and the Game Boy backend lowers that choice to the OAM X-flip bit internally. The optional `paletteSlot` argument is a portable sprite palette slot validated against the target descriptor; Game Boy supports slots `0` and `1` and lowers slot `1` to the OBP1 OAM attribute bit. Raw OAM attribute bytes remain available through the target-intrinsic `sprite_set(...)` API, not through portable `sprite_draw(...)`.

`animation_clip(name, firstFrame, duration...)` declares a portable animation clip resource. The first argument is a clip identifier, the second is the first logical frame index, and the remaining arguments are per-frame durations in ticks. `animation_frame(name, tick)` returns the logical frame for a tick value and loops by the clip's total duration. Game Boy lowering keeps runtime state explicit in source, reduces dynamic ticks modulo the clip duration, then checks frame boundaries in declaration order.

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
- [x] Add tick-based input helpers for edge-triggered and variable-height jump behavior.
- [x] Make the Game Boy runner a playable loop: hitbox-based ground checks, holes, and reset/fail state.
- [x] Add a horizontal world-camera helper that owns scroll state and map-column streaming.
- [x] Add target capability descriptors for Game Boy and NES.
- [x] Add the first observable SDK operation boundary for frame wait and input poll.
- [x] Lower the first portable SDK operation through the shared operation path.
- [x] Define the portable world map resource shape for tile ids and collision flags.
- [x] Generate the runner's initial visible tilemap from world data.
- [x] Generate the runner's streaming map data from the same world resource.
- [x] Generate collision flag tables from the same world resource.
- [x] Add the first position-based camera API and SDK operation boundary.
- [x] Reuse the existing horizontal camera runtime from `camera_set_position(...)`.
- [x] Replace direction-specific camera helpers with a position-based camera API in the runner.
- [x] Unify visual map data, streaming data, and collision flags into one world resource.
- [x] Extend camera position state and `camera_apply()` to vertical scroll.
- [x] Stream visible background rows when vertical camera movement crosses tile boundaries.
- [x] Preserve logical sprite metadata for loaded Game Boy sprite assets.
- [x] Replace raw `sprite_draw` flags with a portable `flipX` boolean.
- [x] Add logical sprite palette slot selection to `sprite_draw`.
- [x] Add animation clip data and looping `animation_frame(...)` lookup.
- [x] Migrate the runner's run animation to an explicit tick plus `animation_frame(...)`.
- [ ] Add a NES parity spike for logical sprites, input, camera scroll, and tile collision.
- [ ] Add a cross-target runner sample that can compile for both Game Boy and NES.

## Progress Snapshot

Landed on 2026-06-01:

- The Game Boy runner can draw an editable 16x27 Aseprite/PNG logical sprite and lower it to 8x16 hardware sprites.
- The runner scrolls the background through the camera runtime and streams generated world-map data through `map_stream_column(...)`.
- `map_tile_at(...)`, `map_flags_at(...)`, and `camera_span_has_flags(...)` let RetroSharp source query generated world-map data for simple tile collision.
- `button_pressed(...)` lets RetroSharp source query the Game Boy joypad.
- The sample now has a small gameplay loop: gravity, simple ground collision, running animation, and A-button jump.
- The compiler subset grew just enough for that loop: runtime-local addition, relational conditions against constants, value-returning runtime intrinsics, and byte-backed state.
- Generated runner screenshots are not tracked as source artifacts; regenerate them with RetroArch when needed.

Landed after the initial runner loop:

- `input_poll()`, `button_down(...)`, `button_just_pressed(...)`, `button_just_released(...)`, and `button_hold_ticks(...)` provide a tick-based input surface.
- The Game Boy runner uses the new input helpers for edge-triggered, variable-height jumping: holding A extends upward impulse for a bounded number of ticks, and releasing A cuts the extension.
- The runner's horizontal scroll, column streaming, and run animation now advance while D-pad right or left is held; when no horizontal input is active, the sprite returns to its idle frame.
- The runner now draws idle, run, and jump states through a single player sprite sheet so the same OAM slots are updated every frame; the jump frame is used whenever the actor is airborne.
- `sprite_draw` accepts optional portable `flipX` and `paletteSlot` values; the runner uses them to make the same idle, run, and jump frames face left while preserving the last facing direction and selecting a logical sprite palette slot.
- `animation_clip(...)` and `animation_frame(...)` now express the runner's run cycle while keeping `animTick`, idle, and jump state explicit in source.

Landed after the playable-loop pass:

- The runner checks the logical player width against each covered foot column instead of using a single source-map tile.
- The initial visible background matches the same source-map pattern used for streaming, including a visible multi-column hole and failure tile.
- Separate left/right streaming cursors keep the background stable when changing direction.
- The reset path restores actor position, velocity, animation, facing, jump, and movement state without rebasing the scrolled background.

Landed after the camera-runtime pass:

- `camera_init(...)`, `camera_apply()`, `camera_move_right()`, `camera_move_left()`, and `camera_tile_column_at(...)` lift horizontal scrolling one layer above raw `SCX` writes and hand-managed streaming cursors.
- The camera runtime owns 16-bit world X, sub-tile scroll state, circular background-map edge columns, source-map edge columns, and 8 px column streaming.
- The runner now asks the camera for source-map foot columns instead of wrapping `screenLeftColumn` manually.
- `camera_span_has_flags(...)` and `sprite_width(...)` let the runner collide against every tile column covered by a logical sprite width using generated world flags instead of hardcoded tile ids.

## Next Milestones

1. Implement the first architecture slice from `ArchitectureRoadmap.md`: capability descriptors, shared SDK operation model, and unified world map resource.
2. Keep the GB runner as the acceptance sample while removing target-specific leakage from portable calls.
3. Move collision from camera span helpers to world-coordinate tile flag queries.
4. Spike the same runner contract on NES after the shared SDK operation model exists.
