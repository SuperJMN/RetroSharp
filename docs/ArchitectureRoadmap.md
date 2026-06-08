# RetroSharp Architecture Roadmap

Status: proposed architecture roadmap.
Last updated: 2026-06-08.

This roadmap defines how RetroSharp should grow from the current Game Boy runner proving ground into a portable 2D SDK without letting one machine's details become the language or public SDK by accident.

The project is viable if portability means "shared contract with explicit target capabilities, compile-time limits, and predictable lowering." It is not viable if the SDK promises identical graphics behavior, cost, and limits across every 8-bit machine.

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
- `struct`, `enum`, fixed-size arrays, constants, casts, and operators.
- Functions, parameters, returns, calling convention, and attributes.
- Memory placement attributes such as `[section]`, `[bank]`, `[zeropage]`, or `[align]`.
- Target attributes such as `[target("gb")]` or `[intrinsic]`.
- Namespaces or modules if needed to separate SDK and intrinsics.

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
| `button_down(...)` | Portable SDK | Good shared input semantics. |
| `button_just_pressed(...)` | Portable SDK | Good shared input semantics. |
| `button_just_released(...)` | Portable SDK | Good shared input semantics. |
| `button_hold_ticks(...)` | Portable SDK | Accepted current variable-height jump seam. |
| `button_pressed(...)` | Transitional/intrinsic-like | Direct-read compatibility path. Prefer tick-based API. |
| `sprite_asset(...)` | Portable SDK candidate | Needs target-neutral asset contract and palette slots. |
| `sprite_draw(...)` | Portable SDK candidate | Replace raw flags with named `flipX` and palette slot. |
| `sprite_width(...)` | Portable SDK | Add `sprite_height(...)`. |
| `sprite_set(...)` | Target intrinsic/transitional | Raw hardware sprite write. |
| `scroll_set(...)` | Target intrinsic/transitional | Raw scroll register concept. Portable API should be camera based. |
| `camera_init(...)` | Portable SDK candidate | Current form is Game Boy horizontal camera. Needs `camera_set_position`. |
| `camera_apply()` | Portable SDK candidate | Valid concept, but should apply SDK camera state. |
| `camera_move_right()` | Transitional SDK helper | Replace with `camera_set_position(x, y)`. |
| `camera_move_left()` | Transitional SDK helper | Replace with `camera_set_position(x, y)`. |
| `camera_tile_column_at(...)` | Transitional SDK helper | Move collision and map reads to world coordinates. |
| `camera_span_tile_at(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_tile(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_flags(...)` | Transitional SDK helper | Bridges current camera-span collision to generated world flags until Collision V1. |
| `map_column(...)` | Transitional/compatibility | Legacy streaming-column authoring; runner uses `world_column(...)` now. |
| `map_tile_at(...)` | Portable SDK candidate | Reads generated world tile-id rows. |
| `map_flags_at(...)` | Portable SDK candidate | Reads generated world flag rows. |
| `map_stream_column(...)` | Target intrinsic/transitional | SDK camera should own streaming. |
| `tilemap_set(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill_column(...)` | Target intrinsic/transitional | Camera streaming implementation detail. |
| `palette_set(...)` | Target intrinsic/transitional | Portable SDK should use logical palette slots. |
| `object_palette_set(...)` | Game Boy intrinsic/transitional | Exposes GB object palette model. |

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
- `Sdk2DOperation.SetHudTile`

`Sdk2DOperationValidator` validates operations against `Target2DCapabilities` before target-specific lowering. The records carry SDK-level concepts only: no Game Boy addresses, NES registers, emitted opcodes, or backend labels.

`GameBoyRomCompiler.CollectSdkOperations(...)` is the first observable operation-creation boundary. It parses the current Game Boy source subset and returns the portable operations detected before `GameBoyRomBuilder` lowers anything to ROM bytes. The initial boundary recognizes `video_wait_vblank()` as `WaitFrame` and `input_poll()` as `PollInput`; raw or transitional calls such as `sprite_set(...)`, `scroll_set(...)`, camera helpers, and tilemap writes remain on the direct Game Boy path until later roadmap tasks move them deliberately.

`GameBoySdkOperationLowerer` lowers the first shared operation to Game Boy bytes: `Sdk2DOperation.WaitFrame` emits the existing VBlank edge wait routine used by `video_wait_vblank()`. `PollInput` remains on the direct Game Boy path until its stateful input lowering can be moved without broadening this slice.

## Shared World Map Resource

The first portable world resource lives under `RetroSharp.Core.Sdk`:

- `WorldMap2D`
- `WorldMapTile`
- `WorldTileFlags`

`WorldMap2D` stores tile ids and collision flags as separate fields while exposing coordinate queries for tile id, flags, or both. This keeps visual tile data, streaming data, and collision flags ready to share one source without forcing a compact binary layout yet.

The first Game Boy integration is `world_column(...)`, `world_flags(...)`, and `world_map(width, streamY, height)`. `world_map(...)` builds a `WorldMap2D`, derives the initial visible background rows from it, regenerates the streaming ROM column tables, and generates parallel collision flag tables from the same resource. `map_column(...)` remains as a compatibility path for older tests and samples, but new runner-level world data should use `world_column(...)` and `world_flags(...)`.

## Agent Task Contract

Use the task breakdown below as issue-sized implementation units. Before starting any task, the agent should inspect the current code paths because candidate file names are guidance, not a mandate.

See `AgentExecution.md` for the operational workflow, GitHub issue seeding commands, execution waves, and agent roles.

Every implementation task must include:

- Scope: one task id or a small contiguous group of task ids.
- Layer decision: language, portable SDK, or target intrinsic.
- Candidate files: expected code/docs/tests to inspect or edit.
- Verification: exact build, unit test, ROM build, or sample check.
- Compatibility check: whether `samples/gameboy-runner/runner.rs` still builds.
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

Tasks:

- Extend camera state to world X and world Y.
- Add row streaming when the camera crosses tile boundaries vertically.
- Support column and row streaming in the same frame when moving diagonally.
- Add budget checks for tile writes per frame.
- Add capability checks for targets that support only X, only Y, or XY scroll.
- Add a Game Boy vertical-scroll sample or runner section that exercises Y movement.

Acceptance criteria:

- Game Boy supports SDK-level vertical camera movement within declared limits.
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

- Add `world_tile_flags_at(level, worldX, worldY)`.
- Add `collision_aabb_tiles(level, x, y, width, height, flags)`.
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

- Layer: target descriptor.
- Candidate files: `src/RetroSharp.NES/`, new shared targeting project, NES tests.
- Steps:
  - Add a NES descriptor for the currently supported static drawing path first.
  - Mark unsupported runtime features honestly until implemented.
  - Include NES sprite count, sprite size modes, palette slot limits, visible tile area, and attribute-write concerns.
  - Do not pretend the current NES target supports the Game Boy runner runtime.
- Verification:
  - Tests prove unsupported runtime operations fail capability checks instead of reaching backend-specific code unexpectedly.

#### AR-1.4: Add capability error formatting

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
  - `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out /tmp/runner.gb samples/gameboy-runner/runner.rs`

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
- Candidate files: Game Boy map-column generation, `samples/gameboy-runner/runner.rs`, tests.
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

- Layer: sample/API adoption.
- Candidate files: `samples/gameboy-runner/runner.rs`, Game Boy runner tests.
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

- Layer: portable SDK camera and Game Boy runtime.
- Candidate files: camera state addresses, capability checks, tests.
- Steps:
  - Add camera Y state separately from scroll register writes.
  - Track tile-boundary crossings in Y.
  - Keep horizontal behavior unchanged.
- Verification:
  - Tests prove X-only movement still uses existing column streaming.

#### AR-5.2: Add row streaming

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

- Layer: portable SDK validation.
- Candidate files: capability validation, camera operation validation tests.
- Steps:
  - Compute required column and row writes for one frame.
  - Compare against target tile/attribute write budgets.
  - Emit a clear capability error if a requested movement mode exceeds budget.
- Verification:
  - Tests include one accepted movement and one rejected movement.

### Iteration 6 Tasks: Logical Sprite Asset Contract

#### AR-6.1: Define portable sprite metadata

- Layer: portable SDK resources.
- Candidate files: sprite asset compiler, `GameBoySpriteAssets.cs`, shared resource model, tests.
- Steps:
  - Add metadata for logical width, logical height, origin/pivot, hitbox, palette slots, and animation clips.
  - Preserve current PNG-based Game Boy sprite asset loading.
  - Keep JSON legacy support if still needed, but mark it transitional in docs.
- Verification:
  - Tests prove current runner asset metadata is preserved.

#### AR-6.2: Replace raw flip flags in portable API

- Layer: portable SDK sprites.
- Candidate files: `samples/gameboy-runner/runner.rs`, Game Boy sprite draw lowering, tests.
- Steps:
  - Add named `flipX` and optionally `flipY` values or booleans.
  - Lower `flipX` to the Game Boy OAM X-flip bit internally.
  - Keep raw OAM flags only for target-intrinsic sprite APIs.
- Verification:
  - Runner source no longer passes raw `32` to portable `sprite_draw`.
  - Existing mirrored metasprite placement tests still pass or are updated.

#### AR-6.3: Add logical palette slot selection

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

- Layer: portable SDK resources.
- Candidate files: shared sprite/animation resource model, tests.
- Steps:
  - Define clips with frame indices and frame durations.
  - Store clip data in ROM-oriented structures.
  - Keep runtime state explicit in source or a small helper.
- Verification:
  - Unit tests query frame for tick values at clip boundaries.

#### AR-7.2: Add frame lookup helper

- Layer: portable SDK runtime helper.
- Candidate files: SDK operation model, target lowering tests.
- Steps:
  - Add a helper such as `animation_frame(clip, tick)`.
  - Document whether it loops, clamps, or errors on invalid clips.
  - Lower the helper with predictable cost.
- Verification:
  - Tests cover looping and fixed-duration clips.

#### AR-7.3: Migrate runner animation constants

- Layer: sample/API adoption.
- Candidate files: `samples/gameboy-runner/runner.rs`, runner tests.
- Steps:
  - Replace manual frame cycle constants where the animation helper can express the same behavior.
  - Keep jump/idle/run state explicit in source.
  - Avoid introducing hidden actor state.
- Verification:
  - Runner visual frame sequence remains equivalent.

### Iteration 8 Tasks: Collision V1

#### AR-8.1: Add world tile flag query

- Layer: portable SDK collision.
- Candidate files: world map resource, SDK operation model, Game Boy lowering, tests.
- Steps:
  - Add `world_tile_flags_at(level, worldX, worldY)`.
  - Convert pixel coordinates to tile coordinates explicitly.
  - Validate map bounds behavior.
- Verification:
  - Tests query flags at ground, hole, and hazard locations.

#### AR-8.2: Add AABB tile collision query

- Layer: portable SDK collision.
- Candidate files: collision helper lowering, tests.
- Steps:
  - Add `collision_aabb_tiles(level, x, y, width, height, flags)`.
  - Check every tile overlapped by the AABB.
  - Return a simple boolean or first matching flag, matching the chosen API.
  - Keep collision resolution outside this helper.
- Verification:
  - Tests cover one-tile, two-tile-span, empty, solid, and hazard overlaps.

#### AR-8.3: Migrate runner away from camera span collision

- Layer: sample/API adoption.
- Candidate files: `samples/gameboy-runner/runner.rs`, Game Boy runner tests.
- Steps:
  - Replace `camera_span_tile_at(...)` and `camera_span_has_tile(...)` in runner collision.
  - Use world coordinates plus sprite hitbox or logical width.
  - Keep reset/failure behavior equivalent.
- Verification:
  - Runner source no longer uses camera span collision helpers.
  - Runner ROM builds.

### Iteration 9 Tasks: NES Portable Spike

#### AR-9.1: Add NES tick input operation

- Layer: portable SDK to NES lowering.
- Candidate files: `src/RetroSharp.NES/`, SDK operation lowering, NES tests.
- Steps:
  - Implement `PollInput` and button state helpers for NES within declared capabilities.
  - Mirror tick semantics from Game Boy: down, just pressed, just released, hold ticks.
  - Keep NES-specific controller reads inside NES lowering.
- Verification:
  - NES tests cover current, previous, and hold tick state.

#### AR-9.2: Add NES logical sprite spike

- Layer: portable SDK to NES lowering.
- Candidate files: NES sprite lowering, sprite resource tests.
- Steps:
  - Lower one logical sprite asset to NES OAM pieces.
  - Enforce NES sprite count and palette slot limits.
  - Add a clear overflow error before byte emission.
- Verification:
  - A minimal NES source draws a logical sprite or fails with a capability error when limits are exceeded.

#### AR-9.3: Add NES camera/map spike

- Layer: portable SDK to NES lowering.
- Candidate files: NES background/PPU code, shared camera operations, tests.
- Steps:
  - Implement the smallest feasible horizontal camera movement path.
  - Stream map data from unified world resources.
  - Defer vertical or diagonal scroll if capability/budget checks say it is not ready.
- Verification:
  - A cross-target sample can compile the shared horizontal camera path for GB and NES.

#### AR-9.4: Add cross-target sample

- Layer: sample acceptance.
- Candidate files: `samples/`, README/docs, CLI tests.
- Steps:
  - Create a small source sample that avoids target intrinsics.
  - Build it for GB and NES.
  - Document unsupported optional features explicitly.
- Verification:
  - `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out /tmp/cross.gb <sample>`
  - `dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out /tmp/cross.nes <sample>`

### Iteration 10 Tasks: HUD V1

#### AR-10.1: Add HUD mode model and capability checks

- Layer: portable SDK HUD.
- Candidate files: capability model, SDK operation validation, tests.
- Steps:
  - Add HUD mode values for `Window`, `SplitScroll`, `SpriteHud`, and `None`.
  - Validate requested HUD mode against target capabilities.
  - Keep HUD disabled by default.
- Verification:
  - Tests cover supported, unsupported, and disabled HUD modes.

#### AR-10.2: Implement Game Boy Window HUD prototype

- Layer: Game Boy intrinsic-backed SDK lowering.
- Candidate files: Game Boy PPU/window lowering, HUD sample, tests.
- Steps:
  - Lower HUD tile writes to the Game Boy window layer if the descriptor allows it.
  - Keep HUD state separate from camera scroll state.
  - Document any window-position restrictions.
- Verification:
  - Game Boy HUD sample builds and does not use camera internals.

#### AR-10.3: Evaluate NES HUD path

- Layer: NES capability and design.
- Candidate files: NES descriptor, docs, tests if implemented.
- Steps:
  - Decide between split scroll, reserved band, sprite HUD, or unsupported.
  - Update NES descriptor honestly.
  - Implement only if the path is compatible with current camera constraints.
- Verification:
  - Unsupported path emits a clear error, or supported path has a sample build.

### Iteration 11 Tasks: API Stabilization

#### AR-11.1: Quarantine transitional APIs

- Layer: public API hygiene.
- Candidate files: docs, samples, compiler diagnostics.
- Steps:
  - Move transitional APIs to target-specific docs or mark them explicitly.
  - Remove transitional APIs from portable samples.
  - Keep compatibility only where needed for existing samples or tests.
- Verification:
  - `rg -n "scroll_set|sprite_set|camera_move_right|camera_move_left|camera_span_" samples`
  - Matches are either target-intrinsic samples or intentionally documented.

#### AR-11.2: Write SDK v1 API reference

- Layer: documentation.
- Candidate files: new SDK reference doc, README.
- Steps:
  - Document signatures, capability requirements, target support, and failure modes.
  - Include minimal GB/NES examples.
  - Link from README.
- Verification:
  - New users can find the portable SDK path without reading Game Boy internals.

#### AR-11.3: Add cross-target acceptance checks

- Layer: validation.
- Candidate files: test projects, CLI test fixtures, samples.
- Steps:
  - Add automated checks that build the portable sample for GB and NES.
  - Keep generated ROM paths under temporary or test output directories.
  - Assert unsupported features fail with capability diagnostics.
- Verification:
  - A single test command covers the portable sample builds and one unsupported-feature diagnostic.

## First Recommended Implementation Slice

Start with Iterations 1 through 3:

1. Add capability descriptors.
2. Add the shared SDK operation model.
3. Unify map visual/logical data.

This gives the project a durable architecture before adding vertical scrolling, NES runtime support, or HUD. It also reduces the highest current maintenance risk: the runner's visual map and collision map are represented separately.

## Acceptance Sample Strategy

The Game Boy runner remains the primary acceptance sample until the portable contract has a NES counterpart.

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
