# RetroSharp Architecture Roadmap

Status: proposed architecture roadmap.
Last updated: 2026-06-30.

This roadmap defines how RetroSharp should grow from the current Game Boy runner proving ground into a portable 2D SDK without letting one machine's details become the language or public SDK by accident.

The project is viable if portability means "shared contract with explicit target capabilities, compile-time limits, and predictable lowering." It is not viable if the SDK promises identical graphics behavior, cost, and limits across every 8-bit machine.

## Current Roadmap State

The #106 portability-lowering slice is complete: SDK operation collection lives outside the language assembly, Game Boy and NES validate the shared operation stream before lowering, logical sprite drawing lowers through per-target lowerers, Game Boy map-column streaming is operation-driven, typed SDK storage descriptors replace opaque operand strings, and the first target-intrinsic prototype proves the SDK-as-library direction for `wait_frame`.

The active SDK v1 stabilization backlog is now narrower than the original #106 epic:

- #120 landed as `camera.AabbHitTop(...)`, a reusable tile-hit collision fact for landing resolution that stays below "platformer physics engine" scope.
- #121 landed as logical `palette.Background(...)` and `palette.Sprite(...)` declarations so SDK-shaped samples do not depend on raw Game Boy palette writes.
- #122 adds a runner-shaped cross-target validation sample, or a precise NES capability diagnostic when the runner-shaped slice is not portable yet.

Separate design debts: #104 tracks type-system soundness and #105 tracks the remaining Tiled import/world-flattening coupling. #103 (unify language/SDK dot-call and receiver-lowering boundaries) is now resolved and closed: SDK module knowledge moved out of the parser into `RetroSharp.Core.Sdk.SdkModuleRegistry` (the parser's `SdkDotCallLowerer` only delegates to it), and `RetroSharp.Parser.SdkDotCallResolver` is the single canonical dot-call decision (a receiver in scope shadows a same-named SDK module; otherwise a known SDK module resolves as an SDK call) consumed by both constant folding and semantic analysis. The only residual is narrow and documented in `SdkDotCallResolver`: constant folding has no variable scope, so it detects receivers by method signature; unifying the pathological collision case (a receiver method named like an SDK method on a bare module) would need full scope in the folder and should be tracked separately if it ever needs to be addressed. For #105, the structural half is extracted: `RetroSharp.Core.Sdk.Tiled.LogicalTiledMapImporter` now owns target-neutral Tiled parsing, tileset descriptors, geometry/world-slice resolution, and collision-flag interpretation, producing a `LogicalTiledMap` of source-tile references. The Game Boy importer consumes it and keeps only pixel generation, deduplication, 8x8 expansion, and per-pixel background composition. The remaining coupling is that `WorldMap2D` still stores already-lowered target tile ids and NES does not consume the neutral map yet.

## Goals

- Keep the RetroSharp language small, explicit, and target-neutral.
- Define a portable 2D SDK for common tile/sprite machines.
- Keep raw machine access available through target intrinsics.
- Make Game Boy and NES the first compatibility pair for the portable 2D contract.
- Preserve the Game Boy runner as the acceptance sample while reducing target-specific leakage.
- Fail early when a feature does not fit a target's declared capabilities.

## Non-goals

- No hidden VM, heap, GC, exceptions, or dynamic runtime.
- No promise of unlimited sprites, free palette changes, or arbitrary VRAM writes.
- No automatic physics engine in the portable SDK.
- No portable guarantee for scanline effects, mapper-specific tricks, or machine-specific register behavior.
- No requirement that every target supports HUD, diagonal scrolling, or software sprites.

## Layer Model

### Language

The language defines source syntax, types, data layout, and ABI. It must not know about cameras, sprites, tilemaps, or controller semantics.

Language work belongs here:

- Fixed-width primitives: `u8`, `i8`, `u16`, `i16`, `bool`.
- Pointers and addressable storage: `ptr<T>`, static data, ROM data, RAM data.
- `struct`, `enum`, fixed-size arrays, constants, casts, operators, and structured control flow. The current cartridge path already has the first zero-cost slice of type aliases, top-level and block-local constants with optional type annotations, decimal/hex/binary integer literal spellings with `_` separators and width suffixes, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, enums, local structs with named and shorthand initializer lists, fixed-size local arrays of byte-backed values or byte-sized structs, byte-array initializer lists and initializer-inferred lengths, per-element struct-array initializer lists, constant or runtime byte indices, struct-array field access such as `actors[i].x`, explicit casts to byte-backed local types, arithmetic and bitwise compound assignment, statement-only `++`/`--`, half-open range membership expressions, `if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, post-test `do while`, explicit infinite `loop`, short-circuit logical conditions and byte-backed 0/1 logical value expressions including unary `!`, byte-backed conditional value expressions, inline statement helpers, inline single-return expression helpers, expression-bodied helpers, named arguments and default parameter values for inline helpers, counted `for` loops, half-open range `for` loops, and `break`/`continue`; mixed-width struct-array layout and the shared ABI/layout work remain broader.
- Functions, parameters, returns, calling convention, and attributes.
- Memory placement attributes such as `[section]`, `[bank]`, `[zeropage]`, or `[align]`.
- Target attributes such as `[target("gb")]` or `[intrinsic]`.
- Namespaces or modules if needed to separate SDK and intrinsics.
- Zero-cost high-level ergonomics such as SDK namespaced dot calls, struct receiver methods, immutable `let` locals, switch expressions, static pipeline syntax, purity/inline contracts, and restricted class syntax, provided they lower to direct calls, branches, local storage, static helper calls, or constants. These features must not require heap allocation, delegates, closure objects, boxing, virtual dispatch, runtime interface tables, or hidden object identity.

### Portable 2D SDK

The portable SDK defines game-facing concepts that can be lowered to multiple machines when the target has enough capability.

SDK work belongs here:

- Frame boundary: wait for frame, begin/end frame if needed.
- Tick-based input: poll, down, just pressed, just released, hold ticks.
- Logical resources: tilesets, maps, sprite sheets, palettes, animations, collision flags.
- Tile/world API: read tile id and tile flags from world data.
- Camera API: set viewport position, apply scroll, stream map columns/rows.
- Logical sprites: variable logical sizes, metasprite lowering, flip, palette slot, animation frame.
- Collision queries: tile flags and AABB checks.
- Optional HUD API with capability-gated modes.

### Target Intrinsics

Intrinsics expose machine-specific operations and registers. They are valid, but they are not portable SDK APIs.

Intrinsic work belongs here:

- Game Boy PPU registers, `SCX`, `SCY`, `JOYP`, OAM, DMA, palette registers, LCD modes.
- NES PPU registers, NMI, OAM DMA, attribute table details, sprite size modes, mapper/banking details.
- Raw tilemap writes, raw sprite/OAM writes, hardware flags, and scanline timing.
- Low-level routines needed by SDK lowering but not promised as portable calls.

## Current API Classification

| API | Layer | Notes |
| --- | --- | --- |
| `WorldMap2D` | Portable SDK resource | Shared tile ids and collision flags in one map object. |
| `WorldMapTile` | Portable SDK resource | Query result that combines one tile id with its flags. |
| `WorldTileFlags` | Portable SDK resource | Defines `Empty`, `Solid`, `Hazard`, and `Platform`. |
| `world_column(...)` | Portable SDK resource/setup candidate | Current source authoring call for one world column of tile ids. |
| `world_flags(...)` | Portable SDK resource/setup candidate | Current source authoring call for one world column of collision flags. |
| `world_map(...)` | Portable SDK resource/setup candidate | Current Game Boy call builds the initial visible tilemap from declared world columns. |
| `GameBoyRomCompiler.CollectSdkOperations(...)` | Compiler architecture boundary | Observes portable `Sdk2DOperation` records before Game Boy lowering. |
| `video_wait_vblank()` | Portable SDK candidate | Rename or alias to frame terminology later. |
| `input_poll()` | Portable SDK | Current tick boundary. |
| `Input.IsDown(...)` | Portable SDK | Good shared input semantics. |
| `Input.WasPressed(...)` | Portable SDK | Good shared input semantics. |
| `Input.WasReleased(...)` | Portable SDK | Good shared input semantics. |
| `Input.HoldTicks(...)` | Portable SDK | Accepted current variable-height jump seam. |
| `button_pressed(...)` | Transitional/intrinsic-like | Direct-read compatibility path. Prefer tick-based API. |
| `sprite_asset(...)` | Portable SDK candidate | Needs target-neutral asset contract and palette slots. |
| `sprite_draw(...)` | Transitional compatibility alias | Game Boy `sprite.Draw(...)` now lowers through a compile-time-operand target intrinsic; the snake_case builtin remains accepted. |
| `Sprite.Width(...)` | Portable SDK | Add `sprite_height(...)`. |
| `sprite_set(...)` | Target intrinsic/transitional | Raw hardware sprite write. |
| `scroll_set(...)` | Target intrinsic/transitional | Raw scroll register concept. Portable API should be camera based. |
| `camera_init(...)` | Portable SDK candidate | Current form configures target camera state and stream band. |
| `camera_set_position(...)` | Portable SDK camera | Position-based camera API; Game Boy supports X and Y within the declared write budget, while NES supports four-screen X/Y movement with horizontal column streaming, vertical row streaming, and staggered diagonal edge updates. |
| `camera_apply()` | Portable SDK candidate | Valid concept, but should apply SDK camera state. |
| `camera_move_right()` | Transitional SDK helper | Replace with `camera_set_position(x, y)`. |
| `camera_move_left()` | Transitional SDK helper | Replace with `camera_set_position(x, y)`. |
| `camera_tile_column_at(...)` | Transitional SDK helper | Move collision and map reads to world coordinates. |
| `camera_span_tile_at(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_tile(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_flags(...)` | Transitional SDK helper | Legacy camera-span collision bridge. |
| `camera_aabb_tiles(...)` | Portable SDK capability-gated query | Camera-relative AABB bridge for fixed-screen actors and projected world-space actors on long maps; requires target support for `CameraRelativeAabb`. |
| `camera_aabb_hit_top(...)` | Portable SDK capability-gated query | Camera-relative AABB tile-hit bridge that returns the top world-pixel Y of the first matching tile, or `255` when none hit; requires target support for `CameraRelativeAabbHitTop`. |
| `map_column(...)` | Transitional/compatibility | Legacy streaming-column authoring; runner uses `world_column(...)` now. |
| `map_tile_at(...)` | Portable SDK candidate | Reads generated world tile-id rows. |
| `map_flags_at(...)` | Portable SDK candidate | Reads generated world flag rows. |
| `map_stream_column(...)` | Target intrinsic/transitional | SDK camera should own streaming. |
| `tilemap_set(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill_column(...)` | Target intrinsic/transitional | Camera streaming implementation detail. |
| `palette_set(...)` | Target intrinsic/transitional | Portable SDK should use logical palette slots. |
| `object_palette_set(...)` | Game Boy intrinsic/transitional | Exposes GB object palette model. |
| `palette_background(...)` | Portable SDK capability-gated declaration | Declares a logical background palette slot with four target color values. |
| `palette_sprite(...)` | Portable SDK capability-gated declaration | Declares a logical sprite palette slot with four target color values. |

## Capability Model

Every portable 2D API must be checked against the target descriptor before it is lowered.

Target descriptors should start with:

```text
Target2DCapabilities:
  name
  screenPixels
  screenTiles
  tileSize
  backgroundBufferTiles
  scrollAxes
  supportsFineScrollX
  supportsFineScrollY
  maxBackgroundTileWritesPerFrame
  maxAttributeWritesPerFrame
  staggerCameraMovementStreams
  spriteCount
  spriteSizeModes
  maxSpritesPerScanline
  spritePaletteSlots
  backgroundPaletteSlots
  supportsSpriteFlipX
  supportsSpriteFlipY
  supportsWindowHud
  supportsSplitScrollHud
  supportsSpriteHud
```

Capability failures should be explicit compiler errors unless the API call requests a documented fallback.

Example:

```text
Target 'nes' does not support Window HUD. Use SplitScroll, SpriteHud, or disable HUD for this target.
```

Capability checks should use the shared targeting helpers so diagnostics stay stable. `TargetCapabilityChecks.RequireHudMode(...)` is the first reusable check and formats unsupported-feature messages through `TargetCapabilityErrorFormatter`.

## Shared SDK Operation Model

Portable 2D calls should be represented as semantic operations before target lowering. The first shared model lives under `RetroSharp.Core.Sdk`:

- `Sdk2DOperation.WaitFrame`
- `Sdk2DOperation.PollInput`
- `Sdk2DOperation.DrawLogicalSprite`
- `Sdk2DOperation.SetCameraPosition`
- `Sdk2DOperation.ApplyCamera`
- `Sdk2DOperation.StreamMapColumn`
- `Sdk2DOperation.StreamMapRow`
- `Sdk2DOperation.ReadWorldTile`
- `Sdk2DOperation.ReadWorldTileFlags`
- `Sdk2DOperation.CameraAabbTiles`
- `Sdk2DOperation.CameraAabbHitTop`
- `Sdk2DOperation.SetHudTile`

`Sdk2DOperationValidator` validates operations against `Target2DCapabilities` before target-specific lowering. The records carry SDK-level concepts only: no Game Boy addresses, NES registers, emitted opcodes, or backend labels. `SetCameraPosition` charges a background-tile-write budget only on targets that stream background tiles at runtime (`CameraMovementStreamsBackground`): for them horizontal movement can require one streamed column and vertical movement one streamed row of the target's backing background buffer. Diagonal movement must either fit the combined write count or use a target-declared staggered camera stream policy (`StaggersCameraMovementStreams`) where each committed edge fits the frame budget. Targets that fine-scroll within a pre-loaded background buffer set `CameraMovementStreamsBackground = false` and are charged no streaming cost for a camera position set. The explicit streaming operations (`StreamMapColumn`, `StreamMapRow`) require the matching `RuntimeBackgroundStreamingAxes` flag, are per-operation budget-checked, and `ValidateFrameBudget(...)` rejects combined explicit background-stream writes, unsupported sprite size modes, total hardware sprites, and constant-Y sprite scanline counts that exceed one frame's target budget.

`GameBoyRomCompiler.CollectSdkOperations(...)` is the first observable operation-creation boundary. It parses the current Game Boy source subset and returns the portable operations detected before `GameBoyRomBuilder` lowers anything to ROM bytes. The boundary recognizes frame/input, camera, HUD tile, world flag reads, logical sprite draw, and map-column streaming operations; raw or transitional calls such as `sprite_set(...)`, `scroll_set(...)`, direction-specific camera helpers, and raw tilemap writes remain on the direct target path until later roadmap tasks move them deliberately.

The collector itself is target-neutral and lives in a dedicated SDK-frontend assembly, `RetroSharp.Sdk.Frontend` (namespace `RetroSharp.Sdk`): `Sdk2DOperationCollector` (with `SdkCallReader` for argument parsing) walks the parsed main block and inlined user functions for any target. It references the parser and `Core` but is **not** part of the language assembly, so SDK call-name knowledge does not live in the language front-end. Both Game Boy and NES run this one collector and then validate the resulting operations through `Sdk2DOperationValidator` against their own `Target2DCapabilities` before lowering, so the portable boundary is no longer Game Boy-only. The same collector also exposes `CollectFrameBudgets(...)`, a control-flow-aware budget pass that treats branches as alternatives and frame waits/input polls as frame boundaries. Targets supply resolved metasprite geometry for `sprite.Draw(...)`, allowing total hardware sprite checks and per-scanline checks when Y is constant; runtime Y positions remain outside static scanline proof.

Each target has a lowerer that maps an `Sdk2DOperation` to its emission: `GameBoySdkOperationLowerer` and `NesSdkOperationLowerer`. A target's runtime compiler routes a source call through `EmitSdkOperation(op)` so the operation drives emission, instead of re-deriving the behavior from the AST. Operations migrated to this model on both targets today: `WaitFrame`, `PollInput`, `SetCameraPosition`, `ApplyCamera`, `DrawLogicalSprite`, horizontal `StreamMapColumn`, `CameraAabbTiles`, and `CameraAabbHitTop`. Operand values are carried by `SdkByteExpression` (`Constant | Variable`); `Variable` carries a typed `SdkStorageLocation` (`Local`, recursive `Field`, or `IndexedElement`) that targets resolve to their runtime local variable maps only at the backend boundary. The IR remains at the "immediate value or storage location" level without gaining general source syntax trees. `DrawLogicalSprite` carries runtime X/Y/frame and optional runtime FlipX operands, while palette slot remains a constant validated against target capabilities and metasprite geometry is resolved by the target lowerer from `SpriteId`. `StreamMapColumn` carries runtime target/source column operands plus constant Y/height. `CameraAabbTiles` carries runtime or constant screen X, runtime world Y, constant or `Sprite.Width(...)` width, height, and collision flags. `CameraAabbHitTop` carries the same AABB shape and returns a byte fact: the top world-pixel Y of the first matching tile, or `255` for no hit. Game Boy and NES runtime lowering now consume `program.SdkOperations` with a cursor for migrated statement calls and value calls such as `CameraAabbTiles` and `CameraAabbHitTop`; Game Boy also consumes `ReadWorldTileFlags`. The builders fail if a source call and the next collected operation disagree, or if collected operations remain after emission.

The first SDK-as-library slice is now in place. Each cartridge target exposes a
declarative `TargetIntrinsicCatalog` instead of a one-off intrinsic switch; Game
Boy and NES currently catalog `wait_frame`, the `wait_vblank` alias, `poll_input`,
`audio_update`, `camera_set_position`, and `camera_apply` (Game Boy additionally
catalogs `world_tile_flags_at`). `RetroSharp.Sdk.Frontend` injects a small
target-selected SDK library before parsing target compilations. That library defines
`video`, `input`, `audio`, and `camera` classes whose `video.WaitVBlank()`,
`input.Poll()`, `audio.Update()`, `camera.SetPosition(x, y)`, and `camera.Apply()`
helpers call `[target(...)] [intrinsic(...)] extern` declarations, and those helpers
emit the same bytes as the previous SDK operation path. The `audio_update` intrinsic
is collected by the separate `SdkAudioOperationCollector` (Game Boy lowers it from the
audio operation stream, NES emits it inline), so the shared `Sdk2DOperation` collectors
consume but ignore it. The `camera_set_position`/`camera_apply` intrinsics route through
the existing `SetCameraPosition`/`ApplyCamera` collection and emission, so their scroll-axis
inference, capability checks, and frame-budget accounting are unchanged; injecting
`class camera` does not shadow non-member camera calls (`camera.Init`, `camera.AabbTiles`,
`camera.AabbHitTop` still lower through the SDK module). `TargetProgramSelector` filters
`[target("gb")]` / `[target("nes")]` function variants before constant folding
or function indexing, so a portable helper can name one target-specific extern
and let the active target select the matching declaration.

The library can also carry **capability-gated, value-returning** members. Game Boy
catalogs a `world_tile_flags_at` intrinsic and exposes `world.TileFlagsAt(x, y)` — a
two-argument query that returns the tile flags as a value, lowering byte-identically
to the existing `Sdk2DOperation.ReadWorldTileFlags` path. NES does not declare the
`WorldTileFlags` collision query, so `SdkLibrarySource` does not inject the `world`
class for NES at all; the helper only appears on targets whose catalog declares the
intrinsic. This proves the pattern extends from void leaf calls to argument-taking,
value-returning queries (parameterized `inline` helpers substitute their arguments
into the operation operands without introducing temporaries, so the bytes match).

SAL-8.2 adds the missing compile-time operand role mechanism to `TargetIntrinsicDescriptor`
without changing parser, AST, ABI, or classic IR. Descriptors can now mark source call slots
as `AssetRef`, `ConstPaletteSlot`, `EnumFlags`, or `WorldId`, and the SDK/frontend resolver
separates those compile-time operands from runtime operands before operation collection. The
minimal proof is a Game Boy `world_tile_flags_for_world` intrinsic whose `WorldId` slot lowers
byte-identically to `world_tile_flags_at(x, y)` for `"default"` while rejecting runtime locals
in that slot.

SAL-8.3 applies that mechanism to Game Boy `sprite.Draw`, and SAL-8.4 applies the same pattern
to NES: the injected SDK library helper calls a target-specific `[intrinsic("sprite_draw")]`
extern, each target descriptor marks the asset id as `AssetRef` and the palette slot as
`ConstPaletteSlot`, and the collector turns the resolved call back into
`Sdk2DOperation.DrawLogicalSprite`. This keeps metasprite resolution, capability validation,
frame-budget validation, and emission byte-identical to the legacy `sprite_draw` builtin on both
targets. Collision migration remains the separate SAL-8.5 decision.

The migration boundary remains deliberate, and the SAL-6 feasibility spike (epic
#139) refined it with evidence rather than assumption. Wrapping the heavy calls in
ordinary parameterized `inline` helpers is **byte-identical** for `camera.SetPosition()`,
`camera.Apply()`, and `sprite.Draw()` (regression tests
`Inline_helper_wrapping_camera_set_position_is_byte_identical` and
`Inline_helper_wrapping_sprite_draw_and_camera_apply_is_byte_identical`). So the
inline/operand mechanics are not the blocker: the collected operation stream is identical
whether a call arrives directly or through an inlined helper, which means the cross-call
streaming/frame-budget state is preserved.

The remaining friction is at the **extern-intrinsic boundary**, not the language:
- `camera.SetPosition()` / `camera.Apply()` carry only `i16`/void operands, so they were a
  clean **GO** and are now migrated (SAL-7): both targets catalog `camera_set_position`
  (arity 2) and `camera_apply` (arity 0), `SdkLibrarySource` injects `class camera` with
  `SetPosition`/`Apply` inline helpers over `[target][intrinsic]` externs, and the
  collector/emitter route them to the existing `SetCameraPosition`/`ApplyCamera` emission
  (Game Boy consumes from the operation stream; NES re-derives from the call, preserving its
  `ScrollAxes.Horizontal` apply). Byte-identical on both targets. Injecting `class camera`
  does not shadow the rest of the `camera` module — `camera.Init`, `camera.AabbTiles`, and
  `camera.AabbHitTop` are not class members, so they still lower through the SDK module.
- `sprite.Draw()` mixes **compile-time** operands (the asset id, the constant palette slot)
  with runtime ones (X/Y/frame/flipX). Game Boy and NES now use the compile-time-operand
  descriptor form, so the public `sprite.Draw(...)` helper can live in the injected SDK library
  while still collecting to the same capability-checked `Sdk2DOperation`. The legacy
  `sprite_draw(...)` spelling remains a compatibility alias during the transition.
- Internal streaming (`StreamMapColumn`/`StreamMapRow`) and camera-relative collision stay
  operations: they are mostly compiler-emitted and carry storage descriptors and capability
  checks, so a source-library form adds surface without removing the operation model.

Net decision: the library pattern now covers frame/input/audio leaf calls, a capability-gated
value query (`world.TileFlagsAt`), the camera position/apply pair, and `sprite.Draw` on Game Boy
and NES.
Streaming/collision operations remain compiler-recognized until their compile-time-operand
intrinsic migrations are proven. Not everything must become a library. The SAL-8 design note
([`docs/CompileTimeOperandIntrinsics.md`](CompileTimeOperandIntrinsics.md)) chooses the narrow
descriptor-role form for those future intrinsics and records the byte-identity goldens that
later SAL-8 slices must preserve.

## Layer Boundary and Golden Rule

`Sdk2DOperation` is an opinionated 2D-game framework model (camera, sprites, tilemap streaming, HUD) used as an intermediate representation on the cartridge path. It is deliberately **isolated** in `RetroSharp.Core.Sdk`. The language and its classic intermediate code (`RetroSharp.Generation.Intermediate`) stay framework-neutral: they must not gain cameras, sprites, scroll, tilemaps, or controller concepts.

Golden rule for anyone extending this area:

1. The language and its classic IR never gain framework concepts. Game-facing concepts live in the SDK layer or in target intrinsics, never in the grammar, AST typing, ABI, or classic IR.
2. `Sdk2DOperation` must not become a dumping ground. Before adding a new operation, ask whether the feature is genuinely a shared portable primitive or whether it should be a target intrinsic plus a library helper. Genre-specific operations are a warning sign.
3. The operand IR (`SdkByteExpression`) stays at the level of "immediate value" or "storage location". Typed storage descriptors such as `Local`, `Field`, and `IndexedElement` are acceptable because they describe storage, not expression evaluation. Do not add `BinaryOp` or other general expression-tree cases: that would pull source syntax into the portable IR.
4. End-state: the 2D SDK should migrate from compiler-recognized operations toward a library written in the language over per-target intrinsics, so the framework becomes optional and replaceable. Until the language has per-target intrinsics and modules, the operation model is the accepted pragmatic bridge.

Where each piece lives:

- Source syntax and AST: `RetroSharp.Parser` (language assembly, framework-neutral).
- Operation collection from source: `RetroSharp.Sdk.Frontend` (SDK-frontend assembly).
- Portable operation records, validator, capabilities, world/Tiled model: `RetroSharp.Core.Sdk` and `RetroSharp.Core.Targeting`.
- Per-target lowering: `GameBoySdkOperationLowerer` / `NesSdkOperationLowerer` and the target runtime compilers.

## Shared World Map Resource

The first portable world resource lives under `RetroSharp.Core.Sdk`:

- `WorldMap2D`
- `WorldMapTile`
- `WorldTileFlags`

`WorldMap2D` stores tile ids and collision flags as separate fields while exposing coordinate queries for tile id, flags, or both. This keeps visual tile data, streaming data, and collision flags ready to share one source without forcing a compact binary layout yet.

Interpreting Tiled collision data into portable `WorldTileFlags` is target-neutral and lives in `RetroSharp.Core.Sdk.Tiled.TiledCollisionFlags`. It reads Tiled JSON/XML objectgroups, `retrosharpCollision`/`retrosharpFlags` custom properties, and collision-layer GIDs without any Game Boy or NES specifics, so collision modeling is shared rather than owned by a target backend. The Game Boy Tiled importer still owns the genuinely target-specific work (Game Boy tile quantization, deduplication, single-tilemap flattening) and calls this shared collision interpretation.

The first Game Boy integration is `world_column(...)`, `world_flags(...)`, and `world_map(width, streamY, height)`. `world_map(...)` builds a `WorldMap2D`, derives the initial visible background rows from it, regenerates the streaming ROM column tables, and generates parallel collision flag tables from the same resource. `map_column(...)` remains as a compatibility path for older tests and samples, but new runner-level world data should use `world_column(...)` and `world_flags(...)`.

## Agent Task Contract

Use the task breakdown below as issue-sized implementation units. Before starting any task, the agent should inspect the current code paths because candidate file names are guidance, not a mandate.

See `AgentExecution.md` for the operational workflow, GitHub issue seeding commands, execution waves, and agent roles.

Every implementation task must include:

- Scope: one task id or a small contiguous group of task ids.
- Layer decision: language, portable SDK, or target intrinsic.
- Candidate files: expected code/docs/tests to inspect or edit.
- Verification: exact build, unit test, ROM build, or sample check.
- Compatibility check: whether `samples/runner/runner.rs` still builds.
- Documentation check: update this roadmap or target docs if public API changes.

General rules for agents:

- Do not add a new portable SDK call without a capability check path.
- Do not expose raw Game Boy or NES hardware bits through portable samples.
- Do not move gameplay behavior into the language layer.
- Keep transitional APIs working until the roadmap explicitly removes them.
- Prefer adding shared SDK concepts before duplicating behavior in a target builder.

## Roadmap Iterations

### Iteration 0: Persist Architecture Roadmap

Purpose: make the architecture decision durable before adding more APIs.

Tasks:

- Add this roadmap document under `docs/`.
- Link it from `README.md` and `docs/GameBoyTarget.md`.
- Record the language, portable SDK, and intrinsic layer boundaries.
- Classify the current API surface.
- State the viability rule for portability.

Acceptance criteria:

- A contributor can identify which layer any current API belongs to.
- The next Game Boy runner feature has a clear architectural home before implementation.

### Iteration 1: Target Capability Descriptors

Purpose: stop portable APIs from assuming Game Boy capabilities implicitly.

Tasks:

- Add an internal `Target2DCapabilities` model.
- Add a Game Boy descriptor.
- Add a NES descriptor for the currently supported static drawing target.
- Add tests for descriptor values that directly affect API checks.
- Add a helper that formats capability errors consistently.
- Update docs with the first GB/NES capability table.

Acceptance criteria:

- GB and NES targets expose their 2D limits through one model.
- A portable SDK lowering path can ask if a target supports an operation before emitting code.
- Capability errors are deterministic and specific.

### Iteration 2: Shared 2D SDK Operation Model

Purpose: define SDK operations once, then lower them per target.

Tasks:

- Add an internal SDK operation model for:
  - `WaitFrame`
  - `PollInput`
  - `DrawLogicalSprite`
  - `SetCameraPosition`
  - `ApplyCamera`
  - `StreamMapColumn`
  - `StreamMapRow`
  - `ReadWorldTile`
  - `ReadWorldTileFlags`
  - `SetHudTile`
- Keep the current Game Boy compiler behavior working while introducing the shared model.
- Add a lowering boundary so Game Boy concepts do not originate in `GameBoyRomBuilder`.
- Add tests that inspect operation creation separately from Game Boy byte emission.
- Make Game Boy runtime lowering consume the collected operation stream instead of rebuilding migrated operations from AST calls.

Acceptance criteria:

- New portable concepts are represented before target-specific emission.
- Existing Game Boy runner ROM generation remains unchanged or intentionally changed with tests.

### Iteration 3: Unified World Map Resource

Purpose: remove duplicate visual and logical map definitions from the runner.

Tasks:

- Define a world map resource shape with tile ids and tile flags.
- Add collision flags such as `Empty`, `Solid`, `Hazard`, and `Platform`.
- Generate the initial visible tilemap from world data.
- Generate streaming data from the same world data.
- Generate collision tables from the same world data.
- Migrate the runner away from separate `draw_starting_scene()` and `define_level_columns()` data.

Acceptance criteria:

- A level edit is made in one source of truth.
- Visual tiles, streamed tiles, and collision flags stay in sync by construction.
- The runner still shows holes, ground, and hazard/failure tiles correctly.

### Iteration 4: Camera2D Horizontal Replacement

Purpose: replace direction-specific camera commands with position-based camera state.

Tasks:

- Add `camera_set_position(x, y)` or equivalent SDK operation.
- Keep `camera_apply()` as the apply boundary.
- Lower X-only movement to the existing Game Boy camera runtime first.
- Mark `camera_move_right()` and `camera_move_left()` as transitional.
- Update the runner to maintain camera/world X in source and call the position API.
- Add tests for tile-boundary crossing and column streaming.

Acceptance criteria:

- The runner no longer depends on direction-specific camera APIs.
- Horizontal scrolling and column streaming still work on Game Boy.
- Camera state is expressed as world position, not as "move left/right" commands.

### Iteration 5: Camera2D Vertical Scroll

Purpose: add vertical scroll as a first-class camera capability.

Status: Game Boy vertical scroll is now exercised by `samples/gameboy-vscroll/vscroll.rs` and an acceptance test that runs the ROM path far enough to observe fresh row streaming in VRAM. Game Boy diagonal movement is exercised by `samples/nes-free-scroll/freescroll.rs` for source-authored columns, `samples/tiled-diagonal/diag.rs` for a 40x40 Tiled `world.Load(...)` map, and `samples/tiled-free-scroll/free-scroll.rs` for the cross-target Tiled diagonal path; the target declares staggered camera stream draining, queues pending column and row edges independently, and commits one visible background-map edge per VBlank inside the 21-tile write budget. The row and column streamers cover the visible 20x18 screen plus fine-scroll exposure, while sharing emitted loops so taller vertical maps do not force unsupported direct control flow across MBC1 program banks. NES now has an emulator-validated four-screen free-scroll path: it emits the iNES four-screen bit, uploads the initial four nametables at startup, tracks X/Y source camera state, writes `$2000`/`$2005` with the 240-row coarse-Y wrap handled, accepts tall Tiled `world.Load(...)` maps on the four-screen vertical path, accepts diagonal Tiled maps that fit the four-screen 64x60 surface, streams horizontal columns for worlds wider than the buffer, and streams vertical rows plus zero-palette attribute refreshes for source-authored or imported worlds taller than the buffer. Mapper-backed scale, banking, and IRQ HUD remain in NF-10; see `docs/NesFreeScrollRoadmap.md`.

Tasks:

- Extend camera state to world X and world Y.
- Add row streaming when the camera crosses tile boundaries vertically. Done for Game Boy and NES source-authored worlds.
- Support column and row streaming when moving diagonally. Game Boy and NES both use capability-declared staggered one-edge-per-VBlank policies for streamed edges.
- Add budget checks for tile writes per frame.
- Add capability checks for targets that support only X, only Y, or XY scroll.
- Add a Game Boy vertical-scroll sample or runner section that exercises Y movement. Done in `samples/gameboy-vscroll/vscroll.rs`.

Acceptance criteria:

- Game Boy supports SDK-level vertical and diagonal camera movement within declared limits, and NES supports four-screen X/Y movement with staggered column/row streaming beyond the initially preloaded 64x60 surface.
- The compiler rejects unsupported scroll modes with clear errors.
- Horizontal behavior from Iteration 4 does not regress.

### Iteration 6: Logical Sprite Asset Contract

Purpose: make sprite behavior portable without exposing OAM flags.

Tasks:

- Define sprite asset metadata:
  - frame width and height
  - origin or pivot
  - hitbox
  - palette slots
  - animation clips
- Add `sprite_height(...)`.
- Replace raw flags in portable samples with named `flipX`, `flipY`, and palette slot values.
- Keep raw OAM flags available only through target intrinsics.
- Add overflow policy support: compile error, hide, or rotate/flicker.
- Add tests for Game Boy metasprite piece placement with logical width and flip.

Acceptance criteria:

- No portable sample passes raw `32` for X flip.
- Variable logical sprite sizes still lower correctly to Game Boy hardware pieces.
- Palette choice is expressed as a logical palette slot.

### Iteration 7: Animation Tables

Purpose: move common frame selection out of hand-coded magic numbers while keeping runtime cost explicit.

Tasks:

- Define animation clips in ROM data.
- Add helper APIs for frame lookup by tick or animation state.
- Support fixed frame duration first.
- Document the cost model for animation lookup.
- Migrate runner idle/run/jump frame selection to animation data where practical.

Acceptance criteria:

- Animation data lives in declared resources, not scattered source constants.
- The runner's visual behavior stays equivalent.
- The generated code cost is predictable and documented.

### Iteration 8: Collision V1

Purpose: make collision world-based and independent from camera internals.

Tasks:

- Add `world_tile_flags_at(worldX, worldY)` while the prototype has one active world map.
- Add `collision_aabb_tiles(x, y, width, height, flags)` while the prototype has one active world map.
- Add `camera_aabb_tiles(screenX, worldY, width, height, flags)` as a camera-relative bridge for long scrolling maps while source locals are still byte-backed.
- Support tile/AABB overlap checks against map flags.
- Keep resolution helpers optional and separate from queries.
- Migrate runner ground, hole, and hazard checks away from camera span APIs.

Acceptance criteria:

- Runner collision uses world coordinates and tile flags.
- Camera API no longer owns collision concepts.
- Tile flags can be changed without rewriting collision code.

### Iteration 9: NES Portable Spike

Purpose: prove that the SDK is not Game Boy-only.

Tasks:

- Implement the minimal shared SDK operations needed for a NES runner spike.
- Support tick-based input on NES.
- Support logical sprite drawing on NES with declared sprite limits.
- Support horizontal camera movement and map streaming on NES if feasible within the descriptor.
- Add capability errors for unsupported features instead of silently degrading.
- Create a small cross-target sample that builds for GB and NES.

Acceptance criteria:

- One sample source can build for GB and NES using the portable SDK subset.
- Unsupported NES features fail through capability checks, not backend surprises.
- The API design survives a second target with different PPU constraints.

### Iteration 10: HUD V1

Purpose: add HUD only where the target can support it without corrupting camera semantics.

Tasks:

- Define HUD modes:
  - `Window`
  - `SplitScroll`
  - `SpriteHud`
  - `None`
- Add capability checks for each mode.
- Implement Game Boy Window HUD first if it fits current runtime constraints.
- Evaluate NES SplitScroll or reserved-band HUD separately.
- Keep HUD sample separate from the core runner until both target paths are clear.

Acceptance criteria:

- HUD is optional and capability-gated.
- Targets without a supported HUD mode fail clearly.
- Camera scroll and HUD behavior do not share hidden state.

### Iteration 11: API Stabilization

Purpose: decide what becomes SDK v1 and what remains transitional.

Status: partially landed. `docs/Portable2DSdkV1.md` is the current SDK v1 reference and `samples/cross-target-camera/camera.rs` is the current portable sample. Camera-relative AABB collision, hit-top landing facts, and logical palette declarations are now capability-gated SDK surfaces for runner-shaped work. The remaining work is to quarantine or replace any future transitional APIs through narrower follow-up issues rather than the closed #106 stabilization backlog.

Tasks:

- Remove or quarantine transitional APIs from portable samples.
- Keep target intrinsics under target-specific namespaces or explicit target docs.
- Document SDK v1 API signatures and capability requirements.
- Add cross-target acceptance tests for the portable sample.
- Update README to point new users at the SDK v1 path.

Acceptance criteria:

- The public docs distinguish language, SDK, and intrinsics consistently.
- GB and NES have a documented portable subset.
- Transitional Game Boy helpers are no longer required by portable samples.

## Agent Implementation Backlog

### Iteration 0 Tasks

#### AR-0.1: Keep the roadmap discoverable

- Status: landed 2026-06-08.
- Layer: documentation.
- Candidate files: `README.md`, `docs/GameBoyTarget.md`, `docs/ArchitectureRoadmap.md`.
- Steps:
  - Ensure the architecture roadmap is linked from the README.
  - Ensure target-specific docs point back to the architecture roadmap.
  - Keep the roadmap status and last-updated date current when large decisions change.
- Verification:
  - `rg -n "ArchitectureRoadmap" README.md docs`
  - Confirm links use relative paths that work from their documents.

#### AR-0.2: Classify new APIs before implementation

- Status: landed 2026-06-08.
- Layer: documentation and API governance.
- Candidate files: `docs/ArchitectureRoadmap.md`, target docs touched by the feature.
- Steps:
  - Add the proposed API to the Current API Classification table before or in the same change as code.
  - Mark it as language, portable SDK, target intrinsic, or transitional.
  - State whether it needs target capability checks.
- Verification:
  - The diff shows a classification entry for each new public call.

### Iteration 1 Tasks: Target Capability Descriptors

#### AR-1.1: Add the capability model types

- Status: landed 2026-06-08.
- Layer: portable SDK infrastructure.
- Candidate files: a shared target/SDK project such as `src/RetroSharp.Targeting/`, or a shared namespace used by `RetroSharp.GameBoy` and `RetroSharp.NES`.
- Steps:
  - Add a `Target2DCapabilities` record or class with the fields listed in the Capability Model section.
  - Add small value types or enums for scroll axes, sprite size modes, and HUD modes.
  - Keep the model independent from Game Boy and NES builders.
  - Avoid storing target-specific register names in the model.
- Verification:
  - Unit tests can construct a descriptor without referencing `RetroSharp.GameBoy` or `RetroSharp.NES` implementation classes.

#### AR-1.2: Add Game Boy capabilities

- Status: landed 2026-06-08.
- Layer: target descriptor.
- Candidate files: `src/RetroSharp.GameBoy/`, new shared targeting project, Game Boy tests.
- Steps:
  - Add a Game Boy descriptor for 160x144 pixels, 20x18 visible tiles, 8x8 tiles, 32x32 background buffer, 40 sprites, and current sprite lowering assumptions.
  - Mark fine scroll support for X and Y according to the hardware, even if the SDK runtime initially uses only X.
  - Record supported HUD modes conservatively.
  - Keep descriptor values testable without reading emitted ROM bytes.
- Verification:
  - Unit tests assert the values used by camera, sprite, palette, and HUD capability checks.

#### AR-1.3: Add NES capabilities

- Status: landed 2026-06-08.
- Layer: target descriptor.
- Candidate files: `src/RetroSharp.NES/`, new shared targeting project, NES tests.
- Steps:
  - Add a NES descriptor for the currently supported static drawing path first.
  - Mark unsupported runtime features honestly until implemented.
  - Include NES sprite count, sprite size modes, palette slot limits, visible tile area, and attribute-write concerns.
  - At the initial descriptor stage, do not pretend the NES target supports the Game Boy runner runtime.
- Verification:
  - Tests prove unsupported runtime operations fail capability checks instead of reaching backend-specific code unexpectedly.

#### AR-1.4: Add capability error formatting

- Status: landed 2026-06-08.
- Layer: portable SDK infrastructure.
- Candidate files: shared targeting project, compiler error helper tests.
- Steps:
  - Add a common helper for messages such as `Target 'nes' does not support Window HUD`.
  - Include target name, requested feature, and suggested alternatives when available.
  - Use the helper in at least one GB or NES capability check.
- Verification:
  - Unit tests assert exact error text for one unsupported feature.

### Iteration 2 Tasks: Shared 2D SDK Operation Model

#### AR-2.1: Add SDK operation records

- Status: landed 2026-06-08.
- Layer: portable SDK infrastructure.
- Candidate files: a shared SDK/lowering namespace, parser or compiler integration tests.
- Steps:
  - Add operation records for `WaitFrame`, `PollInput`, `DrawLogicalSprite`, `SetCameraPosition`, `ApplyCamera`, `StreamMapColumn`, `StreamMapRow`, `ReadWorldTile`, `ReadWorldTileFlags`, and `SetHudTile`.
  - Keep operations semantic; do not include GB addresses, NES registers, or emitted opcodes.
  - Add operation validation against `Target2DCapabilities`.
- Verification:
  - Unit tests construct and validate operations without invoking a ROM builder.

#### AR-2.2: Add an operation creation boundary

Status: landed 2026-06-08.

- Layer: compiler architecture.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyRomCompiler.cs`, `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, new shared operation builder.
- Steps:
  - Identify where source-level calls become Game Boy byte emission.
  - Insert a boundary where portable calls can first become SDK operations.
  - Keep target-intrinsic calls on the direct target path.
  - Do not rewrite all Game Boy emission in one task; start with one low-risk operation such as frame wait or input poll.
- Verification:
  - Existing Game Boy runner ROM still builds.
  - A unit test observes the SDK operation before target lowering.

#### AR-2.3: Lower the first shared operation to Game Boy

Status: landed 2026-06-08.

- Layer: portable SDK to Game Boy lowering.
- Candidate files: Game Boy builder/lowering code and tests.
- Steps:
  - Pick one operation already used by the runner, preferably `WaitFrame` or `PollInput`.
  - Lower it through the new operation path.
  - Keep emitted behavior equivalent to the existing implementation.
  - Leave direct target calls untouched.
- Verification:
  - Game Boy compiler tests still pass.
  - `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out /tmp/runner.gb samples/runner/runner.rs`

### Iteration 3 Tasks: Unified World Map Resource

#### AR-3.1: Define world map data structures

Status: landed 2026-06-08.

- Layer: portable SDK resources.
- Candidate files: new shared resource namespace, `src/RetroSharp.GameBoy/GameBoyRomCompiler.cs`, tests.
- Steps:
  - Define a world map resource containing tile ids and tile flags.
  - Define flag values for `Empty`, `Solid`, `Hazard`, and `Platform`.
  - Keep tile ids and collision flags separate fields, even if compacted later.
  - Add tests for map dimensions, flag lookup, and invalid rows/columns.
- Verification:
  - Unit tests can query tile id and flags from one world map object.

#### AR-3.2: Generate initial visible map from world data

Status: landed 2026-06-08.

- Layer: portable SDK resource to target setup.
- Candidate files: Game Boy compiler static setup, runner sample, tests.
- Steps:
  - Replace hand-authored initial visible tilemap setup with data derived from the world map.
  - Preserve the current visible runner scene.
  - Keep existing `tilemap_set`/`tilemap_fill` available as transitional calls.
- Verification:
  - Tests assert the same key tiles for ground, holes, and hazard positions.
  - Runner ROM builds.

#### AR-3.3: Generate streaming data from world data

Status: landed 2026-06-08.

- Layer: portable SDK camera/resource integration.
- Candidate files: Game Boy map-column generation, `samples/runner/runner.rs`, tests.
- Steps:
  - Replace separate `map_column(...)` source data with generated map columns from the unified world map.
  - Keep source-map column ROM tables equivalent for the current runner.
  - Add a test proving streamed columns and initial visible columns share the same source.
- Verification:
  - Runner still scrolls through the same terrain.
  - Tests fail if visual and streaming maps diverge.

#### AR-3.4: Generate collision flags from world data

Status: landed 2026-06-08.

- Layer: portable SDK collision preparation.
- Candidate files: shared world map resource, Game Boy map lookup/lowering, tests.
- Steps:
  - Add read paths for tile flags alongside tile ids.
  - Keep current collision behavior equivalent: solid ground, holes, and hazard/failure tiles.
  - Do not move collision resolution into the map resource.
- Verification:
  - Tests prove `Hazard` and `Solid` flags can be queried independently.

### Iteration 4 Tasks: Camera2D Horizontal Replacement

#### AR-4.1: Add position-based camera API parsing/lowering

Status: landed 2026-06-08.

- Layer: portable SDK camera.
- Candidate files: parser call handling if needed, Game Boy compiler, SDK operation model, tests.
- Steps:
  - Add `camera_set_position(x, y)` as a portable SDK candidate.
  - Validate arity and byte-backed expression support.
  - Create a `SetCameraPosition` operation.
  - Keep `camera_move_right()` and `camera_move_left()` working as transitional APIs.
- Verification:
  - A minimal source using `camera_set_position` compiles for Game Boy.

#### AR-4.2: Reuse existing Game Boy horizontal camera runtime

Status: landed 2026-06-08.

- Layer: Game Boy lowering.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, tests.
- Steps:
  - Lower X movement by comparing previous and requested camera X.
  - Reuse existing column streaming logic where possible.
  - Document any current limitation, such as moving one pixel or one tile step per frame.
  - Avoid adding vertical behavior in this task.
- Verification:
  - Tests cover moving right, moving left, and no movement.

#### AR-4.3: Migrate runner to camera position state

Status: landed 2026-06-08.

- Layer: sample/API adoption.
- Candidate files: `samples/runner/runner.rs`, Game Boy runner tests.
- Steps:
  - Add a source variable for camera/world X.
  - Update input handling to mutate camera/world X.
  - Replace `camera_move_right()` and `camera_move_left()` calls with `camera_set_position(cameraX, 0)` or the chosen API shape.
  - Keep gameplay behavior equivalent.
- Verification:
  - Runner source no longer contains `camera_move_right()` or `camera_move_left()`.
  - Runner ROM builds.

### Iteration 5 Tasks: Camera2D Vertical Scroll

#### AR-5.1: Extend camera state to Y

Status: landed 2026-06-08.

- Layer: portable SDK camera and Game Boy runtime.
- Candidate files: camera state addresses, capability checks, tests.
- Steps:
  - Add camera Y state separately from scroll register writes.
  - Track tile-boundary crossings in Y.
  - Keep horizontal behavior unchanged.
- Verification:
  - Tests prove X-only movement still uses existing column streaming.

#### AR-5.2: Add row streaming

Status: landed 2026-06-08.

- Layer: Game Boy camera lowering.
- Candidate files: Game Boy builder map streaming code, tests.
- Steps:
  - Add runtime row streaming based on world map data.
  - Handle up and down movement.
  - Check background buffer wrapping.
  - Add capability/budget checks for rows written per frame.
- Verification:
  - Tests cover crossing one row up and one row down.

#### AR-5.3: Add diagonal movement budget checks

Status: landed for strict combined-budget validation 2026-06-08; updated 2026-06-28 for capability-declared staggered stream draining.

- Layer: portable SDK validation.
- Candidate files: capability validation, camera operation validation tests.
- Steps:
  - Compute required column and row writes for one frame.
  - Compare against target tile/attribute write budgets.
  - Accept diagonal streaming when the target declares staggered camera stream draining and each committed edge fits the per-frame budget.
  - Emit a clear capability error if a requested movement mode exceeds budget.
- Verification:
  - Tests include one accepted movement and one rejected movement.

### Iteration 6 Tasks: Logical Sprite Asset Contract

#### AR-6.1: Define portable sprite metadata

Status: landed 2026-06-08.

- Layer: portable SDK resources.
- Candidate files: sprite asset compiler, `GameBoySpriteAssets.cs`, shared resource model, tests.
- Steps:
  - Add metadata for logical width, logical height, origin/pivot, hitbox, palette slots, and animation clips.
  - Preserve current PNG-based Game Boy sprite asset loading.
  - Keep JSON legacy support if still needed, but mark it transitional in docs.
- Verification:
  - Tests prove current runner asset metadata is preserved.

#### AR-6.2: Replace raw flip flags in portable API

Status: landed 2026-06-08.

- Layer: portable SDK sprites.
- Candidate files: `samples/runner/runner.rs`, Game Boy sprite draw lowering, tests.
- Steps:
  - Add named `flipX` and optionally `flipY` values or booleans.
  - Lower `flipX` to the Game Boy OAM X-flip bit internally.
  - Keep raw OAM flags only for target-intrinsic sprite APIs.
- Verification:
  - Runner source no longer passes raw `32` to portable `sprite_draw`.
  - Existing mirrored metasprite placement tests still pass or are updated.

#### AR-6.3: Add logical palette slot selection

Status: landed 2026-06-08.

- Layer: portable SDK sprites and capabilities.
- Candidate files: sprite draw API, palette capability checks, Game Boy/NES descriptors.
- Steps:
  - Add a palette slot parameter or sprite palette binding.
  - Validate the slot against target capabilities.
  - Lower to Game Boy object palette selection where supported.
  - Do not expose raw palette register values through the portable API.
- Verification:
  - Tests cover valid and invalid palette slot selection.

### Iteration 7 Tasks: Animation Tables

#### AR-7.1: Add animation clip resource data

Status: landed 2026-06-08.

- Layer: portable SDK resources.
- Candidate files: shared sprite/animation resource model, tests.
- Steps:
  - Define clips with frame indices and frame durations.
  - Store clip data in ROM-oriented structures.
  - Keep runtime state explicit in source or a small helper.
- Verification:
  - Unit tests query frame for tick values at clip boundaries.

#### AR-7.2: Add frame lookup helper

Status: landed 2026-06-08.

- Layer: portable SDK runtime helper.
- Candidate files: SDK operation model, target lowering tests.
- Steps:
  - Add a helper such as `animation_frame(clip, tick)`.
  - Document whether it loops, clamps, or errors on invalid clips.
  - Lower the helper with predictable cost.
- Verification:
  - Tests cover looping and fixed-duration clips.

#### AR-7.3: Migrate runner animation constants

Status: landed 2026-06-08.

- Layer: sample/API adoption.
- Candidate files: `samples/runner/runner.rs`, runner tests.
- Steps:
  - Replace manual frame cycle constants where the animation helper can express the same behavior.
  - Keep jump/idle/run state explicit in source.
  - Avoid introducing hidden actor state.
- Verification:
  - Runner visual frame sequence remains equivalent.

### Iteration 8 Tasks: Collision V1

#### AR-8.1: Add world tile flag query

Status: landed 2026-06-08.

- Layer: portable SDK collision.
- Candidate files: world map resource, SDK operation model, Game Boy lowering, tests.
- Steps:
  - Add `world_tile_flags_at(worldX, worldY)` while the prototype has one active world map. The SDK operation records `WorldId = "default"` as the future named-map extension point.
  - Convert pixel coordinates to tile coordinates explicitly.
  - Validate map bounds behavior.
- Verification:
  - Tests query flags at ground, hole, and hazard locations.

#### AR-8.2: Add AABB tile collision query

Status: landed 2026-06-08.

- Layer: portable SDK collision.
- Candidate files: collision helper lowering, tests.
- Steps:
  - Add `collision_aabb_tiles(x, y, width, height, flags)` while the prototype has one active world map.
  - Add `camera_aabb_tiles(screenX, worldY, width, height, flags)` for camera-relative actor checks on long maps.
  - Check every tile overlapped by the AABB.
  - Return a simple boolean: `1` when any overlapped tile matches the requested flag mask, otherwise `0`.
  - Keep collision resolution outside this helper.
- Verification:
  - Tests cover one-tile, two-tile-span, empty, solid, and hazard overlaps.

#### AR-8.3: Migrate runner away from camera span collision

Status: landed 2026-06-08.

- Layer: sample/API adoption.
- Candidate files: `samples/runner/runner.rs`, Game Boy runner tests.
- Steps:
  - Replace `camera_span_tile_at(...)` and `camera_span_has_tile(...)` in runner collision.
  - Use world coordinates plus sprite hitbox or logical width.
  - Keep reset/failure behavior equivalent.
- Verification:
  - Runner source no longer uses camera span collision helpers.
  - Runner ROM builds.

### Iteration 9 Tasks: NES Portable Spike

#### AR-9.1: Add NES tick input operation

- Status: landed 2026-06-08.
- Layer: portable SDK to NES lowering.
- Candidate files: `src/RetroSharp.NES/`, SDK operation lowering, NES tests.
- Steps:
  - [x] Implement `PollInput` and button state helpers for NES within declared capabilities.
  - [x] Mirror tick semantics from Game Boy: down, just pressed, just released, hold ticks.
  - [x] Keep NES-specific controller reads inside NES lowering.
- Verification:
  - [x] NES tests cover current, previous, and hold tick state.

#### AR-9.2: Add NES logical sprite spike

- Status: landed 2026-06-08.
- Layer: portable SDK to NES lowering.
- Candidate files: NES sprite lowering, sprite resource tests.
- Steps:
  - [x] Lower one logical sprite asset to NES OAM pieces.
  - [x] Enforce NES sprite count and palette slot limits.
  - [x] Add a clear overflow error before byte emission.
- Verification:
  - [x] A minimal NES source draws a logical sprite or fails with a capability error when limits are exceeded.

#### AR-9.3: Add NES camera/map spike

- Status: landed 2026-06-08.
- Layer: portable SDK to NES lowering.
- Candidate files: NES background/PPU code, shared camera operations, tests.
- Steps:
  - [x] Implement the smallest feasible horizontal camera movement path.
  - [x] Seed the visible nametable from unified world resources.
  - [x] Defer vertical or diagonal scroll with explicit capability errors.
- Verification:
  - [x] A minimal shared horizontal camera path compiles for NES.

#### AR-9.4: Add cross-target sample

- Status: landed 2026-06-08.
- Layer: sample acceptance.
- Candidate files: `samples/`, README/docs, CLI tests.
- Steps:
  - [x] Create a small source sample that avoids target intrinsics.
  - [x] Build it for GB and NES.
  - [x] Document unsupported optional features explicitly.
- Verification:
  - [x] `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out /tmp/ar-9-4-cross.gb samples/cross-target-camera/camera.rs`
  - [x] `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out /tmp/ar-9-4-cross.nes samples/cross-target-camera/camera.rs`

### Iteration 10 Tasks: HUD V1

#### AR-10.1: Add HUD mode model and capability checks

- Status: landed 2026-06-08.
- Layer: portable SDK HUD.
- Candidate files: capability model, SDK operation validation, tests.
- Steps:
  - [x] Add HUD mode values for `Window`, `SplitScroll`, `SpriteHud`, and `None`.
  - [x] Validate requested HUD mode against target capabilities.
  - [x] Keep HUD disabled by default.
- Verification:
  - [x] Tests cover supported, unsupported, and disabled HUD modes.

#### AR-10.2: Implement Game Boy Window HUD prototype

- Status: landed 2026-06-08.
- Layer: Game Boy intrinsic-backed SDK lowering.
- Candidate files: Game Boy PPU/window lowering, HUD sample, tests.
- Steps:
  - [x] Lower HUD tile writes to the Game Boy window layer if the descriptor allows it.
  - [x] Keep HUD state separate from camera scroll state.
  - [x] Document any window-position restrictions.
- Verification:
  - [x] Game Boy HUD sample builds and does not use camera internals.

#### AR-10.3: Evaluate NES HUD path

- Status: landed 2026-06-08.
- Layer: NES capability and design.
- Candidate files: NES descriptor, docs, tests if implemented.
- Steps:
  - [x] Decide between split scroll, reserved band, sprite HUD, or unsupported.
  - [x] Update NES descriptor honestly.
  - [x] Implement only if the path is compatible with current camera constraints.
- Verification:
  - [x] Unsupported path emits a clear error, or supported path has a sample build.

### Iteration 11 Tasks: API Stabilization

#### AR-11.1: Quarantine transitional APIs

- Status: landed 2026-06-08.
- Layer: public API hygiene.
- Candidate files: docs, samples, compiler diagnostics.
- Steps:
  - [x] Move transitional APIs to target-specific docs or mark them explicitly.
  - [x] Remove transitional APIs from portable samples.
  - [x] Keep compatibility only where needed for existing samples or tests.
- Verification:
  - [x] `dotnet test src/RetroSharp.Core.Tests/RetroSharp.Core.Tests.csproj --no-restore --filter SampleApiQuarantineTests`
  - [x] `rg -n "scroll_set|sprite_set|camera_move_right|camera_move_left|camera_span_" samples`
  - [x] Matches are either target-intrinsic samples or intentionally documented.

#### AR-11.2: Write SDK v1 API reference

- Status: landed 2026-06-08.
- Layer: documentation.
- Candidate files: new SDK reference doc, README.
- Steps:
  - [x] Document signatures, capability requirements, target support, and failure modes.
  - [x] Include minimal GB/NES examples.
  - [x] Link from README.
- Verification:
  - [x] New users can find the portable SDK path without reading Game Boy internals.
  - [x] `dotnet test src/RetroSharp.Core.Tests/RetroSharp.Core.Tests.csproj --no-restore --filter SdkV1ReferenceTests`

#### AR-11.3: Add cross-target acceptance checks

- Status: landed 2026-06-08.
- Layer: validation.
- Candidate files: test projects, CLI test fixtures, samples.
- Steps:
  - [x] Add automated checks that build the portable sample for GB and NES.
  - [x] Keep generated ROM paths under temporary or test output directories.
  - [x] Assert unsupported features fail with capability diagnostics.
- Verification:
  - [x] A single test command covers the portable sample builds and one unsupported-feature diagnostic.
  - [x] `dotnet test src/RetroSharp.Cli.Tests/RetroSharp.Cli.Tests.csproj --no-restore --filter CrossTargetCliAcceptanceTests`

### Language V1 Closure

Status: landed 2026-06-09.

Purpose: close the current language work as a coherent zero-cost v1 surface for the Game Boy/NES cartridge targets instead of continuing to add syntax indefinitely.

This iteration treats the implemented source forms as v1 when they satisfy two rules:

- They compile through parser, semantic analysis, and the current cartridge targets.
- They lower to constants, direct branches, direct local storage, direct bit operations, or source-level inline expansion without hidden heap allocation, dispatch tables, closures, iterators, exceptions, or runtime objects.

V1 includes type aliases, top-level and block-local constants, enum constants, plain local structs, fixed-size local arrays of byte-backed values or byte-sized structs, initializer lists for byte-backed value arrays, byte-sized struct arrays, and plain local structs, `sizeof`, `offsetof`, `countof`, casts, compound assignments, statement-only `++`/`--`, `loop`, `do while`, C-style `for`, half-open range `for`, `break`/`continue`, no-fallthrough `switch`, half-open range membership expressions, short-circuit logical value expressions, conditional value expressions, bitwise flag operations, named/default helper arguments, single-return value helpers, and expression-bodied helpers.

Tasks:

- [x] Keep the v1 language surface bounded to features implemented by the front-end and current Game Boy/NES cartridge targets.
- [x] Document remaining gaps as pointer/member access, address-of fields, wider ABI/layout, backend calling conventions, and canonical-type diagnostics.
- [x] Migrate runnable samples to use the v1 style where it improves clarity without changing target behavior.
- [x] Keep diagnostic samples simple enough to isolate target regressions while still using the stable `loop` and mutation syntax.
- [x] Move dot-call SDK namespaces, receiver methods, immutable `let`, switch expressions, pipeline syntax, and explicit `pure`/`inline` contracts out of v1 and into post-v1 candidates. Trait-like constraints remain outside v1 and outside Iteration 12.

Verification:

- [x] Cross-target portable sample builds for Game Boy and NES.
- [x] Game Boy runner sample builds after migration.
- [x] Game Boy drawing, HUD, runner diagnostics, and NES drawing samples build after migration.

### Iteration 12: Post-V1 Zero-Cost High-Level Language Surface

Status: post-v1 candidate.

Purpose: make RetroSharp read more like a compact modern C#/functional language while keeping emitted Game Boy/NES code equivalent to hand-written low-level helpers.

This iteration deliberately stays outside language v1. It also excludes managed object features, runtime polymorphism, closures/delegates, lambda syntax, built-in `Option`/`Result` abstractions, and trait/constraint systems until their ergonomics and runtime model are justified for the current 8-bit target constraints.

Design rules:

- Every accepted form must lower before cartridge target emission to an existing call, inline helper substitution, expression tree, direct branch, direct local storage, or constant.
- Dot syntax has two distinct meanings. `module.Call(...)` is a compile-time SDK/module call when the left side names a known module. `value.Method(...)` is a receiver method only when the left side is a resolved value with a known struct type.
- Receiver methods are static source sugar. They do not introduce object identity, virtual dispatch, method tables, `this` objects, boxing, or hidden heap state.
- Static configuration grouping should stay compile-time only. Current samples may use enum members as readable zero-cost groups such as `World.Width`, but the intended post-v1 surface is a dedicated `module` or `const group` form that lowers exactly like top-level constants.
- Lightweight object-oriented style is acceptable only for real mutable state represented by plain structs plus receiver methods, such as player or enemy state. It must remain opt-in source sugar over direct local storage and inline helper substitution.
- `let` is a local immutable binding. It may fold to a constant when the initializer is compile-time evaluable, otherwise it uses the same local storage shape as an equivalent variable and rejects reassignment.
- `inline` describes the already-supported parameter-substitution lowering. A helper marked `inline` must fail clearly if the current target cannot inline it.
- `pure` is a compile-time contract over the supported subset. It must not emit runtime code by itself.
- Pipeline syntax rewrites to nested static/helper calls. It must not create pipe objects, iterator objects, range objects, delegates, or hidden temporaries beyond those already required by the lowered expression.

Tasks:

#### AR-12.1: Add immutable `let` locals

- Layer: language.
- Candidate files: parser grammar/syntax, semantic analyzer, Game Boy/NES lowering tests, docs.
- Steps:
  - [x] Parse `let name = expr;` as a local immutable declaration.
  - [x] Fold compile-time evaluable initializers when possible.
  - [x] Reject reassignment, compound assignment, and postfix mutation of a `let` binding in semantic analysis or before target lowering with a clear diagnostic.
  - [x] Lower non-constant `let` bindings to the same local storage cost as an equivalent byte-backed variable.
- Verification:
  - [x] Parser tests cover local `let` and reject top-level `let`.
  - [x] Semantic tests cover visible immutable bindings and reassignment errors.
  - [x] Game Boy and NES emitted-code tests prove accepted `let` bindings compile with no helper/runtime object and reject mutation.

#### AR-12.2: Add explicit `inline` and `pure` helper contracts

- Layer: language.
- Candidate files: parser grammar/syntax, helper substitution, semantic analyzer, target compiler tests, docs.
- Steps:
  - [x] Parse `inline` and `pure` modifiers on helper declarations.
  - [x] Preserve existing implicit inline behavior for current cartridge helpers while making explicit `inline` fail clearly when inlining is impossible.
  - [x] Check `pure` helpers against the supported side-effect-free subset before target lowering.
  - [x] Keep both modifiers compile-time only.
- Verification:
  - [x] Parser and semantic tests cover accepted and rejected modifiers.
  - [x] Game Boy/NES emitted-code or lowering tests compare against equivalent current inline helpers and prove no extra helper call, object, hidden temporary, or dispatch artifact is introduced.

#### AR-12.3: Add switch expressions

- Layer: language.
- Candidate files: parser grammar/syntax, switch lowering, semantic analyzer, Game Boy/NES expression lowering tests, docs.
- Steps:
  - [x] Parse `expr switch { Pattern => value, _ => fallback }`.
  - [x] Reuse existing no-fallthrough switch pattern semantics for exact values, multi-values, and half-open ranges.
  - [x] Require a default arm, simple subject, and compatible branch result shapes before target lowering.
  - [x] Lower to the same compare branches as an equivalent conditional chain.
- Verification:
  - [x] Parser and semantic tests cover value cases, default case, ranges, incompatible branch results, and subject restrictions.
  - [x] Game Boy/NES tests prove byte-backed switch expressions lower without dispatch tables or helper calls.

#### AR-12.4: Add SDK namespaced dot-call syntax

- Layer: language-to-SDK call surface.
- Candidate files: parser grammar/syntax, call resolver, SDK operation collector, Game Boy/NES tests, docs.
- Steps:
  - [x] Parse SDK namespaced dot-call syntax such as `video.Init()`, `video.WaitVBlank()`, `input.Poll()`, `camera.SetPosition(x, y)`, and `world.Map(...)`.
  - [x] Treat the left side as a compile-time SDK namespace/module when it names a known SDK module, not as a runtime object instance.
  - [x] Resolve calls statically to existing SDK functions such as `video_init()`, `video_wait_vblank()`, `input_poll()`, and `camera_set_position(...)`.
  - [x] Preserve target capability checks before lowering.
- Verification:
  - [x] Parser and semantic tests distinguish module dot-calls from member access.
  - [x] Game Boy/NES tests prove dot-calls reach the same capability checks and lowering as their existing function-call equivalents.

#### AR-12.5: Add struct receiver methods

- Layer: language.
- Candidate files: parser grammar/syntax, helper resolver, semantic analyzer, target compiler tests, docs.
- Steps:
  - [x] Add receiver methods such as `actor.Move(dx)` when a helper declares `this Actor actor`.
  - [x] Resolve them statically to helper calls such as `Move(actor, dx)` or to an inline equivalent.
  - [x] Support an explicit receiver form in the declaration model, `this Actor actor`, without adding object identity or dynamic dispatch.
  - [x] Keep the feature available only where a matching static helper has a receiver parameter.
- Verification:
  - [x] Parser and semantic tests distinguish receiver calls from SDK module dot-calls.
  - [x] Game Boy/NES tests prove receiver methods lower to static/inline equivalents with no dispatch table.

#### AR-12.6: Add static pipeline syntax

- Layer: language.
- Candidate files: parser grammar/syntax, expression rewriter, helper call lowering tests, docs.
- Steps:
  - [x] Add static pipeline syntax.
  - [x] Lower `value |> Clamp(0, 120) |> SnapToTile()` to ordinary nested static calls or inline helper expansion.
  - [x] Do not create pipe objects, range objects, iterators, or temporaries beyond what the lowered expression requires.
- Verification:
  - [x] Parser and semantic tests cover chained pipelines.
  - [x] Game Boy/NES emitted-code or lowering tests prove generated behavior matches the equivalent nested helper calls with no extra helper call, object, hidden temporary, or dispatch artifact.

Acceptance criteria:

- Each new syntax form has parser tests, semantic tests, and Game Boy/NES emitted-code tests where it reaches cartridge targets.
- Every accepted example lowers to direct branches, direct helper calls, inline substitutions, constants, or existing local storage.
- There is no new runtime allocation, runtime dispatch table, delegate representation, closure object, or hidden iterator/range object.
- SDK namespaced calls and struct receiver methods are represented as distinct concepts in the semantic model even though both use dot-call syntax.
- Docs explicitly state that this iteration adds high-level source ergonomics, not managed objects or polymorphism.
- Trait/constraint systems are not part of this iteration.

### Iteration 13: Static Class Syntax Without Runtime Objects

Status: implemented for restricted class declarations, instance/static methods, and static constants. Dedicated constructor-like initializer syntax remains planned.

Purpose: let authors group state and behavior with familiar class-like syntax while preserving the current 8-bit contract: accepted class code must lower to plain structs plus static or inline helper calls before target emission.

This is source organization, not a managed object model. A class value has fixed layout like a struct. An instance method has a statically known receiver that lowers like a helper with the receiver as the first argument. A constructor-like initializer lowers to an explicit initialization helper. Static members lower to module/static helpers or compile-time constants.

Design rules:

- `class` fields use the same fixed-layout rules as plain structs. No hidden object header, vtable pointer, runtime type id, monitor, or allocator state is inserted.
- Non-virtual instance methods lower to static receiver helpers such as `Move(this Actor actor, dx, dy)` or inline substitutions, using the receiver-method machinery from Iteration 12.
- `static` methods and constants lower like module functions and constants. They do not require an instance or runtime module object.
- Constructor syntax, if accepted, is only shorthand for an explicit `Init`-style helper or zero-fill plus field stores. It must not allocate memory.
- Class variables are value/storage declarations in locals, globals, or explicit pointed storage. There is no implicit `new`, heap allocation, reference identity, copy-on-write, or garbage collection.
- Any feature that would require virtual dispatch, inheritance layout, interface tables, dynamic type tests, RTTI, destructors, exceptions, closures, or heap-backed lifetime is outside this iteration unless a later roadmap adds an explicit opt-in cost model.
- Diagnostics must reject unsupported object-oriented forms by naming the hidden cost they would require, for example "virtual method dispatch requires a runtime method table and is not part of static classes."

Example lowering target:

```c
class Actor {
    u8 x;
    u8 y;

    void Move(i8 dx, i8 dy) {
        x += dx;
        y += dy;
    }
}
```

is equivalent to:

```c
struct Actor {
    u8 x;
    u8 y;
}

inline void Move(this Actor actor, i8 dx, i8 dy) {
    actor.x += dx;
    actor.y += dy;
}
```

Tasks:

#### AR-13.1: Define static class grammar and semantic model

- Layer: language.
- Candidate files: parser grammar/syntax, semantic declaration model, language docs.
- Steps:
  - [x] Parse a restricted `class Name { ... }` declaration with fields, non-virtual instance methods, static methods, and compile-time constants.
  - [x] Represent accepted classes as fixed-layout value types in semantic analysis, not as heap references.
  - [ ] Reject inheritance, `virtual`, `override`, `interface`, `new`, destructors, RTTI, and dynamic casts with explicit diagnostics.
  - [x] Keep class names in the type namespace so `class Actor` can be used anywhere an equivalent `struct Actor` would be valid.
- Verification:
  - [x] Parser and semantic tests cover accepted class declarations and the first rejected managed-object form.

#### AR-13.2: Lower fields and methods to struct plus helpers

- Layer: language lowering.
- Candidate files: semantic lowering, helper resolver, Game Boy/NES target tests.
- Steps:
  - [x] Lower class fields to the same layout as a plain struct with the same fields.
  - [x] Lower non-virtual instance methods to receiver helpers using the same static resolution path as `this Actor actor`.
  - [x] Lower static methods and constants like module helpers and top-level constants.
  - [x] Prove method bodies cannot observe object identity or runtime type.
- Verification:
  - [x] Game Boy/NES emitted-code tests compare class spelling against equivalent struct plus helper spelling.
  - [x] Tests prove there is no vtable, heap allocation, object header, dispatch table, or hidden runtime helper.

#### AR-13.3: Add initializer syntax as explicit zero-cost sugar

- Layer: language.
- Candidate files: parser grammar/syntax, initializer lowering, target tests, docs.
- Steps:
  - [ ] Choose the initializer spelling, for example `Actor player = Actor { x: 10, y: 20 };` or a restricted constructor form.
  - [ ] Lower initializer expressions to zero-fill plus direct field stores or an explicit inline `Init` helper.
  - [ ] Reject allocation-shaped syntax such as `new Actor(...)` unless a later explicit allocation feature exists.
  - [ ] Document copy semantics and storage placement clearly.
- Verification:
  - [ ] Parser, semantic, and target tests prove initializer syntax emits the same code as explicit struct initialization.

Acceptance criteria:

- Class syntax is demonstrably equivalent to the struct plus helper form for accepted samples.
- The language docs define "static class" as source sugar and explicitly exclude managed object semantics.
- Diagnostics make hidden runtime costs visible instead of silently lowering to an expensive fallback.
- The Game Boy runner state objects are migrated to static class syntax without changing ROM behavior.

### Iteration 14: Scalable Platformer Actor Framework Ergonomics

Status: feature-complete for the first scrolling platformer slice; not fully closed. The language/storage prerequisite (byte-sized struct arrays with `arr[i].field` access), actor pool/definition frontend, Game Boy/NES basic behavior `Update`/`Draw`, actor animation/camera-AABB helpers, Tiled object-layer spawn data, runtime camera-window activation, player-contact helper coverage, conservative actor scanline budgeting, and target pool/sprite-count diagnostics landed on branch `feature/actor-framework`. Phase 5.1 through Phase 5.6 are implemented: actor positions use world-space X split as `x` low byte plus `xHi`; draw, tile collision, landing, player contact, and spawn activation are camera-relative, with the one-slot runner draw hiding inactive/off-window sprite slots and collision/contact/spawn activation culling or recycling by camera window; actor pool capability checks use target-resolved metasprite geometry instead of counting each actor as one hardware sprite. Non-blocking follow-ups remain documented in `docs/ActorFrameworkRoadmap.md`: AF-5.7 hoists repeated camera-X projection, AF-5.8 hardens `TouchPlayer` against `screenX + width` byte overflow, AF-5.9 decides one-shot versus reactivation spawn policy, and AF-5.10 reduces the current O(spawns)/frame activation scan cost.

Purpose: make complex platformer characters and enemy behaviors practical without asking game authors to hand-write one large `switch` over every enemy kind. This is a framework/SDK ergonomics goal, not a managed object model. The implementation should preserve the current 8-bit contract: fixed storage, predictable update cost, explicit caps, and no heap allocation or runtime polymorphism.

The intended authoring model is declarative and platformer-oriented. A game should be able to define a small actor pool, declare enemy types with sprites, hitboxes, animation, and standard behaviors, then update/draw the active pool through stable SDK calls. The compiler/framework can lower that surface to arrays, tables, direct helper calls, and grouped update loops.

Candidate source shape:

```c
actor.Pool(enemies, 8);

enemy.Def(Goomba, sprite: goomba, behavior: Walker, speed: 1, hp: 1);
enemy.Def(Bat, sprite: bat, behavior: Flyer, speed: 2, hp: 1);
enemy.Def(Turret, sprite: turret, behavior: Shooter, cooldown: 60);

world.Load("level1.tmj");

loop {
    video.WaitVBlank();
    input.Poll();

    enemies.Update();
    enemies.Draw();
}
```

This is intentionally sugar over a data-oriented implementation, not a promise of `new`, inheritance, virtual methods, interfaces, RTTI, or dynamic dispatch.

Design rules:

- Actor pools have compile-time maximum sizes and explicit storage. The generated representation is fixed arrays or equivalent fixed-layout storage.
- The language prerequisite for hand-authored pools is fixed-size arrays of byte-sized structs with `pool[i].field` access. AR-14 should build on that storage model or an equivalent source/library-level structure-of-arrays lowering, not on genre-specific compiler operations. True `i16`/`u16` pooled fields require a future mixed-width layout pass.
- Actor definitions are data: kind id, sprite/animation ids, hitbox, flags, behavior id, constants such as speed, hp, cooldown, and contact damage.
- Behaviors are statically selected modules such as `Walker`, `Flyer`, `Patrol`, `Shooter`, `Chaser`, `Hazard`, and simple platformer controller variants. They lower to direct helpers or grouped loops, not to function pointers or vtables.
- Runtime actor state is explicit and byte-sized where possible: kind, active flag, x/y, vx/vy, state, timer, facing, animation tick, and health.
- World/Tiled integration should support object-layer spawn data and optional camera-window activation so large levels do not require all enemies to be active at once.
- Collision stays on the SDK collision query layer. The actor framework can provide reusable platformer policies, but it must not hide a full physics engine behind one opaque call.
- Capability checks must account for hardware sprite count, scanline pressure, frame-budget-sensitive streaming, actor pool size, and target-specific unsupported behavior. The current actor draw path uses target-resolved metasprite geometry for pool-level frame and scanline diagnostics, then still runs through the shared frame-budget pass for the lowered `sprite.Draw(...)` operations.
- The same high-level actor definitions should either compile for Game Boy and NES or fail with target capability diagnostics that name the unsupported behavior.

Tasks:

#### AR-14.1: Define fixed actor pool and enemy definition model

- Layer: portable SDK/framework.
- Candidate files: SDK core records, frontend collectors, Game Boy/NES lowering tests, docs.
- Steps:
  - [x] Define the minimum actor pool contract: fixed capacity, active slots, kind/state/timer/position fields, and per-type constant tables.
  - [x] Define enemy/actor declarations for sprite, animation, hitbox, behavior, and constants.
  - [x] Lower a simple `Update`/`Draw` surface to the same shape as hand-authored arrays plus direct helper calls.
  - [x] Reject unbounded pools, dynamic allocation, function-pointer-like behavior values, and uncapped spawn sources.
- Verification:
  - [x] Tests compare a generated actor pool against an equivalent hand-written fixed-array implementation.
  - [x] Docs describe the emitted storage model so authors know the runtime cost.

#### AR-14.2: Add platformer behavior modules

- Layer: portable SDK/framework with target lowering.
- Candidate files: behavior definitions, collision helpers, animation helpers, Game Boy/NES emitted-code tests.
- Steps:
  - [x] Add standard static behaviors for at least ground walker, flying patrol, turret/shooter, passive hazard, and simple chaser.
  - [x] Keep behavior state explicit in actor fields instead of hidden objects.
  - [x] Reuse existing world/camera AABB collision and animation helpers.
  - [x] Allow behavior constants to be data-driven per enemy type.
- Verification:
  - [x] A sample level can contain at least three enemy types with different behavior modules and shared update/draw calls.
  - [x] Tests prove behavior selection does not introduce virtual dispatch, heap allocation, or function-pointer tables.

#### AR-14.3: Spawn actors from Tiled world data

- Layer: portable SDK asset pipeline.
- Candidate files: Tiled logical map importer, world asset model, sample maps, docs.
- Steps:
  - [x] Read a named Tiled object layer or equivalent spawn metadata for actor kind and initial fields.
  - [x] Generate compact spawn data alongside world data.
  - [x] Add camera-window or room-based activation into fixed actor slots.
  - [x] Preserve explicit failure when a room/window can exceed the declared actor pool capacity.
- Verification:
  - [x] A platformer sample can place multiple enemy kinds in Tiled without hard-coding every spawn in source.
  - [x] Game Boy/NES generated ROMs use the same authored map data where target capabilities permit it.

#### AR-14.4: Build a scalable platformer acceptance slice

- Layer: samples and validation.
- Candidate files: `samples/runner/runner.rs`, sample maps/assets, target acceptance tests, MCP/diagnostics docs.
- Steps:
  - [x] Extend the runner or add a focused platformer acceptance sample with several enemy kinds, activation, map collision, player contact, and animation.
  - [x] Keep the source free of a hand-written global enemy-kind switch in `main`.
  - [x] Validate the sample on Game Boy first, then NES where the declared behavior set is supported.
  - [x] Document the low-level equivalent pattern for authors who need to drop down to hand-authored arrays.
- Verification:
  - [x] The sample compiles as a tracked ROM artifact.
  - [x] Validation covers source-level behavior, emitted SDK operations, generated ROM builds, and visible actor-rendering paths.

Acceptance criteria:

- Adding a new basic platformer enemy type requires a definition plus optional behavior constants, not editing a central game-loop switch.
- The generated implementation remains equivalent to fixed arrays, tables, direct branches, and static helpers.
- No accepted actor feature requires heap allocation, vtables, RTTI, inheritance, interface dispatch, delegates, closures, or unbounded collections.
- Platformer use cases covered by the first slice include patrolling ground enemies, flying enemies, stationary shooters, hazards, map collision, player contact, animation, and activation/deactivation.
- The framework exposes target capability errors when a behavior or pool budget cannot fit the selected target.

## First Recommended Implementation Slice

Start with Iterations 1 through 3:

1. Add capability descriptors.
2. Add the shared SDK operation model.
3. Unify map visual/logical data.

This gives the project a durable architecture before adding vertical scrolling, NES runtime support, or HUD. It also reduces the highest current maintenance risk: the runner's visual map and collision map are represented separately.

## Acceptance Sample Strategy

The shared Game Boy/NES runner remains the richest target-acceptance sample for the platformer slice. It now uses a 2-axis dead-zone camera and variable projected screen-position collision over a tall 24x48 Tiled map that expands to a 48x96 tile world. It also declares per-target VGM/VGZ background music through the portable audio calls, while the portable SDK contract is still represented by smaller samples such as `samples/cross-target-camera/camera.rs`. Larger diagonal free scroll is proven separately by `samples/nes-free-scroll/freescroll.rs` and the Tiled `world.Load(...)` path by `samples/tiled-free-scroll/free-scroll.rs` so unsupported camera modes are not hidden behind sample-specific degradation.

The final cross-target sample should prove:

- Frame/tick input.
- Logical animated sprite.
- Horizontal camera movement.
- Vertical camera movement where supported.
- Map streaming from a single world resource.
- Tile flag collision.
- Optional HUD in a separate sample.

If the cross-target sample cannot compile for at least Game Boy and NES with clear capability handling, the SDK is not ready to call portable.

## Risk Register

| Risk | Mitigation |
| --- | --- |
| Game Boy helpers become the SDK by accident. | Classify every new API before implementation. |
| NES support reveals incompatible assumptions late. | Add NES capability descriptor early and spike before SDK stabilization. |
| Scroll XY exceeds per-frame VRAM budgets. | Track tile/attribute write budgets and emit compile-time errors. |
| Logical sprites exceed OAM or scanline limits. | Add sprite overflow policy and capability checks. |
| Palette APIs leak machine-specific slots. | Use logical palette slots in SDK and raw palette registers only in intrinsics. |
| HUD conflicts with scrolling. | Make HUD optional and mode-based. |
| Collision becomes hidden physics. | Keep V1 as tile/AABB queries, not a physics engine. |
| Actor framework becomes hidden dynamic OOP. | Keep actor definitions data-driven and lower to fixed arrays, tables, direct branches, and static helpers. |
| Actor ergonomics hide target budgets. | Require fixed pool capacities, spawn-window limits, sprite/scanline checks, and explicit capability diagnostics. |
