# Packed Tiled Functional Acceptance

Status: accepted by RPH-3.5 / #339 and extended with the horizontal #335
canaries on 2026-07-14.

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
Gameplay ratios and miss streaks are measured only after warm-up. The standard
warm-up is 20 frames. Horizontal canaries use an explicit bottom-framing phase
before X movement and use a 40-frame measurement warm-up.
Camera latency is the maximum request-to-resident/request-to-visible delay in
physical frames.

| Scenario | SHA-256 | Window | Gameplay | Longest miss | Camera latency | Integrity failures |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| `tiled-tall` GB | `9c8b6432c8231831f0a5018f2c8f128b66702245719b52886c665dcdc4317afa` | 360 | 360 / 360 (1.000) | 0 | 1 / 1 | 0 |
| `tiled-hscroll-short` GB | `e51ecdb8e969c8989d920a9ec5cfb6fc604176d3c5547355dde25a332275da27` | 1024 | 1024 / 1024 (1.000) | 0 | 1 / 1 | 0 |
| `tiled-hscroll-short` NES | `e560c78bccda7963d3728ad720645471ea1a3e003065182c1e14f114d3060929` | 1024 | 1018 / 1024 (0.994) | 1 | 0 / 2 | 0 |
| `tiled-hscroll-full` GB | `d5baa0effd76548832a2e7c4fbaab17ca378fe1b3436aada18dd2f00984ebebb` | 2584 | 2584 / 2584 (1.000) | 0 | 1 / 1 | 0 |
| `tiled-hscroll-full` NES | `eebe9be28ed3e5283d24774ac9328a07a83bed3f1fab3a59742296bac0a16d62` | 2584 | 2569 / 2584 (0.994) | 1 | 0 / 2 | 0 |
| `tiled-hscroll-offset` GB | `2df705902c94ae8c6f00c8868efd11cf83ba436e17f029898ff1b098e3f876f9` | 420 | 420 / 420 (1.000) | 0 | 1 / 1 | 0 |
| `tiled-hscroll-offset` NES | `aaf7fa3a9db6de50c55e0f9683ac651c01d347fc5de2833d24dd4de3ed740752` | 420 | 419 / 420 (0.998) | 1 | 0 / 2 | 0 |
| `tiled-vscroll` GB | `3f44d4dffef12dd615955ee1160123a648484ed1225d1e060a213f48769a95d5` | 600 | 596 / 600 (0.993) | 1 | 1 / 1 | 0 |
| `tiled-vscroll` NES | `bca383ca88379215da054b35793b322cd6ccf92783501ef527de7239f5098992` | 600 | 600 / 600 (1.000) | 0 | 0 / 0 | 0 |
| `tiled-diagonal` GB | `de7a6766d98bb901221f34c2fff0f8c80d5b3f7ba9c9a808c936b309edccb431` | 360 | 349 / 360 (0.969) | 1 | 2 / 2 | 0 |
| `tiled-free-scroll` GB | `60948ac30f49cbd1f1814a552f74b1c7346612eeadf6095c308e1b9da8b0983c` | 360 | 349 / 360 (0.969) | 1 | 2 / 2 | 0 |
| `tiled-free-scroll` NES | `f8d842cae94e2a43eff2719a3a4af8da0cca8ff078e83212c2b1d3e25a59dffd` | 360 | 359 / 360 (0.997) | 1 | 0 / 0 | 0 |
| `deadzone-follow` GB | `3db43f7a1b23c8f84c4865ee332eec904d8a9bb033a1b887d74c6807b84dc8b3` | 400 | 380 / 400 (0.950) | 1 | 2 / 2 | 0 |
| `deadzone-follow` NES | `90cf002e477aa5cd6c61bd370a56d274aef7a98a780fe9ba72dcdb69d749859d` | 400 | 396 / 400 (0.990) | 4 | 0 / 0 | 0 |

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

The #335 horizontal canaries isolate packed column streaming from gameplay.
Their local tileset deliberately removes collision object groups, and the
functional tests assert that every imported world flag is empty. Both fixtures
preserve their complete horizontal content, move the bottom 15 authored rows
into the 30-hardware-row camera window, and keep five empty staging rows below
it. `Camera.VerticalScrollMax()` therefore presents the dense bottom of the
scene at visible Y 96 on Game Boy and logical Y 0 on NES. The NES target treats
that configured window as vertically fixed even though the backing map is 40
rows tall, applies the same render-only 8 px bottom-overscan inset as other
screen-tall fixed views, and leaves the empty staging strip below it. X stays
zero for 64 ticks before horizontal acceptance begins. The short crop crosses
the 8-bit camera boundary and circular nametable boundary before reversing at visible X 768;
the full row traverses every chunk and bank window of all 156 `stage1` columns
before reversing at visible X 2240.
Adding those rows fixed NES fast prepared-column subcell/row-stride state,
vertical attribute-table traversal, the requested camera high byte at X=256,
and fast coordinate multiplication for power-of-two chunk-column counts. The
NES authored-palette oracle keeps world coordinates after physical
nametable wrap. All four exact ROMs retain zero tile/palette mismatches and
zero unsafe video writes across the complete 1024/2584-frame traversal and
return windows. Camera visibility remains bounded to two frames, the gameplay
miss streak remains at most one frame, and the steady-state ratio remains at
least 0.994. Game Boy output remains unchanged; the NES ROM hashes change only
because the target-owned safe-area inset now keys off the effective camera
window rather than the complete backing-map height.

The `tiled-hscroll-offset` rung keeps the same full collision-free fixture but
uses a 40-row camera window, settling at visible Y 176 on Game Boy and Y 80 on
NES before horizontal movement. NES packed columns now use the current coarse
camera row for both their authored source and physical nametable destination,
and commit only the 30 hardware rows that can be visible. This prevents the
incoming floor from being projected ten rows too high while keeping the
column within eight touched attribute blocks and the accepted two-frame
visibility budget. The NES checkpoint is physical frame 104, after the
sample's declared 64-gameplay-tick framing delay; frame 40 is still inside
cartridge preload/framing and is not a valid bottom-aligned checkpoint.

The timing gate retains the complete background-raster transaction, not only
`$2007`: `$2000`, `$2005`, `$2006`, and `$2007` writes must all occur during
physical VBlank. The pre-render line is deliberately excluded, leaving its
complete 341 PPU dots as margin before visible scanline 0. On the red ROM, the
first AprNes column began at scanline 245 dot 259, its final attribute reached
scanline 260 dot 108, and the final control/scroll restore landed at visible
scanline 0 dots 31/54/159. The in-process regression consequently reported 211
unsafe raster writes instead of accepting the coherent VRAM bytes alone.

The packed column writer now chooses a physical nametable segment once and
uses X as its tight tile counter, rather than doing absolute-RAM bookkeeping
for every tile. Tiles and all eight attributes remain one coherent commit. On
the regenerated exact ROM, AprNes retained all 66 selected PPU events without
truncation: the final attribute completed at scanline 258 dot 151 and the final
`$2000/$2005/$2005` restore completed at scanline 260 dots 74/97/202. No PPU
write enters pre-render or the visible raster.

Successful packed horizontal requests now publish the boundary pixel
immediately. A rightward crossing still uses the resident half of the 64-column
circular nametable; its prepared lookahead column first becomes visible one
pixel later, after the following VBlank has committed it. A leftward crossing
re-enters the retained trailing half while its prepared column is refreshed.
The exact-ROM regression retains the first value from every `$2005/$2005`
restore pair and requires successive X values to advance by one modulo 256
across more than 32 frames. AprNes likewise observed X values
`1,2,3,4,5,6,7,8,9,10,11` on physical frames 106 through 116; the first
streamed request occurred on frame 113 and committed on frame 114 without a
held or skipped scroll value.

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

The bottom-aligned full canary was additionally traversed and reversed from
its exact tracked cartridges on 2026-07-14:

- GameboyMcp/SameBoy: visible state moved from `(X=2232,Y=96)` at physical
  frame 2320 to `(X=2032,Y=96)` at frame 2520 after the X 2240 reversal. A
  sampled lower strip retained three DMG shades and 16 distinct row hashes;
  the critical bank/decode counters remained zero.
- NesMcp/AprNes: visible state moved from `(X=2230,Y=0)` at physical frame
  2370 to `(X=2032,Y=0)` at frame 2570 after the X 2240 reversal. Rendering
  remained enabled with `PPUCTRL=$80` and `PPUMASK=$1E`; a sampled lower strip
  retained three palette indexes and eight distinct repeated-pattern row
  hashes. The critical bank/directory/decode counters remained zero.

The regenerated NES canary additionally verifies the target-owned safe-area
framing when the 30-row camera window uses the same 40-row backing map. At
physical frame 150, NesMcp/AprNes reported `PPUCTRL=$80`, `PPUMASK=$1E`, and
`t=$0025`, whose coarse Y is one tile row. The complete two-row floor occupied
screen Y `216..231`; the wrapped Y `232..239` strip was a single uniform sky
palette index. Cropping the bottom eight overscan lines therefore retains the
complete floor instead of removing its lower half.

The regenerated exact tracked runner ROM for the nonzero-Y horizontal fix has
SHA-256
`86abfd051017217e50c494e32f89bdb2a457ac55df5fe7bea4f5d9071021f4c2`.
NesMcp routed it to AprNes as mapper 4. After 500 idle frames and 270 RIGHT
frames, requested camera state was `(311,38)` and visible camera state was
`(311,80)`. Request, prepare, resident, commit, and release were all 38; the
last commit wrote the 30 visible tile rows and eight attribute bytes, with zero
bank, directory, or decode work in commit. The four physical nametable dump
placed the authored `$B5/$B8` floor only in rows 8/9 of the lower nametables,
which are world rows 38/39 at the fixed 80-pixel Y origin; the traversed upper
tables had no copy ten rows early. The captured framebuffer consequently had
one floor at the bottom of the viewport, with `PPUCTRL=$81`, `PPUMASK=$1E`,
and rendering enabled.

These external snapshots prove real-emulator traversal and direction change;
the longer in-process windows remain stricter because they compare every
retained transient frame against authored tile and palette provenance.

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
