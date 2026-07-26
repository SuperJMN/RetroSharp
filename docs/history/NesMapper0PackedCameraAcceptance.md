# NES Mapper-0 Packed-Camera NMI Acceptance

Status: accepted evidence for issue #331.
Captured: 2026-07-13.

This checkpoint uses the autonomous
`samples/tiled-vscroll/vscroll.rs` production build with automatic cartridge
profile selection. It does not force MMC3, disable NMI, use the raw camera
path, or change the map or source movement loop.

## Link and vector shape

The production report selects `nes-mapper-0-current`; the iNES header remains
`02 01 09 00` (32 KiB PRG, 8 KiB CHR, mapper 0, four-screen), and the canonical
WorldPack remains linked as `worldpack:default`. The generated 40,976-byte ROM
has SHA-256
`c6083084dd6f5f5c2409237b0c4b37f42ded802379767f768949ecf757ee7a08`.

Its vectors are:

| Vector | Target | Meaning |
| --- | ---: | --- |
| NMI | `$9A8C` | Fixed-PRG, mapper-neutral frame-signal handler |
| Reset | `$8000` | Existing mapper-0 startup entry |
| IRQ | `$8000` | Existing mapper-0 simple interrupt behavior |

The NMI handler preserves `A`, increments the 16-bit hardware-frame counter,
sets the saturated pending-frame byte, restores `A`, and returns with `RTI`.
It performs no mapper, WorldPack, camera, PPU, input, gameplay, or audio work.

## AprNes / NesMcp runtime evidence

NesMcp loaded the production ROM through AprNes as mapper 0. After 20 startup
frames, the PPU already held `PPUCTRL=$80`, `PPUMASK=$1E`, NMI enabled, and
active background/sprite rendering. The hardware-frame counter was 5 because
NMI is enabled only after the initial PPU/bootstrap work.

AprNes then ran 90 additional hardware frames without input:

| Measurement | After startup | After 90 more frames | Delta |
| --- | ---: | ---: | ---: |
| AprNes frame | 20 | 110 | 90 |
| NMI hardware-frame counter | 5 | 95 | 90 |
| Source `cameraY` (`$0000`) | 5 | 95 | 90 |
| Requested/logical camera Y (`$00EA`) | 4 | 94 | 90 |
| Visible camera Y (`$03CD`) | 4 | 94 | 90 |
| Visible camera tile row (`$03F0`) | 0 | 11 | 11 later tile edges |

At frame 110, AprNes still reported `PPUCTRL=$80`, `PPUMASK=$1E`, NMI enabled,
rendering enabled, and rendering active. The exact 90-count NMI delta plus the
source/requested/visible camera progress rules out repeated bootstrap execution
and a merely surviving but inert main loop. Tile row 11 covers later
packed-camera edge progression well beyond startup.

The sample's complete 60-row world fits the 64x60 four-screen backing surface,
so these tile edges publish without a WorldPack streaming request; its
request/prepare/resident/commit/release counters correctly remain zero. The
production report and regression still prove that the compiler selected the
packed-camera/WorldPack build before honestly retaining mapper 0.

## Automated preservation checks

`Automatic_mapper0_tiled_vscroll_vectors_nmi_to_the_fixed_frame_signal_handler`
builds the real sample through automatic selection and freezes its mapper,
WorldPack, header, vector, reset, IRQ, and handler semantics.

`Mapper0_without_a_packed_camera_retains_simple_vectors_and_its_tracked_golden`
keeps all three vectors at `$8000` for `samples/static-drawing/drawing.rs` and
compares the production output byte-for-byte with the tracked ROM (SHA-256
`3115f01722a5b8ec08ffb44c9d0be340de3e8a7cebd045f2f13b30156a452aac`).
The existing forced/automatic MMC3, four-screen streaming, bank restoration,
audio/DPCM cadence, and bounded-commit tests remain the acceptance for the
banked path.
