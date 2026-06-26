# Game Boy Banking Roadmap (code + data overlays)

Status: **transparent MBC1 foundation with multi-bank code/data/audio overlays landed.**
This document records what is known, what is done, and the remaining limits for
transparent MBC1 banking. The current implementation lets large programs such as
`samples/runner/runner.rs` build as an MBC1 ROM without source banking calls.
Multi-bank subroutine bodies, read-only data, and banked music are handled by the
generated runtime; linear main-flow fall-through across switchable program banks
is handled by compiler-inserted continuations.

The executable specification for the landed foundation lives in
`src/RetroSharp.GameBoy.Tests/GameBoyBankingRoadmapTests.cs`. Those tests are
currently un-skipped and passing; add new failing/skipped specs before expanding
the known limits below.

## 1. Problem statement

`samples/runner/runner.rs` no longer fits a 32 KiB ROM-only cartridge and could not
be banked by the original music-only banking. Measured payload at roadmap creation
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

## 3. Landed foundation

- **SDK operation streams:** `Sdk2DProgram` and `SdkAudioProgram` represent main streams plus
  per-subroutine streams. The legacy collectors still flatten the no-subroutine path so existing
  ROM-only output stays stable.
- **GB subroutine emission:** `GameBoyVideoProgram` selects conservative runtime-work helpers
  called more than once. The Game Boy backend emits one `user_fn_*` body with `CALL`/`RET`,
  passes supported value parameters through fixed WRAM slots, and keeps return-value functions
  and receiver-parameter methods inline for now.
- **Stream-aware GB lowering:** the Game Boy SDK 2D/audio consumers enter the matching
  subroutine stream while emitting a subroutine body and consume call markers at call sites.
- **Runner-scale MBC1 layout:** when ROM-only output overflows, the builder first removes music
  from the fixed payload, then reserves as many switchable program tail banks as the supported
  MBC1 layout needs. Bank 0 holds `$0150-$3FFF`; switchable program banks hold contiguous
  code/data tails at `$4000-$7FFF`; banked read-only data and music start after those program
  banks. Fixed-bank helpers restore the current program bank after banked data or music access so
  code in the tail window can continue executing.
- **Bank-0 subroutine entry points:** when a banked layout has subroutines, calls go through
  fixed-bank trampolines that save the caller's current program bank, select the callee body's
  bank, `CALL` the shared body, restore the caller's bank, and `RET`. User source still calls
  ordinary RetroSharp functions.
- **Multi-bank subroutine overlays:** labels in switchable program banks resolve to
  `0x4000+offset_within_bank`, and trampoline bank immediates are patched from the callee label's
  actual bank.
- **Multi-bank main-flow continuations:** when executable bytes approach the end of a switchable
  program bank, `GbBuilder` reserves a small tail stub that loads the next program bank and jumps
  to a bank-0 continuation helper. The helper updates `ProgramCurrentBankAddress`, selects the
  next MBC1 bank, and jumps to `$4000`, so linear fall-through continues without source-level bank
  calls.
- **Banked read-only data:** when the linear banked program would need more than the supported
  one switchable program tail bank, the builder can move read-only data into dedicated data-bank
  placements. Startup-copied blobs (`tile_data`, `tilemap`, and `window_tilemap`) are copied from
  their data bank into VRAM, and runtime map/background/flag row reads go through fixed-bank
  helpers that select the data bank, read the byte, restore the program bank, and return.
- **Banked audio entry points:** when a program tail bank and banked music are both present,
  `music.Play(...)` and `audio.Update()` call fixed-bank helpers. Those helpers can switch to the
  selected music bank, read or initialize music state, restore the program bank, and return, so
  RetroSharp source never needs manual bank switching even when the call site lives in
  `$4000-$7FFF`.
- **Validation:** `GameBoyBankingRoadmapTests` are un-skipped, the runner builds as a 64 KiB MBC1
  ROM, the test CPU supports `CALL`/`RET`, and Game Boy execution tests cross the bank boundary
  without faulting.

## 4. Known limits and follow-ups

### Phase 0 accounting invariant
- `FrameBudgetCollector` intentionally remains source-call-expanded instead of consuming the
  shared emitted subroutine body. This keeps budget checks tied to actual runtime work per call
  site; tests cover a streaming helper called twice in one frame still counting twice.

### Code overlay limits
- Multi-bank subroutine bodies now work through bank-aware fixed trampolines.
- Linear main-flow fall-through across switchable program banks now works through compiler-inserted
  continuations and a fixed-bank helper.
- Banked code must not switch away from the window it is executing from. New banked-data access
  forms inside a banked body should call a bank-0 helper that saves/switches/reads/restores.
- Non-linear control-flow edges still need care: a direct `JP`/`CALL` to code in a different
  switchable bank is rejected explicitly unless it goes through a fixed-bank trampoline/helper.

### Helper hardening
- Startup-copied tile/tilemap data and runtime map/background/flag row data now have explicit
  data-bank metadata when the layout needs it.
- If new read-only data categories are added, route any runtime reads through the same fixed-bank
  helper pattern instead of switching banks inline from code that may live in `$4000-$7FFF`.
- `music.Play(...)` and `audio.Update()` now use fixed-bank helpers in the tail-program/banked-music
  layout. If future audio operations need to select ROM banks, route them through the same pattern.

### Validation invariants
- Keep ROM-only output byte-identical for programs that fit.
- Add tests for any future non-linear cross-bank control-flow design before implementing it.

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
