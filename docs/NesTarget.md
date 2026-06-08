# NES Target

Status: experimental, intentionally narrow.

The NES target currently compiles a constrained static drawing subset directly to an iNES ROM. It does not yet support the Game Boy runner runtime, logical sprite drawing, camera movement, input polling, or frame-by-frame background streaming.

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

The descriptor records NES sprite and palette hardware limits, but runtime sprite lowering is not implemented in this target yet. Portable SDK operations that need scroll axes, per-frame nametable writes, attribute writes, or HUD support must fail capability checks before reaching NES backend code.

## Supported Video API

Static setup calls:

- `video_init()`
- `palette_set(index, color)`
- `tilemap_set(x, y, tile)`
- `tilemap_fill(x, y, width, height, tile)`
- `video_present()`

Parameterless helper functions can group those calls.

The current target stores one visible 32x30 nametable shape and uploads it during startup. `tilemap_fill(...)` rejects rectangles outside that visible area.
