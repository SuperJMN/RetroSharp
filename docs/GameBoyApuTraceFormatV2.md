# Game Boy APU Music: Faithful + Compact Representation Study

Status: **implemented**. This started as a design study and is now the shipped `retrosharp.gbapu`
v2 path. Supersedes the analysis notes in `GameBoyApuTraceFormat.md` ("Current Problems" /
"Better Options"). `retrosharp.gbapu.v1` JSON is still accepted as input for back-compat.

## What Shipped (validated)

Every change below is covered by unit tests and validated end-to-end by running compiled ROMs in
an emulator (PyBoy for bulk per-frame APU snapshot diffs, SameBoy for spot checks):

1. **Loop auto-detection** (`GameBoyApuLoopDetector`): autocorrelation of per-frame write
   signatures trims a capture to intro + one loop body. On the reference track the 120 s capture
   collapses to the real 51.43 s loop automatically. `--no-auto-loop` and `--loop-cycle` override.
2. **Accurate frame timing**: the compiler maps cycles to frames with the true DMG frame period
   (70224 cycles ≈ 59.7275 Hz) instead of a nominal 60 Hz. The GBS timer fields are read and the
   driver replay rate is stored as `replayHz` metadata.
3. **Binary source** (`GameBoyApuTraceBinary`, magic `GBAP`): the default `.gbapu` output. On the
   reference track this is **43 KB vs 959 KB** for the equivalent JSON (~22x). `gbapu-dump` prints
   the `FFxx=yy` lines for inspection; `--emit-json` still writes the JSON debug view. A binary and
   a JSON source compile to byte-identical ROMs.
4. **Group-pool ROM encoding** (v2, stream marker `0x02`): per-frame group bodies are
   deduplicated into a pool referenced by an order stream of `(bodyOffset, wait)` entries. On the
   reference one-loop trace this is **10907 B vs 13397 B** flat (−18.6%), and produces
   **bit-identical APU writes** to the old flat player (verified: identical 3300-frame APU
   snapshot hash across the loop boundary).

### Encoding decision: group-pool over channel-mask

The original study estimated a per-channel change-mask at −23%. Measuring against the *real*
track exposed that drivers write the same register multiple times in one frame (e.g. `NR30`
wave gate toggles in 306 frames of the loop), which a change-mask cannot represent without
faithful sub-grouping. With faithful sub-grouping the change-mask drops to only −11%, while
**group-pool keeps the exact v1 group-body bytes (so the APU writes are bit-identical and exactly
validatable) and still reaches −18.6%** by exploiting musical repetition (417 unique bodies of
1467 frames). Group-pool also reuses the v1 inner decode loop unchanged, so it was the lower-risk
*and* higher-gain choice. The standalone channel-mask was therefore dropped as dominated.

## Goal

Represent Game Boy music **faithfully** (analogous to a WAV: it reproduces what the original
driver makes the hardware do, with no musical re-interpretation) and **compactly** (cheap to
store in a 32 KiB ROM and cheap to diff in the repo), while staying **cheap to play** on SM83
inside the per-frame audio budget.

The `.uge` (hUGETracker) path stays the authoring format for *new* music. This study is only
about faithfully preserving *existing* GB music (GBS rips), where note/instrument transcription
is lossy and the available `.uge` corpus is small and low quality.

## Measurement Baseline

All numbers below are measured from the actual target track, captured with
`gbsplay -o iodumper` (DMG clock 4 194 304 Hz):

- Source: `Super Mario Land 2` (`DMG-L6J.gbs`), subsong 1, author Kazumi Totaka.
- Replay: the GBS header timer is disabled (`TAC=00`), so the driver runs **on VBlank,
  ~59.73 Hz**, not on a hardware timer. Frame quantisation is therefore close to exact for
  this song.
- 30 s capture: 6 553 APU writes; **53.3 %** are redundant non-trigger rewrites of the same
  value and are safely dropped. After dropping, ~1.7 meaningful writes/frame (max 44).

### Where the bytes go (30 s capture, then one detected loop)

| Artifact | 30 s | One loop (51.4 s) |
| --- | --- | --- |
| `.gbapu.json` source (current) | ~510 KB | ~880 KB |
| Compiled flat stream (current v1) | 7 968 B | 13 458 B |
| Channel-mask encoding (proposed) | 6 105 B (-23 %) | ~10–11 KB |
| Group-pool dedup (proposed) | — | ~11 KB (pool 6.8 KB) |
| Entropy floor (DEFLATE, reference) | 1 426 B | 2 425 B |

Two facts dominate every conclusion:

1. **JSON is the "muy pesado" problem, not the music.** The source is ~50–65× the compiled
   payload (17 KB/s of JSON vs 0.27 KB/s of packed bytes). The cartridge payload was already
   small; the checked-in *source* is what is huge.
2. **The single biggest size lever is the loop, not the byte encoding.** The track loops every
   **3 086 frames ≈ 51.43 s** (found by autocorrelation, 96.6 % self-similarity, confirmed by
   the 2× harmonic). Storing one loop instead of an arbitrary `--seconds` capture bounds the
   asset; squeezing the per-frame bytes only changes the constant factor.

## The Core Mistake the Current Design Makes

It conflates three artifacts into one verbose JSON file:

1. **Capture / interchange** — the raw `gbsplay` dump. Lossless superset, debug only.
2. **Source asset (checked in)** — currently verbose JSON. This is the heavy one.
3. **ROM payload (compiled)** — the packed byte stream. Already compact.

JSON is the wrong choice for (2): register data is not human-authored, it is machine-captured,
and the only thing humans do with it is inspect/diff — which a 20-line dumper tool does better
than 880 KB of `{ "deltaCycles": …, "address": "FFxx", "value": "yy" }`.

## Fidelity Analysis

The v1 doc lists "sub-frame timing is lost" as a fidelity risk. The measurements reframe it:

- **Replay rate, not sub-frame scheduling, is the real fidelity knob.** A GBS driver's audible
  output is defined at its *replay tick* (VBlank, or the GBS timer rate). Writes inside a tick
  are setup for that tick; their exact intra-tick cycle offset is inaudible. The correct fix is
  to **sample at the driver's true replay tick** and store that rate explicitly — not to build
  a cycle-accurate sub-frame APU scheduler. v1's hardcoded `framesPerSecond: 60` is a latent
  bug for timer-driven songs (it ignores `TimerModulo`/`TimerControl`); it happens to be ~exact
  for this VBlank-driven song.
- **A register trace cannot loop bit-exactly when the driver has free-running modulators.**
  Comparing frame *f* with frame *f+period*, ~8 % of frames differ, concentrated in
  `FF1C` (software volume envelope on the wave channel, 64 mismatches), the channel-3/4
  frequency registers, and vibrato on `FF13`. These are LFO/envelope phases that never realign
  to the musical bar. Consequence: any automatically cut loop has a small modulator-phase
  discontinuity at the seam. It is usually inaudible but must be chosen deliberately, and the
  author must be able to override it. This is inherent to *any* faithful register-log format,
  including a "specialised WAV"; it is the price of not modelling the driver.
- **The redundant-write optimiser is sound on this corpus.** Dropping same-value non-trigger
  rewrites removed 53 % of writes with no state change; triggers (`NR14/19/1E/23` bit 7) and
  `NR52` are preserved. The hammered `FF24` (NR50, written every frame with one constant value)
  collapses to a single write.

## Recommended Representation: `retrosharp.gbapu.v2`

A fixed-rate APU register-delta stream — the WAV analog — split cleanly into the three
artifacts, with a **binary** source and an optional JSON debug view.

### 1. Canonical model

```
stream  := header loopInfo tick*
tick    := one driver replay step at `replayHz`
```

- `replayHz` is stored explicitly (derived from the GBS timer fields, default VBlank 59.73 Hz).
  This removes the hardcoded-60 fidelity bug and makes timer-driven songs faithful.
- Each `tick` carries the register changes that happened during that step, in write order. No
  notes, no instruments, no patterns: it is a register log, like PCM is a sample log.

### 2. Source artifact: binary `.gbapu`, not JSON

Replace the 880 KB JSON with a small binary container:

```
"GBAP" 0x02 flags                      magic + version + flags
replayHz (u16, Hz*256 fixed point)     true replay rate
loopStartTick (u32) loopLenTick (u32)  detected or author-set loop
tickCount (u32)
<encoded tick stream>                  see §3
metadata block (optional, length-prefixed: title/author/copyright/source/generator)
```

The source then equals the ROM payload size (~13 KB for this loop, not 880 KB). Diffing is done
with a `gbapu-dump` tool that prints the same `FFxx=yy` lines on demand. JSON survives only as
`--emit-json` for debugging. This change alone eliminates the "muy pesado" complaint.

### 3. Tick encoding: per-channel change-mask (canonical) over offset/value pairs

v1 stores `count` then `(lowOffset, value)` pairs. Measured improvement from a change-mask:

- One header byte selects active channels (ch1, ch2, ch3, ch4, NR5x, wave-block).
- Per active channel, one small mask byte selects changed registers; then the changed values.
- Wave RAM stays a single 16-byte block command (it is rare — one write here).

This is self-delimiting (no per-write offset byte) and measured **23 % smaller** than v1 flat,
while staying byte-aligned and streamable. It is the recommended canonical tick encoding. The
v1 `(offset,value)` form is an acceptable simpler fallback if mask iteration code size is a
concern; the runtime cost difference is negligible against the frame budget (a music update is
a few hundred cycles out of 70 224/frame).

### 4. Optional second tier for ROM: group pool + order stream

For songs that must coexist with large graphics assets, or longer than ~one loop in 32 KiB, add
a tracker-shaped (but *derived*, not authored) second tier:

- **Group pool**: deduplicated tick payloads. Measured 454 unique of 1 490 ticks (30.5 %),
  pool 6.8 KB.
- **Order stream**: `(groupIndex, wait)` with run-length for repeats; the loop point is an
  index into this stream. The order sequence is itself highly repetitive (DEFLATEs 2 980→1 115),
  so phrase-level repetition is recoverable cheaply.

Runtime stays O(group size) per tick plus one indirection — far cheaper than DEFLATE, which is
rejected because it needs a RAM window and per-frame cost SM83 cannot spare. This tier is
optional: enable it only when the flat stream does not fit.

### 5. Automatic loop detection

The exporter should detect the loop by autocorrelating per-frame write signatures (the method
that found 3 086 frames here) instead of requiring a manual `--loop-cycle` and a guessed
`--seconds`. It should:

- store intro + exactly one loop body + the loop tick,
- pick the seam at a tick that maximises retriggered/silent channels (minimising the
  modulator-phase discontinuity described above),
- expose `--loop-tick`/`--loop-search` overrides for when the heuristic is wrong.

## Size / Fidelity / Cost Summary

| Lever | Size effect (this track) | Fidelity | Runtime cost |
| --- | --- | --- | --- |
| Binary source instead of JSON | 880 KB → ~13 KB source | none | none |
| **Loop detection** | unbounded/manual → one 51 s loop (~13 KB) | small seam phase glitch | none |
| Change-mask tick encoding | -23 % vs flat | none | ~ neutral |
| Group-pool + order tier | ~13 KB → ~7–9 KB ROM | none | +1 indirection/tick |
| Store true `replayHz` | negligible | fixes timer-driven songs | none |
| DEFLATE whole stream | → ~2.4 KB (floor) | none | rejected: RAM + CPU |

## Delivered Plan (all phases shipped)

1. **Loop auto-detection** — `GameBoyApuLoopDetector` (autocorrelation), `--no-auto-loop` /
   `--loop-cycle` override. ✅
2. **True replay rate + accurate timing** — GBS timer fields → `replayHz` metadata; compiler maps
   cycles to frames with the 70224-cycle DMG period. ✅
3. **Binary `.gbapu`** — default output, `gbapu-dump`, `--emit-json`. ✅
4. **Group-pool v2 ROM encoding** — chosen over the change-mask (see decision above); rewritten
   SM83 player validated bit-identical to the prior player. ✅
5. **Group-pool/order tier** — folded into step 4 (it *is* the dedup tier), always on because real
   music repeats; a non-repetitive trace costs at most ~1 byte/frame more than flat. ✅

## Explicit Non-Goals

- **No note/instrument transcription.** That is the lossy, song-specific path the removed
  GBS→UGE work was weak at. A faithful format must not guess musical intent.
- **No cross-target portability for this format.** It is DMG-APU specific by design and belongs
  in the Game Boy target layer. The portable `retrosharp.music.v1` envelope keeps wrapping it;
  NES needs its own faithful path, not a translation of DMG register writes.
- **No cycle-accurate sub-frame scheduler.** Sample at the driver's replay tick instead; that is
  where fidelity actually lives, at a fraction of the cost. Timer-driven songs whose replay rate
  exceeds VBlank still collapse multiple driver ticks into one frame; that needs a timer-interrupt
  player and remains future work (the `replayHz` metadata is recorded for it).

## Integration Surface (unchanged)

The language/SDK contract stays identical, so v2 is transparent to game code:

```csharp
Music.Asset(stage_theme, "music/stage.gbapu");   // binary now; .json still accepted
Music.Play(stage_theme);
loop { Video.WaitVBlank(); Audio.Update(); }
```

`Audio.Update()` keeps its per-tick `LDH (C),A` replay loop; v2 adds one indirection to fetch the
pooled group body, then walks the same command bytes.
