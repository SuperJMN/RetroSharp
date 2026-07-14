# Packed Tiled Functional Acceptance

Status: accepted by RPH-3.5 / #339 on 2026-07-14.

This is the production-path acceptance boundary for the packed Tiled camera
samples. It binds the exact checked-in source, exact tracked cartridge bytes,
authored map and palette data, and every retained frame to the shared
functional runner introduced by #337. It does not substitute raw rows, a
synthetic camera loop, or a reduced map.

The regression motivating #339 reduced the 90-frame production measurements
from the pre-Large-Worlds baselines of approximately 88 Game Boy and 87 NES
gameplay ticks to 74 and 71. The matrix below exercises longer windows so
vertical, horizontal, diagonal, reverse, circular tilemap/nametable, chunk,
and bank-sensitive behavior cannot be hidden by startup or one favorable
edge.

## Exact production matrix

Every row recompiles the checked-in source, requires byte equality with the
tracked ROM, then executes those tracked bytes through the target test CPU.
Gameplay ratios and miss streaks are measured only after the 20-frame warm-up.
Camera latency is the maximum request-to-resident/request-to-visible delay in
physical frames.

| Scenario | SHA-256 | Window | Gameplay | Longest miss | Camera latency | Integrity failures |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `tiled-tall` GB | `9c8b6432c8231831f0a5018f2c8f128b66702245719b52886c665dcdc4317afa` | 360 | 360 / 360 (1.000) | 0 | 1 / 1 | 0 |
| `tiled-vscroll` GB | `3f44d4dffef12dd615955ee1160123a648484ed1225d1e060a213f48769a95d5` | 600 | 596 / 600 (0.993) | 1 | 1 / 1 | 0 |
| `tiled-vscroll` NES | `6d723a734c8192802e636e03dab659385a9514097fa31b7fd7af2f4ea8f2351b` | 600 | 600 / 600 (1.000) | 0 | 0 / 0 | 0 |
| `tiled-diagonal` GB | `de7a6766d98bb901221f34c2fff0f8c80d5b3f7ba9c9a808c936b309edccb431` | 360 | 349 / 360 (0.969) | 1 | 2 / 2 | 0 |
| `tiled-free-scroll` GB | `60948ac30f49cbd1f1814a552f74b1c7346612eeadf6095c308e1b9da8b0983c` | 360 | 349 / 360 (0.969) | 1 | 2 / 2 | 0 |
| `tiled-free-scroll` NES | `297675f54f334c0b328f7500fd209c2ea66b4487c8267b2e67502b8c78cedd4e` | 360 | 359 / 360 (0.997) | 1 | 0 / 0 | 0 |
| `deadzone-follow` GB | `3db43f7a1b23c8f84c4865ee332eec904d8a9bb033a1b887d74c6807b84dc8b3` | 400 | 380 / 400 (0.950) | 1 | 2 / 2 | 0 |
| `deadzone-follow` NES | `3e659cf9794f713060942063ecd3c353f5c338b2c31cf9f9230f24a30d7c1ef6` | 400 | 396 / 400 (0.990) | 4 | 0 / 0 | 0 |

The exact pre-Large-Worlds Game Boy dead-zone cartridge completes 400/400
source waits after the same warm-up. Its production packed gate therefore
keeps the common minimum 0.95 gameplay ratio and one-frame miss streak, plus
the same two-frame diagonal residency/visibility ceiling as the sprite-free
diagonal rows. The NES dead-zone row also requires 0.95 and allows only its
authored four-frame idle interval. These are baseline- and hardware-reviewed
limits, not values learned from the current build.

## What every row proves

- The exact production source recompiles byte-for-byte to the tracked ROM.
- The independent oracle derives visible tile identity and palette identity
  from the authored Tiled map. A single transient mismatch fails the row.
- Request, resident, committed, and visible sequence transitions are retained
  even when several transitions occur between sampled physical frames.
- Fixed checkpoints make forward, reverse, wrap, chunk-return, and bank-sensitive
  trajectory observable instead of allowing a stationary but coherent screen.
- Game Boy MBC1 entry-bank and shadow restoration, or the NES mapper state,
  remains correct throughout the observation window.
- Every VRAM/PPU and OAM write is cycle-positioned and must occur in a legal
  display phase.
- Directory access, bank selection, and decode counters remain zero inside
  Game Boy VBlank and NES NMI. No later frame can erase such a failure.

The Game Boy standard packed path uses three 64-byte visual cache slots (362
bytes total staging); the diagonal path uses six (554 bytes, the accepted v1
maximum). Diagonal source calls serialize edge preparation and alternate the
prepared axis, so a cold row and column never compete for an unsafe VBlank
budget. Cache replacement protects the current row/column working set and the
last opposite-axis group. Raw and RLE decode checkpoints defer while LCD is in
VBlank, and VBlank commits remain bounded to one 19-tile column or 21-tile row.
The RLE hot path uses an inline no-audio LY guard below scanline 128, retains a
full guard when audio wrap observation is active, records every completed
packet, and combines stored-byte validation with its decrement. This restores
the production dead-zone cadence without changing the canonical RLE pack.
When an edge commit owns the VBlank window, the backend retains the previous
OAM projection before attempting another wait; cooperative audio therefore
remains at one update per physical frame instead of losing a frame merely to
decide that OAM must be deferred.

The NES acceptance exposed a separate transient palette defect: palette RAM
aliases at `$3F10`, `$3F14`, `$3F18`, and `$3F1C` could overwrite the derived
Tiled universal background color. Startup now writes the derived background
color to all four sprite-side aliases. The free-scroll source also uses signed
camera coordinates with explicit map bounds, so the production run advances,
reverses, and returns without wrapping its source coordinates.

## External emulator checkpoints

The exact tracked `tiled-free-scroll` cartridges were also inspected outside
the in-process runner.

- GameboyMcp/SameBoy: after 20 frames the source camera at `$C000` was
  `(15,15)`; after 340 frames it was `(37,37)` with the direction still proving
  that the `(144,144)` boundary had been reached and reversed. Visible camera
  state at `$C14D` was also `(37,37)`. Request, prepare, resident, commit, and
  release counts at `$C152` were all 78. Bank/decode/directory work in commit
  or VBlank at `$C157`, `$C158`, `$C19C`, `$C1E9`, and `$C1EA` was zero.
  `SCX` and `SCY` were both `$25`, and the captured screen remained coherent.
- NesMcp/AprNes: after 20 frames the source camera was `(1,1)`; after 340
  frames source and visible camera were both `(33,33)`, again after the
  `(144,144)` reversal. `PPUCTRL=$80`, `PPUMASK=$1E`, rendering was enabled,
  all critical-work counters were zero, and the four physical nametables had
  distinct stable contents while the observed screen advanced continuously.

The emulator checkpoints corroborate the exact-ROM tests; the authored map
oracle and cycle-positioned transient integrity checks remain the acceptance
authority.

## Focused validation

```bash
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter FullyQualifiedName~PackedTiledFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter FullyQualifiedName~PackedTiledFunctionalAcceptanceTests
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
tools/gameboy/generate_sample_roms.py --dry-run
```

Full closeout remains:

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
```
