# NES Runner Cadence Acceptance

Status: accepted evidence for issue #321.
Captured: 2026-07-12.

This checkpoint uses the shared `samples/runner/runner.retrosharp.json` project,
the complete 312x40-hardware-cell `stage1.tmj`, the automatically selected
`nes-mmc3-tvrom-v1` profile, and NesMcp `auto` routing to AprNes. It does not
change runner movement constants, collision policy, `WorldPack` v1, mapper, or
public SDK APIs.

## Runtime shape

- Complete-stage collision planes use pinned O(1) descriptors. Raw planes read
  directly; RLE planes use two fully tagged 64-byte slots and an exact-cell
  memo. Decode publication remains deterministic and bounds/malformed results
  retain the existing ABI.
- The canonical one-byte-ID visual cache uses six 64-byte slots: the two next
  horizontal chunk columns across all three viewport-height chunk rows. With
  two 64-byte collision slots and two 41-byte immutable edge slots, staging is
  exactly 594 bytes, the existing v1 limit. No whole-level image or unbounded
  cache is present.
- The fixed NMI handler only increments the 16-bit hardware-frame counter and
  sets a saturated pending-frame byte. It does not touch R6/R7, directories, decoders, PPU data,
  audio, or gameplay. `Video.WaitVBlank()` consumes that signal and performs the
  already-bounded camera commit in mainline code.
- Packed input takes paired controller snapshots and retries a mismatch up to
  three times. This prevents DPCM DMA from producing phantom direction bits
  without changing the portable `Input.Poll()` contract.

## Measured steady corridor

The generated 81,936-byte ROM had SHA-256
`ee8133361f5f9398405bfb4deec77fb402ac91dcd8ca2dd044639ef508cc415d`.
After 500 emulator frames at rest, AprNes held RIGHT for exactly 120 frames:

| Measurement | Before | After | Delta |
| --- | ---: | ---: | ---: |
| AprNes hardware frames | 500 | 620 | 120 |
| NMI hardware-frame counter | `$01CB` | `$0243` | 120 |
| Gameplay/input ticks, modulo 256 | `$CB` | `$43` | 120 |
| Audio ticks, modulo 256 | `$CB` | `$43` | 120 |
| Player world X | 72 | 222 | 150 |
| Collision decodes | 1 | 2 | 1 bounded crossing |
| Visual decodes | 6 | 6 | 0 |
| Requested / visible camera X | 126 | 126 | matched |

The last edge lifecycle counts were request/prepare/resident/commit/release
`15/15/15/15/15`. Request and residency shared frame `$023F`; commit completed
at `$0240`. The last commit wrote 32 tile bytes and 8 attributes. R6,
directory, and decoder-in-commit counters were all zero, as was the critical
section marker.

## Boundary and interaction evidence

- The focused CPU harness measures complete-stage first collision decode below
  5,000 cycles, resident chunk hits below 375 cycles, exact repeated cells
  below 120 cycles, cached visual hits below 400 cycles, and a complete cached
  32-cell column preparation below 13,000 cycles.
- RIGHT plus a 12-frame A hold over the same 120-frame window produced the same
  120 gameplay and audio ticks, moved X by 150, returned to world Y=273, and
  exercised landing, ceiling, wall, and SFX paths while collision crossings
  remained bounded.
- A subsequent 120-frame LEFT traversal retained 120 gameplay/audio ticks and
  exercised reversal plus bidirectional edge reuse. Focused tests retain the
  diagonal, row-phase, wrong-tag, malformed, raw/RLE, bank-boundary, and
  `255 <-> 256` contracts.
- AprNes reported PPUCTRL `$80`, PPUMASK `$18`, visible OAM, four-screen
  nametable data, R6/R7 shadows `0/1`, no `$A000` write, and no `$4014` OAM DMA.
  APU writes covered `$4000-$4017`, including DPCM `$4010/$4012/$4013/$4015`.

The generated cadence manifest is opt-in:

```bash
RETROSHARP_NES_RUNNER_CADENCE_ROM=/tmp/retrosharp-nes-runner-cadence.nes \
  dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 \
  --filter FullyQualifiedName~Nes_full_stage1_runner_cadence_harness
```

It records scenario expectations, RAM measurement addresses, and fixed runtime
symbols beside the ROM.
