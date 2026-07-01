# Camera Vertical Scroll Roadmap (AR-5 execution plan)

Status: **GB vertical and staggered diagonal paths are proven by sample/tests; NES
has a separate bounded four-screen free-scroll path.** This is the branch-scoped execution plan for roadmap **Iteration 5:
Camera2D Vertical Scroll** (`docs/ArchitectureRoadmap.md`, AR-5.1..AR-5.3). It is
written so an autonomous agent (Codex) can pick up a task and know exactly what
to change, where, and how to verify it.

Read `AGENTS.md` and `docs/AgentContext.md` first for repo discipline and
validation commands. Read `docs/GameBoyTarget.md` and `docs/NesTarget.md` for the
current supported subset per target.

## TL;DR for the agent

- **Do NOT start from scratch on Game Boy.** Vertical scroll is already
  implemented at the codegen level and is internally consistent. The VS-1..VS-5
  slice proved it with `samples/gameboy-vscroll/vscroll.rs`, ROM/VRAM acceptance,
  and a row-streamer emission fix. The diagonal Strategy A slice is also proven
  by `samples/nes-free-scroll/freescroll.rs`, `samples/tiled-diagonal/diag.rs`,
  and `samples/tiled-free-scroll/free-scroll.rs` on Game Boy: the runtime queues
  a column and row independently and drains one visible background-map edge per VBlank.
- **NES is tracked separately.** The bounded free-scroll path now uses iNES
  four-screen VRAM, writes `$2000`/`$2005` for X and Y, and handles the 240-row
  coarse-Y wrap for maps that fit 64x60 tiles. Tall Tiled `World.Load(...)`
  maps now enter that four-screen path when a vertical camera axis is used, and
  `samples/tiled-free-scroll/free-scroll.rs` proves diagonal Tiled maps inside
  the 64x60 four-screen surface. Runtime row, diagonal, and attribute streaming for larger worlds remains
  governed by `docs/NesFreeScrollRoadmap.md`.
- Every change keeps the layer golden rule: the language and classic IR never
  learn about cameras; vertical scroll lives in the SDK operation model + per
  target lowering + capability checks.

## What already exists (verified in source)

Game Boy — fully wired, coherent, and now exercised by samples/tests:

- Capability declares both axes: `src/RetroSharp.GameBoy/GameBoyTarget.cs:18`
  (`ScrollAxes: Horizontal | Vertical`) and `:20` (`SupportsFineScrollY: true`).
- `Sdk2DOperation.SetCameraPosition` already carries `Y` and `Axes`
  (`src/RetroSharp.Core/Sdk/Sdk2DOperation.cs:39`); `StreamMapRow` already exists
  (`:73`).
- The validator already checks vertical: `SetCameraPosition` requires the axis +
  fine scroll + movement budget, and `StreamMapRow` requires `ScrollAxes.Vertical`
  + write budget — `src/RetroSharp.Core/Sdk/Sdk2DOperationValidator.cs:111-130`.
- GB runtime fully lowers Y:
  - Y state init: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs:2923-2936`
    (`CameraYLow/High`, `CameraFineY`, top/bottom background+source row addresses).
  - `EmitSetCameraPosition` applies Y via up/down move steps: `:2939-2964`.
  - `EmitApplyCamera` writes `SCY` (`0x42`) and drains the pending row stream
    during VBlank: `:2995-3008`, `:3010-3046`.
  - Up/down move steps + pending-row queue: `:3172-3214`.
  - Row streamer reads the world by source row: `:3261-3296`.
  - `CameraConfig.SourceHeight = program.MapColumnHeight`: `:5244-5247`.
- `samples/gameboy-vscroll/vscroll.rs` moves Y by one pixel per frame over a
  24-row source-authored map, scrolls down and back up, and builds as a Game Boy ROM.
  `samples/tiled-tall/tall.rs` proves the same vertical row streamer over a 16x40
  Tiled `World.Load(...)` map whose full height is kept in the imported world rows.
  `samples/tiled-vscroll/vscroll.rs` builds for Game Boy and NES from a 40x60 Tiled
  map; NES uses it to prove four-screen vertical scroll over all four nametables.
  `samples/tiled-diagonal/diag.rs` moves X and Y together by one pixel per frame
  over a 40x40 Tiled `World.Load(...)` map, proving that the imported rows and
  columns feed the staggered diagonal streamer.
  `samples/tiled-free-scroll/free-scroll.rs` moves X and Y together by one pixel
  per frame over a 50x60 Tiled `World.Load(...)` map and builds for both Game Boy
  and NES.
  `GameBoyVerticalScrollAcceptanceTests` compiles the sample, confirms the SDK
  operation carries a variable Y axis, runs the emitted ROM, and observes fresh
  row data in VRAM after the 32-row background buffer wraps.
- `samples/nes-free-scroll/freescroll.rs` moves X and Y by one pixel per frame
  over a 64x60 source map and builds as both Game Boy and NES. The Game Boy
  acceptance test runs the emitted ROM and observes both fresh wrapped columns
  and fresh wrapped rows after diagonal movement.
- The row streamer now shares one emitted 21-column write block across source
  rows. That fixed the latent tall-map failure where fully unrolled per-row
  stream code could force unsupported direct control flow across MBC1 program
  banks.

NES — bounded four-screen free scroll:

- Capability declares X/Y fine scroll over a 64x60 preloaded four-screen buffer:
  `src/RetroSharp.NES/NesTarget.cs`.
- `EmitSetCameraPosition` tracks Y for four-screen programs and rejects maps that
  cannot fit the preloaded surface.
- `Camera.Apply()` writes both nametable bits and both `$2005` scroll bytes while
  avoiding invalid NES Y scroll values in the 240..255 range.
- Runtime row/attribute streaming for larger NES worlds is implemented with the
  same one-edge-per-VBlank policy documented in `docs/NesFreeScrollRoadmap.md`.

## Guardrails (do not violate)

- The language and its classic IR never gain camera/scroll concepts. Vertical
  scroll stays in `Sdk2DOperation` + per-target lowerers + `Target2DCapabilities`.
- Do not add a new genre-specific operation if the existing `SetCameraPosition` /
  `StreamMapRow` records suffice. They do for GB.
- Every vertical feature must be capability-checked before lowering and must fail
  with a target diagnostic that names "vertical camera movement" when the target
  cannot do it. Cross-target samples must stay honest.
- Byte-backed values wrap at 256; vertical world/camera math must clamp or use
  explicit split-byte fields (`CameraYLow/High` already does 16-bit). Watch the
  240 vs 256 difference on NES (coarse-Y wraps at 240).
- Keep tracked ROMs byte-identical unless a task deliberately regenerates them
  with `tools/gameboy/generate_sample_roms.py`.
- Keep transitional APIs working; do not remove horizontal behavior.

## Reliable commands

```bash
git status --short --branch
dotnet test RetroSharp.sln -m:1
git diff --check
# Build a GB sample after changing it:
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb --out samples/<sample>/<name>.gb samples/<sample>/<name>.rs
# Regenerate tracked sample ROMs deliberately:
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

The CLI has no `--help`; verify options from `src/RetroSharp.Cli/Program.cs`.

---

## Phase A — Game Boy: prove and finish vertical scroll (AR-5.1 / AR-5.2)

### VS-1: Add a Game Boy vertical-scroll acceptance sample

- Layer: samples + validation.
- Files: new `samples/gameboy-vscroll/vscroll.rs` (target-intrinsic or
  target-acceptance), `samples/manifest.json`, `samples/README.md`.
- Steps:
  - [x] Author a minimal source that calls `Camera.Init(...)` over a world that
    is **taller than the visible band** and moves the camera with non-zero Y, e.g.
    `Camera.SetPosition(0, y)` where `y` ramps up/down per frame. As of the
    per-frame walk change, `Camera.SetPosition` walks the camera toward the
    requested position on its own (up to one tile crossing / ≤8 px per axis per
    call), so a single call per frame suffices even when `y` moves several pixels.
  - [x] Keep horizontal at 0 first to isolate vertical; add diagonal only after
    VS-2 passes.
  - [x] Classify the sample in `samples/manifest.json` (NES is NOT portable for
    this sample — mark GB-only until Phase D).
- How: model it on `samples/cross-target-camera/camera.rs` but drive Y instead of
  X. Use `World.Load(...)` with a tall map or `World.Map(width, streamY, height)`
  that leaves rows above/below the visible window (see VS-3).
- Verify:
  - [x] `dotnet run ... --target gb` builds the ROM.
  - [x] Add a GB acceptance test asserting the sample compiles and that the
    collected operation stream contains a `SetCameraPosition` with non-zero Y and
    at least one `StreamMapRow`-driving path (mirror `CrossTargetScrollAcceptanceTests`
    in `src/RetroSharp.NES.Tests/` / GB equivalents).
  - [x] Run it in the emulator/diagnostics and confirm the map scrolls vertically
    without tearing or wrong rows.

### VS-2: Validate and harden the GB row streamer

- Layer: GB target lowering.
- Files: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs` (`:3172-3296`,
  `:2923-2936`, `:3010-3046`), GB tests under `src/RetroSharp.GameBoy.Tests/`.
- Steps:
  - [x] Trace the up/down crossing: `CameraFineY` rolls 0..7, top/bottom
    background rows advance mod 32, top/bottom source rows advance mod
    `SourceHeight`. Confirm the **initial** bottom-row values
    (`:2929-2936`) place the first streamed row correctly (off-screen edge, not a
    visible duplicate).
  - [x] Confirm `EmitMapStreamRow` (`:3284-3296`) reads the correct source row and
    writes the correct VRAM background row for both up (top edge) and down
    (bottom edge) crossings.
  - [x] Confirm the VBlank drain (`EmitCommitPendingStream`, `:3010-3046`) commits
    at most one row per frame and does not exceed the GB background write budget
    (21 visible columns vs `MaxBackgroundTileWritesPerFrame`).
- How: add golden-byte tests like the existing column-streaming tests; if the
  emulator shows wrong rows, fix the row/source address init or the modulo
  arithmetic — do not paper over it in the sample.
- Verify:
  - [x] New GB test asserts row streaming for a known tall sample through ROM/VRAM behavior.
  - [x] `dotnet test RetroSharp.sln -m:1` green.
  - [x] Visual/smoke: vertical scroll sample runs in the Game Boy emulator; automated test observes fresh rows.

### VS-3: Ensure the world has vertical extent to stream

- Layer: portable SDK asset/world pipeline.
- Files: `src/RetroSharp.Core/Sdk/Tiled/LogicalTiledMapImporter.*`,
  `src/RetroSharp.GameBoy/GameBoyTiledMapImporter.*`, world-map structures, the
  sample map under `samples/.../maps/`.
- Steps:
  - [x] Confirm `program.MapColumnHeight` / `WorldMap2D` height can exceed the
    visible 18 GB rows so there are off-screen rows to stream in. Today the world
    band is authored for horizontal streaming; vertical scroll needs real rows
    above and/or below the initial window.
  - [x] If the importer flattens to a fixed-height band, extend it to keep the
    vertical extent the camera can reach, or provide a `World.Map(...)` path with
    explicit taller height for the sample.
  - [x] Keep `retrosharpWorldY` / `retrosharpStreamY` alignment intact (see
    `docs/AgentContext.md` Tiled section) so background and world layers stay
    visually aligned when the band shifts.
- Verify:
  - [x] The VS-1 sample reads distinct rows as it scrolls (not a repeating band).
  - [x] `dotnet test` green; tracked ROMs regenerated only if a tracked sample
    changed.

---

## Phase B — Diagonal (both axes in one frame) budget (AR-5.3)

### VS-4: Make the budget account for column + row in the same frame

- Layer: shared SDK validation.
- Files: `src/RetroSharp.Core/Sdk/Sdk2DOperationValidator.cs` (`:111-130` and the
  `RequireCameraMovementBudget` / `RequireBackgroundTileWriteBudget` helpers),
  `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs` (`EmitCommitPendingStream`).
- Steps:
  - [x] Today `StreamMapColumn` and `StreamMapRow` are budgeted independently. A
    diagonal frame can cross a column AND a row boundary at once. Decide the
    worst-case write budget (column height + row width) and enforce it, or
    serialize crossings across frames in the runtime so only one is committed per
    VBlank.
  - [x] The GB runtime currently commits one pending stream kind per frame
    (`PendingStreamKind` is single-valued, `:3021-3045`). Confirm a column and a
    row crossing in the same frame cannot both be lost or both committed past
    budget; if they can, add a small two-slot queue or a documented
    "one crossing per frame" policy with a capability note.
  - [x] Add capability checks for targets that support only X, only Y, or XY
    (`ScrollAxes` already models this; ensure `RequireAxes` rejects the
    unsupported combination clearly).
- Verify:
  - [x] Tests that a same-frame diagonal crossing either stays within budget or
    fails with a clear diagnostic.
  - [x] Horizontal-only regression tests still pass.

---

## Phase C — Public surface + docs for GB vertical

### VS-5: Promote vertical to a documented GB SDK capability

- Layer: docs + SDK reference.
- Files: `docs/GameBoyTarget.md`, `docs/Portable2DSdkV1.md`,
  `docs/ArchitectureRoadmap.md` (Iteration 5 status + API classification table),
  `docs/AgentContext.md`.
- Steps:
  - [x] Document `Camera.SetPosition(x, y)` with non-zero `y` as supported on GB,
    including the per-pixel call rule and the one-crossing-per-frame policy.
  - [x] Correct any remaining "horizontal-only" wording for GB.
  - [x] Update `samples/manifest.json` / `samples/README.md` for the new sample.
- Verify: `git diff --check`; docs match the shipped behavior; `SdkV1ReferenceTests`
  still pass.

---

## Phase D — NES vertical scroll (historical gate; see free-scroll roadmap)

The first NES vertical gate was superseded by `docs/NesFreeScrollRoadmap.md`.
That roadmap selected four-screen VRAM for the bounded free-scroll milestone and
kept mapper-backed larger worlds as NF-10. Use this section only as historical
context for why NES Y needed a separate substrate decision.

### VS-NES-0 (do this first): keep the gate honest

- Layer: NES validation + docs.
- Files: `src/RetroSharp.Core/Sdk/Sdk2DOperationValidator.cs`,
  `src/RetroSharp.NES/NesRomBuilder.cs:1699-1726`, `docs/NesTarget.md`.
- Steps:
  - [x] Keep a clear diagnostic while NES vertical was unsupported.
  - [x] Replace that gate with the bounded four-screen path only after NF-0 gave
    the acceptance emulator four distinct nametables.
- Verify: `dotnet test` green; unsupported larger-world cases now fail with the
  64x60 preloaded-surface diagnostic.

### The NES constraints Codex must respect (why this is painful)

- **Nametable arrangement / mirroring.** With 2 KiB VRAM you get 2 nametables.
  Vertical mirroring (current) = two side-by-side tables (horizontal scroll).
  Vertical scroll wants two stacked tables → horizontal mirroring. You cannot get
  seamless 2-axis scroll on NROM with 2 nametables; free 4-way scroll needs
  4-screen VRAM (extra cart RAM) or a mapper with runtime mirroring control
  (MMC1/MMC3). Decide: (a) vertical-only mode that switches the iNES mirroring
  flag, or (b) move the NES target to a mapper that supports mirroring control.
- **Scroll registers.** Vertical scroll uses `$2000` bit 1 (base nametable Y) and
  the **second** `$2005` write (fine/coarse Y). `Camera.Apply()` currently writes
  `$2000` horizontal bit + horizontal `$2005` + zero Y; extend it to compute and
  write the Y nametable bit and Y scroll.
- **The 240 wrap quirk.** Coarse Y wraps at 240 (30 rows), not 256. Writing
  240..255 to the `$2005` Y byte makes the PPU fetch attributes from the wrong
  place. Camera Y math must wrap at 240 and convert world-Y → (nametable, coarse,
  fine) correctly.
- **Attribute streaming.** Crossing a 16px vertical boundary needs a new attribute
  row, and `MaxAttributeWritesPerFrame` is `0` today (`NesTarget.cs`). You must
  raise the attribute budget and implement runtime attribute-row writes — which
  the NES target does not do for any axis yet (it only writes attributes at
  startup). The 30-row table also means attribute tables don't divide evenly
  (last attribute row covers 2 tile rows).
- **VBlank timing.** A full 32-tile row + its attributes may not fit one VBlank;
  plan a striped/partial update or a per-frame budget like the GB row drain.

### VS-NES-1: Decide mirroring/mapper and add NES camera-Y state

- Layer: NES target.
- Files: `src/RetroSharp.NES/NesTarget.cs`, `NesRomBuilder.cs` (iNES header,
  camera state, `EmitSetCameraPosition`/`EmitApplyCamera`).
- Steps:
  - [ ] Implement the chosen VRAM/mirroring decision (vertical-only mode vs
    mapper change). Keep horizontal mode working.
  - [ ] Add `CameraNewY`/coarse/fine Y state mirroring the X state.
  - [ ] Flip `NesTarget.Capabilities` (`ScrollAxes` add `Vertical`,
    `SupportsFineScrollY`, non-zero attribute budget) ONLY behind the working
    implementation.
- Verify: horizontal NES samples unchanged; new state initialized.

### VS-NES-2: Vertical row + attribute streaming and `$2005`/`$2000` Y writes

- Layer: NES target.
- Files: `NesRomBuilder.cs` (`EmitSetCameraPosition`, `EmitApplyCamera`, stream
  helpers), `NesSdkOperationLowerer.cs` (add `StreamMapRow` case),
  `Sdk2DOperationValidator.cs`.
- Steps:
  - [ ] On a vertical tile crossing, stream the next world row into the off-screen
    (stacked) nametable, plus the matching attribute row, within budget, handling
    the 240 wrap.
  - [ ] In `Apply`, write the Y nametable bit to `$2000` and the Y scroll to the
    second `$2005` write.
  - [ ] Add the `StreamMapRow` lowering case to `NesSdkOperationLowerer` (it
    currently throws `NotSupportedException`).
- Verify: golden-byte NES tests for the row/attribute writes; emulator shows clean
  vertical scroll; horizontal regression green.

### VS-NES-3: Cross-target vertical sample + acceptance

- Layer: samples + validation.
- Files: shared sample (extend `samples/cross-target-camera/camera.rs` or a new
  one), `CrossTargetScrollAcceptanceTests`, `NesTarget.md`, `manifest.json`.
- Steps:
  - [ ] Same source drives vertical scroll on both GB and NES (or fails with a
    precise capability diagnostic where a target cannot).
  - [ ] Update `docs/NesTarget.md` to remove the "vertical not supported" limits
    only when they are actually shipped.
- Verify: both targets build; acceptance tests cover source, operations, and
  emitted bytes.

---

## Acceptance criteria (whole iteration)

- GB supports SDK-level vertical camera movement within declared limits, proven by
  a tracked sample + tests, not just wired code (AR-5 acceptance).
- The compiler rejects unsupported scroll modes (e.g., NES vertical until shipped)
  with clear, named diagnostics.
- Horizontal behavior from Iteration 4 does not regress on either target.
- Diagonal (same-frame column+row) is either budget-safe or explicitly serialized
  with a documented policy.
- Docs (`GameBoyTarget.md`, `NesTarget.md`, `Portable2DSdkV1.md`,
  `ArchitectureRoadmap.md` Iteration 5 + classification table, `AgentContext.md`)
  match the shipped behavior in the same patch.

## Suggested order

1. VS-1 → VS-2 → VS-3 (prove GB end to end). This is the highest-value, lowest-risk
   slice and likely surfaces the first real bugs.
2. VS-4 (diagonal budget) and VS-5 (GB docs).
3. VS-NES-0 (honest gate) immediately, so cross-target samples stay correct.
4. For NES, follow `docs/NesFreeScrollRoadmap.md`; NF-4..NF-6 still require a
   human decision on the VBlank streaming policy before larger worlds are enabled.

---

## Phase E — Diagonal scroll (Strategy A implemented)

Status: **implemented with staggered edge commits.** A `Camera.SetPosition(x, y)`
that moves both axes remains a diagonal SDK request; the collector does not
degrade it to horizontal or vertical. Game Boy accepts it because
`GameBoyTarget.Capabilities` declares staggered camera stream draining and each
committed visible edge fits the 21-tile background write budget.

Real Game Boy games (e.g. Super Mario Land 2) scroll in 8 directions, so diagonal
is clearly feasible on hardware. The blockers are two of *our* design choices, not
the DMG:

- `MaxBackgroundTileWritesPerFrame: 21`
  (`src/RetroSharp.GameBoy/GameBoyTarget.cs`). A diagonal tile-boundary crossing
  exposes a visible background-map column (19 tiles) **and** a visible background-map row (21 tiles) -> 40 writes if committed in
  one VBlank.
- Horizontal/vertical-only programs still use the original deferred stream slot
  (`PendingStreamKind/Target/Source`) so their ROM shape stays compact. Diagonal
  programs emit separate pending column and row slots so the second edge is not
  overwritten.

The real VBlank (~4560 cycles) easily affords ~38 tiles with tight copy loops, so
this is engineering cost in our pipeline, not a hardware limit.

### Two strategies (cost ordered)

**Strategy A — stagger crossings across frames (recommended PoC).** Allow a column
**and** a row to be pending at once, but drain **one per VBlank** within the
existing 21-tile budget. Cost: a two-slot pending queue (a few WRAM bytes + a
commit that handles both kinds) and a validator rule keyed off the target's
staggered-stream capability. **No streamer rewrite.** Price: the second-axis edge appears one frame
late — practically invisible at 1px/frame. Effort is comparable to the GB vertical
slice this branch shipped.

**Strategy B — full same-frame diagonal (matches SML2 exactly).** Commit column +
row in one VBlank. Requires raising the budget to ≥38 **and** rewriting the row/
column streamers from per-tile address recomputation (modulo + pointer math) into
precomputed-pointer tight copy loops so they fit the VBlank cycle budget (this also
speeds up the single-axis path). Needs cycle measurement, likely a GB cycle-budget
test harness. Effort: a small epic; this is the risky part.

### What is already in place (no cost)

- Both-axis source data exists: column source tables (horizontal) and row source
  tables (`MapRowLabel`, vertical).
- `Camera.SetPosition` walks toward the target one pixel per step, bounded to at
  most one tile crossing (≤8 px) per axis per call, and queues per axis; only the
  destination slot is single. `AxesFor(x, y)` already produces
  `Horizontal | Vertical` when both move.
- Runner/actor ROMs are unaffected unless they adopt diagonal movement.

### VS-DIAG tasks

- [x] VS-DIAG-1 (Strategy A): add a two-kind pending queue so a column and a row
  can both be pending, drained one per VBlank; document the ≤1-frame edge latency.
- [x] VS-DIAG-2: in the validator, stop rejecting `Horizontal | Vertical` for GB
  when staggered draining keeps each frame within budget; keep a clear diagnostic
  for genuinely over-budget single-frame demands.
- [x] VS-DIAG-3: add a GB diagonal acceptance sample + emulator test that observes
  both a fresh column and a fresh row after diagonal movement.
- [ ] VS-DIAG-4 (optional, Strategy B): tighten the streamers into precomputed-
  pointer copy loops, raise the budget, and add a VBlank cycle-budget check so a
  full column+row commit fits one frame.

Strategy A is the shipped behavior. Only pursue Strategy B if profiling shows the
one-frame edge lag is visible in real gameplay.

### When to escalate to Strategy B (decision criteria)

Strategy A (shipped) commits at most one edge per VBlank with column priority.
That is correct and imperceptible at platformer/runner scroll speeds (≤1-2 px per
frame), where boundary crossings are sparse (~every 8 frames per axis). Do **not**
pursue Strategy B preemptively. Escalate only when one of these concrete needs
appears:

- **Fast diagonal scrolling.** If the camera can cross a column boundary almost
  every frame (≈8 px/frame horizontal), the priority column monopolizes the single
  per-VBlank commit slot and the **row queue starves** → visible gaps/garbage on
  the newly exposed row edge. Strategy B (both edges in one VBlank) is then the
  correct fix. Triggers: rapid free-look, chases, strong screen-shake, snap moves.
- **Pixel-perfect / no 1-frame edge lag.** Strategy A updates the second-axis edge
  one frame late; at the crossing instant a freshly revealed corner/row can show a
  stale tile for one frame. On high-contrast scenes or for visual polish, B removes
  it.
- **Large camera cuts/jumps** (respawn, cut, teleport moving many tiles at once):
  A repaints one edge per frame → a multi-frame "wipe". B (or a full VRAM rebuild
  path) repaints faster.
- **VBlank headroom for other features.** Strategy B requires rewriting the
  streamers from per-tile address recomputation into precomputed-pointer tight copy
  loops (`LDI`-style). That also speeds up the **single-axis** horizontal/vertical
  streamers and frees VBlank budget for OAM DMA, audio, or HUD. If VBlank pressure
  from other systems forces the issue, B pays off across all axes, not just
  diagonal.

Honest cost/risk: Strategy B must rewrite the streamers **and** raise the budget
**and** prove the combined column + row + OAM DMA commit fits the GB VBlank
(~4560 cycles). If it does not fit, B does not work and you fall back to A, so B
carries a feasibility/measurement risk and needs a VBlank cycle-budget test harness
(VS-DIAG-4) to be honest about the fit. Treat B as a "when you hit the wall"
optimization, not a default.
