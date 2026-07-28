# Game Boy runner observer fidelity

Status: **historical/background only.** This is the RPH-6.2/6.2a/6.3 forensic
record of how the runner joint-load cadence observer was hardened. It is not
active dispatch and not a closeout gate: its multi-replay SameBoy differential
and deferred-bisect harness are forensic tooling, and its runner-cadence result
was `NOT_REPRODUCED`. For live runner debugging use
[`../GameBoyRunnerDebugging.md`](../GameBoyRunnerDebugging.md); for the product
acceptance rule (a confirmed perceptual report is fixed against the physical
observer, never closed as `NOT_REPRODUCED`) see the Acceptance Policy in
[`../../AGENTS.md`](../../AGENTS.md).

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

## Full-load phase matrix

RPH-6.3 keeps the complete ROM and 680-frame descriptor fixed. `RIGHT+B`
remains held for all 360 observation frames. The only mutation moves the same
six-frame A/jump/SFX span to every physical start frame from 330 through 350,
covering both parities and the authored frame 340 without shortening movement,
streaming, collision, BGM, or SFX load.
The command rejects any descriptor that changes the 320-frame warm-up,
360-frame observation, authored A start/duration, or reviewed cadence/camera
budgets; this is also the compatibility boundary for a deferred bisect.

```bash
mkdir -p /tmp/rph63
RETROSHARP_RPH62_REPORT="/tmp/rph63/in-process.json" \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 \
  --filter 'FullyQualifiedName~GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate'

python3 tools/gameboy/compare_runner_joint_load_sameboy.py \
  --library /home/jmn/Repos/GameboyMcp/native/out/linux-x64/libgameboy_debug_sameboy.so \
  --rom /tmp/rph63/in-process.gb \
  --timeline /tmp/rph63/in-process.timeline.json \
  --in-process-report /tmp/rph63/in-process.json \
  --out /tmp/rph63/comparison.json

python3 tools/gameboy/sweep_runner_joint_load_sameboy.py \
  --library /home/jmn/Repos/GameboyMcp/native/out/linux-x64/libgameboy_debug_sameboy.so \
  --rom /tmp/rph63/in-process.gb \
  --timeline /tmp/rph63/in-process.timeline.json \
  --in-process-comparison /tmp/rph63/comparison.json \
  --out /tmp/rph63/phase-matrix.json
```

The matrix makes two complete passes over all 21 phases. Every pass invokes
the physical observer, which itself executes three fresh SameBoy replays.
Consequently a green closeout covers 42 case runs and 126 emulator replays.
It stops sweeping new phases on a physical RED, then repeats that phase to
two matching matrix runs before returning exit 1. If two runs disagree, it
runs that phase once more and returns exit 125. An invalid observer contract,
missing full-load coverage, failed canary, behavioral-observer disagreement,
cross-pass digest/verdict disagreement, or non-cadence physical failure returns
exit 125 instead. Only the reviewed gameplay/audio cadence codes can become a
bisect `bad` result.

The 2026-07-26 run from `ac86494` used fresh runner ROM SHA-256
`1617f7c82464beb042e444a39686237d68ebfd686e5e3c43a781abdcae9ac3a6`.
All 21 phases were `NOT_REPRODUCED`; the matrix digest was
`34144bf86555eb636c3a1fa8a2fb0aeb1633e246a226f5b6f6fef30aaf08fbb1`.
Every phase retained 360 `RIGHT+B` frames, six A frames, player X range
73..430, player Y range 234..273, 41 camera requests, active BGM and SFX, and
changing retained-background and OAM digests. The in-process behavioral
classification and the independent SameBoy comparison were also
`NOT_REPRODUCED`.

The deterministic artifacts are
[`validation/gameboy/runner-joint-load-comparison.json`](../../validation/gameboy/runner-joint-load-comparison.json)
and
[`validation/gameboy/runner-joint-load-phase-matrix.json`](../../validation/gameboy/runner-joint-load-phase-matrix.json).
The comparison is bound to the exact ROM and base timeline hashes. Every
physical case is independently bound to its mutated timeline, ROM, SameBoy
library, observer schema, three replay digests, canary proofs, and enforced
load-coverage checks. `GameBoyTestCpu` disagreement can therefore invalidate a
closeout but cannot yield a physical RED or a `git bisect` bad exit.
The paired comparison covers the exact authored base timeline. Phase mutations
run only through the SameBoy physical authority: synthesizing phase-shifted
`GameBoyTestCpu` rows would reintroduce the fixed-cycle bucket as a frame clock,
so those rows may neither establish nor veto a phase-specific physical RED.

These hashes record provenance and repeatability, not byte-identity product
requirements. The conclusion is bounded to the reviewed cadence, camera, and
forbidden-work budgets. Because no phase produced a canonical RED, RPH-6.3
does not delete dimensions, rank a production owner, or run a historical
bisect.

### Deferred bisect contract

There is no compatible good/bad pair today. The isolated `d4f7837` checkpoint
is the earliest commit containing the manifest/scenario/descriptor/observer
contract and is behaviorally identical to `ac86494` for those inputs, but it is
not an ancestor of `ac86494` and therefore cannot be its `git bisect` good
endpoint. On the `master` ancestry, `ac86494` is the first compatible revision.
The historical `f612a7e` report predates the scenario, descriptor, observer,
and current runtime layout, so it is not a valid good endpoint either.

Only after one phase records a deterministic physical RED and a distinct
compatible revision records `NOT_REPRODUCED` may a bisect use the single-case
exit code. The RPH-6.3 harness must be an immutable checkout outside the
worktree whose `HEAD` is being bisected:

```bash
export RPH63_A_START=340
export RPH63_SAMEBOY_LIBRARY=/home/jmn/Repos/GameboyMcp/native/out/linux-x64/libgameboy_debug_sameboy.so
export RPH63_HARNESS=/absolute/path/to/immutable-rph63-harness
export RPH63_HARNESS_COMMIT=replace-with-merged-rph63-commit
: "${BAD:?set BAD}"
: "${GOOD:?set GOOD}"
test -n "$RPH63_HARNESS" && test -n "$RPH63_HARNESS_COMMIT" || exit 2
test "$(git -C "$RPH63_HARNESS" rev-parse HEAD)" = "$RPH63_HARNESS_COMMIT" || exit 2
test -z "$(git -C "$RPH63_HARNESS" status --porcelain)" || exit 2
test "$(git -C "$RPH63_HARNESS" rev-parse --show-toplevel)" != "$(git rev-parse --show-toplevel)" || exit 2
git merge-base --is-ancestor "$GOOD" "$BAD" || exit 2
git bisect start "$BAD" "$GOOD" || exit 2
git bisect run bash -lc '
  test "$(git -C "$RPH63_HARNESS" rev-parse HEAD)" = "$RPH63_HARNESS_COMMIT" || exit 125
  test -z "$(git -C "$RPH63_HARNESS" status --porcelain)" || exit 125
  for path in \
    samples/runner/runner.retrosharp.json \
    validation/scenarios/runner-joint-load.gb.json \
    src/RetroSharp.GameBoy.Tests/GameBoyRunnerJointLoadCadenceTests.cs
  do
    git cat-file -e "HEAD:$path" || exit 125
  done
  test -f "$RPH63_HARNESS/tools/gameboy/compare_runner_joint_load_sameboy.py" || exit 125
  test -f "$RPH63_HARNESS/tools/gameboy/sweep_runner_joint_load_sameboy.py" || exit 125
  out=$(mktemp -d /tmp/rph63-bisect.XXXXXX)
  trap "rm -rf \"$out\"" EXIT
  RETROSHARP_RPH62_REPORT="$out/in-process.json" \
    dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 \
    --filter "FullyQualifiedName~GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate" \
    || {
      test -s "$out/in-process.json" \
        && test -s "$out/in-process.gb" \
        && test -s "$out/in-process.timeline.json" \
        || exit 125
    }
  python3 "$RPH63_HARNESS/tools/gameboy/compare_runner_joint_load_sameboy.py" \
    --library "$RPH63_SAMEBOY_LIBRARY" \
    --rom "$out/in-process.gb" \
    --timeline "$out/in-process.timeline.json" \
    --in-process-report "$out/in-process.json" \
    --out "$out/comparison.json" || exit 125
  python3 "$RPH63_HARNESS/tools/gameboy/sweep_runner_joint_load_sameboy.py" \
    --library "$RPH63_SAMEBOY_LIBRARY" \
    --rom "$out/in-process.gb" \
    --timeline "$out/in-process.timeline.json" \
    --in-process-comparison "$out/comparison.json" \
    --a-start-frame "$RPH63_A_START" \
    --out "$out/matrix.json"
'
```

Exit 125 skips contract-incompatible revisions. Exit 0 is a compatible green
case and exit 1 is a compatible RED. Without both `$BAD` and `$GOOD`, this
command is documentation, not authority to start a bisect.

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
