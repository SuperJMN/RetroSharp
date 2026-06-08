# Portable 2D SDK v1 Reference

Status: draft v1 reference for the current portable 2D surface.

This document is the first public API reference for the portable 2D SDK layer. It describes calls that are intended to represent game concepts before target lowering. Game Boy and NES may still support target-specific or transitional calls, but those calls are not part of SDK v1 unless listed here.

The SDK is portable by capability, not by identical machine behavior. A call is valid only when the selected target descriptor can support the requested mode, budget, and asset shape. Unsupported features must fail during compilation with explicit target diagnostics.

## SDK v1 Surface

### Frame and input

| Signature | Semantics |
| --- | --- |
| `video_init()` | Initialize the target video path for samples that need explicit setup. This remains a narrow setup call while the SDK frame lifecycle is stabilized. |
| `video_wait_vblank()` | Wait for the next frame boundary. This maps to `Sdk2DOperation.WaitFrame`. |
| `input_poll()` | Snapshot controller state for the current tick. Call once per frame before tick-based button helpers. |
| `button_down(button)` | Return `1` while the named button is down in the current poll. |
| `button_just_pressed(button)` | Return `1` only on the up-to-down edge. |
| `button_just_released(button)` | Return `1` only on the down-to-up edge. |
| `button_hold_ticks(button)` | Return consecutive polls held, saturating at `255` and resetting to `0` on release. |

Supported logical buttons are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

### World data and collision

| Signature | Semantics |
| --- | --- |
| `world_column(index, tile0, tile1, ...)` | Define one source-level world column of tile ids. |
| `world_flags(index, flags0, flags1, ...)` | Define matching tile collision flags for a world column. |
| `world_map(width, streamY, height)` | Build the active `WorldMap2D` resource from declared columns and flags. |
| `world_tile_flags_at(worldX, worldY)` | Read collision flags by world pixel coordinates; out-of-bounds reads return `0`. |
| `collision_aabb_tiles(x, y, width, height, flags)` | Return `1` if any tile overlapped by a world-space AABB has the requested flag bits. |

World flag values are `0` empty, `1` solid, `2` hazard, and `4` platform. Values can be combined.

### Camera

| Signature | Semantics |
| --- | --- |
| `camera_init(mapWidth, streamY, streamHeight)` | Initialize camera state for the active world map. |
| `camera_set_position(x, y)` | Request a camera position in world pixels. This maps to `Sdk2DOperation.SetCameraPosition`. |
| `camera_apply()` | Apply the current camera state to the target during the frame. |

Targets may lower camera movement differently. The SDK contract is position-based; direction-specific helpers such as `camera_move_right()` and raw scroll calls such as `scroll_set(...)` are transitional or target-intrinsic APIs.

### Logical sprites and animation

| Signature | Semantics |
| --- | --- |
| `sprite_asset(name, path[, frameWidth, frameHeight])` | Declare a logical sprite asset. PNG sheets use explicit frame dimensions; JSON assets remain a transitional compatibility format. |
| `sprite_width(name)` | Return the logical sprite width known at compile time. |
| `sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])` | Draw a logical sprite frame with portable horizontal flip and logical palette-slot selection. |
| `animation_clip(name, firstFrame, duration...)` | Declare a looping frame-duration table. |
| `animation_frame(name, tick)` | Return the frame index for a tick in a declared clip. |

The `flipX` argument is a portable boolean, not a raw hardware attribute byte. `paletteSlot` is a logical sprite palette slot checked against the target descriptor.

### Optional HUD

| Signature | Semantics |
| --- | --- |
| `hud_set_tile(mode, x, y, tile)` | Declare one static HUD tile for a capability-gated HUD mode. |

Current mode names are `window`, `split_scroll`, `sprite_hud`, and `none`. `none` disables HUD and must compile as a no-op even on targets that declare no HUD support.

## Capability Requirements

The compiler must validate portable SDK calls against `Target2DCapabilities` before target lowering.

| SDK area | Required capabilities |
| --- | --- |
| Frame/input | Target runtime support for frame wait and controller polling. |
| World map | Active world dimensions must fit the target's static setup or streaming path. |
| Camera X | `ScrollAxes.Horizontal`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed column. |
| Camera Y | `ScrollAxes.Vertical`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed row. |
| Diagonal camera movement | Combined row plus column write budget must fit one frame or fail explicitly. |
| Logical sprites | Sprite count, sprite size modes, scanline limits, sprite transforms, and palette-slot count must fit the lowered metasprite. |
| Animation | Clip frame indexes and durations must fit the declared logical asset. |
| Collision | `world_map(...)` data and matching flag rows must exist before runtime collision reads. |
| HUD | Requested `HudMode` must be declared by the target, except `none`, which is always accepted as disabled HUD. |

## Target Support

| API group | Game Boy | NES |
| --- | --- | --- |
| Frame/input | Supported. `video_wait_vblank()` and `input_poll()` lower to DMG VBlank and JOYP reads. | Supported in the runtime spike. `input_poll()` reads controller port `$4016`. |
| World map setup | Supported. `world_map(...)` builds initial visible tiles, streaming rows/columns, and collision flags. | Supported for initial visible nametable setup. Runtime streaming is not implemented. |
| Camera X | Supported with one-pixel stepping and column streaming. | Supported for `camera_set_position(x, 0)` and `camera_apply()`. |
| Camera Y | Supported, but diagonal movement can exceed budget and fail. | Not supported in the current NES camera spike. |
| Logical sprites | Supported for PNG Game Boy sheets and transitional JSON assets. | Supported for JSON assets with `platforms.nes.frames` in the current spike. |
| Palette slots | Sprite slots `0..1`. | Sprite slots `0..3`. |
| Animation helpers | Supported on Game Boy runner path. | Runtime animation is not part of the current NES spike. |
| World collision queries | Supported on Game Boy runner path. | Not implemented in the current NES spike. |
| HUD | `window` HUD supported for static startup tiles. `split_scroll` is rejected. | No portable HUD mode declared. `none` is accepted; `window` fails. |

Use `samples/manifest.json` to identify which samples are portable. Currently `samples/cross-target-camera/camera.rs` is the only `portable-sdk` sample and builds for both Game Boy and NES.

## Failure Modes

Portable calls should fail early with target-specific diagnostics instead of reaching backend-specific surprises. Current important diagnostics include:

| Scenario | Diagnostic |
| --- | --- |
| Game Boy split-scroll HUD | `Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.` |
| NES Window HUD | `Target 'nes' does not support Window HUD. Use disable HUD for this target.` |
| NES vertical camera position | `Target 'nes' supports only horizontal camera_set_position(x, 0) in the current camera spike.` |
| Game Boy diagonal camera movement over budget | `Target 'gb' supports 20 background tile writes per frame, but 38 are required for moving the camera diagonally (18 column tiles + 20 row tiles).` |
| Game Boy sprite palette overflow | `Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.` |
| NES sprite palette overflow | `Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.` |

Calls that expose raw hardware state are outside SDK v1. Examples include `scroll_set(...)`, `sprite_set(...)`, `tilemap_set(...)`, `tilemap_fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `palette_set(...)`, and `object_palette_set(...)`. They can remain available in target-intrinsic samples while compatibility is needed.

## Minimal Game Boy/NES Example

This shape is the current smallest portable sample. It uses unified world data, tick input, horizontal camera positioning, and logical sprite drawing while avoiding raw target calls.

```c
void main() {
    video_init();

    world_column(0, 1, 2, 3, 4);
    world_column(1, 2, 3, 4, 5);
    world_column(2, 3, 4, 5, 1);
    world_column(3, 4, 5, 1, 2);
    world_flags(0, 0, 0, 1, 1);
    world_flags(1, 0, 0, 1, 1);
    world_flags(2, 0, 0, 1, 1);
    world_flags(3, 0, 0, 1, 1);
    world_map(4, 10, 4);
    camera_init(4, 10, 4);
    sprite_asset(marker, "marker.json");

    i16 cameraX = 0;

    while (true) {
        video_wait_vblank();
        input_poll();
        cameraX = button_hold_ticks(right);
        camera_set_position(cameraX, 0);
        camera_apply();
        sprite_draw(marker, 72, 72, 0, 0, 0);
    }
}
```

Build it for Game Boy:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out /tmp/cross-camera.gb \
  samples/cross-target-camera/camera.rs
```

Build the same source for NES:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out /tmp/cross-camera.nes \
  samples/cross-target-camera/camera.rs
```

The sample uses a target-variant JSON sprite asset (`platforms.gb` and `platforms.nes`). That asset format is transitional, but the source-level `sprite_asset(...)` and `sprite_draw(...)` calls are the portable SDK surface.
