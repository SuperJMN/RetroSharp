# NES Runner Four-Screen Visual Parity Acceptance

Status: accepted evidence for issue #327.
Captured: 2026-07-13.

This checkpoint exercises the exact tracked `samples/runner/bin/runner.nes`
through the complete `stage1.tmj` WorldPack. It is a differential runtime
acceptance, not a screenshot heuristic: it compares CPU lifecycle state, all
four physical nametables, exact visible tile IDs and attribute palette
selectors, authored collision cells, PPU state, and framebuffers.

## One harness, three independent paths

Run from the repository root:

```bash
python3 tools/nes/verify_runner_visual_parity.py
```

The single harness uses:

- NesMcp with its AprNes backend;
- RetroArch with FCEUmm `(SVN) 3a84a6f` and a complete disposable config;
- Nestopia through a minimal libretro software-framebuffer session. By default
  the harness downloads the current Linux x86_64 nightly core into its
  temporary artifact directory; `--nestopia-core` pins a local core instead.

This capture used Nestopia core SHA-256
`17b4fd0a57899ad679626f93d379ba5bb713de1402bb84a6d12743274a7d3cfe`
from archive SHA-256
`40c65173f599976840faaafe792756ad61fd3010f722678368ff3f881e7f681d`.

Artifacts are written under `artifacts/nes-runner-visual-parity/`. The JSON
summary records the ROM/core hashes, input-frame bounds, logical and visible
camera coordinates, hardware/lifecycle counters, forbidden commit work,
framebuffers, PPU registers/writer counts, palettes, physical nametable hashes
and bytes, exact visible `(tile ID, palette selector)` cells, and collision
evidence.

The RetroArch path copies `/app/etc/retroarch.cfg` into the temporary run,
replaces conflicting keys, uses `--config` rather than `--appendconfig`, sets
`config_save_on_exit=false`, and redirects core options, states, saves,
screenshots, logs, playlists, remaps, and other outputs. `ConfigIntegrityGuard`
hashes both the user's persistent `retroarch.cfg` and `FCEUmm.opt` before the
first emulator starts and verifies them again even when the run fails.

## Input and comparison sequence

After 500 idle frames, every path performs the same behavioral sequence:

1. Hold Right until the published camera crosses X=256 and reaches X>300.
2. Release input and reproduce RetroArch's measured save-state continuation so
   all paths resume on the same effective hardware frame.
3. Hold A for six input frames, verify an airborne trajectory, and wait for the
   player to land on the authored floor.
4. Hold Left until the published camera crosses back through X=256 and reaches
   X<240.

Paused frontend APIs may expose the first input poll at different points inside
the current emulated frame, so raw frontend call counts are recorded and
bounded rather than treated as gameplay state. Acceptance requires the exact
published player/camera/lifecycle state to converge at both directional
checkpoints.

The accepted tracked ROM has SHA-256
`0b9ab90d814dbacc11fb30511237ac2b69994d5074b93b7886ce8964a861ef06`.
At the right checkpoint all three paths publish player `(403,273)`, requested
and visible camera X `307`, lifecycle `38/38/38/38/38`, last writes `32/8`,
and forbidden bank/directory/decode work `0/0/0`. At the return checkpoint they
publish player `(302,273)`, requested and visible camera X `238`, lifecycle
`47/47/47/47/47`, last writes `32/8`, and the same zero forbidden work.
The bounded frontend-call counts were AprNes/FCEUmm/Nestopia `268/265/269`
right and `85/82/82` left; the converged runtime state, rather than frontend
sampling phase, is the acceptance boundary.

Both checkpoints require byte-for-byte equality of the complete 4096-byte
four-screen nametable state and equality of every visible tile/palette cell.
The jump reaches Y `243` for 21 airborne frames, returns to Y `273`, and lands
with its feet at world Y `304` over authored solid GID `104` cells in all three
paths.

## Red evidence and runtime invariant

Before the fix, the right checkpoint reached the same logical state in all
three emulators but FCEUmm disagreed with the AprNes/Nestopia physical
nametable consensus at 49 bytes and 37 visible tile/palette cells. It produced
repeated vertical `$98/$9A` runs instead of the authored staircase. Exact CPU
instrumentation proved that the resident payload and its PPU addresses were
correct, while the column commit consumed 5604 CPU cycles and wrote after
rendering resumed.

The NES lowering now enforces these concrete invariants:

- `Video.WaitVBlank()` treats `FramePending` only as a coalesced NMI signal and
  rechecks `$2002` before entering the PPU commit, so a late main loop waits for
  a fresh hardware VBlank instead of consuming an old signal during rendering.
- Every column commit resets the shared `$2005/$2006` latch from `$2002`, uses
  `PPUCTRL=$84` only while streaming vertical PPUDATA segments, and reloads
  PPUADDR at physical row boundaries 30/60.
- Attribute writes restore horizontal PPUDATA increment with `PPUCTRL=$80` and
  use exact physical attribute addresses derived from the target column and
  row.
- The measured worst-case 32-tile plus 8-attribute routine is 2172 NTSC CPU
  cycles; its PPUDATA writes span cycles 437..2058, below the 2273-cycle VBlank
  budget. Bank selection, WorldPack directory lookup, and raw/RLE decode remain
  outside the commit.

The frozen complete-stage runtime probe grows from 8,935 to 8,999 fixed bytes
for these two corrections while pinned R7 remains 6,204 bytes. It still selects
the same `nes-mmc3-tvrom-v1` profile and retains the same ROM size, mapper
header, resident CHR budget, and bank ownership.

No emulator-specific core option, map edit, collision weakening, gameplay
speed change, or mapper/profile change is part of the fix.

## Closeout validation

The differential harness complements, rather than replaces, normal target
validation:

```bash
python3 -m unittest tools.nes.tests.test_runner_visual_parity -q
tools/nes/verify_runner_power_on_ram.py
dotnet test RetroSharp.sln -m:1
tools/gameboy/generate_sample_roms.py
tools/gameboy/generate_sample_roms.py --dry-run
git diff --check
```

The emitted-code regression records `$2000/$2006/$2007`, resolved VRAM
addresses, payload bytes, and CPU cycles. It checks the exact 32 tile and 8
attribute identities and also proves that packed `WaitFrame` rechecks hardware
VBlank after observing the NMI signal.
