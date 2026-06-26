# Game Boy Banking Roadmap (code + data overlays)

Status: **foundation started, feature incomplete.** This document records what is
known, what is done, and a detailed, file-level plan to finish transparent MBC1
banking so large programs (e.g. `samples/runner/runner.rs`) build and run on real
DMG hardware.

The executable specification for the finished feature lives in
`src/RetroSharp.GameBoy.Tests/GameBoyBankingRoadmapTests.cs` (skipped tests, each
tagged with the phase it belongs to). Un-skip them as each phase lands.

## 1. Problem statement

`samples/runner/runner.rs` no longer fits a 32 KiB ROM-only cartridge and cannot be
banked by the existing music-only banking, so it fails to build. Measured payload
(GB target, delight music):

| Component | Bytes | Source of truth |
|---|---:|---|
| Generated **code** | ~22,261 | overflow message: `fixed-bank program is 27369 bytes` minus data below |
| `tile_data` | 1,568 | `(FirstSpriteTile+SpriteTileCount)*16` |
| `tilemap` | 1,024 | `program.TileMap.Length` |
| map / world data | 2,516 | `map_row + background + flags` |
| music (`delight.gbapu`) | 10,907 | `MusicAssetsInLoadOrder[].Data.Length` |
| **ROM-only total** | **~38,276** | > `RomOnlyPayloadLimit` 32,432 |

Key constants (`GameBoyRomBuilder.cs`): `BankSize=16384`, `FixedBankProgramStart=0x150`,
`RomOnlyPayloadLimit=32,432`, `FixedBankPayloadLimit=16,048`.

## 2. Root-cause findings (verified in code)

1. **Every user function is inlined at each call site.** `GameBoyRuntimeCompiler.TryEmitUserFunction`
   does `EmitBlock(ParameterSubstitution.Substitute(function, call, "Game Boy"))`. There is no
   `CALL`/`RET` mechanism. The `inline` keyword is currently a no-op (everything is inlined).
   Consequence: the runner's ~22 KB is mostly inherent single-call logic; de-duplicating the
   few multi-call methods saves only ~2-3 KB → still ~19-20 KB > 16 KB.

2. **Executing code that touches banked data must live in bank 0.** On MBC1 the switchable
   window is `0x4000-0x7FFF`; code cannot switch the window it is executing from. So banked
   functions must be reached via **bank-0 trampolines** and must route any banked-data access
   through **bank-0 helpers** (the pattern the audio runtime already uses for banked music reads).

3. **The SDK-operation pipeline assumes whole-program inlining.** `Sdk2DOperationCollector`
   (and `SdkAudioOperationCollector`, `FrameBudgetCollector`) builds the portable op list by
   walking the program with every function inline-expanded (`CollectUserFunction` →
   `ParameterSubstitution.Substitute`). The GB and NES targets then consume that list through a
   single linear cursor (`nextSdkOperation++` in `ConsumeSdkOperation`). Emitting any function
   that contains SDK calls as a real subroutine (once) desynchronises the cursor. **This is the
   true prerequisite for the calling convention** and must be reworked first (see Phase 0).

4. **Existing music banking is the template.** `GameBoyRomLayout.CreateBankedMusicLayout`
   places music assets in banks; `UsesBankedMusic`; banked reads use a transient cursor bank
   (`MusicDataCursorBankAddress` etc.). Non-banked output is byte-identical. Extend this layout
   model to code and data.

## 3. Done so far — Increment 1 (SDK-op pipeline foundation)

- **NEW** `src/RetroSharp.Core/Sdk/Sdk2DProgram.cs`: `Sdk2DStreamItem` (`Op` | `CallSubroutine`)
  and `Sdk2DProgram` (main stream + per-subroutine streams).
- **REFACTOR** `src/RetroSharp.Sdk.Frontend/Sdk2DOperationCollector.cs`:
  `CollectProgram(main, functions, target, subroutineNames)` collects a subroutined function's
  body **once** (receiver params bound to their instance, value params left as local reads so
  they can flow through slots) and turns its call sites into `CallSubroutine` markers. The
  legacy `Collect(...)` is an empty-subroutine run flattened to ops → **byte-identical**.
- **TESTS** `src/RetroSharp.GameBoy.Tests/Sdk2DProgramStreamTests.cs`: empty set ≡ legacy;
  subroutined body collected once + call markers; no inlined copies.
- **Validation:** full solution green except the pre-existing runner-overflow failures
  (14 GB + 1 CLI), unchanged from baseline. Zero regressions.

## 4. Roadmap (phased, each phase keeps the suite green)

### Phase 0 — finish the SDK-op stream rework (started)
- 0a. `SdkAudioOperationCollector`: same `CollectProgram`/per-subroutine-stream treatment.
- 0b. `FrameBudgetCollector`: make subroutine-aware (don't double-count a shared body).
- 0c. Thread a `Sdk2DProgram` (+ audio equivalent) through `GameBoyVideoProgram` /
      `NesRom*` so targets can consume per-subroutine streams. Keep an empty-subroutine
      path byte-identical.
- Decide the **subroutine policy** (which functions become subroutines). Suggested:
  non-`inline` functions become subroutines; `inline` stays inlined; `main` is the body.
  Validate blast radius on existing byte-exact tests; if large, gate to non-`inline` AND
  called ≥1 time, and update affected sample/test expectations deliberately.

### Phase 1 — calling convention (subroutine emission), bank 0 only
- GB consumer becomes **stream-aware**: keep a stack of `(stream, cursor)`; consume the
  active subroutine stream while emitting its body; consume the main stream otherwise.
- For each subroutined function emit ONE body: `Label(user_fn_<name>)`, body, `RET (0xC9)`.
  Bodies are emitted after the `forever` jump (unreachable by fall-through).
- Calling convention:
  - **Receiver params** → bound at compile time to the single instance (already handled by
    `CollectProgram`'s receiver substitution); the body uses the instance's fixed addresses.
    If a function is called on >1 distinct receiver, monomorphise per receiver, or fall back
    to inline.
  - **Value params** → one fixed WRAM slot per (function,param), pre-declared via
    `DeclareVariable("<fn>__p_<name>")`. At each call site: `EmitExpressionToA(arg)` then
    `StoreA(slot)`; then `CALL user_fn_<name>` (`JumpAbsolute(0xCD, label)`).
  - **Return values** (value functions) → leave result in `A`; defer to a later sub-phase
    (keep value-returning user functions inlined initially).
  - **No recursion** (already enforced); nested subroutine calls are fine (distinct slots).
- This shrinks code but the runner is still > 16 KB → still ROM-only-overflowing. Validate
  with execution tests (subroutine called twice produces correct observable state).

### Phase 2 — MBC1 code overlays + trampolines
- Extend `GameBoyRomLayout`: place selected subroutine bodies (and `tile_data`/`tilemap`/map
  data, Phase 3) into banks; keep bank 0 = startup + main loop + trampolines + bank-0 helpers.
- Address resolution: the `GbBuilder` currently resolves all labels to `0x150+offset` (bank 0).
  Add per-bank label spaces so banked bodies resolve to `0x4000+offset_within_bank` and the
  builder records each label's bank. Calls to a banked body go through a **trampoline**:
  save current ROM bank, switch to the body's bank, `CALL` it, restore bank, `RET`.
- Banked code must not switch the window itself: any banked-data access inside a banked body
  calls a **bank-0 helper** that saves/switches/reads/restores (mirror the music read helpers).

### Phase 3 — bank read-only data (tile/map) + music (already banked)
- Startup copies (`tile_data`→`0x8000`, `tilemap`→`0x9800`): switch to the data bank, copy,
  restore. One-time, simplest.
- Gameplay map reads (`map_stream_column`, `camera.AabbTiles`/`AabbHitTop`,
  `EmitCameraTileFlagsAt`, `map_tile_at`): route each read through a bank-0 helper that
  switches to the map-data bank, reads, restores. Reuse the music transient-cursor-bank idea.

### Phase 4 — wire the runner + validation
- Make the runner build as MBC1; regenerate `samples/runner/runner.gb` (and confirm NES path).
- Un-skip `GameBoyBankingRoadmapTests`; add SM83 execution tests (`GameBoyTestCpu`) for
  bank-boundary crossings and banked-data reads, like `GameBoyMusicTests`.
- Keep ROM-only output byte-identical for programs that fit (regression guard).

## 5. Risks / invariants
- **Byte-identical ROM-only output** for programs that don't need banking (many tests assert
  exact bytes). Every phase must preserve this; gate new behavior behind "needs banking".
- **NES parity:** the SDK-op rework touches the shared frontend; keep NES green (its consumer
  must accept the new stream shape even if it never subroutines).
- **`GbBuilder` relative jumps (`JR`)** cannot cross a 16 KB bank boundary; banked bodies must
  use absolute `JP`/`CALL` across banks and keep `JR` within a bank.

## 6. Pointers
- Builder/layout/limits: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`
  (`Build`, `BuildProgram`, `GameBoyRomLayout`, `CreateBankedMusicLayout`, `GbBuilder`).
- Inlining: `GameBoyRuntimeCompiler.TryEmitUserFunction` / `TryEmitUserValueFunction`.
- SDK pipeline: `src/RetroSharp.Sdk.Frontend/Sdk2DOperationCollector.cs`,
  `SdkAudioOperationCollector.cs`; consumption `ConsumeSdkOperation`/`ConsumeSdkAudioOperation`.
- Music banking reference: the `UsesBankedMusic` branches and `Music*BankAddress` runtime state.
- ROM execution tests: `src/RetroSharp.GameBoy.Tests/GameBoyTestCpu.cs`,
  `GameBoyMusicTests.cs`.
