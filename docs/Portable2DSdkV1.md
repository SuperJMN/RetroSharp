# Portable 2D SDK v1 Reference

Status: draft v1 reference for the current portable 2D surface.

This document is the first public API reference for the portable 2D SDK layer. It describes calls that are intended to represent game concepts before target lowering. Game Boy and NES may still support target-specific or transitional calls, but those calls are not part of SDK v1 unless listed here.

The SDK is portable by capability, not by identical machine behavior. A call is valid only when the selected target descriptor can support the requested mode, budget, and asset shape. Unsupported features must fail during compilation with explicit target diagnostics.

## SDK v1 Surface

The current source spelling is the SDK dot-call form, for example `video.WaitVBlank()` and `camera.SetPosition(x, y)`. These calls lower to the same SDK operations as the older snake_case compatibility names such as `video_wait_vblank()` and `camera_set_position(...)`.

### Frame and input

| Signature | Semantics |
| --- | --- |
| `video.Init()` | Initialize the target video path for samples that need explicit setup. This remains a narrow setup call while the SDK frame lifecycle is stabilized. |
| `video.WaitVBlank()` | Wait for the next frame boundary. This maps to `Sdk2DOperation.WaitFrame`. |
| `input.Poll()` | Snapshot controller state for the current tick. Call once per frame before tick-based button helpers. |
| `button_down(button)` | Return `1` while the named button is down in the current poll. |
| `button_just_pressed(button)` | Return `1` only on the up-to-down edge. |
| `button_just_released(button)` | Return `1` only on the down-to-up edge. |
| `button_hold_ticks(button)` | Return consecutive polls held, saturating at `255` and resetting to `0` on release. |

Supported logical buttons are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

### World data and collision

| Signature | Semantics |
| --- | --- |
| `world.Column(index, tile0, tile1, ...)` | Define one source-level world column of tile ids. |
| `world.Flags(index, flags0, flags1, ...)` | Define matching tile collision flags for a world column. |
| `world.Map(width, streamY, height)` | Build the active `WorldMap2D` resource from declared columns and flags. |
| `world.Load(path)` | Import a Tiled JSON map (`.tmj`) into the active `WorldMap2D` resource when the target supports that asset pipeline. |
| `world_tile_flags_at(worldX, worldY)` | Read collision flags by world pixel coordinates; out-of-bounds reads return `0`. |
| `collision_aabb_tiles(x, y, width, height, flags)` | Return `1` if any tile overlapped by a world-space AABB has the requested flag bits. |

World flag values are `0` empty, `1` solid, `2` hazard, and `4` platform. Values can be combined.

### Camera

| Signature | Semantics |
| --- | --- |
| `camera.Init(mapWidth, streamY, streamHeight)` | Initialize camera state for the active world map. |
| `camera.SetPosition(x, y)` | Request a camera position in world pixels. This maps to `Sdk2DOperation.SetCameraPosition`. |
| `camera.Apply()` | Apply the current camera state to the target during the frame. |

Targets may lower camera movement differently. The SDK contract is position-based; direction-specific helpers such as `camera_move_right()` and raw scroll calls such as `scroll_set(...)` are transitional or target-intrinsic APIs.

### Logical sprites and animation

| Signature | Semantics |
| --- | --- |
| `sprite.Asset(name, path[, frameWidth, frameHeight])` | Declare a logical sprite asset. PNG sheets use explicit frame dimensions; JSON assets remain a transitional compatibility format. |
| `sprite_width(name)` | Return the logical sprite width known at compile time. |
| `sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` | Draw a logical sprite frame with portable horizontal flip and logical palette-slot selection. |
| `animation.Clip(name, firstFrame, duration...)` | Declare a looping frame-duration table. |
| `animation.Frame(name, tick)` | Return the frame index for a tick in a declared clip. |

The `x`, `y`, `frame`, and `flipX` arguments are byte-backed constants or storage locations in the shared SDK operation model. `flipX` is a portable boolean, not a raw hardware attribute byte. `paletteSlot` is a compile-time logical sprite palette slot checked against the target descriptor.

### Optional HUD

| Signature | Semantics |
| --- | --- |
| `hud.SetTile(mode, x, y, tile)` | Declare one static HUD tile for a capability-gated HUD mode. |

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
| Collision | `world.Map(...)` or `world.Load(...)` data and matching flag rows must exist before runtime collision reads. |
| HUD | Requested `HudMode` must be declared by the target, except `none`, which is always accepted as disabled HUD. |

## Target Support

| API group | Game Boy | NES |
| --- | --- | --- |
| Frame/input | Supported. `video.WaitVBlank()` and `input.Poll()` lower to DMG VBlank and JOYP reads. | Supported in the runtime spike. `input.Poll()` reads controller port `$4016`. |
| World map setup | Supported. `world.Map(...)` and `world.Load(...)` build initial visible tiles, streaming rows/columns, and collision flags. | Supported for initial visible nametable setup. Runtime streaming is not implemented. |
| Camera X | Supported with one-pixel stepping and column streaming. | Supported for `camera.SetPosition(x, 0)` and `camera.Apply()`. |
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

Calls that expose raw hardware state are outside SDK v1. Examples include `scroll.Set(...)`, `sprite.Set(...)`, `tilemap.Set(...)`, `tilemap.Fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `palette.Set(...)`, and `objectPalette.Set(...)`. They can remain available in target-intrinsic samples while compatibility is needed.

## Minimal Game Boy/NES Example

This shape is the current smallest portable sample. It uses unified world data, tick input, horizontal camera positioning, and logical sprite drawing while avoiding raw target calls.

```c
void main() {
    video.Init();

    world.Column(0, 1, 2, 3, 4);
    world.Column(1, 2, 3, 4, 5);
    world.Column(2, 3, 4, 5, 1);
    world.Column(3, 4, 5, 1, 2);
    world.Flags(0, 0, 0, 1, 1);
    world.Flags(1, 0, 0, 1, 1);
    world.Flags(2, 0, 0, 1, 1);
    world.Flags(3, 0, 0, 1, 1);
    world.Map(4, 10, 4);
    camera.Init(4, 10, 4);
    sprite.Asset(marker, "marker.json");

    loop {
        video.WaitVBlank();
        input.Poll();
        let cameraX = button_hold_ticks(right);
        camera.SetPosition(cameraX, 0);
        camera.Apply();
        sprite.Draw(marker, 72, 72, 0, false, 0);
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

The sample uses a target-variant JSON sprite asset (`platforms.gb` and `platforms.nes`). That asset format is transitional, but the source-level `sprite.Asset(...)` and `sprite.Draw(...)` calls are the portable SDK surface.
