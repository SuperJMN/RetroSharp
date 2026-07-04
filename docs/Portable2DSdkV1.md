# Portable 2D SDK v1 Reference

Status: draft v1 reference for the current portable 2D surface.

This document is the first public API reference for the portable 2D SDK layer. It describes calls that are intended to represent game concepts before target lowering. Game Boy and NES may still support target-specific or transitional calls, but those calls are not part of SDK v1 unless listed here.

The SDK is portable by capability, not by identical machine behavior. A call is valid only when the selected target descriptor can support the requested mode, budget, and asset shape. Unsupported features must fail during compilation with explicit target diagnostics.

## SDK v1 Surface

Projects load `RetroSharp.Portable2D` from their manifest `libraries` list, then use SDK dot-call forms such as `Video.WaitVBlank()` and `Camera.SetPosition(x, y)` from source without a code-file `import`. Standalone source files can instead declare `import RetroSharp.Portable2D;`. Unknown imports are rejected. SDK calls require an imported SDK library or a library supplied by the host/project, while source that does not use the SDK can compile without loading it. Additional source-level SDK libraries can be supplied through `SdkLibraryRegistry`. The CLI exposes the same MVP resolver through repeated `--lib-path <path>` options plus project-manifest `libraryPaths` and `libraries` fields: paths discover packages, while `libraries` names the import paths to load. Each path may be a package directory containing `retrosharp-library.json`, or a directory whose direct children are package directories. A library manifest declares the import path, source files, and optional target ids:

```json
{
  "import": "Acme.Wait",
  "rootNamespace": "Acme.Wait",
  "sourceRoot": "src",
  "namespaceMode": "physical",
  "sources": [
    "src/api.rs",
    "src/timing/rules.rs"
  ],
  "targets": [ "gb", "nes" ]
}
```

`sources` are source-only RetroSharp files loaded relative to the package directory. `targets` is optional; when present, importing the package for any other target fails before target lowering. The built-in `RetroSharp.Portable2D` package lives in `sdk/RetroSharp.Portable2D` and is registered through the same manifest loader used by external packages. This MVP does not yet model package versions, dependencies between packages, remote package feeds, binary libraries, per-library asset roots, or backend plugins. Runtime helpers such as `Video.WaitVBlank()`, `Input.Poll()`, `Audio.Update()`, `Music.Play(...)`, `Sfx.Play(...)`, `Camera.SetPosition(...)`, camera AABB queries, `Sprite.Draw(...)`, and `World.TileFlagsAt(...)` are ordinary package source over target intrinsics. Resource declarations such as `Sprite.Asset(...)`, `World.Load(...)`, `Music.Asset(...)`, `Sfx.Asset(...)`, `Palette.*(...)`, and `Animation.Clip(...)` are ordinary package facades marked with `[resource(...)]`; the frontend resolves that metadata to generic resource declaration descriptors, then the active target performs the existing asset import, variant resolution, palette validation, animation clip validation, and audio asset validation.

Library packages can opt into `namespaceMode: "physical"` with the same
`rootNamespace` and `sourceRoot` fields used by project manifests. The import
opens the package's rewritten source for the consuming program: root files are
the practical place for public facade classes, while files under folders gain
compile-time namespace segments. Path-qualified type names, top-level function
calls, and static references rewrite to unique internal symbols before target
lowering. Project or package files can also use C#-style `using Root.Player;`
directives to open physical namespaces for unqualified references inside that
file. `using` is compile-time name resolution only; it does not inject library
source, which should come from the manifest-level library selection or the
explicit source-level `import` transition path.

### Frame and input

| Signature | Semantics |
| --- | --- |
| `Video.Init()` | Initialize the target video path for samples that need explicit setup. This remains a narrow setup call while the SDK frame lifecycle is stabilized. |
| `Video.WaitVBlank()` | Wait for the next frame boundary. This maps to `Sdk2DOperation.WaitFrame`. |
| `Input.Poll()` | Snapshot controller state for the current tick. Call once per frame before tick-based button helpers. |
| `Input.IsDown(button)` | Return `true` while the named button is down in the current poll. |
| `Input.WasPressed(button)` | Return `true` only on the up-to-down edge. |
| `Input.WasReleased(button)` | Return `true` only on the down-to-up edge. |
| `Input.HoldTicks(button)` | Return consecutive polls held, saturating at `255` and resetting to `0` on release. |
| `button` argument | A `Button` enum member (`Button.A`, `Button.Right`, ...). The snake_case `button_*`/`sprite_width` builtins and bare lowercase button identifiers remain as transitional aliases. |

Supported logical buttons are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

### Audio and music

| Signature | Semantics |
| --- | --- |
| `Audio.Init()` | Initialize the target audio path and reset portable BGM playback state. |
| `Music.Asset(name, path)` | Declare a music resource. VGM/VGZ is the preferred faithful input; per-target variants such as `theme.gb.vgz` and `theme.nes.vgz` are resolved automatically when present. The portable envelope format is `retrosharp.music.v1` for explicit platform entries. |
| `Music.Play(name)` | Start the declared BGM resource. This maps to `SdkAudioOperation.PlayMusic` and is capability-checked through `TargetAudioCapabilities`. |
| `Music.Stop()` | Stop BGM playback. |
| `Sfx.Asset(name, path)` | Declare a one-shot sound effect resource. VGM/VGZ is the supported v1 input, with per-target variants such as `jump.gb.vgz` and `jump.nes.vgz` resolved automatically. |
| `Sfx.Play(name)` | Start or restart the declared one-shot sound effect. This maps to `SdkAudioOperation.PlaySoundEffect` and is capability-checked through `TargetAudioCapabilities`. |
| `Audio.Update()` | Advance the target audio runtime once. Call it once per frame after the frame boundary for targets whose audio runtime is tick-driven. Game Boy advances queued BGM and active SFX streams here, applying SFX after BGM for the same frame. NES advances BGM here; its current SFX lowering writes a filtered action-audio burst immediately from `Sfx.Play(...)` and lets the next BGM tick refresh the music state. |

The resource envelope intentionally allows one source-level theme to carry different target variants:

```json
{
  "format": "retrosharp.music.v1",
  "platforms": {
    "gb": { "format": "vgm", "path": "theme.gb.vgz" },
    "nes": { "format": "vgm", "path": "theme.nes.vgz" }
  }
}
```

Game Boy currently accepts VGM/VGZ DMG register logs, hUGETracker `.uge` v6 resources, and transitional `.gbapu` APU traces (binary, or legacy `retrosharp.gbapu.v1` `.gbapu.json`) for BGM directly or through the envelope. Game Boy SFX v1 accepts VGM/VGZ DMG register logs and reuses the compact APU-trace repack as a one-shot stream. The CLI has a target-specific `gbs-to-gbapu` export helper for preserving GBS APU register writes as `.gbapu`; `.gbs` is not a portable asset format. NES accepts VGM/VGZ 2A03 register logs for BGM and SFX; BGM can include pulse, triangle, noise, DMC/DPCM, `$4015`, and `$4017`, while the current SFX path compiles the first supported channel-write frame as an immediate one-shot action burst and drops captured global/DMC writes such as `$4010`, `$4015`, and `$4017`. Expansion audio remains out of scope for v1. See `GameBoyApuTraceFormat.md` and `NesApuTraceFormat.md` for target-specific compiled trace details.

### World data and collision

| Signature | Semantics |
| --- | --- |
| `World.Column(index, tile0, tile1, ...)` | Define one source-level world column of tile ids. |
| `World.Flags(index, flags0, flags1, ...)` | Define matching tile collision flags for a world column. |
| `World.Map(width, streamY, height)` | Build the active `WorldMap2D` resource from declared columns and flags. |
| `World.Load(path)` | Import a Tiled JSON map (`.tmj`) into the active `WorldMap2D` resource when the target supports that asset pipeline. |
| `world_tile_flags_at(worldX, worldY)` | Read collision flags by world pixel coordinates; out-of-bounds reads return `0`. |
| `collision_aabb_tiles(x, y, width, height, flags)` | Return `1` if any tile overlapped by a world-space AABB has the requested flag bits. |
| `Camera.AabbTiles(screenX, worldY, width, height, flags)` | Return `1` if a camera-relative AABB overlaps requested world flags at the current camera position. `screenX` and `worldY` may be literals or byte-backed runtime expressions, so fixed-screen actors and projected world-space actors can share the same SDK operation. |
| `Camera.AabbHitTop(screenX, worldY, width, height, flags)` | Return the top world-pixel Y of the first/top overlapped tile matching the requested flags for a camera-relative AABB, or `255` when there is no hit. This exposes a collision fact for landing resolution without owning movement policy. |
| `Camera.ScreenAabbTiles(screenX, screenY, width, height, flags)` | Return `1` if a screen-space AABB overlaps requested world flags after adding the current camera X/Y state. This is the actor/runtime form used when both actor axes are projected to screen bytes. |
| `Camera.ScreenAabbHitTop(screenX, screenY, width, height, flags)` | Return the top screen-pixel Y of the first/top overlapped tile matching the requested flags for a screen-space AABB, or `255` when there is no hit. Source/framework code can add the current camera Y bytes back when it needs to resolve a world Y. |

World flag values are `0` empty, `1` solid, `2` hazard, and `4` platform. Values can be combined.

For Tiled maps, external tilesets keep the PNG path saved by Tiled as the editable baseline. During target lowering, that PNG path follows the target-variant convention used by sprite assets: `tiles.png` can resolve to `tiles.gb.png`/`tiles.GameBoy.png` on Game Boy or `tiles.nes.png`/`tiles.NES.png` on NES, falling back to `tiles.png` when no variant exists. The current NES lowering derives a universal background color, up to four background palette slots, and the initial attribute table from the selected tileset PNG's placed map tiles; runtime-streamed rows refresh touched attributes as palette slot 0 until richer Tiled palette provenance is carried into the streaming path.

### Logical palettes

| Signature | Semantics |
| --- | --- |
| `Palette.Background(slot, c0, c1, c2, c3)` | Declare one logical background palette slot with four logical luminance tones `0..3`. |
| `Palette.Sprite(slot, c0, c1, c2, c3)` | Declare one logical sprite palette slot with four logical luminance tones `0..3`. |

The slot is logical and capability-checked against the target descriptor. The four color values are target-independent luminance tones `0..3`; each backend maps them to its hardware palette representation. NES sprite PNG assets can derive a hardware sprite palette for the draw slot that uses the asset, while preserving the universal background color in sprite palette entry `0`. Raw `Palette.Set(...)` and `ObjectPalette.Set(...)` remain target-intrinsic compatibility calls and are not the SDK v1 palette declaration surface.

### Camera

| Signature | Semantics |
| --- | --- |
| `Camera.Init(mapWidth, streamY, streamHeight)` | Initialize camera state for the active world map. |
| `Camera.SetPosition(x, y)` | Request a camera position in world pixels. This maps to `Sdk2DOperation.SetCameraPosition`. |
| `Camera.Apply()` | Apply the current camera state to the target during the frame. |

Targets may lower camera movement differently. The SDK contract is position-based; direction-specific helpers such as `camera_move_right()` and raw scroll calls such as `scroll_set(...)` are transitional or target-intrinsic APIs. Game Boy supports non-zero Y through `Camera.SetPosition(x, y)` with one-pixel-per-call stepping and row streaming during `Camera.Apply()`. Diagonal Game Boy movement is accepted through the target's staggered camera-stream capability: a column and row can be pending together, but only one visible background-map edge is committed per VBlank inside the 21-tile budget. NES supports non-zero Y through four-screen nametables: maps up to the initial 64x60 surface scroll without runtime tile writes, and larger source-authored worlds stream one exposed column or row per VBlank with a 32-tile row/column budget and a 9-byte row-attribute refresh.

### Logical sprites and animation

| Signature | Semantics |
| --- | --- |
| `Sprite.Asset(name, path[, frameWidth, frameHeight])` | Declare a logical sprite asset. PNG sheets use explicit frame dimensions; JSON assets remain a transitional compatibility format. |
| `Sprite.Width(name)` | Return the logical sprite width known at compile time. |
| `Sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` | Draw a logical sprite frame with portable horizontal flip and logical palette-slot selection. |
| `Animation.Clip(name, firstFrame, duration...)` | Declare a looping frame-duration table. |
| `Animation.Frame(name, tick)` | Return the frame index for a tick in a declared clip. |

For PNG assets, a generic path can be specialized per target without changing source. If source asks for `Mario.png`, the selected target first looks for `Mario.gb.png`/`Mario.GameBoy.png` or `Mario.nes.png`/`Mario.NES.png` next to it, then falls back to `Mario.png`. Existing platform-suffixed source paths are normalized while searching for the selected target variant, so a transitional `Mario.gb.png` reference can still pick `Mario.nes.png` when compiling NES.

The `x`, `y`, `frame`, and `flipX` arguments are byte-backed constants or storage locations in the shared SDK operation model. `flipX` is a portable boolean, not a raw hardware attribute byte. `paletteSlot` is a compile-time logical sprite palette slot checked against the target descriptor.

On Game Boy and NES, `Sprite.Draw(...)` is injected as an SDK library helper over a target intrinsic whose descriptor treats the sprite asset id and palette slot as compile-time operands. The collector still produces `Sdk2DOperation.DrawLogicalSprite`, so capability checks, frame-budget checks, and emitted bytes stay aligned with the legacy `sprite_draw(...)` compatibility spelling.

### Actor framework slice

The current actor framework frontend is source sugar over fixed storage. It does
not add actor-specific target intrinsics and does not introduce heap allocation,
object identity, virtual dispatch, delegates, closures, or function pointers.

| Signature | Semantics |
| --- | --- |
| `Actors.Pool(name, capacity)` | Declare a fixed local actor pool. `capacity` must be a literal `1..255` and fit the fixed struct-array storage model. For `pool.Draw()`, targets validate `capacity * max(enemy metasprite hardware sprites)` against their sprite budget after JSON/PNG assets have been resolved. The frontend expands this to `Actor name[capacity];`, where `Actor` is a byte-sized framework state record. |
| `Actors.SpawnLayer(pool, "map.tmj", "layer")` | Read a Tiled object layer from the named map, keep its authored spawns as generated ROM-table helpers, and emit a runtime activation pass at the call site. Call it once per frame after `Camera.SetPosition(...)` to recycle actors outside the current camera window and activate newly visible spawns into free pool slots. Objects use a `kind` string property, Tiled `type`/`class`, or object `name` to select the actor kind; world `x` and `y` must fit `0..65535` and are split into low `x`/`y` plus high `xHi`/`yHi`. |
| `Actors.SpawnWindow(pool, "map.tmj", "layer", left, width)` | Read the same Tiled object layer, but activate against the camera-relative half-open window `[cameraX + left, cameraX + left + width)`. `left` and `width` are literal bytes. This is a runtime window, not compile-time filtering. |
| `Enemies.Def(name, sprite: asset, behavior: Behavior, animation: clip, speed: n, hp: n, cooldown: n, contactDamage: n, hitboxWidth: n, hitboxHeight: n)` | Declare byte-sized per-enemy metadata. `behavior`, `sprite`, and `animation` must be identifiers when supplied; numeric properties must be literal bytes. Omitted numeric properties default to `0`, except `hp`, which defaults to `1`. |
| `Enemies.Behavior(kind)`, `Enemies.Speed(kind)`, `Enemies.Hp(kind)`, `Enemies.Cooldown(kind)`, `Enemies.ContactDamage(kind)`, `Enemies.HitboxWidth(kind)`, `Enemies.HitboxHeight(kind)` | Return metadata for a runtime kind through generated inline helpers over constants. The frontend emits only helpers that source actually calls, so unused metadata helpers do not remain in the lowered program or add bytes. |
| `pool.TouchTiles(yOffset, flags)` | Read the current camera X/Y once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX`/`screenY` from world `x`/`xHi` and `y`/`yHi`, branch by kind, cull slots outside the visible camera window, and call `Camera.ScreenAabbTiles(...)` with the kind's literal hitbox width/height. On hit, set actor `state` to `contactDamage` or `1` when no damage is declared. |
| `pool.LandOnTiles(searchTopOffset, searchHeight, flags)` | Read the current camera X/Y once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX`/`screenY`, branch by kind, cull slots outside the visible camera window, and call `Camera.ScreenAabbHitTop(...)` with the kind's literal hitbox width. On hit, add camera Y back to the returned screen top, assign actor `y`/`yHi`, clear `vy`, and set `state` to `1`. |
| `pool.TouchPlayer(playerX, playerY, playerWidth, playerHeight)` | Read the current camera X/Y once for the helper's generated loop, loop active slots, compute each actor's camera-relative `screenX`/`screenY`, branch by kind, cull slots outside the visible camera window, and test each kind's literal hitbox against a literal player AABB in screen coordinates. On hit, set actor `state` to `contactDamage` or `1`. |

### Projectile framework MVP

The projectile frontend is source sugar over fixed local storage, like the actor
framework. It does not add projectile-specific target intrinsics, heap
allocation, replacement policies, virtual dispatch, or direct OAM writes.

| Signature | Semantics |
| --- | --- |
| `Projectiles.Pool(name, hero: n, enemy: n, requests: n, offscreenMargin: n[, effects: pool])` | Declare separate fixed hero/enemy projectile pools plus a fixed spawn-request queue, and initialize every pool/request slot inactive before gameplay code observes it. Literal defaults are `hero: 3`, `enemy: 8`, `requests: 8`, and `offscreenMargin: 16`; capacities must fit byte-sized fixed struct arrays. The optional `effects` pool receives projectile spawn, impact, and expiration effect requests. |
| `Projectiles.Def(name, team: Hero|Enemy, sprite: asset, speedX: n, speedY: n, damage: n, lifetime: n, hitboxWidth: n, hitboxHeight: n[, behavior: Linear|GravityArc][, tileCollision: None|Expire|Bounce][, bounceSpeedY: n][, spawnEffect: effect][, impactEffect: effect][, expireEffect: effect])` | Declare projectile metadata as constants. `Linear` is the default movement behavior. `None` is the default tile collision response. `Expire` deactivates the projectile on tile contact. `Bounce` keeps it active and assigns `vy = -bounceSpeedY`, so later `GravityArc` updates produce fireball-style arcs. The first MVP supports positive byte horizontal speeds, byte-sized hitboxes, and signed stored vertical velocity for bounce/gravity. Optional effect hooks enqueue requests into the projectile pool's bound effect pool; impact effects are used by actor, hero, and tile contacts without participating in damage logic. |
| `pool.Request(kind, x, y, direction[, result[, owner]])` | Enqueue a spawn request in deterministic slot order without mutating projectile pools directly. `direction` selects horizontal travel: `0` moves the projectile rightward (`+speedX`), any non-zero value moves it leftward (`-speedX`). If `result` is supplied, it is set to `1` when a queue slot was claimed and left/set to `0` when the request queue is full. |
| `pool.ProcessRequests()` | Drain queued requests in order into the matching hero or enemy pool. Full pools do not replace active projectiles; the request is consumed deterministically. |
| `pool.Update()` | Move active projectiles horizontally by their stored `vx` in the per-projectile travel direction (rightward for `direction == 0`, leftward otherwise) and vertically by signed stored `vy`: positive values move down, negative values move up. It increments age and deactivates when age reaches `lifetime`. `GravityArc` applies the current `vy` and then increments it for following frames. Projectiles are also culled when their projected position leaves the current camera rectangle expanded by `offscreenMargin` on axes whose screen span plus margin fits in the byte-sized projection; 256-pixel axes cull at the visible edge. |
| `pool.Draw()` | Project active projectiles through the current camera and submit visible projectiles through `Sprite.Draw(...)`; no backend-specific OAM allocation is exposed to gameplay source. When the program never configures a camera (no `Camera.Init`), the projection uses a literal `0` camera instead of reading the target's camera runtime state, so camera-less samples draw projectiles deterministically across targets and emulators rather than depending on uninitialized power-on memory. |
| `pool.TouchTiles(yOffset, flags)` | Test active hero and enemy projectiles that declare a non-`None` `tileCollision` response against `Camera.ScreenAabbTiles(...)` after the same camera projection used for draw and actor/hero contact. `yOffset` shifts the tested AABB vertically, and `flags` selects world collision flags. `Expire` optionally enqueues the projectile impact effect and deactivates the projectile. `Bounce` optionally enqueues the impact effect and sets the projectile's signed `vy` to `-bounceSpeedY` without deactivating it. |
| `pool.TouchActors(actorPool)` | Test hero projectiles against active actors in the supplied actor pool using camera-projected, page-aware AABBs and literal projectile/enemy hitboxes. On the first hit, subtract projectile damage from actor `health`, copy damage to actor `state`, optionally enqueue the impact effect, and deactivate the projectile. |
| `pool.TouchHero(playerX, playerY, playerWidth, playerHeight, damageTarget)` | Test enemy projectiles against a screen-space hero AABB after camera projection. On hit, add projectile damage to `damageTarget`, optionally enqueue the impact effect, and deactivate the projectile. |
| `Effects.Pool(name, capacity: n, requests: n)` | Declare a fixed visual-effect pool plus a fixed effect-spawn request queue. Pools initialize inactive and use deterministic first-free-slot request and spawn handling. |
| `Effects.Def(name, sprite: asset, lifetime: n)` | Declare visual-only effect metadata. Effects draw through `Sprite.Draw(...)`, age once per `effects.Update()`, and deactivate at `lifetime`. |
| `effects.Request(kind, x, y)` | Enqueue a visual effect request with a low-byte world position and zero high bytes. Projectile hooks enqueue requests with the projectile's full split world position. |
| `effects.ProcessRequests()` | Drain queued effect requests in deterministic order into the fixed effect pool. Full pools do not replace active effects; the request is consumed deterministically. |
| `effects.Update()` | Increment active effect age and deactivate when it reaches the effect lifetime. |
| `effects.Draw()` | Project active effects through the current camera and submit visible effects through `Sprite.Draw(...)`; effects do not write backend OAM directly. |

This is the first projectile lifecycle slice, not the complete issue #220 end
state. Sound hooks, actor-emitter cooldown helpers, richer particle/effect
behavior, and target-seeking movement such as homing remain follow-up work. The current effect pool is kept
visual-only and separate from projectile collision/damage state.

`Actors.Pool(...)` and `Enemies.Def(...)` are accepted as statements inside the
compiled source and disappear before target lowering. The generated actor state
fields are `kind`, `active`, `x`, `xHi`, `y`, `yHi`, `vx`, `vy`, `state`,
`timer`, `facing`, `animTick`, and `health`. `x`/`y` are low bytes of world
position, paired with `xHi`/`yHi`; this keeps pooled fields byte-sized without
requiring mixed-width struct array layout. Game Boy currently supports `pool.Update()` and
`pool.Draw()` for the basic byte-field behavior set by expanding them to grouped
loops over slots with direct kind checks and stable `Sprite.Draw` calls.

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
that basic behavior set. `pool.Draw()` reads the current camera X/Y state once
for its generated loop and computes `screenX = actorWorldX - cameraX` and
`screenY = actorWorldY - cameraY`. One-slot
pools initialize draw coordinates to an offscreen Y, overwrite them only when
the slot is active and inside the visible camera window, and emit ordinary
`Sprite.Draw(...)` calls through stable call sites so hardware sprite slots are
hidden rather than left stale. Larger pools keep the current camera-relative
visible-actor draw/cull shape until a per-slot OAM allocation policy lands.
Draw uses `Animation.Frame(...)` when a definition declares an animation clip. Drawn pools
are checked against target sprite budgets with target-resolved metasprite
geometry, so a pool of multi-sprite enemy definitions is charged by hardware
sprite pieces rather than by actor slots. Aggregate hardware sprite usage is
also validated by the same frame-budget pass as hand-written sprite draws.
`pool.TouchTiles(...)`, `pool.LandOnTiles(...)`, and
`pool.TouchPlayer(...)` use the same per-phase camera cache and per-actor
2-axis projection/visibility guard as draw, so collision/contact is tested per visible
actor instead of at one fixed screen column. Separate helper calls remain
separate phases and do not share a hoisted camera read across statements. The
tile helpers emit ordinary camera screen-AABB SDK calls; they do not introduce
actor-specific target intrinsics. Tiled spawn helpers read the same
target-neutral map importer as
`World.Load(...)` and lower to generated ROM-table helpers plus runtime fixed-slot
activation before target lowering.

#### Low-level equivalent pattern

The actor framework is deliberately equivalent to hand-authored fixed storage.
For a small pool, the source-level sugar:

```c
Actors.Pool(enemies, 2);
Enemies.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1,
    hitboxWidth: 8, hitboxHeight: 8);
Actors.SpawnLayer(enemies, "level.tmj", "actors");
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
    u8 yHi;
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
inline u8 __enemies_spawn_0_yHi(u8 index) => 0;
inline u8 __enemies_spawn_0_active(u8 index) => 1;
inline u8 __enemies_spawn_0_vx(u8 index) => 0;
inline u8 __enemies_spawn_0_vy(u8 index) => 0;
inline u8 __enemies_spawn_0_state(u8 index) => 0;
inline u8 __enemies_spawn_0_timer(u8 index) => 0;
inline u8 __enemies_spawn_0_facing(u8 index) => 0;
inline u8 __enemies_spawn_0_animTick(u8 index) => 0;
inline u8 __enemies_spawn_0_health(u8 index) => 0;

void Main() {
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
                            enemies[slot].yHi = __enemies_spawn_0_yHi(spawn);
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
    u8 cameraY = __rs_actor_camera_y_lo();
    u8 cameraYHi = __rs_actor_camera_y_hi();
    for (u8 i = 0; i < countof(enemies); i += 1) {
        if (enemies[i].active != 0) {
            if (enemies[i].kind == Goomba) {
                enemies[i].x += GoombaSpeed;
                if (enemies[i].x < GoombaSpeed) {
                    enemies[i].xHi += 1;
                }

                u8 screenX = enemies[i].x - cameraX;
                u8 screenY = enemies[i].y - cameraY;
                if ((((enemies[i].xHi == cameraXHi) && (enemies[i].x >= cameraX)) ||
                     ((enemies[i].xHi == cameraXHi + 1) && (enemies[i].x < cameraX))) &&
                    (screenX < 160) &&
                    (((enemies[i].yHi == cameraYHi) && (enemies[i].y >= cameraY)) ||
                     ((enemies[i].yHi == cameraYHi + 1) && (enemies[i].y < cameraY))) &&
                    (screenY < 144)) {
                    if (Camera.ScreenAabbTiles(screenX, screenY,
                            GoombaHitboxWidth, GoombaHitboxHeight, 1) != 0) {
                        enemies[i].state = 1;
                    }

                    u8 hitTop = Camera.ScreenAabbHitTop(screenX, screenY - 4,
                        GoombaHitboxWidth, 12, 1);
                    if (hitTop != 255) {
                        enemies[i].y = hitTop;
                        enemies[i].y += cameraY;
                        enemies[i].yHi = cameraYHi;
                        if (enemies[i].y < hitTop) {
                            enemies[i].yHi += 1;
                        }
                        enemies[i].vy = 0;
                        enemies[i].state = 1;
                    }

                    if (screenX < 88 && screenX + GoombaHitboxWidth > 72 &&
                        screenY < 56 &&
                        screenY + GoombaHitboxHeight > 40) {
                        enemies[i].state = 1;
                    }

                    Sprite.Draw(goomba, screenX, screenY, 0, false, 0);
                }
            }
        }
    }
}
```

The generated code keeps the helper calls as separate phases. Each
projection-bearing actor phase reads `cameraX`/`cameraXHi` and
`cameraY`/`cameraYHi` once before its slot loop, then computes `screenX` and
`screenY` per active actor from the cached camera bytes. Spawn activation remains
an X-window scan and reads only the camera X bytes.

### Optional HUD

| Signature | Semantics |
| --- | --- |
| `Hud.SetTile(mode, x, y, tile)` | Declare one static HUD tile for a capability-gated HUD mode. |

Current mode names are `window`, `split_scroll`, `sprite_hud`, and `none`. `none` disables HUD and must compile as a no-op even on targets that declare no HUD support.

## Capability Requirements

The compiler must validate portable SDK calls against `Target2DCapabilities` and `TargetAudioCapabilities` before target lowering.

Static enforcement starts with per-operation checks, then applies a frame-budget pass for aggregate SDK budgets. The shared operation list remains flattened across control flow, so aggregate checks must not count that list directly. Instead, `Sdk2DOperationCollector.CollectFrameBudgets(...)` computes possible frame windows around `Video.WaitVBlank()` and `Input.Poll()`, treats `if`/`else` arms as exclusive alternatives, and validates the result through `Sdk2DOperationValidator.ValidateFrameBudget(...)`. Multiple explicit stream calls that exceed `MaxBackgroundTileWritesPerFrame` in one possible frame fail before target lowering.

For logical sprites, targets feed their compiled metasprite geometry and hardware sprite size mode into the same frame-budget pass. The compiler rejects unsupported `SpriteSizeModes`, one possible frame that exceeds `SpriteCount`, and `MaxSpritesPerScanline` when the sprite draw uses a constant Y position. Actor pool draws receive an earlier target-aware diagnostic after sprite assets are compiled: for every drawn pool, `capacity * max(Enemies.Def metasprite hardware sprites)` must fit `SpriteCount`, and `capacity * max(Enemies.Def busiest-scanline hardware sprites)` must fit `MaxSpritesPerScanline`. That diagnostic names the pool and the enemy definition with the largest metasprite pressure. The frame-budget pass still validates the lowered draw operations with branch-as-alternative and frame-boundary semantics. Other runtime Y positions still cannot be placed on a specific scanline statically, so dynamic per-scanline overflow remains author/runtime responsibility outside the actor pool path. An unsound count over the flattened operation list would reject valid programs and is intentionally avoided. See issue #102.

| SDK area | Required capabilities |
| --- | --- |
| Frame/input | Target runtime support for frame wait and controller polling. |
| Audio/BGM | Target BGM support and a music resource format listed by the target descriptor. |
| World map | Active world dimensions must fit the target's static setup or streaming path. |
| Camera X | `ScrollAxes.Horizontal`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed column. |
| Camera Y | `ScrollAxes.Vertical`, fine-scroll support as required by target lowering, and enough background tile writes for any streamed row. |
| Diagonal camera movement | Combined row plus column writes must fit one frame, or the target must declare staggered camera stream draining so each committed edge fits its frame budget. |
| Explicit background streaming | `RuntimeBackgroundStreamingAxes` must include the requested `StreamMapColumn` or `StreamMapRow` axis, and the requested tile count must fit `MaxBackgroundTileWritesPerFrame`. |
| Logical palettes | Palette declarations must provide exactly four colors and the requested background or sprite slot must fit the target descriptor. |
| Logical sprites | Sprite count, sprite size modes, scanline limits, sprite transforms, and palette-slot count must fit the lowered metasprite. |
| Animation | Clip frame indexes and durations must fit the declared logical asset. |
| Actor framework | Pool capacity must be a literal fixed size, pooled fields are byte-sized, and enemy metadata must be literal/identifier data that can lower to constants and inline helper branches. |
| Collision | `World.Map(...)` or `World.Load(...)` data and matching flag rows must exist before runtime collision reads. |
| Camera-relative collision | Target must declare `CameraRelativeAabb` or `CameraRelativeAabbHitTop` collision-query support for the requested query, and the screen span must fit the visible target width. |
| HUD | Requested `HudMode` must be declared by the target, except `none`, which is always accepted as disabled HUD. |

## Target Support

| API group | Game Boy | NES |
| --- | --- | --- |
| Frame/input | Supported. `Video.WaitVBlank()` and `Input.Poll()` lower to DMG VBlank and JOYP reads. | Supported in the runtime spike. `Input.Poll()` reads controller port `$4016`. |
| World map setup | Supported. `World.Map(...)` and `World.Load(...)` build initial visible tiles, streaming rows/columns, and collision flags. | Supported for horizontal maps that fit the one-byte streaming runtime. Startup seeds a 64-column two-nametable buffer and runtime camera movement streams wider source maps through it. Four-screen free-scroll maps, including tall Tiled `World.Load(...)` maps, preload the initial 64x60 surface and keep source rows/columns in ROM for staggered runtime streaming beyond that buffer. |
| Camera X | Supported with one-pixel stepping and column streaming. | Supported for `Camera.SetPosition(x, 0)` and `Camera.Apply()`, with absolute source-tile tracking, horizontal nametable selection, and runtime column streaming into the off-screen nametable for horizontal-only maps wider than 32 columns. In four-screen free-scroll mode, X movement pans within the 64x60 buffer and streams wider worlds one edge per VBlank. |
| Camera Y | Supported with one-pixel stepping and row streaming. Diagonal movement uses a staggered one-edge-per-VBlank policy so a 19-row column or 21-column row crossing stays inside the 21-tile background write budget. | Supported through four-screen nametables. Maps up to 64x60 move without runtime tile writes; taller source-authored worlds stream the exposed 32-tile row and 9 touched attribute bytes with the staggered one-edge-per-VBlank policy. |
| Logical sprites | Supported for PNG Game Boy sheets and transitional JSON assets. | Supported for PNG NES sheets and transitional JSON assets with `platforms.nes.frames`. |
| Palette declarations | Background slot `0` and sprite slots `0..1` through `Palette.Background(...)` and `Palette.Sprite(...)`. | Background and sprite slots `0..3` through `Palette.Background(...)` and `Palette.Sprite(...)`. |
| BGM | Supported for VGM/VGZ DMG logs, hUGETracker `.uge` v6 songs, and transitional `.gbapu` APU traces in the current runtime. GBS files must first be exported to `.gbapu` with the target-specific CLI helper. | Supported for VGM/VGZ 2A03 logs covering pulse, triangle, noise, DMC/DPCM, `$4015`, and `$4017`; expansion audio is deferred. |
| Animation helpers | Supported on Game Boy runner path. | Supported for byte-sized clip frame indexes, frame durations, and total duration. |
| Actor framework slice | `Actors.Pool`, `Actors.SpawnLayer`, `Actors.SpawnWindow`, `Enemies.Def`, called `Enemies.*` metadata helpers, and `pool.Update()`/`pool.Draw()`/`pool.TouchTiles()`/`pool.LandOnTiles()`/`pool.TouchPlayer()` lower before Game Boy target emission to fixed struct arrays, constants, inline helper branches, generated spawn-table helpers, `used[]`, runtime activation, camera-relative 2-axis draw/collision/player contact, and the basic behavior set: `Walker`, `Flyer`, `Patrol`, `Shooter`, `Hazard`, and direction-driven `Chaser`. | The same source-to-source actor framework slice lowers before NES target emission with NES sprite/scanline budgets and 2-axis camera-relative actor draw/collision support. |
| Projectile/effect framework MVP | `Projectiles.Pool`, `Projectiles.Def`, `pool.Request()`, `pool.ProcessRequests()`, `pool.Update()`, `pool.Draw()`, `pool.TouchTiles()`, `pool.TouchActors()`, `pool.TouchHero()`, `Effects.Pool`, `Effects.Def`, and `effects.*` lifecycle calls lower before Game Boy target emission to fixed inactive-initialized projectile/effect arrays, fixed inactive-initialized request queues, deterministic pool-full behavior, signed vertical projectile movement, camera-margin projectile culling, camera-relative sprite drawing, tile bounce/expiration responses through `Camera.ScreenAabbTiles(...)`, page-aware projectile-vs-actor collision, accumulated hero damage, and optional projectile spawn/impact/expiration effect requests. | The same source-to-source projectile/effect MVP lowers before NES target emission, initializes pool/request slots inactive, culls projectiles at the projected camera bounds, handles tile bounce/expiration through the shared camera collision query, and submits visible projectiles/effects through `Sprite.Draw(...)`; platform-specific OAM allocation remains outside gameplay source. |
| World collision queries | Supported on Game Boy runner path. | Generic `world_tile_flags_at(...)` and `collision_aabb_tiles(...)` are not implemented in the current NES spike. |
| Camera-relative collision | Supported through `Camera.AabbTiles(...)`/`Camera.AabbHitTop(...)` for literal or byte-backed screen X plus world Y values, and `Camera.ScreenAabbTiles(...)`/`Camera.ScreenAabbHitTop(...)` for byte-backed screen X/Y values. | Supported through the same camera AABB and screen-AABB forms on horizontal and four-screen camera paths. |
| HUD | `window` HUD supported for static startup tiles. `split_scroll` is rejected. | No portable HUD mode declared. `none` is accepted; `window` fails. |

Use `samples/manifest.json` to identify which samples are portable. Currently `samples/cross-target-camera/camera.rs` is the only `portable-sdk` sample and builds for both Game Boy and NES.

## Failure Modes

Portable calls should fail early with target-specific diagnostics instead of reaching backend-specific surprises. Current important diagnostics include:

| Scenario | Diagnostic |
| --- | --- |
| Game Boy split-scroll HUD | `Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.` |
| NES Window HUD | `Target 'nes' does not support Window HUD. Use disable HUD for this target.` |
| NES four-screen camera stream area overflow | `NES four-screen free scroll stream area must fit within the 60-row four-screen height.` |
| Game Boy sprite palette slot overflow | `Target 'gb' supports sprite palette slots 0..1, but palette slot 2 was requested.` |
| NES world tile flag query | `Target 'nes' does not support world tile flag queries.` |
| Game Boy hUGETracker timer tempo | `hUGETracker timer-based tempo is not supported by the Game Boy BGM v1 runtime.` |
| Game Boy sprite palette overflow | `Target 'gb' supports sprite palette slots 0..1, but slot 2 was requested.` |
| NES sprite palette overflow | `Target 'nes' supports sprite palette slots 0..3, but slot 4 was requested.` |
| Actor generated name collision | `actor framework cannot generate Enemies.Def 'Goomba' speed constant named 'GoombaSpeed' because user constant 'GoombaSpeed' is already declared.` |
| Actor pool dynamic capacity | `Actors.Pool for 'enemies' requires a literal capacity from 1 to 255.` |
| Actor pool metasprite budget overflow | `Target 'gb' supports 40 hardware sprites per frame, but Actors.Pool for 'enemies' can draw up to 42 because capacity 21 times Enemies.Def 'Goomba' sprite 'goomba' uses 2 hardware sprites.` |

Calls that expose raw hardware state are outside SDK v1. Examples include `Scroll.Set(...)`, `Sprite.Set(...)`, `Tilemap.Set(...)`, `Tilemap.Fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `Palette.Set(...)`, and `ObjectPalette.Set(...)`. They can remain available in target-intrinsic samples while compatibility is needed. Prefer `Palette.Background(...)` and `Palette.Sprite(...)` for SDK-shaped logical-tone palette declarations.

## Current Stabilization Gaps

SDK v1 is usable for the current cross-target camera sample, and the runner-shaped camera-relative collision/animation/audio slice now lowers on both Game Boy and NES. The full runner is still a target-acceptance scenario rather than a portable SDK sample because several broader world/HUD contracts are still missing. It now uses a 2-axis dead-zone camera over a tall 24x48 Tiled map that expands to a 48x96 tile world and declares per-target VGM/VGZ background music; larger diagonal free scroll is demonstrated by `samples/nes-free-scroll/freescroll.rs` for source-authored columns and by `samples/tiled-free-scroll/free-scroll.rs` for Tiled `World.Load(...)`.

- `Camera.AabbTiles(...)`, `Camera.AabbHitTop(...)`, `Camera.ScreenAabbTiles(...)`, and `Camera.ScreenAabbHitTop(...)` are capability-gated SDK queries for camera-relative AABBs. Game Boy and NES both support the runner-shaped projected-screen-X form and actor-framework calls with per-actor projected X/Y.
- On Game Boy, `Camera.AabbTiles(...)` and `Camera.AabbHitTop(...)` are injected library helpers over target intrinsics whose descriptors carry the hidden world id and flags as compile-time operands, then collect to the same SDK operations as the legacy snake_case compatibility calls.
- `collision_aabb_tiles(...)` still reports overlap only. Use `Camera.AabbHitTop(...)` when an actor needs the contacted tile's top edge while keeping landing and movement resolution in source.
- Logical palette declarations now cover background and sprite palette slots through `Palette.Background(...)` and `Palette.Sprite(...)`. The color values are logical tones `0..3`; targets map those tones to their hardware palette registers or palette RAM. NES sprite PNG assets may refine the sprite slot with a derived hardware palette for their opaque colors.
- `samples/cross-target-camera/camera.rs` is the only `portable-sdk` sample. `samples/runner/runner.retrosharp.json` remains a shared Game Boy/NES `target-acceptance` project with game-owned helper/state files under `samples/runner/src`, a 2-axis dead-zone camera, and per-target VGM/VGZ music. `samples/tiled-tall/tall.rs` is Game Boy-only target-acceptance coverage for vertical Tiled `World.Load(...)` scrolling, while `samples/tiled-vscroll/vscroll.rs` covers the same vertical Tiled path on Game Boy and NES with a wider 40x60 map. `samples/nes-free-scroll/freescroll.rs` is target-acceptance coverage for diagonal camera movement on Game Boy and NES over source-authored columns, `samples/tiled-diagonal/diag.rs` is Game Boy-only target-acceptance coverage for diagonal Tiled `World.Load(...)`, and `samples/tiled-free-scroll/free-scroll.rs` is Game Boy/NES target-acceptance coverage for diagonal Tiled `World.Load(...)`.

## Minimal Game Boy/NES Example

This shape is the current smallest portable sample. It uses unified world data, tick input, horizontal camera positioning, and logical sprite drawing while avoiding raw target calls.

```c
void Main() {
    Video.Init();

    World.Column(0, 1, 2, 3, 4);
    World.Column(1, 2, 3, 4, 5);
    World.Column(2, 3, 4, 5, 1);
    World.Column(3, 4, 5, 1, 2);
    World.Flags(0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 1, 1);
    World.Map(4, 10, 4);
    Camera.Init(4, 10, 4);
    Sprite.Asset(marker, "marker.json");

    while (true) {
        Video.WaitVBlank();
        Input.Poll();
        let cameraX = Input.HoldTicks(Button.Right);
        Camera.SetPosition(cameraX, 0);
        Camera.Apply();
        Sprite.Draw(marker, 72, 72, 0, false, 0);
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

The sample uses a target-variant JSON sprite asset (`platforms.gb` and `platforms.nes`). That asset format is transitional, but the source-level `Sprite.Asset(...)` and `Sprite.Draw(...)` calls are the portable SDK surface. Game Boy and NES currently lower `Sprite.Draw(...)` through the `RetroSharp.Portable2D` package helper; `sprite_draw(...)` remains accepted as a compatibility alias.
