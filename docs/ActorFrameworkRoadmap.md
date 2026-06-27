# Actor Framework Roadmap (scalable platformer actors / enemies)

Status: **in progress on branch `feature/actor-framework`.** Phases 1–4 landed
as a working, byte-reproducible non-scrolling acceptance slice. Phase 5 tracks
the review findings (world-space actors, per-actor collision, runtime activation,
metasprite-aware budgets) that must land before the framework is platformer-ready.

This document is the branch-scoped execution plan for roadmap **Iteration 14:
Scalable Platformer Actor Framework Ergonomics** (`docs/ArchitectureRoadmap.md`).
It breaks AR-14.1..AR-14.4 into concrete, verifiable tasks, records what is
already landed on this branch, and tracks the post-review limitations in Phase 5.

The goal: let a game declare a small, fixed actor pool and several enemy types
(sprite, hitbox, animation, behavior, constants), then update and draw the
active pool through stable calls — **without** a hand-written global
enemy-kind `switch` in `main`, and **without** heap, GC, vtables, RTTI,
inheritance, interfaces, delegates, closures, function pointers, or hidden
object identity. Every accepted form must lower to fixed arrays, constant
tables, direct branches, and static/inline helper calls.

## Non-negotiable guardrails

These come from the architecture golden rule and the Iteration 14 risk register.
Any task that cannot meet them must stop and surface a diagnostic instead of
silently lowering to an expensive fallback.

- The language and its classic IR never gain framework (camera/sprite/actor)
  concepts. Actor concepts live in the SDK/library layer or target intrinsics.
- Do **not** add genre-specific `Sdk2DOperation` cases for actors/behaviors.
  Behaviors are static dispatch (direct kind/behavior branches that call
  helpers), authored as a source/library layer over the
  existing SDK calls (`sprite.Draw`, `camera.AabbTiles`, `camera.AabbHitTop`,
  `animation.Frame`). Prefer intrinsic + library over a compiler operation.
- Pools have a compile-time maximum capacity and explicit fixed-layout storage.
- Reject unbounded pools, dynamic allocation, function-pointer-like behavior
  values, and uncapped spawn sources.
- Every actor/behavior feature is capability-checked against
  `Target2DCapabilities` (sprite count, per-scanline pressure, pool size,
  frame budget) before lowering, and fails with a target diagnostic that names
  the unsupported behavior.
- Keep a documented low-level escape hatch: authors must be able to drop down
  to hand-authored fixed arrays and helpers and get the same result.

## Landed on this branch

- **AF-0 — byte-sized struct arrays with runtime-indexed field access**.
  Fixed-size local arrays of plain structs whose fields are byte-sized (`u8`,
  `i8`, `bool`, or enums), with `arr[i].field` reads/writes for constant and
  runtime indices on Game Boy and NES. Storage is array-of-structs (AoS):
  per-element field slots flattened as
  `arr[0].x, arr[0].y, arr[1].x, ...`, stride = field count; runtime access
  computes `base(arr[0].field) + i * stride`. No bounds check, no heap. Fields
  must be byte-sized; true `i16`/`u16` fields are rejected until mixed-width
  layout exists. Total storage is capped at 255 byte slots. This is the storage
  foundation the pool is built on.
- **AF-1.1 — struct-array initializers and per-element field defaults**. Local
  arrays such as `Actor actors[3] = [{ x: 1, active: 1 }, { y: seed + 1 }];`
  lower to the same zero-fill plus direct field stores as declaring the array
  and assigning `actors[0].x`, `actors[0].active`, and `actors[1].y` by hand.
  Omitted fields and omitted trailing elements remain zero; there is still no
  heap, helper call, bounds check, or hidden array object.
- **AF-1.2 — pool iteration and in-place element update model**. The accepted
  low-level equivalent is a counted `for` over `countof(pool)`, guarded by an
  explicit `active` field, with direct `pool[i].field` reads/writes. A future
  `enemies.Update()`/`Draw()` surface must lower to this grouped-loop shape:
  no per-slot object, closure capture, by-reference actor value, vtable, or
  function-pointer table. The first slice keeps AoS storage and pays the
  visible `i * stride` cost by repeated addition; SoA is reserved for a later
  hot-field optimization if tests or profiling justify it.
- **AF-1.3 — wider pooled fields decision**. The first actor framework slice
  stays byte-sized for pooled fields (`u8`, `i8`, `bool`, and enums). True
  `i16`/`u16` pooled fields need mixed-width stride/offset lowering and are
  rejected for struct arrays until that layout exists. Enemy positions for v1
  therefore use screen/world bytes or explicit split-byte fields.
- **AF-2.1 — fixed actor pool and enemy definition model**. The shared SDK
  frontend now expands `actor.Pool(enemies, 8)` to a fixed `Actor enemies[8]`
  struct array and consumes `enemy.Def(...)` as byte-sized per-kind metadata.
  It generates kind/behavior constants and inline lookup helpers such as
  `enemy.Speed(kind)`/`enemy.Hp(kind)` without adding `Sdk2DOperation` cases.
  `sprite` and behavior metadata must be identifiers, numeric metadata must be
  literal bytes, and pool capacity must be a literal `1..255`.
- **AF-2.2/AF-2.3 basic behavior update/draw**. On Game Boy and NES,
  `enemies.Update()` and `enemies.Draw()` expand to grouped loops over
  `countof(enemies)`, skip inactive slots, dispatch by direct `kind` checks,
  and draw through the existing `sprite.Draw` SDK call. `Update()` now covers
  byte-sized static policies for `Walker` (`x += speed`), `Flyer` (`y +=
  speed`), `Patrol` (move by `facing`, tick `timer`, flip at `cooldown`),
  `Shooter` (tick `timer`, pulse `state` when `cooldown` is reached),
  `Chaser` (move horizontally by the precomputed `facing` direction), and
  `Hazard` (publish `contactDamage` through `state`). The SDK byte-expression
  model now carries runtime indexed fields such as `enemies[i].x` so existing
  SDK operations can read actor pool storage without adding actor-specific
  operations. NES supports the same `Update()` and `Draw()` lowering after the
  `for` loop condition path gained a local long-branch trampoline.

## Task breakdown

Task id format: `AF-<phase>.<n>`, with the originating roadmap task in
parentheses. Each task lists layer, candidate files, steps, and verification.
Candidate file names are guidance; inspect the real code paths first.

### Phase 1 — Language/storage completeness for pools

#### AF-1.1: Struct-array initializers and per-element field defaults (AR-14.1)
- Layer: language + target lowering.
- Candidate files: `SomeParser.cs`, `SemanticAnalyzer.cs`, `ConstantFolder.cs`,
  `GameBoyRomBuilder.cs`, `NesRomBuilder.cs`, target docs, `RetroSharp.Language.md`.
- Steps:
  - [x] Accept an initializer for a struct array (per-element struct initializer
    list), lowering to zero-fill plus direct field stores like the existing
    plain-struct and byte-array initializer paths.
  - [x] Support a compact "all elements default to zero, then spawn fills slots"
    pattern so a pool starts inactive without per-field manual stores.
  - [x] Keep the AoS storage model; no heap, no hidden helper.
- Verification:
  - [x] Byte-exact GB and NES tests compare the initializer against explicit
    per-field stores.
- Depends on: AF-0 (done).

#### AF-1.2: Pool iteration and in-place element update model (AR-14.1/14.2)
- Layer: language + framework.
- Candidate files: inline-helper/parameter substitution paths, `SomeParser.cs`,
  `SemanticAnalyzer.cs`, GB/NES emitted-code tests, `RetroSharp.Language.md`.
- Steps:
  - [x] Decide and document the zero-cost iteration pattern over a pool: a
    counted `for` over slots driving direct `arr[i].field` operations and/or an
    `inline` per-actor helper that takes the pool plus an index (no pointers,
    no by-reference object, no closure capture).
  - [x] Define how a future `enemies.Update()`/`Draw()` lowers: a grouped loop
    over active slots that dispatches to behavior helpers; no per-call object.
  - [x] Record the AoS index-multiply cost (`i * stride` by repeated addition)
    and decide whether hot fields use a structure-of-arrays (SoA) lowering.
- Verification:
  - [x] A hand-written pool update loop compiles to fixed-array stores and
    direct branches with no vtable, heap, or function-pointer table.
- Depends on: AF-0 (done).

#### AF-1.3: Wider pooled fields (contingent) (AR-14.1)
- Layer: language + target lowering.
- Candidate files: struct-array layout in `GameBoyRomBuilder.cs`,
  `NesRomBuilder.cs`, `sizeof`/`offsetof` paths, target docs.
- Steps:
  - [x] Decide whether v1 enemies need 16-bit (`i16`/`Pixel`) pooled fields for
    sub-pixel motion. If yes, extend struct-array stride/offset lowering to mixed
    byte/word fields. If no, document the byte-field-only constraint and keep
    enemies on 8-bit screen coordinates.
- Verification:
  - [x] Either byte-exact tests for word fields in pools, or a rejecting
    diagnostic test plus a documented constraint.
- Depends on: AF-0 (done). Only start if AF-2 needs it.

### Phase 2 — Actor framework surface

#### AF-2.1: Fixed actor pool and enemy definition model (AR-14.1)
- Layer: portable SDK/framework (source/library over AF-0 storage).
- Candidate files: SDK core records / a new actor library module, frontend
  collectors only if strictly needed, GB/NES lowering tests, docs.
- Steps:
  - [x] Define the runtime actor state record: kind, active flag, x, y, vx, vy,
    state, timer, facing, animation tick, health — byte-sized where possible.
  - [x] Define per-type metadata: validated sprite id, hitbox, behavior id,
    speed, hp, cooldown, contact damage.
  - [x] Provide the authoring surface (`actor.Pool(...)` / `enemy.Def(...)` sugar
    or an equivalent source/library pattern) that lowers to AF-0 arrays plus
    constant tables — not to new compiler operations.
  - [x] Reject unbounded pools and dynamic/function-pointer-like metadata with
    explicit diagnostics. Spawn sources are not accepted until AF-3, so there is
    no uncapped spawn path in AF-2.1.
- Verification:
  - [x] Tests compare a generated pool against an equivalent hand-written
    fixed-array implementation.
  - [x] Docs describe the emitted storage model and per-actor cost.
- Depends on: AF-1.1, AF-1.2.

#### AF-2.2: Platformer behavior modules (AR-14.2)
- Layer: portable SDK/framework + target lowering.
- Candidate files: behavior helpers, collision/animation helpers, GB/NES
  emitted-code tests.
- Steps:
  - [x] Add static byte-field behaviors for ground `Walker`, `Flyer`, `Patrol`,
    `Shooter`, `Hazard`, and a simple direction-driven `Chaser` on Game Boy.
  - [x] Implement the first behavior selection as direct kind checks calling
    inline logic; keep behavior state in actor fields.
  - [x] Generalize behavior dispatch for the broader behavior set without
    vtables, heap, delegates, closures, or function-pointer tables.
  - [x] Reuse `camera.AabbTiles`/`camera.AabbHitTop` and animation/sprite SDK.
  - [x] Make Walker speed data-driven per enemy type.
  - [x] Make behavior speed, cooldown, and contact-damage constants data-driven
    per enemy type.
- Verification:
  - [x] A focused source-authored sample contains ≥3 enemy types with different
    behaviors and shared update/draw.
  - [x] Tests compare the Game Boy Walker update/draw lowering against a
    hand-authored fixed-array implementation.
  - [x] Broader Game Boy behavior tests compare the generated behavior dispatch
    against a hand-authored fixed-array implementation with no central source
    `switch`, virtual dispatch, heap allocation, or function-pointer tables.
  - [x] Tests prove actor animation and tile helpers lower through existing
    `animation.Frame`, `sprite.Draw`, `camera.AabbTiles`, and
    `camera.AabbHitTop` SDK calls on Game Boy, with cross-target acceptance for
    NES.
- Depends on: AF-2.1.

#### AF-2.3: Update/Draw over the active pool (AR-14.2)
- Layer: framework + target lowering.
- Candidate files: actor library update/draw, GB/NES tests.
- Steps:
  - [x] Lower Game Boy `Update`/`Draw` to grouped loops over active slots; skip
    inactive slots.
  - [x] Lower NES `Update`/`Draw` with a backend-safe loop-condition trampoline.
  - [x] Cap aggregate hardware sprite usage through the shared frame-budget path
    and honor the active flag in generated loops for every supported target.
- Verification:
  - [x] Game Boy emitted-code tests show grouped loops + direct branches, no
    objects.
  - [x] NES emitted-code tests show grouped update/draw loops + direct helper
    calls, no objects.
- Depends on: AF-2.1, AF-2.2.

### Phase 3 — World/Tiled spawn integration (AR-14.3)

#### AF-3.1: Spawn actors from Tiled object data (AR-14.3)
- Layer: portable SDK asset pipeline.
- Candidate files: `LogicalTiledMapImporter`, world asset model, sample maps, docs.
- Steps:
  - [x] Read a named Tiled object layer (or equivalent spawn metadata) for actor
    kind and initial fields; keep parsing target-neutral (mind the #105 coupling).
  - [x] Generate compact spawn data alongside world data. The first slice lowered
    startup/window activation to fixed slot stores; AF-5.3 replaced that with
    generated spawn tables and runtime camera-window activation.
- Verification:
  - [x] A platformer sample places multiple enemy kinds in Tiled without
    hard-coding every spawn in source.
- Depends on: AF-2.1.

#### AF-3.2: Camera-window / room activation (AR-14.3)
- Layer: framework.
- Candidate files: activation logic, capacity checks, tests.
- Steps:
  - [x] Activate spawns into fixed actor slots by a literal camera window.
    Superseded by AF-5.3: `actor.SpawnLayer`/`actor.SpawnWindow` now generate
    runtime camera-window activation rather than compile-time slot fills.
  - [x] Fail explicitly when a spawn layer/window can exceed declared pool
    capacity, now using the maximum simultaneously activatable authored spawns
    in the camera-relative window rather than total layer count.
- Verification:
  - [x] Test proves overflow of the declared pool is a compile-time/spawn-time
    error, not silent truncation.
- Depends on: AF-2.1, AF-3.1.

### Phase 4 — Capability checks and acceptance (AR-14.4)

#### AF-4.1: Capability checks and target diagnostics (AR-14.4, risk register)
- Layer: portable SDK / targeting.
- Candidate files: `Target2DCapabilities`, `TargetCapabilityChecks`,
  `Sdk2DOperationValidator` (only if a validated op is involved), tests.
- Steps:
  - [x] Check pool size plus aggregate hardware sprite/frame budget against the
    target descriptor.
  - [x] Preserve per-scanline pressure checks for constant-Y sprite draws.
  - [x] Extend actor-specific per-scanline proof beyond constant-Y draws by
    treating `pool[i].y` as a conservative same-scanline worst case.
  - [x] Emit a diagnostic that names the unsupported behavior; the same actor
    definitions compile for GB and NES or fail clearly.
- Verification:
  - [x] Tests cover fitting pools and over-budget pool/sprite-count diagnostics
    on both targets.
- Depends on: AF-2.*, AF-3.*.

#### AF-4.2: Scalable platformer acceptance slice (AR-14.4)
- Layer: samples and validation.
- Candidate files: `samples/runner/runner.rs` or a new platformer sample, maps,
  assets, acceptance tests, tracked ROMs.
- Steps:
  - [x] Add a focused actor-framework sample with ≥3 enemy kinds.
  - [x] Extend that sample with literal-window activation, map collision, and
    animation.
  - [x] Add a focused player-contact helper test.
  - [x] Keep the source free of a hand-written global enemy-kind `switch` in `main`.
  - [x] Validate Game Boy first, then NES where the behavior set is supported.
  - [x] Regenerate the actor-framework sample ROMs via
    `tools/gameboy/generate_sample_roms.py`.
- Verification:
  - [x] Sample compiles to Game Boy and NES ROM artifacts from the manifest.
  - [x] Emitted-code, collector, sample-build, and targeted helper tests cover
    visible actor rendering paths plus collision, activation, player-contact,
    and animation lowering.
- Depends on: AF-2.*, (AF-3.* for spawn).

#### AF-4.3: Documentation and closure (AR-14.4)
- Layer: docs.
- Candidate files: `docs/GameBoyTarget.md`, `docs/NesTarget.md`,
  `docs/Portable2DSdkV1.md`, `samples/README.md`, `samples/manifest.json`,
  `docs/ArchitectureRoadmap.md`.
- Steps:
  - [x] Document the actor API, the emitted storage/cost model, and the
    hand-authored low-level equivalent.
  - [x] Classify the new sample in `samples/manifest.json`.
  - [x] Flip Iteration 14 status in `ArchitectureRoadmap.md` when complete.
- Verification:
  - [x] `git diff --check`; manifest-reading tests pass.
- Depends on: AF-4.2.

### Phase 5 — Review findings: remove v1 limitations

The first slice (Phases 1–4) landed and is byte-reproducible, but a review found
limitations that make the framework unsuitable for a real scrolling platformer.
This phase removes them, in priority order. The same guardrails apply: no heap,
vtables, function pointers, closures, or genre-specific `Sdk2DOperation` cases.

#### AF-5.1: World-space actor positioning and camera-relative draw (priority 1)
- Problem: actor `x`/`y` are screen-space bytes drawn with a raw `sprite.Draw(x, y)`.
  When the camera scrolls, enemies stay glued to the screen instead of to world
  tiles. The acceptance sample hides this with a tiny non-scrolling world.
- Layer: framework (+ language if world X needs more than one byte).
- Candidate files: `ActorFrameworkLowerer.cs` (draw + spawn lowering), camera
  state access, GB/NES emitted-code tests, sample.
- Steps:
  - [x] Store actor positions in world coordinates; if world X exceeds 255, use
    AF-1.3 mixed-width fields or explicit split hi/lo byte fields (no heap).
    The implemented model keeps the existing `x` field as the low byte and adds
    `xHi` as the high byte so existing byte-sized actor code stays source-compatible.
  - [x] Draw actors camera-relative (`screenX = worldX - cameraX`) and cull
    actors outside the visible window instead of drawing at a raw screen byte.
  - [x] Prove the draw loop stays a grouped loop with direct branches.
- Verification:
  - [x] A scrolling sample keeps enemies anchored to world tiles as the camera
    moves; off-screen actors are not drawn.
- Depends on: AF-2.3 (and AF-1.3 if world X > 255).

#### AF-5.2: Per-actor collision X (priority 1)
- Problem: `TouchTiles`/`LandOnTiles` apply a fixed literal `screenX` to every
  actor, ignoring each actor's own `x`, so collision is tested at one column.
- Layer: framework.
- Candidate files: `ActorFrameworkLowerer.cs` (collision lowering), tests, sample.
- Steps:
  - [x] Use each actor's (camera-relative) `x` for the collision AABB instead of
    a fixed column, keeping camera-relative AABB capability gating.
- Verification:
  - [x] Two actors at different X positions collide against different tiles.
- Depends on: AF-5.1.

#### AF-5.3: Runtime camera-window / room activation (priority 2)
- Problem: before this slice, `actor.SpawnWindow` filtered spawns at compile
  time; every spawn was active from frame 0 and there was no runtime activation
  as the camera scrolled, so large levels could not keep distant enemies
  inactive. (Extends AF-3.2 from literal/compile-time windows to true runtime
  activation.)
- Layer: framework + asset pipeline.
- Candidate files: spawn-table generation, activation logic, GB/NES tests, sample.
- Steps:
  - [x] Keep authored spawn tables (kind + world x/y + fields) as ROM data.
    Implemented as generated inline ROM-table helpers plus a fixed `used[]`
    byte array per spawn layer.
  - [x] Activate/recycle fixed slots at runtime by camera window/room; preserve
    the explicit pool-capacity overflow diagnostic.
- Verification:
  - [x] A wide level activates enemies as the camera reaches them and frees slots
    when they leave; the declared pool capacity is never exceeded.
- Depends on: AF-3.1, AF-5.1.

#### AF-5.4: Metasprite-aware capability checks (priority 3)
- Problem: the pool-capacity check assumes one actor equals one hardware sprite,
  ignoring metasprite size, so dense pools of multi-sprite enemies can be
  accepted past the target budget. (Tightens AF-4.1.)
- Layer: portable SDK / targeting.
- Candidate files: capability checks in `ActorFrameworkLowerer.cs`, `Target2DCapabilities`,
  tests.
- Steps:
  - [ ] Account for each enemy def's resolved metasprite hardware-sprite count
    when checking pool budget vs target sprite count and per-scanline limits.
  - [ ] Diagnostic names the offending pool and def.
- Verification:
  - [ ] A pool of multi-sprite metasprites that exceeds the budget fails with a
    clear diagnostic on GB and NES.
- Depends on: AF-4.1.

#### AF-5.5: Minor robustness follow-ups (priority 4)
- Layer: framework / target lowering.
- Steps:
  - [ ] Guard generated constant names (enemy name, `{Name}Speed`, etc.) against
    collisions with user constants/enums, mirroring the existing `Actor`
    struct-name guard.
  - [ ] Prune or document the currently unused generated lookup helpers
    (`enemy_speed`, `enemy_hp`, ...).
  - [ ] Relax the NES `for`-loop trampoline to emit only when a direct branch
    would be out of range, removing the per-loop size overhead on small loops.
- Verification:
  - [ ] Tests cover a colliding generated-name diagnostic and an in-range NES
    `for` loop emitting no trampoline.
- Depends on: AF-2.1.

#### AF-5.6: Reentrant variable-vs-variable expression lowering (priority 1)
- Problem: the AF-5.1 actor draw work generalized variable-vs-variable
  subtraction and comparison in the Game Boy/NES backends using one shared
  expression scratch byte. Nested expressions such as `a - (b - c)` or
  `(a - b) < (c - d)` can recursively reuse that scratch byte while an outer
  operand is still live.
- Layer: target lowering.
- Candidate files: `GameBoyRomBuilder.cs`, `NesRomBuilder.cs`, target byte tests,
  language/target docs.
- Steps:
  - [x] Preserve the left operand across right-side sub-expression lowering with
    the target CPU stack instead of a shared scratch byte.
  - [x] Cover `==`/`!=`, relational comparisons, and `-` on Game Boy and NES.
  - [x] Document variable-vs-variable subtraction and comparison as general
    byte-backed language support, not actor-only lowering.
- Verification:
  - [x] Game Boy and NES tests cover nested subtraction, equality/inequality,
    and relational compare emission without scratch reentrancy.
- Depends on: AF-5.1.

## Known limitations after the first slice

These are the open gaps captured as Phase 5. Until they land, treat the actor
framework as an acceptance slice, not a shipping platformer SDK:

- Capability checks count one actor as one hardware sprite (AF-5.4).

## Recommended first slice

Land a minimal, Game-Boy-only vertical slice before breadth:

1. AF-1.1 (struct-array initializers), AF-1.2 (iteration/update model), and
   the AF-1.3 byte-sized-field decision. Done on this branch.
2. AF-2.1 (pool + enemy definition) with AF-2.2/AF-2.3 Game Boy update/draw for
   the basic byte-field behavior set on Game Boy and NES. Done on this branch
   for source-authored active slots.
3. AF-4.2 minimal acceptance (multiple enemy kinds spawned from source data, map
   collision, player contact) on Game Boy.

Next, connect the behavior set to collision/animation and an acceptance sample,
then expand to Tiled spawn (AF-3.*), NES parity, and capability breadth
(AF-4.1). Defer mixed-width pooled fields unless a slice needs true 16-bit actor
state, and defer AF-3.* until the #105 Tiled/world coupling is acceptable for
spawn data.

## Validation commands

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```
