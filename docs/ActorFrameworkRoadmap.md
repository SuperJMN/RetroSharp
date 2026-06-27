# Actor Framework Roadmap (scalable platformer actors / enemies)

Status: **in progress on branch `feature/actor-framework`.**

This document is the branch-scoped execution plan for roadmap **Iteration 14:
Scalable Platformer Actor Framework Ergonomics** (`docs/ArchitectureRoadmap.md`).
It breaks AR-14.1..AR-14.4 into concrete, verifiable tasks and records what is
already landed on this branch.

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
  Behaviors are static dispatch (a generated `switch` over a kind/behavior id
  that calls direct helpers), authored as a source/library layer over the
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

- **AF-0 — byte-backed struct arrays with runtime-indexed field access** (commit
  `feat(lang): byte-backed struct arrays with runtime-indexed field access`).
  Fixed-size local arrays of plain structs whose fields are byte-backed, with
  `arr[i].field` reads/writes for constant and runtime indices on Game Boy and
  NES. Storage is array-of-structs (AoS): per-element field slots flattened as
  `arr[0].x, arr[0].y, arr[1].x, ...`, stride = field count; runtime access
  computes `base(arr[0].field) + i * stride`. No bounds check, no heap. Fields
  must be byte-backed; struct-array initializers are not supported yet; total
  storage is capped at 255 byte slots. This is the storage foundation the pool
  is built on.

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
  - [ ] Accept an initializer for a struct array (per-element struct initializer
    list), lowering to zero-fill plus direct field stores like the existing
    plain-struct and byte-array initializer paths.
  - [ ] Support a compact "all elements default to zero, then spawn fills slots"
    pattern so a pool starts inactive without per-field manual stores.
  - [ ] Keep the AoS storage model; no heap, no hidden helper.
- Verification:
  - [ ] Byte-exact GB and NES tests compare the initializer against explicit
    per-field stores.
- Depends on: AF-0 (done).

#### AF-1.2: Pool iteration and in-place element update model (AR-14.1/14.2)
- Layer: language + framework.
- Candidate files: inline-helper/parameter substitution paths, `SomeParser.cs`,
  `SemanticAnalyzer.cs`, GB/NES emitted-code tests, `RetroSharp.Language.md`.
- Steps:
  - [ ] Decide and document the zero-cost iteration pattern over a pool: a
    counted `for` over slots driving direct `arr[i].field` operations and/or an
    `inline` per-actor helper that takes the pool plus an index (no pointers,
    no by-reference object, no closure capture).
  - [ ] Define how a future `enemies.Update()`/`Draw()` lowers: a grouped loop
    over active slots that dispatches to behavior helpers; no per-call object.
  - [ ] Record the AoS index-multiply cost (`i * stride` by repeated addition)
    and decide whether hot fields use a structure-of-arrays (SoA) lowering.
- Verification:
  - [ ] A hand-written pool update loop compiles to fixed-array stores and
    direct branches with no vtable, heap, or function-pointer table.
- Depends on: AF-0 (done).

#### AF-1.3: Wider pooled fields (contingent) (AR-14.1)
- Layer: language + target lowering.
- Candidate files: struct-array layout in `GameBoyRomBuilder.cs`,
  `NesRomBuilder.cs`, `sizeof`/`offsetof` paths, target docs.
- Steps:
  - [ ] Decide whether v1 enemies need 16-bit (`i16`/`Pixel`) pooled fields for
    sub-pixel motion. If yes, extend struct-array stride/offset lowering to mixed
    byte/word fields. If no, document the byte-field-only constraint and keep
    enemies on 8-bit screen coordinates.
- Verification:
  - [ ] Either byte-exact tests for word fields in pools, or a rejecting
    diagnostic test plus a documented constraint.
- Depends on: AF-0 (done). Only start if AF-2 needs it.

### Phase 2 — Actor framework surface

#### AF-2.1: Fixed actor pool and enemy definition model (AR-14.1)
- Layer: portable SDK/framework (source/library over AF-0 storage).
- Candidate files: SDK core records / a new actor library module, frontend
  collectors only if strictly needed, GB/NES lowering tests, docs.
- Steps:
  - [ ] Define the runtime actor state record: kind, active flag, x, y, vx, vy,
    state, timer, facing, animation tick, health — byte-sized where possible.
  - [ ] Define per-type constant tables: sprite id, hitbox, behavior id, speed,
    hp, cooldown, contact damage.
  - [ ] Provide the authoring surface (`actor.Pool(...)` / `enemy.Def(...)` sugar
    or an equivalent source/library pattern) that lowers to AF-0 arrays plus
    constant tables — not to new compiler operations.
  - [ ] Reject unbounded pools, dynamic allocation, function-pointer behavior
    values, and uncapped spawn sources with explicit diagnostics.
- Verification:
  - [ ] Tests compare a generated pool against an equivalent hand-written
    fixed-array implementation.
  - [ ] Docs describe the emitted storage model and per-actor cost.
- Depends on: AF-1.1, AF-1.2.

#### AF-2.2: Platformer behavior modules (AR-14.2)
- Layer: portable SDK/framework + target lowering.
- Candidate files: behavior helpers, collision/animation helpers, GB/NES
  emitted-code tests.
- Steps:
  - [ ] Add static behaviors for at least ground Walker, flying Patrol/Flyer,
    Turret/Shooter, passive Hazard, and a simple Chaser.
  - [ ] Implement behavior selection as a generated `switch` over behavior/kind
    id calling direct helpers; keep behavior state in actor fields.
  - [ ] Reuse `camera.AabbTiles`/`camera.AabbHitTop` and animation/sprite SDK.
  - [ ] Make behavior constants data-driven per enemy type.
- Verification:
  - [ ] A sample level can contain ≥3 enemy types with different behaviors and
    shared update/draw.
  - [ ] Tests prove no virtual dispatch, heap allocation, or function-pointer
    tables are emitted.
- Depends on: AF-2.1.

#### AF-2.3: Update/Draw over the active pool (AR-14.2)
- Layer: framework + target lowering.
- Candidate files: actor library update/draw, GB/NES tests.
- Steps:
  - [ ] Lower `Update`/`Draw` (or the chosen surface) to grouped loops over
    active slots; skip inactive slots.
  - [ ] Cap hardware sprite usage and honor the active flag.
- Verification:
  - [ ] Emitted-code tests show grouped loops + direct helper calls, no objects.
- Depends on: AF-2.1, AF-2.2.

### Phase 3 — World/Tiled spawn integration (AR-14.3)

#### AF-3.1: Spawn actors from Tiled object data (AR-14.3)
- Layer: portable SDK asset pipeline.
- Candidate files: `LogicalTiledMapImporter`, world asset model, sample maps, docs.
- Steps:
  - [ ] Read a named Tiled object layer (or equivalent spawn metadata) for actor
    kind and initial fields; keep parsing target-neutral (mind the #105 coupling).
  - [ ] Generate compact spawn tables alongside world data.
- Verification:
  - [ ] A platformer sample places multiple enemy kinds in Tiled without
    hard-coding every spawn in source.
- Depends on: AF-2.1.

#### AF-3.2: Camera-window / room activation (AR-14.3)
- Layer: framework.
- Candidate files: activation logic, capacity checks, tests.
- Steps:
  - [ ] Activate spawns into fixed actor slots by camera window / room.
  - [ ] Fail explicitly when a window/room can exceed declared pool capacity.
- Verification:
  - [ ] Test proves overflow of the declared pool is a compile-time/spawn-time
    error, not silent truncation.
- Depends on: AF-2.1, AF-3.1.

### Phase 4 — Capability checks and acceptance (AR-14.4)

#### AF-4.1: Capability checks and target diagnostics (AR-14.4, risk register)
- Layer: portable SDK / targeting.
- Candidate files: `Target2DCapabilities`, `TargetCapabilityChecks`,
  `Sdk2DOperationValidator` (only if a validated op is involved), tests.
- Steps:
  - [ ] Check pool size, hardware sprite count, per-scanline pressure, and frame
    budget against the target descriptor.
  - [ ] Emit a diagnostic that names the unsupported behavior; the same actor
    definitions compile for GB and NES or fail clearly.
- Verification:
  - [ ] Tests cover a fitting pool and an over-budget pool diagnostic on both
    targets.
- Depends on: AF-2.*, AF-3.*.

#### AF-4.2: Scalable platformer acceptance slice (AR-14.4)
- Layer: samples and validation.
- Candidate files: `samples/runner/runner.rs` or a new platformer sample, maps,
  assets, acceptance tests, tracked ROMs.
- Steps:
  - [ ] Extend the runner or add a focused platformer sample with ≥3 enemy kinds,
    activation, map collision, player contact, and animation.
  - [ ] Keep the source free of a hand-written global enemy-kind `switch` in `main`.
  - [ ] Validate Game Boy first, then NES where the behavior set is supported.
  - [ ] Regenerate tracked ROMs via `tools/gameboy/generate_sample_roms.py`.
- Verification:
  - [ ] Sample compiles and runs as a tracked ROM artifact; validation covers
    source behavior, generated ROM execution, and visible actor rendering.
- Depends on: AF-2.*, (AF-3.* for spawn).

#### AF-4.3: Documentation and closure (AR-14.4)
- Layer: docs.
- Candidate files: `docs/GameBoyTarget.md`, `docs/NesTarget.md`,
  `docs/Portable2DSdkV1.md`, `samples/README.md`, `samples/manifest.json`,
  `docs/ArchitectureRoadmap.md`.
- Steps:
  - [ ] Document the actor API, the emitted storage/cost model, and the
    hand-authored low-level equivalent.
  - [ ] Classify the new sample in `samples/manifest.json`.
  - [ ] Flip Iteration 14 status in `ArchitectureRoadmap.md` when complete.
- Verification:
  - [ ] `git diff --check`; manifest-reading tests pass.
- Depends on: AF-4.2.

## Recommended first slice

Land a minimal, Game-Boy-only vertical slice before breadth:

1. AF-1.1 (struct-array initializers) and AF-1.2 (iteration/update model).
2. AF-2.1 (pool + enemy definition) with AF-2.2 limited to **Walker only** and
   AF-2.3 update/draw.
3. AF-4.2 minimal acceptance (one Walker enemy spawned from a source array, map
   collision, player contact) on Game Boy.

Only then expand to Flyer/Shooter/Hazard/Chaser, Tiled spawn (AF-3.*), NES
parity, and capability breadth (AF-4.1). Defer AF-1.3 unless a slice needs
16-bit pooled fields, and defer AF-3.* until the #105 Tiled/world coupling is
acceptable for spawn data.

## Validation commands

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```
