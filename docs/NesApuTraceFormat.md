# NES APU Trace Format

Status: implemented v1 internal ROM representation for VGM/VGZ-sourced NES BGM.

RetroSharp uses VGM/VGZ as the user-facing faithful input format for NES music. The VGM importer reads 2A03 register writes, quantizes 44100 Hz waits into 60 Hz frame buckets, and passes the ordered per-frame writes to the NES asset compiler. The raw VGM is not embedded in the cartridge.

## Scope

The v1 NES path supports frame-driven writes for:

- Pulse 1 registers `$4000-$4003`
- Pulse 2 registers `$4004-$4007`
- Triangle registers `$4008-$400B`
- Noise registers `$400C-$400F`
- DMC registers `$4010-$4013`
- Channel enable/status `$4015`
- Frame counter `$4017`

NES DPCM data blocks are imported from VGM stream data (`0x07`) and NES APU RAM writes (`0xC2`). The ROM builder places those sample bytes in PRG ROM at the CPU addresses used by `$4012/$4013`; samples must fit in `$C000-$FFF9` so the interrupt vectors remain available. Expansion audio remains out of scope for v1, and unsupported chip commands fail explicitly.

## On-ROM Stream

The compiled NES asset uses a compact group-pool stream analogous to the Game Boy GBAPU repack:

```text
[0]    marker 0x03
[1..2] orderStartOffset (u16, from data start)
[3..4] loopOrderOffset  (u16, from data start)
[5..]  pool: concatenated group bodies
order: { bodyOffsetLo, bodyOffsetHi, waitAfter } per frame
end:   { 0, 0, 0 } loop sentinel
```

Each group body is:

```text
commandCount
{ registerOffset, value } * commandCount
```

`registerOffset` is the low byte relative to `$4000`, for example `0x00` for `$4000`, `0x10` for `$4010`, and `0x15` for `$4015`. Identical frame bodies are stored once in the pool and referenced by multiple order entries. At ROM emission time, relative offsets are turned into absolute PRG pointers so the 6502 runtime can read them directly. DPCM sample data is not part of the group-pool stream; it is emitted separately at its target PRG address.

## Runtime

`Audio.Init()` enables pulse, triangle, and noise channels through `$4015`, initializes `$4017`, and resets music state. DMC playback starts when the imported stream writes `$4015` with bit 4 set after configuring `$4010/$4012/$4013`. `Music.Play(theme)` points the runtime at the selected asset's order stream and loop point. `Audio.Update()` should be called once per frame; it waits the compiled frame delay, decodes the next group body, and writes the contained values to `$4000-$4017`. Zero-wait order entries continue in the same `Audio.Update()` call so dense bursts do not gain accidental blank frames.

## Sound effects (SFX)

Sound effects use a separate, simpler flat per-frame format. Pulse 1 (`$4000-$4003`) is the dedicated NES SFX channel, so the compiler keeps only those registers and drops captured global/other-channel writes (`$4010`, `$4015`, `$4017`, and every non-pulse-1 channel). There is no header, pool, or order stream:

```text
frame body * N        // one body per source frame, in order
0xFF                  // end-of-effect marker
```

Each frame body is `commandCount` followed by `{ registerOffset, value } * commandCount`, exactly like a BGM group body. A gap frame (no pulse 1 change) is encoded as an empty body (`commandCount = 0`). A few empty tail-hold frames are appended so the note keeps ringing before the effect ends. The `0xFF` byte sits where a body's `commandCount` would be (a real pulse 1 frame never has more than four writes), so it is an unambiguous stop marker.

This data is position-independent — the runtime walks it from a start label — so it is emitted after the DPCM samples and does not consume the DPCM window.

### SFX runtime

`Sfx.Play(name)` only arms the engine: it sets a zero-page cursor to the effect's first frame body and sets the `SfxActive` flag. It never touches the BGM order/body/tick state. After the BGM update, `Audio.Update()` ticks the SFX: it plays the current frame body on pulse 1 through the shared APU body writer, advances the cursor to the next frame body, and stops when it reads the `0xFF` marker. While `SfxActive` is set, the BGM engine suppresses its own pulse 1 writes (register offsets `$00-$03`) so the effect owns pulse 1 cleanly, but it still *shadows* those intended values to RAM (`$0313-$0316`). When the effect ends, the shadowed `$4001` (sweep) is restored to hardware so the BGM's melody is not left carrying the effect's sweep residue; `$4000/$4002/$4003` are re-established by the BGM on its next note. The other APU channels keep playing the BGM throughout.
