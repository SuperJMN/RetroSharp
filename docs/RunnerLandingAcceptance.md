# Runner Landing Acceptance

Status: accepted evidence for issue #319.
Captured: 2026-07-12.

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

## Validation

The final branch state passed:

```text
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1
439 passed, 0 failed

dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1
296 passed, 0 failed

dotnet test RetroSharp.sln -m:1
1143 passed, 3 skipped, 0 failed

tools/gameboy/generate_sample_roms.py --dry-run
exit 0

git diff --check
exit 0
```
