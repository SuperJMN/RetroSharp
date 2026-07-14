# Runner Landing Acceptance

Status: accepted evidence for issue #319 plus the one-way-platform revision.
Captured: 2026-07-12; revised 2026-07-15.

This checkpoint covers the exact shared `samples/runner/runner.retrosharp.json`
project with the complete `samples/runner/assets/maps/stage1.tmj`. It supersedes
the landing behavior and cartridge hashes recorded before RPH-1; it does not
replace the banking, streaming, or traversal evidence from the Large Worlds
checkpoints.

## Correction and red evidence

The runner now declares all derived world-Y collision values as `i16`:
`footWorldY`, `movementFootWorldY`, `headProbeY`, and `wallProbeY`. The change
is source-only gameplay policy; collision ABIs, WorldPack v1, cartridge profile
selection, gravity, audio, and the shared project/map inputs are unchanged.

The behavioral regressions were run against the real shared runner before the
source correction:

- Idle landing failed after the visible Game Boy camera reached Y=176: on the
  ninth observed frame Mario was at world Y=293 with foot Y=324 instead of
  remaining at world Y=273 / foot Y=304.
- The scripted jump regression failed because the A press never left the floor
  after the broken fall/reset cycle had lost stable grounded state.

Both tests pass after the correction and live in
`GameBoyRunnerLandingTests`.

## Exact generated cartridges

`tools/gameboy/generate_sample_roms.py` regenerated both tracked artifacts.
`tools/gameboy/generate_sample_roms.py --dry-run` then completed with exit code
0, and fresh direct CLI builds compared byte-for-byte equal with the tracked
files.

| Target | Tracked artifact | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Game Boy | `samples/runner/bin/runner.gb` | 131072 | `fe746d40ae161d7b8c6be0565112350c31a97faaf612dade3c9e817a9dc9cbfe` |
| NES | `samples/runner/bin/runner.nes` | 81936 | `4175d7eb50021c8ec561cc00cb0f7696a55604f6abd343f711a3ff81e14f9376` |

## SameBoy through GameboyMcp

GameboyMcp loaded the exact tracked `runner.gb` in SameBoy as DMG title
`RETROSHARPGB`. After startup/camera settlement, a per-frame OAM timeline
captured 300 consecutive frames. Sprite 0 had the single Y value `113` for all
300 frames. With visible camera Y=176 and the Game Boy OAM Y offset of 16, this
is player world Y `113 - 16 + 176 = 273`, hence foot Y `273 + 31 = 304`.

A second per-frame timeline held A for 20 frames and observed 400 frames total:

- sprite Y range: `83..113`;
- returns to floor Y=113: exactly one, at timeline frame 112;
- stable frames after that return: 288;
- final sprite Y: 113.

## AprNes through NesMcp

NesMcp `auto` loaded the exact tracked `runner.nes` through AprNes as mapper 4
with four 16 KiB PRG units and two 8 KiB CHR units. At emulator frame 500, RAM
`$0002..$0003` contained `11 01`, proving player world Y=273; sprite 0 used NES
OAM Y=192 on the camera-max floor presentation. A per-frame OAM timeline then
captured 300 consecutive frames with the single sprite Y value `192`.

A second per-frame timeline held A for 40 emulator frames and observed 400
frames total:

- sprite Y range: `141..192`;
- returns to floor Y=192: exactly one, at timeline frame 110;
- stable frames after that return: 290;
- final sprite Y: 192.

## One-way platform revision (2026-07-14)

The existing green ledges in complete `stage1` now use tile property
`retrosharpCollision=platform`. They expand to 56 `Platform` hardware cells;
the solid count remains 788. No language, SDK, intrinsic, ABI, WorldPack-format,
or cartridge-profile change was required.

Shared runner source requests `Landable = Solid | Platform` only from the
non-rising feet/support query. Walls and ceilings still request `Solid`. The
feet-relative landing window is `footWorldY - 3` with height 9. Source captures
the previous foot before applying gravity and only snaps when that previous foot
was at or above the returned top and the current foot is at or below it;
this rejects the `-1` no-hit value, accepts a downward step that crosses a top
still overlapping the landing window, and ignores any platform top observed
while the actor is underneath it. Gravity does not move a
grounded actor. A grounded actor whose query no longer finds support clears
`grounded`, so walking off the ledge falls to the floor. The original
`Jump.BoostTicks = 12` variable-height jump already reaches player Y=222 (foot
Y=253), clearing the authored ledge top at Y=272 with 19 pixels of margin.

Executable tests compile the real manifest/map and prove on both target CPU
models that a short jump below the ledge returns to the floor without snapping,
while a full held jump crosses Y=272 from below and then remains landed at
player Y=241; a separate walk-off probe leaves Y=241 and settles on floor
Y=273. Held-jump regressions additionally pin the original Y=222 apex and read
the player's word-backed Y directly on every gameplay tick, proving that it
never crosses floor Y=273 or reaches the fall-reset path. Game Boy additionally
proves packed camera collision at source
columns 132 and 264, covering both an intermediate byte overflow and a nonzero
source-column high byte.

The content revision adds one collision profile. Exact production measurements
are 60 chunks, 770 stored visual bytes, 326 stored collision bytes, and a
largest combined stored chunk of 49 bytes: 2,568 serialized bytes on Game Boy
and 2,780 on NES.

`tools/gameboy/generate_sample_roms.py` regenerated both tracked artifacts;
the dry run then reported no pending generation work.

| Target | Tracked artifact | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Game Boy | `samples/runner/bin/runner.gb` | 131072 | `87574c9c3f60a180687f0e5ce9c113a914ee9127ab8ca8777ab4121e6caa80da` |
| NES | `samples/runner/bin/runner.nes` | 81936 | `6d836b041af897d431fd3d5123aabcefa5b241216f00deb833e364fe6031604c` |

The 2026-07-12 hashes and SameBoy/AprNes timelines above remain historical
evidence for the earlier world-Y correction; this section superseded those
hashes at the one-way-platform revision.

## Run-speed staircase regression (2026-07-15)

The first staircase begins at world X=448; with an 18-pixel-wide player, X=430
is the last valid position. Before this correction, B-speed ticks could execute
two one-pixel substeps while the shared source camera X advanced between them,
but `Camera.AabbTiles(...)` still projected through the runtime camera position
from the start of the tick. For input phases that reached the wall on the first
substep, the second probe repeated the same world column and admitted X=431.
The following support query then observed the side of the stair, cleared
`grounded`, and let Mario fall through the floor before the reset path recovered
him. The same source defect reproduced on Game Boy and NES.

`CameraState.ApplyMotion(...)` now captures camera X once per movement tick and
passes that projection origin to both horizontal substeps. Accepted movement
still advances player and source camera state one pixel at a time, followed by
the existing single end-of-tick camera position update. Real-ROM CPU tests for
both targets vary the delay between Right and B across nine input phases and
assert on every gameplay tick that `playerX + 18 <= 448`, player Y does not
cross the floor position 273, and `grounded` remains set. Every phase ends at
X=430.

Both tracked cartridges were regenerated, and fresh direct CLI builds compare
byte-for-byte equal with them. These hashes supersede the one-way-platform
revision hashes for the current tracked runner.

| Target | Tracked artifact | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Game Boy | `samples/runner/bin/runner.gb` | 131072 | `d4f482cd48663debc123e92b3dfea46f48183fc9dc7606582c1377d9606f8dd1` |
| NES | `samples/runner/bin/runner.nes` | 81936 | `89608ebdc2ef98a99449acd944f36b709012376e756bf412c1a3d24072a66177` |

## Validation

The final branch state passed:

```text
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1
469 passed, 0 failed

dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1
336 passed, 0 failed

dotnet test RetroSharp.sln -m:1
1239 passed, 3 skipped, 0 failed

python3 -m unittest tools.nes.tests.test_runner_visual_parity
45 passed, 0 failed

tools/gameboy/generate_sample_roms.py --dry-run
exit 0

git diff --check
exit 0
```
