# Game Boy runner observer fidelity

RPH-6.2 compares the joint-load detector's **ROM-visible** observations before
considering its host instrumentation. It is an observer experiment, not a
runtime, lowering, sample, or fluidity-threshold change.

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
host-only diagnostics and are never cross-backend equality gates.

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
route 2), it stops there: it does not try to certify injected canaries through
SameBoy. Cross-backend canaries would conflate the already-unresolved baseline
with the injection. They remain stable in-process red proofs only.

## Observation-layer canaries

The focused Game Boy test keeps the same ROM and inputs while injecting only
the captured observation:

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
