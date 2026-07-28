# Game Boy runner observer fidelity

Status: **historical/background only.** This is the RPH-6.2/6.2a/6.3 record of
the runner joint-load cadence observer. Its multi-replay SameBoy differential
and deferred-bisect harness were forensic tooling that has since been removed
(see "Retired cross-backend physical replay" below), and its runner-cadence
result was `NOT_REPRODUCED`. For live runner debugging use
[`../GameBoyRunnerDebugging.md`](../GameBoyRunnerDebugging.md); for the product
acceptance rule — a confirmed perceptual report is fixed against the physical
observer, never closed as `NOT_REPRODUCED` — see the Acceptance Policy in
[`../../AGENTS.md`](../../AGENTS.md).

The Game Boy runner joint-load cadence guard is the in-process behavioral test
`GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate` in
`src/RetroSharp.GameBoy.Tests`. `GameBoyTestCpu` runs the freshly compiled
runner ROM and input timeline and classifies gameplay/audio cadence, camera
visibility, and OAM against the reviewed budgets. It owns repeatability and
safety evidence for runner cadence; physical playback owns the final experience
verdict. `GameBoyTestCpu` is the fast behavioral simulation and authored
background/OAM oracle, not a physical-frame clock.

## Reproducible command

The focused test writes a freshly compiled ROM and a compact normalized report
(one baseline plus 360 frame rows/digests) only when an output path is
requested. No tracked ROM is changed.

```bash
mkdir -p /tmp/rph62
RETROSHARP_RPH62_REPORT="/tmp/rph62/in-process.json" \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --no-restore \
  --filter 'FullyQualifiedName~GameBoyRunnerJointLoadCadenceTests.Shared_runner_joint_load_cadence_gate'
```

The test creates three independent in-process machines and requires
byte-identical normalized JSON before emitting its replay descriptor, which
carries the cadence budgets and expected-audio spans. It classifies the real
timeline as `NOT_REPRODUCED` or the first `gameplay-cadence-gap` /
`audio-service-gap` (or final `gameplay-tick-ratio`) with its frame. Owners are
emitted per failed dimension and are `null` for a match.

## Observation-layer canaries

The focused test keeps the same ROM and inputs while injecting only a captured
in-process observation:

- gameplay-only freeze: `gameplay-cadence-gap`;
- audio-only freeze: `audio-service-gap`;
- four-frame delayed camera visibility: `camera-visible-gap`;
- OAM corruption: `sprite-oam`.

The functional-runner boundary tests separately prove that resident and visible
requests at their limits pass and absence through the next physical frame fails.
These codes classify observer fidelity; they do not alter product acceptance
budgets.

## Retired cross-backend physical replay

An earlier investigation (RPH-6.2 / 6.2a / 6.3) drove a separate SameBoy
`GB_run_frame` cross-backend replay and a 21-phase A/jump/SFX bisect harness to
decide whether a runner cadence stutter reproduced under a second emulator. It
certified the runner timeline as `NOT_REPRODUCED` on both backends and never
became a product gate. Per the acceptance policy, independent-emulator and
multi-emulator differential runs are forensic diagnostics, not closeout gates,
so that Python harness was removed. The deterministic conclusion is preserved in
[`validation/gameboy/runner-joint-load-comparison.json`](../../validation/gameboy/runner-joint-load-comparison.json)
and
[`validation/gameboy/runner-joint-load-phase-matrix.json`](../../validation/gameboy/runner-joint-load-phase-matrix.json).
If a future runner cadence stutter is physically reported, reproduce it with the
in-process gate above or the SameBoy `gameboy` MCP, not a bespoke replay script.
