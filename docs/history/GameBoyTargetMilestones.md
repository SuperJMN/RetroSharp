# Game Boy Target — milestones and progress history

Status: background only. Last updated: 2026-07-26.

Completed checklist, dated progress snapshots, and the closed framework
backlog for the Game Boy target. This is historical record, not the current
subset; for current Game Boy support read [`../GameBoyTarget.md`](../GameBoyTarget.md).

## Short-Term Checklist

- [x] Parse `while`.
- [x] Generate a real Game Boy runtime loop.
- [x] Move sprites by writing OAM during the loop.
- [x] Add `Scroll.Set(x, y)` over Game Boy `SCX`/`SCY`.
- [x] Build a runner sample with a fixed actor and scrolling background.
- [x] Stream new background columns every 8 pixels.
- [x] Represent maps as source data instead of ad hoc `Tilemap.Set` calls.
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
- [x] Reuse the existing horizontal camera runtime from `Camera.SetPosition(...)`.
- [x] Replace direction-specific camera helpers with a position-based camera API in the runner.
- [x] Unify visual map data, streaming data, and collision flags into one world resource.
- [x] Extend camera position state and `Camera.Apply()` to vertical scroll.
- [x] Stream visible background rows when vertical camera movement crosses tile boundaries.
- [x] Preserve logical sprite metadata for loaded Game Boy sprite assets.
- [x] Consume the collected SDK operation stream during Game Boy runtime lowering.
- [x] Replace raw `Sprite.Draw` flags with a portable `flipX` boolean.
- [x] Add logical sprite palette slot selection to `Sprite.Draw`.
- [x] Add animation clip data and looping `Animation.Frame(...)` lookup.
- [x] Migrate the runner's run animation to an explicit tick plus `Animation.Frame(...)`.
- [x] Add world-coordinate tile flag queries through `World.TileFlagsAt(...)`.
- [x] Add boolean AABB tile collision queries through `collision_aabb_tiles(...)`.
- [x] Add a NES parity spike for logical sprites, input, and horizontal camera scroll.
- [x] Add a cross-target camera sample that can compile for both Game Boy and NES.
- [x] Add a Game Boy Window HUD prototype behind capability checks.

## Progress Snapshot

Landed on 2026-06-01:

- The Game Boy runner can draw an editable 16x27 Aseprite/PNG logical sprite and lower it to 8x16 hardware sprites.
- The runner scrolls the background through the camera runtime and streams generated world-map data through `map_stream_column(...)`.
- `map_tile_at(...)`, `map_flags_at(...)`, and `camera_span_has_flags(...)` let RetroSharp source query generated world-map data for simple tile collision.
- `Input.Poll()` and the tick-based `Input.*` helpers let RetroSharp source query the Game Boy joypad.
- The sample now has a small gameplay loop: gravity, simple ground collision, running animation, and A-button jump.
- The compiler subset grew just enough for that loop: runtime-local addition, relational conditions against constants, value-returning runtime intrinsics, and byte-backed state.
- Generated runner screenshots are not tracked as source artifacts; regenerate them with RetroArch when needed.

Landed after the initial runner loop:

- `Input.Poll()`, `Input.IsDown(...)`, `Input.WasPressed(...)`, `Input.WasReleased(...)`, and `Input.HoldTicks(...)` provide a tick-based input surface.
- The Game Boy runner uses the input helpers for edge-triggered, variable-height jumping: while A is held and vertical velocity is below `-$20`, gravity is `+1`; release or reaching the threshold switches to `+5` without clamping velocity.
- The runner's horizontal movement, dead-zone camera state, and run animation now advance from a horizontal speed value rather than raw D-pad state: holding a direction moves at a brisk base walk speed and faces that way immediately, holding B builds speed up to a faster run limit only while grounded (Mario has traction), airborne input preserves horizontal momentum without building or bleeding speed, releasing the D-pad coasts to a stop through ground friction, and pressing the opposite direction turns instantly instead of drifting backward. Both single-pixel collision probes in a two-step run tick project against the camera X captured at tick start while source camera state advances after accepted movement; this keeps the probes aligned with the runtime camera until the single end-of-tick `Camera.SetPosition(...)` and prevents a one-pixel entry into a solid stair.
- `Camera.SetPosition(x, y)` walks the runtime camera toward the requested word position one pixel per step. Each call advances up to two tile crossings (16 px) per axis; same-axis crossings keep two pending stream slots and commit both during the next `Camera.Apply()`, so a single call per frame reaches runner-scale targets without stale background edges.
- The runner now draws idle, run, and jump states through a single player sprite sheet so the same OAM slots are updated every frame; the jump frame is used whenever the actor is airborne.
- `Sprite.Draw` accepts optional portable `flipX` and `paletteSlot` values; the runner uses them to make the same idle, run, and jump frames face left while preserving the last facing direction and selecting a logical sprite palette slot.
- `Palette.Background(...)` and `Palette.Sprite(...)` declare logical palette slots for SDK-shaped samples; the runner uses them instead of raw `Palette.Set(...)` and `ObjectPalette.Set(...)`.
- `Animation.Clip(...)` and `Animation.Frame(...)` now express the runner's run cycle while keeping `animTick`, idle, and jump state explicit in source.
- `World.TileFlagsAt(...)` lets collision code query generated world flags by pixel coordinates without depending on camera-span helpers.
- `collision_aabb_tiles(...)` reports whether an actor-sized world-space rectangle overlaps requested tile flags while keeping movement resolution explicit in source.
- `Camera.AabbTiles(...)` reports collision for camera-relative AABBs against the current camera view, including fine-scroll X alignment.
- `Camera.AabbHitTop(...)` reports the contacted tile's complete word top for a caller-defined camera-relative search AABB, using `-1` as the no-hit sentinel.

Landed after the playable-loop pass:

- The runner checks the logical player width against each covered foot column instead of using a single source-map tile.
- The initial visible background matches the same source-map pattern used for streaming, including a visible multi-column hole and failure tile.
- Separate left/right streaming cursors keep the background stable when changing direction.
- The fall path enters a staged respawn only after a complete airborne `player.y >= 320` update. It freezes physics and movement input while the source camera returns one axis at a time, at 4 px or less per tick, through the existing single `Camera.SetPosition(...)` call; VBlank, camera publication, OAM, audio, and input polling continue normally.
- Once camera source and visible state have settled at `(0, 176)` for two source publications, the runner restores the authored world spawn `(72, 273)` through `Land(...)`. Its Game Boy screen pose is therefore `(72, 97)` and the first metasprite piece is published at OAM `(80, 113)` after the retained-OAM pipeline catches up.

Landed after the camera-runtime pass:

- `Camera.Init(...)`, `Camera.Apply()`, `camera_move_right()`, `camera_move_left()`, and `camera_tile_column_at(...)` lift horizontal scrolling one layer above raw `SCX` writes and hand-managed streaming cursors.
- The camera runtime owns 16-bit world X, sub-tile scroll state, circular background-map edge columns, source-map edge columns, and 8 px column streaming.
- Camera span helpers remain available for source-map checks, including logical sprite widths through `Sprite.Width(...)`, but the runner no longer depends on them for player feet.

Landed after the Collision V1 pass:

- The runner stores player world X/Y, derives the actual screen X/Y from player position minus camera position, passes that byte-backed screen X into `Camera.AabbTiles(...)` / `Camera.AabbHitTop(...)`, and probes generated world flags with the logical width from `Sprite.Width(mario_player)`.
- Camera span collision and world-space `collision_aabb_tiles(...)` remain available, but the runner uses the camera-relative AABB helper so long maps stay aligned after the camera scrolls beyond the source-local byte range.

Landed after the landing-query pass:

- The runner uses `Camera.AabbHitTop(...)` to query the top edge of the first solid tile in a caller-defined landing search window, so descending actors can snap to stacked or multi-tile solids without copying a ladder of one-pixel probes.
- Landing policy remains source-owned: the runner queries non-rising actors for `Solid | Platform` and calls `player.Land(...)` only when the actor's previous/current feet straddle the returned tile top. The `-1` no-hit result fails that comparison naturally; when a grounded actor has no landable support, source clears `grounded` so walking off an edge starts a fall.

Landed after the NES portable spike:

- NES now supports the first shared tick-input, logical sprite, unified world-map, and horizontal camera-scroll subset.
- `samples/cross-target-camera/camera.rs` builds for both Game Boy and NES without raw sprite, scroll, tilemap, or target-palette calls.
- Later NES runner work added runtime map streaming, 2-axis dead-zone camera movement, camera-relative collision queries, runtime animation, and VGM-sourced NES BGM for the runner-shaped path. The cross-target sample still deliberately excludes generic world-space collision and HUD until those features have explicit capability-gated support on both targets.

Landed after the first HUD pass:

- `Hud.SetTile(window, x, y, tile)` compiles to the Game Boy Window tilemap at `$9C00` and enables the Window layer without sharing camera scroll state.
- `samples/window-hud/hud.rs` builds as the first HUD sample and keeps Window restrictions explicit.

Landed after the richer runner scene pass:

- The runner project `samples/runner/runner.retrosharp.json` lists local helper/state code from `samples/runner/src` and imports complete `samples/runner/assets/maps/stage1.tmj` with `World.Load(...)`. The 156x20 source cells become a 312x40 packed world in a 128 KiB MBC1 ROM while retaining target BGM/SFX, Y=304 collision, 2-axis scrolling, bank restoration, and bounded resident edge commits.
- Tileset `objectgroup` rectangles provide the runner's solid blocks and ground, while tile `30` uses `retrosharpCollision=platform` for the green one-way ledges without a separate hand-authored collision layer.
- The runner scene focuses on the player, 2-axis camera movement, packed Tiled map streaming, tileset-authored solid/one-way collision, fall reset, and variable-height jump over the complete 312x40 expanded tile world.
- Runner vertical motion is signed 4.4 fixed point in portable source. Standing/walking/running/maximum-speed takeoff uses `-$38/-$3A/-$3C/-$40`; held A applies `+1` gravity only while velocity is below `-$20`, while release or velocity `>= -$20` applies `+5` without a jump-cut clamp. Exact ROM tests measure `20.625`, `70.6875`, `85.0625`, and `100.4375` pixel rises for tap, standing, running, and maximum-speed profiles, making the standard visible standing apex 71 pixels.

Landed after the ceiling-collision pass:

- Solid blocks now block the player from below: `FrameState.ResolveCeilingHit(...)` probes a short AABB over the head with `Camera.AabbTiles(...)` while the actor is rising (`velocityY >= Level.SignedVelocityWrap`) and calls `player.BounceDown()` on contact, cancelling the jump and applying a small downward velocity so the actor rebounds with a physical feel instead of passing through the block.
- The head probe is offset to the sprite's visible content, not its full cell. The player sheet is 32 px tall but the figure is bottom-aligned with ~4 px of transparent padding at the top, so the probe references the real head at `footWorldY - CeilingProbeTopOffset` with `CeilingProbeTopOffset = 28` (probe band `[footWorldY - 28, footWorldY - 24]`). This makes the impact register when the visible head reaches the block instead of a few pixels early.
- The landing search window is feet-relative (`LandingSearchTopOffset = 3`, `LandingSearchHeight = 9`) instead of spanning the whole sprite body. `Camera.AabbHitTop(...)` may see a landable surface at or just below the feet, but source captures the previous foot before gravity and requires the previous/current pair to straddle the returned top. A non-rising actor therefore accepts a downward step that crosses a top still overlapping that window, while avoiding a snap onto a platform whose underside it crossed. A failed support query clears `grounded`, so walking off the ledge falls normally. Walls and ceilings continue to query only `Solid`. This remains source-level policy in `samples/runner/src/frame/state.rs`.

## Current Framework Backlog

The SDK v1 reference already exists in `docs/Portable2DSdkV1.md`. The #106 stabilization backlog items for runner-shaped collision, cross-target diagnostics, and logical palette declarations have landed. New work in this area should be filed as narrower follow-up issues rather than reusing the closed stabilization backlog.
