# Game Boy Runner Cadence Acceptance

Status: accepted evidence for issue #353.
Captured: 2026-07-15.

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
