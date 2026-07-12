# Large Worlds v1 final checkpoint

Status: **joint `LW-3.5` acceptance recorded on 2026-07-12.**

This checkpoint records the production artifacts and external emulator evidence
used to close #305 and #275. The assertions remain in the GB/NES full-stage,
runner, camera, reader, and small-ROM golden suites; this file preserves the
MCP-only observations that cannot run inside `dotnet test`.

## Production artifacts

Both ROMs come from `samples/runner/runner.retrosharp.json`, whose shared source
loads complete `assets/maps/stage1.tmj` (156x20 source cells, 312x40 hardware
tiles). Deliberate regeneration produced:

| Artifact | Profile | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `samples/runner/bin/runner.gb` | 128 KiB MBC1 | 131,072 | `ad3c6d2d83c449461451c5ffddb2c5c7cef77e228ca8ba6048ceb7ddafa23f52` |
| `samples/runner/bin/runner.nes` | MMC3, four-screen, 64 KiB PRG + 16 KiB CHR | 81,936 | `b351fd7bcac3db09513353141390c1998e141ecceadd197bfc872aae5289bc68` |

The exact canonical packs are GB 2,550 bytes and NES 2,762 bytes. Both contain
60 chunks, 770 stored visual bytes, and 312 stored collision bytes. Runner
acceptance retains target BGM and jump SFX; NES also retains both DPCM blocks.

Probe ROMs can be reproduced without modifying tracked artifacts:

```bash
RETROSHARP_FULL_STAGE1_TRAVERSAL_ROM=/tmp/retrosharp-gb-stage1-traversal.gb \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 \
  --filter 'FullyQualifiedName~Full_stage1_traversal_crosses_column_255'

RETROSHARP_FULL_STAGE1_COLLISION_ROM=/tmp/retrosharp-gb-stage1-collision.gb \
  dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 \
  --filter 'FullyQualifiedName~Full_stage1_runtime_reconstructs_every_visual'

RETROSHARP_NES_FULL_STAGE1_TRAVERSAL_ROM=/tmp/retrosharp-nes-stage1-traversal.nes \
RETROSHARP_NES_FULL_STAGE1_COLLISION_ROM=/tmp/retrosharp-nes-stage1-collision.nes \
  dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 \
  --filter 'FullyQualifiedName~Full_stage1_nes_runner_now_satisfies'
```

## SameBoy through GameboyMcp

- The collision probe settled to `C000 = 30 01 FF FF`: floor top Y=304 and
  unambiguous no-hit `0xFFFF`. The following 12 bytes also matched the dynamic
  wall/landing probes frozen by the test.
- A scripted traversal held Right to visible X=2336, then Left back to X=0,
  crossing hardware column 255 in both directions. At the far right the full
  160x144 shade histogram was `21119/1008/0/913`; returning to origin left all
  five lifecycle counters equal to 72, forbidden bank/decode/directory work at
  zero, and the last resident commit at 19 VRAM writes.
- The tracked MBC1 runner reported bank shadow `C1FA=02`; the last write to the
  MBC register at `$2000` was also `02`. Its packed audio tick moved `4A -> C2`
  over exactly 120 emulator frames (modulo delta 120). The tracked screen had
  all four DMG shades and six visible player OAM pieces.

## AprNes through NesMcp

- The full-map collision probe honestly selected mapper 0 and settled to zero
  page `30 01 FF FF` after 60 frames.
- The MMC3 traversal held Right to visible X=2336/Y=80, then Left back to
  X=0/Y=80. Final request/prepare/resident/commit/release counts were all 72;
  no axis remained pending; critical, R6, directory, and decode work in commit
  were zero; the final column phase wrote 32 tiles and 8 attributes.
- At the far right, all four physical nametables were populated and distinct:
  `$2000` `nonzero=83`, FNV `10946db1`; `$2400` `131`, `1cd3868c`;
  `$2800` `123`, `3f6b4762`; `$2C00` `96`, `dbe06d04`. The 256x240 palette
  histogram was `15:53612, 26:343, 32:34, 39:3901, 42:589, 54:2961`, with
  first/last row hashes `8c75701d`/`b99ade2b`.
- R6 shadow `$0324` was `00`, and the final mapper writes were `$8000=06`,
  `$8001=00`, proving hardware/shadow restoration. On the tracked runner,
  player OAM was visible and APU writes continued from frame 300 to 420
  (`$4000` count 41 -> 61, `$4015` 6 -> 8) with `$4010/$4012/$4013` DPCM
  configuration present. MMC3 emitted no page-$02 `$4014` DMA; its packed
  camera profile used sequential `$2003/$2004` OAM writes that AprNes rendered
  correctly.

## Closeout gates

`tools/gameboy/generate_sample_roms.py` completed, its `--dry-run` listed the
same deterministic commands, `dotnet test RetroSharp.sln -m:1` passed, and
`git diff --check` was clean. Automatic and forced MMC3 reports were identical
for the full runner, while collision-only and other fitting programs retained
honest mapper-0/ROM-only selection and the representative small-ROM byte
goldens stayed unchanged.
