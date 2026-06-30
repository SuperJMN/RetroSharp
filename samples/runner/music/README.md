# Music Assets

`free_06_delight.uge` is from Tronimal's Free Game Boy Music Pack and is licensed `CC-BY 2025`.

`delight.gbapu` is a faithful Game Boy APU register trace **derived from** `free_06_delight.uge`
(captured from its playback and stored in the `retrosharp.gbapu` v2 format), and remains
`CC-BY 2025` (Tronimal); the `.uge` is kept as the authoring source.

`runner.gb.vgz` and `runner.nes.vgz` are the current runner BGM inputs. The shared source declares
`music/runner.vgz`; the compiler resolves the `.gb.vgz` or `.nes.vgz` variant for the selected
target and repacks the VGM register writes into the target's compact on-ROM stream.

`sml2_track1.gbapu` is the previous Game Boy-only runner trace. Confirm its provenance before
redistributing packaged artifacts that include it. See `docs/GameBoyApuTraceFormat.md` and
`docs/NesApuTraceFormat.md` for the target repack formats.
