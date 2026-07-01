# NES APU Trace Format

Status: implemented v1 internal ROM representation for VGM/VGZ-sourced NES BGM.

RetroSharp uses VGM/VGZ as the user-facing faithful input format for NES music. The VGM importer reads 2A03 register writes, quantizes 44100 Hz waits into 60 Hz frame buckets, and passes the ordered per-frame writes to the NES asset compiler. The raw VGM is not embedded in the cartridge.

## Scope

The v1 NES path supports frame-driven writes for:

- Pulse 1 registers `$4000-$4003`
- Pulse 2 registers `$4004-$4007`
- Triangle registers `$4008-$400B`
- Noise registers `$400C-$400F`
- Channel enable/status `$4015`
- Frame counter `$4017`

DPCM registers `$4010-$4013`, DPCM sample data blocks, and expansion audio are out of scope for v1. The importer skips DPCM data needed by the provided runner VGM when the four-channel path does not consume it, and unsupported chip commands fail explicitly.

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

`registerOffset` is the low byte relative to `$4000`, for example `0x00` for `$4000` and `0x15` for `$4015`. Identical frame bodies are stored once in the pool and referenced by multiple order entries. At ROM emission time, relative offsets are turned into absolute PRG pointers so the 6502 runtime can read them directly.

## Runtime

`Audio.Init()` enables pulse, triangle, and noise channels through `$4015`, initializes `$4017`, and resets music state. `Music.Play(theme)` points the runtime at the selected asset's order stream and loop point. `Audio.Update()` should be called once per frame; it waits the compiled frame delay, decodes the next group body, and writes the contained values to `$4000-$4017`. Zero-wait order entries continue in the same `Audio.Update()` call so dense bursts do not gain accidental blank frames.
