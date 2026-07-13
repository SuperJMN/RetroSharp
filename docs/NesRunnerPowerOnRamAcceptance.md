# NES Runner Power-On RAM Acceptance

Status: accepted evidence for issue #326.
Captured: 2026-07-13.

The production NES runtime does not assume any CPU RAM power-on value. This
checkpoint covers the exact shared `samples/runner/bin/runner.nes` path, the
complete `stage1.tmj` WorldPack, the automatically selected
`nes-mmc3-tvrom-v1` profile, BGM/SFX/DPCM, and the packed four-screen camera.

## Initialization contract

Reset clears only target-runtime-owned memory:

- `$0326..$03FF`: WorldPack and packed-camera control, cache tags, counters,
  slot metadata, pending descriptors, critical-section state, scratch, and
  visible-publication state. MMC3 R6/R7 shadows at `$0324..$0325` are outside
  the range and retain their bootstrap values.
- `$0400..$0651`: the exact 594-byte staging layout (six visual slots, two
  collision slots, and two immutable edge slots). `$0652` is not touched.
- `SelectedSlot` is assigned the explicit `$FF` `NoSlot` sentinel after the
  zero initialization. Both edge-slot states are therefore `Empty`, all work
  counters/pending axes are zero, and no power-on byte can masquerade as a
  lifecycle state or valid edge tag.

No whole-RAM clear is emitted. OAM, mapper state, audio state, game-owned
variables, canonical WorldPack ROM bytes, and memory after the staging layout
remain under their existing owners.

## FCEUmm matrix

The checked-in harness drives RetroArch through its network control and remote
RetroPad interfaces, using the same isolated RetroArch session as the visual
parity harness:

```bash
tools/nes/verify_runner_power_on_ram.py
```

It requires FCEUmm `(SVN) 3a84a6f`, fills all 2 KiB of CPU RAM, resets the real
tracked runner, settles 500 frames, then advances exactly 120 paused frontend
frames while holding only RIGHT. The shared session uses a complete disposable
RetroArch config with saves disabled and all output paths isolated. The tested
core SHA-256 was
`2896e04ccf43ba7a46458c10214fdce906f83833e5aa69b2d1ae185d595216fb`.
After the #327 VBlank/column-commit correction, the regenerated runner SHA-256
is `014a3495b31e9ca6be41ef6f22a676d33b1e3eeac9219ad43a723201d8c2c773`.

| CPU RAM pattern | Hardware / gameplay / audio deltas | Player X | Requested / visible X | Lifecycle deltas | First byte after staging |
| --- | ---: | ---: | ---: | ---: | ---: |
| `$00` | `120/119/119` | `72 -> 220` | `124 / 124` | `15/15/15/15/15` | `$00` |
| `$FF` | `120/119/119` | `72 -> 220` | `124 / 124` | `15/15/15/15/15` | `$FF` |
| deterministic nonzero | `120/119/119` | `72 -> 220` | `124 / 124` | `15/15/15/15/15` | `$0D` |

All three runs produced exactly 120 hardware frames and 119 gameplay/audio
ticks after the fresh-NMI `WaitVBlank` synchronization; player displacement
was 148 pixels in every fill model. Every run kept
player Y at the authored floor `273`; ended with released/empty edge slots;
performed zero bank, directory, or decode work inside commit; and kept the last
commit at 32 tile plus 8 attribute writes. The nonzero pattern is
address-dependent and reproducible; its first byte outside staging is `$0D`,
which proves the initializer stops at `$0651`.

## AprNes through NesMcp

The original #326 capture used runner SHA-256
`92284841df6411130a88c1fd85f6c4bda723c11013701506248eb86efb0aac49`.
NesMcp `auto` routed that ROM to AprNes as mapper 4. After 500 idle frames and
120 RIGHT frames it produced the equivalent initialization evidence:

- player X `72 -> 222`, player Y `273 -> 273`;
- requested/visible camera X `126 / 126`;
- request/prepare/resident/commit/release `15/15/15/15/15`;
- hardware/gameplay/audio deltas `120/120/120` and collision decodes `1 -> 2`;
- R6/R7 shadows `0/1`, zero forbidden commit work, zero critical-section state,
  and a final 32-tile/8-attribute commit;
- PPUCTRL `$80`, PPUMASK `$18`, four-screen rendering enabled.

The current tracked ROM's AprNes behavior, including right/left crossings,
jump/collision, exact physical nametables, and lifecycle convergence with
FCEUmm and Nestopia, is recorded in
[`NesRunnerVisualParityAcceptance.md`](NesRunnerVisualParityAcceptance.md).

## Validation

The focused emitted-boot regression runs the real runner compilation against
`$00`, `$FF`, and `$A5` initial RAM models and proves the exact clear loops and
guard bytes. Normal closeout also requires the complete NES tests, the full
solution, tracked ROM regeneration plus a clean dry-run, and
`git diff --check`.
