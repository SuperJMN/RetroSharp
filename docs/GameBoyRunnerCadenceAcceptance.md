# Game Boy Runner Cadence Acceptance

Status: accepted evidence for issues #353 and #406.
Captured: 2026-07-18.

This checkpoint restores gameplay smoothness in the exact shared Game Boy
runner after Large Worlds without replacing complete `stage1.tmj`, falling
back to legacy rows, or changing the public SDK, WorldPack bytes, MBC1 profile,
audio path, or staged camera contracts. The pre-Large-Worlds calibration point
remains `95f166886713ff3b88bc1e17c03ef0ffe93d649a`.

## Regression and final gate

The deterministic runner test settles the production camera at visible Y 176,
holds Right for 120 physical DMG frames, and observes the source wait marker,
audio service, player word position, collision lookup coordinates, and maximum
consecutive missed ticks.

| Build | Gameplay ticks | Longest miss | Horizontal progress |
| --- | ---: | ---: | ---: |
| `74ec827` before #353 | 37 / 120 | 4 | 45 px |
| #353 final | 115 / 120 | 1 | 142 px |

The checked-in budget is at least 114/120 gameplay ticks, at most one missed
frame in a row, input-visible movement by the second physical frame, at least
142 pixels of source-model progress, and 119-121 audio updates. These limits
retain headroom for phase alignment while rejecting the sustained slowdown.

## B-speed packed-camera closeout

Issue #406 adds the corresponding sustained run gate over 250 physical DMG
frames. After 500 warm-up frames and 300 grounded idle frames, the test holds
Right+B on the exact shared runner source and complete `stage1.tmj`. It requires
at least 238 gameplay ticks, no consecutive miss above one, 249-251 audio
ticks, the authored first-stair stop at X=430/Y=273, and source/visible camera
X=334 after crossing 255/256 and the relevant packed chunk/bank boundaries.

The isolated measurements distinguish the two required target changes:

| Runtime shape | Gameplay ticks | Audio ticks | Longest miss |
| --- | ---: | ---: | ---: |
| Column plane before early commit admission | 209 / 250 | 209 / 250 | 1 |
| Early commit admission with the original chunk decode path | 209 / 250 | 250 / 250 | 1 |
| Column plane plus early commit admission | 250 / 250 | 250 / 250 | 0 |

The column plane is immutable target-private derived data, not a WorldPack
format change. When a packed camera world is wider than 255 hardware columns
and 19-255 hardware rows, the
complete column-major hardware-tile plane is at most 16 KiB, and it fits after
the final WorldPack segment in the same ROM window, column staging copies its
19 tiles with one bank selection instead of revisiting chunk decode and visual
expansion. Other layouts retain the existing generic preparation path. For the
runner, the unchanged 2,568-byte pack is followed at bank 2 `$4A08` by the
12,480-byte plane; the 128 KiB profile and occupied banks 0-5 are unchanged.

A syntactically valid pending slot id (0 or 1) for a column in a word-wide map
now enters or waits for an admissible VBlank before reading and validating its
resident metadata. The id is re-read after admission; `NoSlot` still returns
without waiting. Narrow-map columns and every row retain the prior
validate-then-admit order. This moves
the runner's LY145 commit admission ahead of roughly one thousand cycles of
validation/counter work without widening the LY148 guard. Wrong tags,
malformed data, rows, diagonals, reversals, and the one-edge-per-VBlank
lifecycle retain their existing behavior.

The final in-process run observes 4,390 packed collision queries, 95 memo hits,
3 collision decodes, and exact request/prepare/resident/commit/release counts
of 41/41/41/41/41. Maximum request-to-visible delay is zero observed frames;
bank/decode/directory work in commit or VBlank, unsafe VRAM/OAM writes, and
bank-shadow mismatches are all zero.
The source camera crosses the 256-pixel chunk boundary as 255 to 256; the
staged visible camera publishes 254 to 256 after the two pixel-safe movement
substeps, and both finish at 334. Every one of the 250 measured frames compares
all touched background cells and the complete player metasprite/OAM payload to
the authored runner assets, while the trace also proves use and restoration of
the bank containing the derived column plane.
The regenerated tracked ROM used by the gate has SHA-256
`10c60b0df52754da88a50579b547e463108d9ea3d09fdd0c230ad752e552f12d`;
the test compiles the production manifest/source, asserts byte identity with
that file, and executes the tracked bytes.

An independent SameBoy run through GameboyMcp loaded that exact tracked ROM,
settled for the same 500+300 frames, and then held Right+B for 250 frames. The
gameplay and packed-audio counters both advanced exactly 250 ticks; the player
finished at X=430/Y=273, source and visible camera X both reached 334, and all
five lifecycle counters advanced by 41. Collision decode count advanced by 3
and the memo counter by 99 modulo 256. Forbidden commit/VBlank counters stayed
zero, program and visible bank shadows remained 1/1, and `LCDC=$97` retained
the background and sprites. The six player OAM pieces remained visible and the
nonblank 160x144 shade histogram was 19,387 / 1,773 / 96 / 1,784.

## Collision cost and safety

The fixed-bank collision reader now keeps two exact-tagged decoded RLE chunks
with deterministic round-robin replacement. Raw collision chunks read only the
requested ID directly from ROM. Immediate repeated cells use a one-entry cache;
the complete 2x2-metatile `stage1` geometry additionally uses a bounded
64-entry direct-mapped memo table. The measured complete-stage costs are:

| Query | DMG cycles | Checked budget |
| --- | ---: | ---: |
| Cold RLE chunk | 55,272 | 70,224 (one frame) |
| Resident decoded chunk, new cell | 2,120 | 2,300 |
| Memo-table hit | 776 | 900 |
| Immediate repeated cell | 236 | 300 |

Tags are published only after a complete successful RLE decode. Raw/RLE,
one-byte/two-byte IDs, clipped chunks, non-2x2 metatiles, two-slot retention,
third-chunk replacement, explicit decode invalidation, malformed header,
directory and RLE payloads, full `stage1` collision parity, bank restoration,
and Y=304 landing all retain executable coverage. The existing visual staging,
VBlank commit, camera lifecycle, and audio service paths are unchanged.

## SameBoy through GameboyMcp

GameboyMcp loaded the exact tracked 128 KiB DMG cartridge
`samples/runner/bin/runner.gb` with SHA-256
`9980dcfe65f600a674af39a26f4c96870f0bbef6e0b8abce6d063fcf6671951a`.
After 400 settlement frames, player state was X=72/Y=273, visible camera Y was
176, and `LCDC=$97`. Holding Right for exactly 120 additional physical frames
produced:

- gameplay counter delta: 115;
- packed-audio counter delta: 120;
- player X: 72 to 215, or 143 pixels;
- RLE collision decode count: 1 to 2;
- forbidden bank/decode work in commit and directory work in VBlank: all zero;
- final `SCX=$76`, `SCY=$B0`, LCD and sprites enabled;
- nonblank 160x144 shade histogram: 20,426 / 1,262 / 0 / 1,352.

The independent emulator result matches the in-process cadence and progress
budget while exercising the exact regenerated cartridge rather than a
test-only source or reduced map.

## Validation

The closeout matrix is:

```bash
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1
dotnet test RetroSharp.sln -m:1
tools/gameboy/generate_sample_roms.py
tools/gameboy/generate_sample_roms.py --dry-run
git diff --check
```

Only affected Game Boy cartridges change; regenerated NES artifacts remain
byte-identical.
