# Phase-Banked Frame Sample

Sample Layer: `target-acceptance`

This NES-only pair is the stable canary for **phase-based R6 bank placement**. Both builds
share `src/scene.rs`, so their hot frame loop is the same source and the same emitted unit.
They differ only in one-shot cold work:

| Build | Project | Cold init | Selected profile |
| --- | --- | --- | --- |
| Candidate | `phase-banked-frame.retrosharp.json` | `PrepareLevel(...)` bulk | `nes-mmc3-tvrom-codebank-v1` |
| Control | `phase-banked-frame-control.retrosharp.json` | none | `nes-mmc3-tvrom-v1` (fixed execution) |

Build both from the repository root:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/phase-banked-frame/bin/phase-banked-frame.nes \
  samples/phase-banked-frame/phase-banked-frame.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/phase-banked-frame/bin/phase-banked-frame-control.nes \
  samples/phase-banked-frame/phase-banked-frame-control.retrosharp.json
```

## Why the candidate forces the code-banked profile

`PrepareLevel(...)` is one-shot level preparation that runs before the first frame. It is
bulky on purpose: it pushes the movable program past what the constant 16 KiB fixed PRG
region can hold, so the normal final-link ladder falls through the exact mapper-0 link and
the fixed-execution MMC3 link and selects `nes-mmc3-tvrom-codebank-v1`. The source names no
bank and no board.

## What this sample discriminates

The cold `program:main:init` phase is large enough that a raw emission-order fill would land
the hot `program:main:frame` phase across a bank cut: the cold phase plus the hot phase do
not fit together in one 8 KiB R6 bank. Phase placement therefore has to give the hot phase a
fresh bank so steady-state frames never pay a bank transition or a fixed veneer. The control
build proves the frame loop itself is unchanged, and supplies the steady-state frame-work
baseline the candidate's upper-bound budget is derived from.

The frame loop leaves through `Level.GoalX`, which the automated observer never reaches, so
`CompleteLevel(...)` is real cold `program:main:tail` work rather than dead syntax.

`NesPhaseBankPlacementCanaryTests` owns the acceptance evidence: profile selection, hot-phase
bank containment, the discriminating straddle margin, one logical tick per physical frame,
zero unsafe PPU/OAM writes, and the bounded active-tick budget. This sample is a placement
and link canary; it pins no ROM bytes and no exact cycle counts.
