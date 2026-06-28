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

### Audio and music

| Signature | Semantics |
| --- | --- |
| `audio.Init()` | Initialize the target audio path and reset portable BGM playback state. |
| `music.Asset(name, path)` | Declare a music resource. The portable envelope format is `retrosharp.music.v1`, with per-platform variants such as a Game Boy `.uge` file or `.gbapu` APU trace. |
| `music.Play(name)` | Start the declared BGM resource. This maps to `SdkAudioOperation.PlayMusic` and is capability-checked through `TargetAudioCapabilities`. |
| `music.Stop()` | Stop BGM playback. |
| `audio.Update()` | Advance the target audio runtime once. Call it once per frame after the frame boundary for targets whose BGM runtime is tick-driven. |

The resource envelope intentionally allows one source-level theme to carry different target variants:

```json
{
  "format": "retrosharp.music.v1",
  "platforms": {
    "gb": { "format": "gbapu", "path": "theme.gbapu" },
    "nes": { "format": "future", "path": "theme.nes.json" }
  }
}
```

Game Boy currently accepts hUGETracker `.uge` v6 resources and `.gbapu` APU traces (binary, or legacy `retrosharp.gbapu.v1` `.gbapu.json`) directly or through the envelope. The CLI has a target-specific `gbs-to-gbapu` export helper for preserving GBS APU register writes as `.gbapu`; `.gbs` is not a portable asset format. See `GameBoyApuTraceFormat.md` for the target-specific trace format analysis. NES recognizes the source-level audio calls for validation, but BGM playback is not implemented yet.

### World data and collision

| Signature | Semantics |
| --- | --- |
| `world.Column(index, tile0, tile1, ...)` | Define one source-level world column of tile ids. |
| `world.Flags(index, flags0, flags1, ...)` | Define matching tile collision flags for a world column. |
| `world.Map(width, streamY, height)` | Build the active `WorldMap2D` resource from declared columns and flags. |
| `world.Load(path)` | Import a Tiled JSON map (`.tmj`) into the active `WorldMap2D` resource when the target supports that asset pipeline. |
| `world_tile_flags_at(worldX, worldY)` | Read collision flags by world pixel coordinates; out-of-bounds reads return `0`. |
| `collision_aabb_tiles(x, y, width, height, flags)` | Return `1` if any tile overlapped by a world-space AABB has the requested flag bits. |
| `camera.AabbTiles(screenX, worldY, width, height, flags)` | Return `1` if a camera-relative AABB overlaps requested world flags at the current camera position. `screenX` and `worldY` may be literals or byte-backed runtime expressions, so fixed-screen actors and projected world-space actors can share the same SDK operation. |
| `camera.AabbHitTop(screenX, worldY, width, height, flags)` | Return the top world-pixel Y of the first/top overlapped tile matching the requested flags for a camera-relative AABB, or `255` when there is no hit. This exposes a collision fact for landing resolution without owning movement policy. |

World flag values are `0` empty, `1` solid, `2` hazard, and `4` platform. Values can be combined.

For Tiled maps, external tilesets keep the PNG path saved by Tiled as the editable baseline. During target lowering, that PNG path follows the target-variant convention used by sprite assets: `tiles.png` can resolve to `tiles.gb.png`/`tiles.GameBoy.png` on Game Boy or `tiles.nes.png`/`tiles.NES.png` on NES, falling back to `tiles.png` when no variant exists. The current NES lowering derives a universal background color, up to four background palette slots, and the initial attribute table from the selected tileset PNG's placed map tiles; runtime attribute streaming is still outside SDK v1.

### Logical palettes

| Signature | Semantics |
| --- | --- |
| `palette.Background(slot, c0, c1, c2, c3)` | Declare one logical background palette slot with four logical luminance tones `0..3`. |
| `palette.Sprite(slot, c0, c1, c2, c3)` | Declare one logical sprite palette slot with four logical luminance tones `0..3`. |

The slot is logical and capability-checked against the target descriptor. The four color values are target-independent luminance tones `0..3`; each backend maps them to its hardware palette representation. NES sprite PNG assets can derive a hardware sprite palette for the draw slot that uses the asset, while preserving the universal background color in sprite palette entry `0`. Raw `palette.Set(...)` and `objectPalette.Set(...)` remain target-intrinsic compatibility calls and are not the SDK v1 palette declaration surface.

### Camera

| Signature | Semantics |
| --- | --- |
| `camera.Init(mapWidth, streamY, streamHeight)` | Initialize camera state for the active world map. |
| `camera.SetPosition(x, y)` | Request a camera position in world pixels. This maps to `Sdk2DOperation.SetCameraPosition`. |
| `camera.Apply()` | Apply the current camera state to the target during the frame. |

Targets may lower camera movement differently. The SDK contract is position-based; direction-specific helpers such as `camera_move_right()` and raw scroll calls such as `scroll_set(...)` are transitional or target-intrinsic APIs. Game Boy supports non-zero Y through `camera.SetPosition(x, y)` with one-pixel-per-call stepping and row streaming during `camera.Apply()`. NES supports non-zero Y for source-authored maps that fit a preloaded four-screen 64x60 tile surface; larger free-scroll worlds still fail until row, diagonal, and attribute streaming have a documented VBlank policy.

### Logical sprites and animation

| Signature | Semantics |
| --- | --- |
| `sprite.Asset(name, path[, frameWidth, frameHeight])` | Declare a logical sprite asset. PNG sheets use explicit frame dimensions; JSON assets remain a transitional compatibility format. |
| `sprite_width(name)` | Return the logical sprite width known at compile time. |
| `sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` | Draw a logical sprite frame with portable horizontal flip and logical palette-slot selection. |
| `animation.Clip(name, firstFrame, duration...)` | Declare a looping frame-duration table. |
| `animation.Frame(name, tick)` | Return the frame index for a tick in a declared clip. |

For PNG assets, a generic path can be specialized per target without changing source. If source asks for `Mario.png`, the selected target first looks for `Mario.gb.png`/`Mario.GameBoy.png` or `Mario.nes.png`/`Mario.NES.png` next to it, then falls back to `Mario.png`. Existing platform-suffixed source paths are normalized while searching for the selected target variant, so a transitional `Mario.gb.png` reference can still pick `Mario.nes.png` when compiling NES.

The `x`, `y`, `frame`, and `flipX` arguments are byte-backed constants or storage locations in the shared SDK operation model. `flipX` is a portable boolean, not a raw hardware attribute byte. `paletteSlot` is a compile-time logical sprite palette slot checked against the target descriptor.

### Actor framework slice

The current actor framework frontend is source sugar over fixed storage. It does
not add `Sdk2DOperation` cases and does not introduce heap allocation, object
identity, virtual dispatch, delegates, closures, or function pointers.

| Signature | Semantics |
| --- | --- |
| `actor.Pool(name, capacity)` | Declare a fixed local actor pool. `capacity` must be a literal `1..255` and fit the fixed struct-array storage model. For `pool.Draw()`, targets validate `capacity * max(enemy metasprite hardware sprites)` against their sprite budget after JSON/PNG assets have been resolved. The frontend expands this to `Actor name[capacity];`, where `Actor` is a byte-sized framework state record. |
| `actor.SpawnLayer(pool, "map.tmj", "layer")` | Read a Tiled object layer from the named map, keep its authored spawns as generated ROM-table helpers, and emit a runtime activation pass at the call site. Call it once per frame after `camera.SetPosition(...)` to recycle actors outside the current camera window and activate newly visible spawns into free pool slots. Objects use a `kind` string property, Tiled `type`/`class`, or object `name` to select the actor kind; world `x` must fit `0..65535` and is split into low `x` plus high `xHi`, while `y` must fit a byte. |
| `actor.SpawnWindow(pool, "map.tmj", "layer", left, width)` | Read the same Tiled object layer, but activate against the camera-relative half-open window `[cameraX + left, cameraX + left + width)`. `left` and `width` are literal bytes. This is a runtime window, not compile-time filtering. |
| `enemy.Def(name, sprite: asset, behavior: Behavior, animation: clip, speed: n, hp: n, cooldown: n, contactDamage: n, hitboxWidth: n, hitboxHeight: n)` | Declare byte-sized per-enemy metadata. `behavior`, `sprite`, and `animation` must be identifiers when supplied; numeric properties must be literal bytes. Omitted numeric properties default to `0`, except `hp`, which defaults to `1`. |
| `enemy.Behavior(kind)`, `enemy.Speed(kind)`, `enemy.Hp(kind)`, `enemy.Cooldown(kind)`, `enemy.ContactDamage(kind)`, `enemy.HitboxWidth(kind)`, `enemy.HitboxHeight(kind)` | Return metadata for a runtime kind through generated inline helpers over constants. The frontend emits only helpers that source actually calls, so unused metadata helpers do not remain in the lowered program or add bytes. |
| `pool.TouchTiles(yOffset, flags)` | Read the current camera X once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX` from world `x`/`xHi`, branch by kind, cull slots outside the visible camera window, and call `camera.AabbTiles(...)` with the kind's literal hitbox width/height. On hit, set actor `state` to `contactDamage` or `1` when no damage is declared. |
| `pool.LandOnTiles(searchTopOffset, searchHeight, flags)` | Read the current camera X once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX`, branch by kind, cull slots outside the visible camera window, and call `camera.AabbHitTop(...)` with the kind's literal hitbox width. On hit, assign actor `y` to the returned top Y, clear `vy`, and set `state` to `1`. |
| `pool.TouchPlayer(playerX, playerY, playerWidth, playerHeight)` | Read the current camera X once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX`, branch by kind, cull slots outside the visible camera window, and test each kind's literal hitbox against a literal player AABB in screen coordinates. On hit, set actor `state` to `contactDamage` or `1`. |

`actor.Pool(...)` and `enemy.Def(...)` are accepted as statements inside the
compiled source and disappear before target lowering. The generated actor state
fields are `kind`, `active`, `x`, `xHi`, `y`, `vx`, `vy`, `state`, `timer`,
`facing`, `animTick`, and `health`. `x` is the low byte of world X, paired with
`xHi`; this keeps pooled fields byte-sized without requiring mixed-width struct
array layout. Game Boy currently supports `pool.Update()` and
`pool.Draw()` for the basic byte-field behavior set by expanding them to grouped
loops over slots with direct kind checks and stable `sprite.Draw` calls.

Spawn activation is one-shot per authored Tiled object. The frontend generates a
fixed `used[]` byte array per spawn layer; a spawn is marked used only after it
successfully claims a free slot. Active slots are recycled when they leave the
same camera-relative activation window, and any source code can still free a slot
by setting `active = 0` for death/despawn. A recycled slot does not clear the
spawn's `used` bit, so scrolling back does not respawn the same authored object.
If no free slot exists when an eligible spawn is scanned, the spawn is skipped
deterministically for that activation pass and retried while it remains in the
window. At compile time, the frontend rejects any spawn layer/window whose maximum
simultaneous authored spawns can exceed the declared pool capacity.
`Walker`, `Flyer`, `Patrol`, `Shooter`, `Hazard`, and a direction-driven
`Chaser` are implemented without actor-specific SDK operations. NES accepts the
same pool/definition metadata slice plus `pool.Update()` and `pool.Draw()` for
that basic behavior set. `pool.Draw()` reads the current camera X state once for
its generated loop and computes `screenX = actorWorldX - cameraX`. One-slot
pools initialize draw coordinates to an offscreen Y, overwrite them only when
the slot is active and inside the visible camera window, and emit ordinary
`sprite.Draw(...)` calls through stable call sites so hardware sprite slots are
hidden rather than left stale. Larger pools keep the current camera-relative
visible-actor draw/cull shape until a per-slot OAM allocation policy lands.
Draw uses `animation.Frame(...)` when a definition declares an animation clip. Drawn pools
are checked against target sprite budgets with target-resolved metasprite
geometry, so a pool of multi-sprite enemy definitions is charged by hardware
sprite pieces rather than by actor slots. Aggregate hardware sprite usage is
also validated by the same frame-budget pass as hand-written sprite draws.
`pool.TouchTiles(...)`, `pool.LandOnTiles(...)`, and
`pool.TouchPlayer(...)` use the same per-phase camera-X cache and per-actor
projection/visibility guard as draw, so collision/contact is tested per visible
actor instead of at one fixed screen column. Separate helper calls remain
separate phases and do not share a hoisted camera read across statements. The
tile helpers emit ordinary camera AABB SDK calls; they do not introduce
actor-specific target intrinsics. Tiled spawn helpers read the same
target-neutral map importer as
`world.Load(...)` and lower to generated ROM-table helpers plus runtime fixed-slot
activation before target lowering.

#### Low-level equivalent pattern

The actor framework is deliberately equivalent to hand-authored fixed storage.
For a small pool, the source-level sugar:

```c
actor.Pool(enemies, 2);
enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1,
    hitboxWidth: 8, hitboxHeight: 8);
actor.SpawnLayer(enemies, "level.tmj", "actors");
enemies.Update();
enemies.TouchTiles(0, 1);
enemies.LandOnTiles(4, 12, 1);
enemies.TouchPlayer(72, 40, 16, 16);
enemies.Draw();
```

lowers to the same shape as this hand-authored pattern, as covered by the
Game Boy and NES differential tests:

```c
struct Actor {
    u8 kind;
    u8 active;
    u8 x;
    u8 xHi;
    u8 y;
    i8 vx;
    i8 vy;
    u8 state;
    u8 timer;
    u8 facing;
    u8 animTick;
    u8 health;
}

const Walker = 1;
const Goomba = 1;
const GoombaSpeed = 1;
const GoombaContactDamage = 0;
const GoombaHitboxWidth = 8;
const GoombaHitboxHeight = 8;

inline u8 __enemies_spawn_0_kind(u8 index) => Goomba;
inline u8 __enemies_spawn_0_x(u8 index) => index == 0 ? 24 : 72;
inline u8 __enemies_spawn_0_xHi(u8 index) => 0;
inline u8 __enemies_spawn_0_y(u8 index) => 40;
inline u8 __enemies_spawn_0_active(u8 index) => 1;
inline u8 __enemies_spawn_0_vx(u8 index) => 0;
inline u8 __enemies_spawn_0_vy(u8 index) => 0;
inline u8 __enemies_spawn_0_state(u8 index) => 0;
inline u8 __enemies_spawn_0_timer(u8 index) => 0;
inline u8 __enemies_spawn_0_facing(u8 index) => 0;
inline u8 __enemies_spawn_0_animTick(u8 index) => 0;
inline u8 __enemies_spawn_0_health(u8 index) => 0;

void main() {
    Actor enemies[2];
    u8 __enemies_spawn_0_used[2];

    u8 recycleCameraX = __rs_actor_camera_x_lo();
    u8 recycleCameraXHi = __rs_actor_camera_x_hi();
    for (u8 recycle = 0; recycle < countof(enemies); recycle += 1) {
        if (enemies[recycle].active != 0) {
            u8 screenX = enemies[recycle].x - recycleCameraX;
            if (!((((enemies[recycle].xHi == recycleCameraXHi) &&
                    (enemies[recycle].x >= recycleCameraX)) ||
                   ((enemies[recycle].xHi == recycleCameraXHi + 1) &&
                    (enemies[recycle].x < recycleCameraX))) &&
                  (screenX < 160))) {
                enemies[recycle].active = 0;
            }
        }
    }

    u8 spawnCameraX = __rs_actor_camera_x_lo();
    u8 spawnCameraXHi = __rs_actor_camera_x_hi();
    for (u8 spawn = 0; spawn < 2; spawn += 1) {
        if (__enemies_spawn_0_used[spawn] == 0) {
            u8 spawnX = __enemies_spawn_0_x(spawn);
            u8 spawnXHi = __enemies_spawn_0_xHi(spawn);
            u8 screenX = spawnX - spawnCameraX;
            if ((((spawnXHi == spawnCameraXHi) && (spawnX >= spawnCameraX)) ||
                 ((spawnXHi == spawnCameraXHi + 1) && (spawnX < spawnCameraX))) &&
                (screenX < 160)) {
                u8 assigned = 0;
                for (u8 slot = 0; slot < countof(enemies); slot += 1) {
                    if (assigned == 0) {
                        if (enemies[slot].active == 0) {
                            enemies[slot].kind = __enemies_spawn_0_kind(spawn);
                            enemies[slot].x = spawnX;
                            enemies[slot].xHi = spawnXHi;
                            enemies[slot].y = __enemies_spawn_0_y(spawn);
                            enemies[slot].vx = __enemies_spawn_0_vx(spawn);
                            enemies[slot].vy = __enemies_spawn_0_vy(spawn);
                            enemies[slot].state = __enemies_spawn_0_state(spawn);
                            enemies[slot].timer = __enemies_spawn_0_timer(spawn);
                            enemies[slot].facing = __enemies_spawn_0_facing(spawn);
                            enemies[slot].animTick = __enemies_spawn_0_animTick(spawn);
                            enemies[slot].health = __enemies_spawn_0_health(spawn);
                            enemies[slot].active = __enemies_spawn_0_active(spawn);
                            __enemies_spawn_0_used[spawn] = 1;
                            assigned = 1;
                        }
                    }
                }
            }
        }
    }

    u8 cameraX = __rs_actor_camera_x_lo();
    u8 cameraXHi = __rs_actor_camera_x_hi();
    for (u8 i = 0; i < countof(enemies); i += 1) {
        if (enemies[i].active != 0) {
            if (enemies[i].kind == Goomba) {
                enemies[i].x += GoombaSpeed;
                if (enemies[i].x < GoombaSpeed) {
                    enemies[i].xHi += 1;
                }

                u8 screenX = enemies[i].x - cameraX;
                if ((((enemies[i].xHi == cameraXHi) && (enemies[i].x >= cameraX)) ||
                     ((enemies[i].xHi == cameraXHi + 1) && (enemies[i].x < cameraX))) &&
                    (screenX < 160)) {
                    if (camera.AabbTiles(screenX, enemies[i].y,
                            GoombaHitboxWidth, GoombaHitboxHeight, 1) != 0) {
                        enemies[i].state = 1;
                    }

                    u8 hitTop = camera.AabbHitTop(screenX, enemies[i].y - 4,
                        GoombaHitboxWidth, 12, 1);
                    if (hitTop != 255) {
                        enemies[i].y = hitTop;
                        enemies[i].vy = 0;
                        enemies[i].state = 1;
                    }

                    if (screenX < 88 && screenX + GoombaHitboxWidth > 72 &&
                        enemies[i].y < 56 &&
                        enemies[i].y + GoombaHitboxHeight > 40) {
                        enemies[i].state = 1;
                    }

                    sprite.Draw(goomba, screenX, enemies[i].y, 0, false, 0);
                }
            }
        }
    }
}
```

The generated code keeps the helper calls as separate phases. Each
projection-bearing phase reads `cameraX`/`cameraXHi` once before its slot or
spawn loop, then computes `screenX` per active actor or candidate spawn from the
cached camera bytes.

### Optional HUD

| Signature | Semantics |
| --- | --- |
| `hud.SetTile(mode, x, y, tile)` | Declare one static HUD tile for a capability-gated HUD mode. |

Current mode names are `window`, `split_scroll`, `sprite_hud`, and `none`. `none` disables HUD and must compile as a no-op even on targets that declare no HUD support.

## Capability Requirements

The compiler must validate portable SDK calls against `Target2DCapabilities` and `TargetAudioCapabilities` before target lowering.

Static enforcement starts with per-operation checks, then applies a frame-budget pass for aggregate SDK budgets. The shared operation list remains flattened across control flow, so aggregate checks must not count that list directly. Instead, `Sdk2DOperationCollector.CollectFrameBudgets(...)` computes possible frame windows around `video.WaitVBlank()` and `input.Poll()`, treats `if`/`else` arms as exclusive alternatives, and validates the result through `Sdk2DOperationValidator.ValidateFrameBudget(...)`. Multiple explicit stream calls that exceed `MaxBackgroundTileWritesPerFrame` in one possible frame fail before target lowering.

For logical sprites, targets feed their compiled metasprite geometry and hardware sprite size mode into the same frame-budget pass. The compiler rejects unsupported `SpriteSizeModes`, one possible frame that exceeds `SpriteCount`, and `MaxSpritesPerScanline` when the sprite draw uses a constant Y position. Actor pool draws receive an earlier target-aware diagnostic after sprite assets are compiled: for every drawn pool, `capacity * max(enemy.Def metasprite hardware sprites)` must fit `SpriteCount`, and `capacity * max(enemy.Def busiest-scanline hardware sprites)` must fit `MaxSpritesPerScanline`. That diagnostic names the pool and the enemy definition with the largest metasprite pressure. The frame-budget pass still validates the lowered draw operations with branch-as-alternative and frame-boundary semantics. Other runtime Y positions still cannot be placed on a specific scanline statically, so dynamic per-scanline overflow remains author/runtime responsibility outside the actor pool path. An unsound count over the flattened operation list would reject valid programs and is intentionally avoided. See issue #102.

| SDK area | Required capabilities |
| --- | --- |
| Frame/input | Target runtime support for frame wait and controller polling. |
| Audio/BGM | Target BGM support and a music resource format listed by the target descriptor. |
| World map | Active world dimensions must fit the target's static setup or streaming path. |
| Camera X | `ScrollAxes.Horizontal`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed column. |
| Camera Y | `ScrollAxes.Vertical`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed row. |
| Diagonal camera movement | Combined row plus column write budget must fit one frame or fail explicitly. |
| Explicit background streaming | `RuntimeBackgroundStreamingAxes` must include the requested `StreamMapColumn` or `StreamMapRow` axis, and the requested tile count must fit `MaxBackgroundTileWritesPerFrame`. |
| Logical palettes | Palette declarations must provide exactly four colors and the requested background or sprite slot must fit the target descriptor. |
| Logical sprites | Sprite count, sprite size modes, scanline limits, sprite transforms, and palette-slot count must fit the lowered metasprite. |
| Animation | Clip frame indexes and durations must fit the declared logical asset. |
| Actor framework | Pool capacity must be a literal fixed size, pooled fields are byte-sized, and enemy metadata must be literal/identifier data that can lower to constants and inline helper branches. |
| Collision | `world.Map(...)` or `world.Load(...)` data and matching flag rows must exist before runtime collision reads. |
| Camera-relative collision | Target must declare `CameraRelativeAabb` or `CameraRelativeAabbHitTop` collision-query support for the requested query, and the screen span must fit the visible target width. |
| HUD | Requested `HudMode` must be declared by the target, except `none`, which is always accepted as disabled HUD. |

## Target Support

| API group | Game Boy | NES |
| --- | --- | --- |
| Frame/input | Supported. `video.WaitVBlank()` and `input.Poll()` lower to DMG VBlank and JOYP reads. | Supported in the runtime spike. `input.Poll()` reads controller port `$4016`. |
| World map setup | Supported. `world.Map(...)` and `world.Load(...)` build initial visible tiles, streaming rows/columns, and collision flags. | Supported for horizontal maps that fit the one-byte streaming runtime. Startup seeds a 64-column two-nametable buffer and runtime camera movement streams wider source maps through it. Source-authored free-scroll maps can instead preload a four-screen 64x60 surface; Tiled `world.Load(...)` still keeps the current visible-slice limits. |
| Camera X | Supported with one-pixel stepping and column streaming. | Supported for `camera.SetPosition(x, 0)` and `camera.Apply()`, with absolute source-tile tracking, horizontal nametable selection, and runtime column streaming into the off-screen nametable for horizontal-only maps wider than 32 columns. In four-screen free-scroll mode, X movement pans within the preloaded 64x60 surface. |
| Camera Y | Supported with one-pixel stepping and row streaming. Diagonal movement is rejected when the combined row+column write budget would exceed one frame. | Supported for maps that fit the preloaded four-screen 64x60 surface. Runtime row/attribute streaming for larger worlds is not implemented yet. |
| Logical sprites | Supported for PNG Game Boy sheets and transitional JSON assets. | Supported for PNG NES sheets and transitional JSON assets with `platforms.nes.frames`. |
| Palette declarations | Background slot `0` and sprite slots `0..1` through `palette.Background(...)` and `palette.Sprite(...)`. | Background and sprite slots `0..3` through `palette.Background(...)` and `palette.Sprite(...)`. |
| BGM | Supported for hUGETracker `.uge` v6 songs and `.gbapu` APU traces in the current runtime. GBS files must first be exported to `.gbapu` with the target-specific CLI helper. | Real playback not implemented; audio calls are accepted and lowered as no-ops for shared acceptance sources. |
| Animation helpers | Supported on Game Boy runner path. | Supported for byte-sized clip frame indexes, frame durations, and total duration. |
| Actor framework slice | `actor.Pool`, `actor.SpawnLayer`, `actor.SpawnWindow`, `enemy.Def`, called `enemy.*` metadata helpers, and `pool.Update()`/`pool.Draw()`/`pool.TouchTiles()`/`pool.LandOnTiles()`/`pool.TouchPlayer()` lower before Game Boy target emission to fixed struct arrays, constants, inline helper branches, generated spawn-table helpers, `used[]`, runtime activation, camera-relative draw/collision/player contact, and the basic behavior set: `Walker`, `Flyer`, `Patrol`, `Shooter`, `Hazard`, and direction-driven `Chaser`. | The same source-to-source actor framework slice lowers before NES target emission with NES sprite/scanline budgets and horizontal camera-relative collision support. |
| World collision queries | Supported on Game Boy runner path. | Generic `world_tile_flags_at(...)` and `collision_aabb_tiles(...)` are not implemented in the current NES spike. |
| Camera-relative collision | Supported through `camera.AabbTiles(...)` and `camera.AabbHitTop(...)` for literal or byte-backed screen X values. | Supported through `camera.AabbTiles(...)` and `camera.AabbHitTop(...)` for literal or byte-backed screen X values on horizontal maps. |
| HUD | `window` HUD supported for static startup tiles. `split_scroll` is rejected. | No portable HUD mode declared. `none` is accepted; `window` fails. |

Use `samples/manifest.json` to identify which samples are portable. Currently `samples/cross-target-camera/camera.rs` is the only `portable-sdk` sample and builds for both Game Boy and NES.

## Failure Modes

Portable calls should fail early with target-specific diagnostics instead of reaching backend-specific surprises. Current important diagnostics include:

| Scenario | Diagnostic |
| --- | --- |
| Game Boy split-scroll HUD | `Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.` |
| NES Window HUD | `Target 'nes' does not support Window HUD. Use disable HUD for this target.` |
| NES four-screen free-scroll map overflow | `NES four-screen free scroll supports preloaded maps up to 64x60 tiles.` |
| Game Boy diagonal camera movement over budget | `Target 'gb' supports 20 background tile writes per frame, but 38 are required for moving the camera diagonally (18 column tiles + 20 row tiles).` |
| NES runtime row streaming | `Target 'nes' does not support runtime vertical background streaming.` |
| NES BGM playback on targets without no-op audio enabled | `Target 'nes' does not support BGM playback yet.` |
| Game Boy sprite palette slot overflow | `Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.` |
| NES world tile flag query | `Target 'nes' does not support world tile flag queries.` |
| Game Boy hUGETracker timer tempo | `hUGETracker timer-based tempo is not supported by the Game Boy BGM v1 runtime.` |
| Game Boy sprite palette overflow | `Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.` |
| NES sprite palette overflow | `Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.` |
| Actor generated name collision | `actor framework cannot generate enemy.Def 'Goomba' speed constant named 'GoombaSpeed' because user constant 'GoombaSpeed' is already declared.` |
| Actor pool dynamic capacity | `actor.Pool for 'enemies' requires a literal capacity from 1 to 255.` |
| Actor pool metasprite budget overflow | `Target 'gb' supports 40 hardware sprites per frame, but actor.Pool for 'enemies' can draw up to 42 because capacity 21 times enemy.Def 'Goomba' sprite 'goomba' uses 2 hardware sprites.` |

Calls that expose raw hardware state are outside SDK v1. Examples include `scroll.Set(...)`, `sprite.Set(...)`, `tilemap.Set(...)`, `tilemap.Fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `palette.Set(...)`, and `objectPalette.Set(...)`. They can remain available in target-intrinsic samples while compatibility is needed. Prefer `palette.Background(...)` and `palette.Sprite(...)` for SDK-shaped logical-tone palette declarations.

## Current Stabilization Gaps

SDK v1 is usable for the current cross-target camera sample, and the runner-shaped camera-relative collision/animation slice now lowers on both Game Boy and NES. The full runner is still a target-acceptance scenario rather than a portable SDK sample because NES audio calls are currently no-ops and several broader world/HUD contracts are still missing.

- `camera.AabbTiles(...)` and `camera.AabbHitTop(...)` are capability-gated SDK queries for camera-relative AABBs. Game Boy and NES both support the runner-shaped horizontal form and actor-framework calls with per-actor projected X.
- `collision_aabb_tiles(...)` still reports overlap only. Use `camera.AabbHitTop(...)` when an actor needs the contacted tile's top edge while keeping landing and movement resolution in source.
- Logical palette declarations now cover background and sprite palette slots through `palette.Background(...)` and `palette.Sprite(...)`. The color values are logical tones `0..3`; targets map those tones to their hardware palette registers or palette RAM. NES sprite PNG assets may refine the sprite slot with a derived hardware palette for their opaque colors.
- `samples/cross-target-camera/camera.rs` is the only `portable-sdk` sample. `samples/runner/runner.rs` remains a shared Game Boy/NES `target-acceptance` sample; NES lowers its audio calls as no-ops until real BGM support exists.

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
