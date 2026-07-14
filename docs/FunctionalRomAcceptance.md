# Functional ROM Acceptance

Status: implemented shared contract for CSL-2 / #337, with the canonical
static/source-camera rung bound by CSL-3 / #338 and the packed Tiled
production matrix bound by RPH-3.5 / #339.

This document defines the functional acceptance boundary for canonical RetroSharp sample ROMs. Compilation and final-state checks remain useful, but they cannot prove sustained gameplay cadence, transient visual integrity, legal video writes, or correct camera publication. Functional acceptance runs the exact emitted ROM and evaluates every retained observation through one target-neutral contract.

## Ownership

`RetroSharp.FunctionalAcceptance` is validation infrastructure. It does not belong to the language, portable SDK, or either target runtime, and it does not add gameplay APIs. Target-specific test CPUs and MCP sessions remain responsible for executing hardware behavior; the shared runner owns scenario timing, integrity rules, and reports.

```text
checked-in scenario + exact ROM bytes
                  |
         FunctionalScenarioRunner
                  |
        GB adapter / NES adapter
                  |
  test CPU, GameboyMcp, NesMcp, or emulator session
                  |
       per-frame observations
                  |
 shared timing checks + strict integrity checks
                  |
       JSON report + text report
```

The runner accepts a `FunctionalRomArtifact`, passes its bytes unchanged to `IFunctionalRomMachineFactory`, and records the artifact path and SHA-256 in both reports. Scenario code cannot compile a rewritten source, substitute camera state, or silently select a different ROM.

## Checked-in scenario contract

Scenarios live under `validation/scenarios/` and conform to `functional-scenario.schema.json`. Each scenario is keyed by a stable `sampleId` plus `target` (`gb` or `nes`). The scenario `id` identifies one concrete sample/target/input combination.

| Field | Meaning |
| --- | --- |
| `warmUpFrames` | Physical frames executed before timing measurements. |
| `observationFrames` | Exact steady-state measurement window. |
| `inputs` | Frame-based held-button spans; an optional `responseSignal` enables input-to-state latency. |
| `checkpoints` | Exact per-frame state-signal expectations. |
| `expectedFeatures` | Observations the adapter must supply for this scenario. |
| `audio` | Scenario-owned service expectation plus explicit authored-silence spans. |
| `budgetEvidence` | Reviewed baseline commit, hardware rationale, and production-trace rationale. |
| `budgets` | Reviewed absolute timing limits; never learned from the current build. |

`FunctionalScenarioLoader` rejects malformed values, duplicate input/checkpoint ids, out-of-window events, unknown JSON properties, missing audio budgets, and camera scenarios without a relevant residency or visibility deadline. `validation/scenarios/fixtures/contract-probe.json` is the minimal complete example; it is a contract fixture, not a canonical sample migration.

## Timing semantics

Warm-up affects timing only. The runner captures frame zero, executes every physical frame, and subtracts the cumulative counters at the warm-up boundary from those at the end of the observation window.

- Gameplay ratio is `gameplay tick delta / observation frames`.
- A missed gameplay frame is a frame with no positive gameplay-counter delta; the longest consecutive streak is checked.
- Authored-silent audio frames declared by the scenario do not count as starvation. The adapter cannot label a stalled frame as silence. Active frames check the longest zero-service streak and absolute cumulative drift from one service tick per active frame.
- Input latency starts on the first frame of an input span and ends on the first change of its declared response signal.
- Camera latency starts when a new request sequence appears and ends when the same sequence becomes resident or visible. Requested, resident, committed, and visible sequence counts remain separate in the report.
- If the final observation-window request is still in flight, the runner drains only the declared request-to-resident/request-to-visible budget. Drain frames receive no input, cannot improve gameplay or integrity measurements, and are counted separately in `totalPhysicalFrames`; they exist only so a legal final request can prove its bounded completion.

Every timing result records the observed value, reviewed limit, comparison, remaining headroom, and pass/fail result. The report records warm-up, observation, and total executed physical frames separately, so a stable ratio cannot hide a longer startup. Limits must be calibrated from the known-good pre-Large-Worlds commit `95f166886713ff3b88bc1e17c03ef0ffe93d649a`, hardware timing, and production-path traces. Relaxing a budget is a source review change; the runner has no baseline-learning mode.

## Integrity semantics

Integrity is strict within the declared observation window and is reported separately from timing tolerance. A later correct frame never erases an earlier corrupt retained frame.

- Any additional reset fails.
- An independent scenario oracle supplies authored background and logical-sprite expectations; the machine adapter supplies only actual retained state.
- Expected and observed background tile plus palette identities must match at every retained location.
- Logical sprite visibility and complete OAM projections must match; stale, missing, or unexpected sprites fail.
- Unsafe visible-time VRAM/PPU or OAM writes fail. Every reported write retains its address, safety verdict, absolute cycle, scanline, dot, PPU/LCD phase, and display-enabled state.
- Selected bank/mapper state must match its expected shadow/restored state.
- Required observations that an adapter omits fail explicitly.
- Checkpoint state signals must match at the exact declared frame.

The target adapter declares its observation capabilities before execution. The runner refuses a scenario whose required camera, audio, bank, background, sprite, input, or write-timing observations are unavailable, preventing a reduced adapter from passing by omission.

Both JSON and text reports retain every observation-window frame after validation. Each evidence row includes cumulative gameplay/audio/reset counters and any required state signals, camera lifecycle, bank/mapper state, actual background and sprite/OAM projections, timed writes, plus the independent visual expectation. Successful transient commit windows therefore remain auditable instead of disappearing into a final aggregate.

## Adapter contract

`GameBoyFunctionalRomAdapter` and `NesFunctionalRomAdapter` share `IFunctionalRomMachineFactory` and `IFunctionalRomMachine`. A machine receives the exact ROM bytes, captures frame zero, applies the runner's held-input set for each requested physical frame, and returns one actual `FunctionalFrameObservation` with the matching frame number and monotonic cumulative counters. Visual scenarios also supply a separate `IFunctionalFrameOracle`, normally derived from authored map and sprite intent rather than emulator memory.

The adapters deliberately do not introduce target-specific acceptance rules:

- Fast Game Boy scenarios wrap the existing production-ROM `GameBoyTestCpu` instrumentation.
- Fast mapper-0 NES scenarios wrap the deterministic production-ROM execution path in `NesTestCpu`, including reset-vector re-entry detection, CPU execution, an integer PPU-dot clock with rendering-dependent odd-frame skip, controller reads, nametable/attribute/palette/OAM state, scroll state, and cycle-positioned PPU/OAM writes.
- `GameboyMcp` and `NesMcp` transports implement the same machine interface, so they feed the shared runner rather than introducing target-specific acceptance logic.
- External-emulator launch scripts may provide transport, but the shared .NET runner remains the behavioral oracle and report producer.

Concrete sample adapters and scenarios are added by the ladder rung that owns the sample. CSL-2 establishes the contract and deliberately does not migrate all samples.

The first binding proof is `FunctionalProductionRomAcceptanceTests`: it loads `validation/scenarios/tiled-vscroll.gb.json`, passes the exact tracked `samples/tiled-vscroll/vscroll.gb` bytes through `GameBoyFunctionalRomAdapter`, and drives the existing cycle-accurate `GameBoyTestCpu`. That scenario enables only the observations this seam can prove reliably (gameplay cadence, resets, and request/resident/commit/visible camera state). It does not claim bank or PPU write-timing coverage; later sample rungs must enable those features only through instrumentation that can actually observe them.

CSL-3 binds eight canonical sample/target scenarios to exact production source
and emitted ROM bytes: static drawing on GB/NES, the shared input-driven camera
on GB/NES, source-authored vertical scrolling on GB, source-authored free
scroll on GB/NES, and the Game Boy Window HUD. These bindings enable authored
background and palette checks on every retained frame, exact gameplay-tick
checks for every animated sample, request/resident/commit/visible camera
deadlines, and legal video/OAM write timing. See
[`SimpleSampleFunctionalAcceptance.md`](SimpleSampleFunctionalAcceptance.md)
for the matrix, reviewed budgets, hashes, MCP checkpoints, and focused command.

RPH-3.5 binds the next eight canonical sample/target scenarios to the packed
Tiled production path: `tiled-tall` GB, `tiled-vscroll` GB/NES,
`tiled-diagonal` GB, `tiled-free-scroll` GB/NES, and `deadzone-follow` GB/NES.
They add complete authored tile/palette oracles, request/resident/commit/visible
lifecycles, bank/mapper restoration, and cycle-positioned video/OAM writes over
long vertical, diagonal, reversal, circular-buffer, chunk, and bank-sensitive
windows. Fixed state checkpoints retain forward, reverse, wrap, and chunk-return
trajectory inside those windows. See
[`PackedTiledFunctionalAcceptance.md`](PackedTiledFunctionalAcceptance.md) for
the exact hashes, measured budgets, and external emulator checkpoints.

The horizontal #335 slice extends that packed matrix to twelve rows with two
stable sample identities on both targets: a collision-free 64x20-cell crop and
the complete collision-free 156x20-cell `stage1`. These scenarios remove
player/input/audio/collision behavior, advance the camera one pixel per source
tick, and retain exact authored tile and palette checks across chunk, circular
background, and X=256 boundaries.

## CI tiers

1. Ordinary PR CI runs deterministic in-process scenarios and the contract probes.
2. Selected packed, audio, sprite, and runner scenarios execute the same scenario/oracle contract through GameboyMcp or NesMcp.
3. Scheduled or integration parity may use slower external emulators, but it must feed the same observations into the .NET runner instead of introducing a second Python oracle.

Focused validation:

```bash
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
```

Full validation remains:

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
```
