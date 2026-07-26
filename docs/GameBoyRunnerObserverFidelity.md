# Game Boy runner observer fidelity

RPH-6.2 compares the joint-load detector's **ROM-visible** observations before
considering its host instrumentation. RPH-6.2a then selects SameBoy's
`GB_run_frame` boundary as the one physical-frame authority consumed by
downstream runner cadence investigations. These are observer changes, not
runtime, lowering, sample, or fluidity-threshold changes.

## Boundary and provenance

The in-process runner samples after `GameBoyTestCpu.RunFrames(n)` reaches at
least `n * 70,224` cycles. SameBoy instead advances through `GB_run_frame`.
The independent replay never writes ROM, WRAM, VRAM, or OAM: it replays the
fresh ROM and input timeline exactly as emitted. SameBoy's absolute packed
camera value is unspecified at power-on, so only its baseline-relative value is
removed when checking the three-replay digest; every counter comparison itself
uses frame-to-frame deltas.

The normalized comparison includes ROM WRAM `$C20C` (gameplay tick), `$C19D`
(packed audio tick), packed camera counters and visible coordinates, player
coordinates, bank shadow, music/SFX activity, the five forbidden-work counters,
retained VRAM viewport, and OAM. The C# timeline owns every sampled WRAM
address, including lifecycle, visible-camera, and audio-activity addresses; the
Python runner does not hard-code them. `SourceWaitCompletions`,
`AudioUpdateCalls`, CPU cycles, APU/write traces and reset detection remain
host-only diagnostics and are never cross-backend equality gates or
physical-frame inputs.

## Reproducible command

The focused test writes a freshly compiled ROM, a compact normalized report
(one baseline plus 360 frame rows/digests), and the exact per-frame input
timeline only when the output path is explicitly requested. No tracked ROM is
changed.

```bash
mkdir -p /tmp/rph62
RETROSHARP_RPH62_REPORT="/tmp/rph62/in-process.json" \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --no-restore \
  --filter 'FullyQualifiedName~GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate'

python3 tools/gameboy/compare_runner_joint_load_sameboy.py \
  --library /home/jmn/Repos/GameboyMcp/native/out/linux-x64/libgameboy_debug_sameboy.so \
  --rom /tmp/rph62/in-process.gb \
  --timeline /tmp/rph62/in-process.timeline.json \
  --in-process-report /tmp/rph62/in-process.json \
  --out /tmp/rph62/sameboy-comparison.json
```

The SameBoy command performs three fresh C-ABI replays and rejects a run if
their normalized digests differ. Its JSON keeps three independent outcomes:
`sameCounterVerdict` for ROM-counter deltas, `projectionVerdict` for RPH-6.1's
host `GameplayTicks`/`AudioUpdateCalls` deltas, and `stateVisualVerdict` for
state and viewport/OAM observations. `overallVerdict` is their composition;
one result never overwrites another. `counterDeltaAlignment` searches bounded
offsets using *per-frame* counter deltas, reports the best offset, mismatch
count, and at most eight representative rows. The owner of an outcome is
`GameBoyTestCpu` fixed-cycle frame bucketing and `AudioUpdateCalls` projection,
not production runtime or lowering.

The baseline test itself also creates three independent in-process machines and
requires byte-identical normalized JSON before emitting the replay descriptor.
The descriptor carries the cadence budgets and expected-audio spans. The result
therefore classifies the real timeline in both backends as `NOT_REPRODUCED` or
the first `gameplay-cadence-gap` / `audio-service-gap` (or final
`gameplay-tick-ratio`) with its physical frame. The SameBoy cadence pass uses
only the contractual measurement rows, never the extra predecessor rows kept
for bounded offset alignment.
Owners are emitted per failed dimension and are `null` for a match.

The compact baseline establishes the two emulator offsets. The first successor
of that baseline is not treated as a comparable physical-frame interval; the
comparison begins with transitions wholly inside the 360-frame observation
window.

Once this route identifies a baseline frame-boundary discrepancy (resolution
route 2), the comparison stops there. It does not align or reinterpret the
in-process host counters.

## Authoritative physical-frame command

The focused test can emit the fresh ROM and replay descriptor without emitting
or consuming an in-process report:

```bash
mkdir -p /tmp/rph62a
RETROSHARP_RPH62_REPLAY="/tmp/rph62a/runner.timeline.json" \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --no-restore \
  --filter 'FullyQualifiedName~GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate'

python3 tools/gameboy/observe_runner_joint_load_sameboy.py \
  --library /home/jmn/Repos/GameboyMcp/native/out/linux-x64/libgameboy_debug_sameboy.so \
  --rom /tmp/rph62a/runner.gb \
  --timeline /tmp/rph62a/runner.timeline.json \
  --out /tmp/rph62a/runner.sameboy.json
```

The observer applies each input and calls `GB_run_frame` exactly once before
sampling ROM-visible state. It executes three fresh replays, records the ROM,
timeline, and SameBoy-library SHA-256 values as provenance, and requires one
normalized digest. Its result schema is
`retrosharp-rph62a-sameboy-physical-timeline-v1`.

Downstream tools consume only these fields as the physical verdict:

```text
verdict             = .verdict
firstFailure        = .firstFailure
physicalDigest      = .deterministicDigest
physicalTimeline    = [.baseline] + .frames
```

They must additionally require `.deterministic`, `.canariesPassed`,
`.replayCount == 3`, `.authority.physicalFrameBoundary == "GB_run_frame"`, and
`.authority.gameBoyTestCpuPhysicalAuthority == false`. The observer CLI has no
in-process-report argument. `GameBoyTestCpu` remains the fast behavioral
simulation and authored background/OAM oracle; it is not a physical-frame
clock.

## Observation-layer canaries

The focused Game Boy test keeps the same ROM and inputs while injecting only
the captured in-process observation:

- gameplay-only freeze: `gameplay-cadence-gap`;
- audio-only freeze: `audio-service-gap`;
- four-frame delayed camera visibility: `camera-visible-gap` on the first frame
  after the injected request's declared visible budget, followed by recovered
  publication in the non-fail-fast replay;
- OAM corruption: `sprite-oam`.

The functional-runner boundary tests separately prove that resident and visible
requests at their limits pass and absence through the next physical frame fails.
These codes classify observer fidelity; they do not alter product acceptance
budgets.

The authoritative SameBoy artifact also carries four controlled transcript
canaries. They freeze the captured ROM gameplay counter, freeze the captured
packed-audio counter, delay the captured visible-camera sequence, or corrupt
one captured OAM digest. Each must return the expected bounded code/frame and a
stable transcript digest. These are classifier/serialization proofs over the
real SameBoy transcript; they do not write emulator memory and are not claims
that a modified ROM or modified hardware state was executed.
