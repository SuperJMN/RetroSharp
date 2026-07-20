# NES Runner Four-Screen Visual Parity Acceptance

Status: accepted evidence for issue #327.
Captured: 2026-07-13.

Policy update (2026-07-20): this multi-emulator differential harness is now an
optional diagnostic smoke check, not a mandated acceptance gate. The product
gate is in-process gameplay simulation. See the Acceptance Policy in `AGENTS.md`.
Run a single emulator for a visual sanity pass when useful, and do not block work
on FCEUmm, Nestopia, or RetroArch byte or raster parity.

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

- published Nes.Mcp `0.0.7.0` with its AprNes backend and the atomic
  `observe_execution`/`trace_ppu_register_writes` surface;
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

Before any emulator starts, the harness loads the compiler-generated
`samples/runner/bin/runner.nes.runtime-abi.json`, validates its v1 schema and
required semantic fields, and compares its bound ROM SHA-256 with the selected
`--rom`. `--runtime-abi <path>` selects another generated sidecar. Each atomic
AprNes frame coalesces the contract's runner-local, camera, lifecycle,
commit-descriptor, slot, collision, gameplay, and audio addresses into the
smallest contiguous probe spans. Requested camera Y resolves through
`camera.Y` rather than the adjacent `camera.TileColumn` scratch byte. Long
observations use one frame per call within the MCP's 600-frame limit and at most 2,000 PPU
events; focal single-frame traces allow 10,000. The accepted run made 391
atomic observations and returned all 4,072 observed events with matching declared counts and no
`ppuTraceTruncated` or result `truncated` flag.

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
`014a3495b31e9ca6be41ef6f22a676d33b1e3eeac9219ad43a723201d8c2c773`.
At the right checkpoint all three paths publish player `(403,273)`, requested
and visible camera X `307`, lifecycle `38/38/38/38/38`, last writes `32/8`,
and forbidden bank/directory/decode work `0/0/0`. At the return checkpoint they
publish player `(302,273)`, requested and visible camera X `238`, lifecycle
`47/47/47/47/47`, last writes `32/8`, and the same zero forbidden work.
The bounded frontend-call counts were AprNes/FCEUmm/Nestopia `266/267/266`
right and `82/82/82` left; the converged runtime state, rather than frontend
sampling phase, is the acceptance boundary.

Both checkpoints require byte-for-byte equality of the complete 4096-byte
four-screen nametable state and equality of every visible tile/palette cell.
The jump reaches Y `243`, returns to Y `273`, and lands with its feet at world Y
`304` over authored solid GID `104` cells in all three paths. Startup sampling
can expose `21` or `22` airborne observations; the gate permits at most that
one-frame phase skew while requiring every trajectory, endpoint, and collision
fact to remain identical.

For AprNes/FCEUmm/Nestopia respectively, each physical table has the same raw
identity. The exact SHA-256 of the concatenated 4,096-byte
`$2000/$2400/$2800/$2C00` state is
`610d0f7860f9e1ca8ce78f8648f5f98cc6d4378093a51d8d36b33727045e36a9`
initially, `8652ca284a755019627372d43b5c99b9edb37c06e4adc82cd62f2358ffd163ba`
at the right checkpoint, and
`cea03e6ca044b81c22c6d1049ed12d98c5affbd958e7aa724e740da40b717ac1`
after returning left. Per-table tile/attribute/raw hashes and all 4,096 bytes
remain in `summary.json`.

At the right checkpoint visible X `307` agrees with `PPUCTRL=$81` / selected
`$2400`, `t=$0546`, fine X `3`, and `w=false`; at return, visible X `238`
agrees with `PPUCTRL=$80` / `$2000`, `t=$015D`, fine X `6`, and `w=false`.
The recorded `v` values (`$0D48` and `$095F`) are the live beam addresses at
scanline 240, while `t/x/w` retain the published scroll latch state.

The transient acceptance retains all `47` column commits in every backend as
complete five-frame windows (`commit-2 .. commit+2`). AprNes contributes an
atomic palette-index SHA-256, compact pixel/tile diff, and exact FNV identity
for every visible background row outside the runner's 20x32 OAM rectangle.
FCEUmm and Nestopia contribute complete RGB frames mapped back to AprNes
palette indices from the initial/right/return checkpoints. The verifier
requires exact background-region digest equality, so an isolated tile,
attribute-palette, 6x8 rectangle, line, or column changes the result. The one
FCEUmm sample whose runtime state lands between AprNes samples (step 239) is
independently accepted only because its exact digest equals AprNes step 238;
no retained frame remains unmatched or corrupt.

Five deterministic AprNes replays cover the first column, immediately before
and across X=256 moving right, the right target, and X=256 moving left. Every
Nes.Mcp trace returns all `65` events without truncation, exactly `32` tile and
`8` attribute `PPUDATA` writes, correct `$2006` latch/address segments,
row-stride attribute targets, `PPUCTRL=$84 -> $80`, and the final display
PPUCTRL plus two-write PPUSCROLL restore. Tile and attribute targets are derived
from the focal runtime commit axis/target/start descriptor, and that target must
itself equal the edge implied by direction plus requested camera X (`+32` tiles
moving right, current edge moving left, modulo 64). The final control and scroll
bytes are derived from the requested X and visible Y runtime camera state; a
self-consistent descriptor and trace shifted to the wrong column or viewport
therefore fails. Each has one lifecycle
commit/release and zero bank, directory, or decode work inside the commit. All
`PPUDATA` writes are inside physical VBlank; the latest focal write finishes at
scanline `260`, dot `258`. Nes.Mcp's
`vblank` field is the clear-on-read `$2002` status latch, so the verifier uses
the authoritative scanline/dot interval plus rendering state after the runtime
has deliberately cleared that latch.

Replay is deterministic rather than a manual debugger loop: the harness saves
states before the right and left trajectories, reloads the relevant state,
runs to `frameOffset-1`, traces exactly one frame, then captures
`read_screen_region`, `dump_nametables`, `dump_tilemap`, `dump_oam`,
`read_ppu_state`, and one focal framebuffer. Hundreds of raw framebuffers are
never returned.

## Red evidence and runtime invariant

The base-`f1010b6` MCP trace first preserved for this slice was red: its final
attribute `PPUDATA` reached pre-render scanline `261`, dot `12`. A second long
observation exposed stale `FramePending` entry as early as scanline 248, which
is why the production correction waits for a fresh NMI edge rather than merely
shaving the write loop.

Before #329, the right checkpoint reached the same logical state in all
three emulators but FCEUmm disagreed with the AprNes/Nestopia physical
nametable consensus at 49 bytes and 37 visible tile/palette cells. It produced
repeated vertical `$98/$9A` runs instead of the authored staircase. Exact CPU
instrumentation proved that the resident payload and its PPU addresses were
correct, while the column commit consumed 5604 CPU cycles and wrote after
rendering resumed.

The NES lowering now enforces these concrete invariants:

- `Video.WaitVBlank()` treats `FramePending` only as a coalesced NMI signal. It
  consumes an already-stale saturated signal without publishing and suppresses
  the immediately following explicit `Camera.Apply()`. A path without a signal
  waits for a new NMI edge and rechecks `$2002`, so a late main loop cannot
  enter a commit or publish OAM partway through VBlank.
- Every column commit resets the shared `$2005/$2006` latch from `$2002`, uses
  `PPUCTRL=$84` only while streaming vertical PPUDATA segments, and reloads
  PPUADDR at physical row boundaries 30/60.
- Attribute writes restore horizontal PPUDATA increment with `PPUCTRL=$80` and
  use exact physical attribute addresses derived from the target column and
  row.
- The measured worst-case 32-tile plus 8-attribute routine is 2136 NTSC CPU
  cycles; its PPUDATA writes span cycles 437..2026, below the 2273-cycle VBlank
  budget. Bank selection, WorldPack directory lookup, and raw/RLE decode remain
  outside the commit.

The frozen complete-stage runtime probe is 8,989 fixed bytes after preserving
the vertical attribute stride, specializing prepared 2x2 column traversal, and
selecting the physical 30-row attribute table for columns; pinned R7 is 7,310
bytes. It still selects
the same `nes-mmc3-tvrom-v1` profile and retains the same ROM size, mapper
header, resident CHR budget, and bank ownership.

No emulator-specific core option, map edit, collision weakening, gameplay
speed change, or mapper/profile change is part of the fix.

## Closeout validation

The differential harness complements, rather than replaces, normal target
validation:

```bash
python3 -m unittest tools.nes.tests.test_runtime_abi tools.nes.tests.test_runner_visual_parity -q
tools/nes/verify_runner_power_on_ram.py
dotnet test RetroSharp.sln -m:1
tools/gameboy/generate_sample_roms.py
tools/gameboy/generate_sample_roms.py --dry-run
git diff --check
```

The emitted-code regression records `$2000/$2006/$2007`, resolved VRAM
addresses, payload bytes, and CPU cycles. It checks the exact 32 tile and 8
attribute identities and also proves that packed `WaitFrame` consumes a stale
coalesced signal without publishing, while the fresh-signal path rechecks
hardware VBlank before camera or OAM publication.
