# Game Boy APU Trace Format

Status: `retrosharp.gbapu` v2 (implemented). The companion design study and rationale live in
`GameBoyApuTraceFormatV2.md`.

`*.gbapu` (binary, magic `GBAP`) is RetroSharp's Game Boy register-trace music format. The legacy
`*.gbapu.json` (`retrosharp.gbapu.v1`) is still accepted as input. It is a target-specific asset
format for the Game Boy BGM runtime, not a portable music format and not an editable tracker
module.

The format exists to preserve the audible output of an existing Game Boy music driver without
embedding that driver in the generated ROM. It records timed writes to the DMG APU registers and
Wave RAM, then the Game Boy compiler repacks those writes into a compact, deduplicated
frame-driven stream consumed by `Audio.Update()`.

## What changed in v2

- **Binary source by default.** `gbs-to-gbapu` writes a compact binary `.gbapu` (≈22x smaller
  than the equivalent JSON). `--emit-json` (or a `.json` output extension) still writes JSON, and
  `gbapu-dump <file>` prints the `FFxx=yy` lines for inspection. Binary and JSON sources compile
  to identical ROMs.
- **Automatic loop detection.** The exporter autocorrelates per-frame write signatures, trims the
  capture to intro + one loop body, and sets the loop point. `--no-auto-loop` disables it and
  `--loop-cycle` pins it manually.
- **Accurate frame timing.** Cycles map to frames using the true DMG frame period (70224 cycles
  ≈ 59.7275 Hz), not a nominal 60 Hz. The GBS timer fields are read and the driver replay rate is
  stored as `replayHz` metadata.
- **Group-pool ROM encoding (stream marker `0x02`).** Identical per-frame group bodies are
  deduplicated into a pool referenced by an order stream. This is ~−18% versus the previous flat
  stream while producing bit-identical APU writes.

## Intended Use

Use `.gbapu.json` when the source music exists as a GBS rip or another post-driver APU write trace and fidelity matters more than editability.

Do not use `.gbapu.json` as:

- A general cross-target music asset.
- A tracker source format.
- A direct `.gbs` runtime. GBS contains executable driver code plus load/init/play entry points; `.gbapu.json` contains only the resulting APU register writes.
- A guarantee of sub-frame playback accuracy in the current runtime. The source keeps cycle deltas, but current lowering schedules playback by frames.

For newly authored Game Boy music, hUGETracker `.uge` remains the more editable source format. For ripped music, the current supported path is GBS to `.gbapu.json`, not GBS to `.uge`.

## Generation From GBS

The CLI helper captures an APU write dump from `gbsplay`:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  gbs-to-gbapu \
  --in path/to/theme.gbs \
  --subsong 1 \
  --seconds 120 \
  --out path/to/theme.gbapu
```

Capture for at least two full loops (`--seconds`) so auto-detection can find the loop. Useful
options:

- `--no-auto-loop`: keep the full capture; do not trim to one loop.
- `--loop-cycle <n>`: pin the loop point manually (disables auto-detection).
- `--emit-json`: write the JSON debug view instead of binary (also implied by a `.json` output
  extension).
- `--gbsplay <path>`: point at a specific `gbsplay` binary.

Inspect a trace with `gbapu-dump path/to/theme.gbapu`, which prints `<absoluteCycles> ffXX=YY`.

Generation flow:

1. Read the GBS header for title, author, copyright, subsong count, timer, and entry-point metadata.
2. Validate `--subsong`, `--seconds`, `--loop-cycle`, and `--gbsplay`.
3. Run `gbsplay -q -o iodumper -t <seconds> -f 0 -g 0 <input.gbs> <subsong> <subsong>`.
4. Parse lines shaped like `00000010 ff30=12`.
5. Keep only supported APU register and Wave RAM writes.
6. Auto-detect the loop (unless `--no-auto-loop`/`--loop-cycle`) and trim to intro + one loop.
7. Write a binary `.gbapu` (or JSON with `--emit-json`) with timing deltas, loop point, the
   GBS-derived `replayHz`, and GBS metadata.

The helper uses a 4,194,304 Hz clock. `loopCycle` and `durationCycles` are set by auto-detection
or by the caller. The driver replay rate (VBlank ~59.7275 Hz, or the GBS timer rate) is recorded
as `replayHz`.

Example generated during development (one detected loop of the reference track):

- Source: `Super Mario Land 2` subsong 1
- Loop length: `51.43` seconds (auto-detected from a 120 s capture)
- Binary `.gbapu` size: `43440` bytes (the equivalent JSON is `959062` bytes)
- Compiled ROM payload: `10907` bytes (group-pool v2)

## JSON Shape

Minimal example:

```json
{
  "format": "retrosharp.gbapu.v1",
  "clockHz": 4194304,
  "framesPerSecond": 60,
  "durationCycles": 140448,
  "loopCycle": 0,
  "metadata": {
    "title": "Trace Fixture",
    "author": "Composer",
    "copyright": "Source copyright",
    "subsong": 1,
    "source": "theme.gbs"
  },
  "events": [
    { "deltaCycles": 0, "address": "FF24", "value": "77" },
    { "deltaCycles": 0, "address": "FF25", "value": "FF" },
    { "deltaCycles": 70224, "address": "FF12", "value": "F0" },
    { "deltaCycles": 0, "address": "FF14", "value": "87" },
    { "deltaCycles": 0, "address": "FF30", "value": "12" }
  ]
}
```

Top-level fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `format` | Yes | Must be `retrosharp.gbapu.v1`. |
| `clockHz` | Yes | Clock used by `deltaCycles` and `durationCycles`. Current exporter writes `4194304`. |
| `framesPerSecond` | Yes | Frame rate used to group cycles into runtime frame updates. Current exporter writes `60`. |
| `durationCycles` | Yes | Captured duration. Used to decide the wait before the loop sentinel. |
| `loopCycle` | Yes | Absolute cycle where playback should loop. `0` means loop from the first compiled group. |
| `metadata` | No | Optional descriptive metadata. The current exporter writes it from the GBS header. |
| `events` | Yes | Ordered list of APU writes. Must contain at least one event. |

Metadata fields:

| Field | Meaning |
| --- | --- |
| `title` | Source title, usually from the GBS header. |
| `author` | Source author, usually from the GBS header. |
| `copyright` | Source copyright, usually from the GBS header. |
| `subsong` | 1-based subsong captured from the source GBS. |
| `source` | Source file name. |
| `replayHz` | Driver replay rate derived from the GBS timer fields (VBlank ~59.7275 Hz, or the timer rate). Informational. |

Event fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `deltaCycles` | Yes | Non-negative cycle delta from the previous kept event, interpreted in file order. |
| `address` | Yes | Four-digit hexadecimal high-RAM address. |
| `value` | Yes | Two-digit hexadecimal byte value. |

Supported addresses are `FF10..FF26` and `FF30..FF3F`. That covers DMG APU registers `NR10` through `NR52` plus Wave RAM. The accepted `FF10..FF26` range is contiguous for simple validation; not every address in the range is meaningful on hardware.

Unknown top-level JSON properties are currently ignored by the reader. Required fields still need the exact names above.

## Using It In RetroSharp Source

Direct asset:

```csharp
void Main() {
    Video.Init();
    Music.Asset(stage_theme, "music/stage.gbapu.json");
    Audio.Init();
    Music.Play(stage_theme);
    loop {
        Video.WaitVBlank();
        Audio.Update();
    }
}
```

Portable envelope:

```json
{
  "format": "retrosharp.music.v1",
  "platforms": {
    "gb": {
      "format": "gbapu",
      "path": "music/stage.gbapu.json"
    }
  }
}
```

`Music.Asset(...)` can point directly to `.gbapu.json` or to a `retrosharp.music.v1` envelope whose `platforms.gb.format` is `gbapu`.

## Compiler Packing

The source file is not stored directly in ROM. `GameBoyMusicAssetCompiler` compiles it into a
compact, deduplicated byte stream (v2 group-pool) with this shape:

```text
02 orderStartLo orderStartHi loopOrderLo loopOrderHi   ; 5-byte header
poolBody*                                              ; unique group bodies
orderEntry*                                            ; bodyOffsetLo bodyOffsetHi waitAfter
00 00 00                                               ; loop sentinel (bodyOffset 0)
```

The leading `02` marks the asset as a v2 APU trace stream (the `.uge` row-stream header has
ticks-per-row in byte 0). `orderStartOffset` and `loopOrderOffset` are byte offsets from the
data start: the order stream begins at `orderStartOffset`, and the loop resumes at
`loopOrderOffset`.

Each pool body is the per-frame group, identical to the previous flat format minus its wait:

```text
commandCount command*
```

Each command is one of:

```text
registerOffset value
FF waveByte0 ... waveByte15
```

Register commands store only the low high-RAM offset. For example, address `FF24` becomes offset
`24`. `FF` is reserved as the Wave RAM block command because valid APU offsets are in `10..26`
and `30..3F`.

Each order entry plays one frame: it references a pool body by its data-relative `bodyOffset`
(`0` is the loop sentinel, since offset 0 is the header) and carries the `waitAfter` frame delay.
Identical bodies are stored once and shared by every order entry that needs them, so musical
repetition collapses to a small pool plus a thin order list.

Current packing rules:

- Events are accumulated from `deltaCycles` into absolute cycles.
- Absolute cycles are rounded to runtime frames through the true DMG frame period:
  `frame = round(cycles / 70224)` (scaled by `clockHz` if it is not the standard 4.194304 MHz).
- Consecutive events in the same frame become one group body.
- A group split is forced when `loopCycle` is crossed, so the loop target lands on a group boundary.
- Redundant writes to the same register with the same value are removed when they are not considered side-effectful.
- `NR52` writes are always preserved.
- Trigger writes are preserved when bit 7 is set on `NR14`, `NR24`, `NR34`, or `NR44`.
- Contiguous 16-byte writes to `FF30..FF3F` are packed as one Wave RAM block command.
- Identical group bodies are deduplicated into the pool and shared by their order entries.
- `waitAfter` is the frame delay until the next group, or until `durationCycles` after the last group.
- Waits longer than 255 frames currently fail.
- More than 255 commands in one frame body currently fail.
- A pool or order stream that pushes any offset beyond 65535 bytes currently fails because the
  ROM-only runtime stores 16-bit offsets.

## Runtime Playback

`Audio.Init()` enables the APU through `NR52`, routes channels through `NR51`, sets master volume through `NR50`, clears the BGM state, and clears the row cache shared with `.uge` playback.

`Music.Play(theme)` records the music data pointer and sets the active music kind:

- `.uge` assets use the row-stream path.
- `.gbapu` / `.gbapu.json` assets use the APU trace path and reset the order pointer to the first
  order entry.

`Audio.Update()` should be called once per frame after `Video.WaitVBlank()`. For APU traces it:

1. Checks whether the current wait counter is zero.
2. If waiting, decrements the counter and exits.
3. If ready, reads the next order entry (`bodyOffset`, `waitAfter`) and advances the order pointer.
4. A `bodyOffset` of zero is the loop sentinel; the runtime resets the order pointer to the
   compiled loop order offset.
5. It resolves the pooled group body at `dataPointer + bodyOffset` and reads its command count.
6. Register commands use `LDH (C),A` to write dynamic APU offsets.
7. Wave block commands write 16 bytes to `FF30..FF3F`.
8. The order entry `waitAfter` value becomes the next wait counter.
9. If `waitAfter` is zero, the runtime immediately processes the next order entry in the same
   `Audio.Update()` call.

The runtime is therefore frame-driven. The source trace keeps cycle precision, but today that precision is used only to choose frame groups.

## Current Problems

The format is useful as a faithful preservation/playback path. Several v1 problems are resolved in
v2 (marked **Resolved**); the remaining ones are inherent to a post-driver register trace.

1. It is target-specific.
   The format stores DMG APU writes. It is not portable to NES or other future targets without a separate translation layer.

2. It is not editable music.
   A `.gbapu` file has register writes, not notes, instruments, patterns, song order, or musical intent. It is a preservation/playback format, not an authoring format.

3. **Resolved — JSON is verbose.**
   The default source is now a compact binary `.gbapu` (≈22x smaller than JSON). JSON remains
   available via `--emit-json`/`gbapu-dump` for inspection.

4. **Resolved — Loop points are manual.**
   The exporter auto-detects the loop by autocorrelation and trims to one loop body. `--loop-cycle`
   and `--no-auto-loop` remain as overrides. Note the loop is similarity-based, not bit-exact:
   drivers with free-running modulators leave a small modulator-phase discontinuity at the seam.

5. Sub-frame timing is currently lost at playback.
   The runtime collapses writes into VBlank frame groups. For VBlank-driven songs this is faithful;
   for timer-driven songs whose replay rate exceeds VBlank, multiple driver ticks collapse into one
   frame until a timer-interrupt player exists. The `replayHz` metadata is recorded for that work.

6. Runtime bursts can become expensive.
   Zero-wait order entries execute in one `Audio.Update()` call. That avoids accidental one-frame gaps, but a dense trace can create a long write burst in VBlank-adjacent code.

7. The optimizer is conservative but not formally proven.
   It preserves trigger writes and `NR52`, and removes repeated non-trigger writes with the same value. That is reasonable for normal APU state writes, but this format has no explicit side-effect annotations beyond hardcoded register rules.

8. It depends on `gbsplay` behavior.
   Generation fidelity depends on `gbsplay -o iodumper`, the selected subsong, the capture duration, and the external tool's interpretation of the GBS driver.

9. It drops non-APU context.
   The exported trace ignores writes outside supported APU and Wave RAM addresses. That is intentional for post-driver playback, but it means the resulting file cannot explain or reconstruct driver state, timers, bank switching, or source song structure.

10. It is constrained by the current 32 KiB ROM-only target.
    The compiled stream uses 16-bit offsets and no banked data path. Longer traces can hit size limits before the target grows MBC support.

11. **Resolved — It used a nominal 60 FPS grouping rate.**
    The compiler now maps cycles to frames with the true DMG frame period (70224 cycles ≈
    59.7275 Hz), removing the ~0.46% drift the nominal 60 Hz introduced.

12. The schema has no generator metadata.
    The file records source metadata, but not the RetroSharp version, exporter version, `gbsplay` version, command line, source hash, or measured loop-quality notes. Those would help future analysis.

## Better Options To Evaluate Later

This format should be treated as a useful experiment, not the final answer.

| Option | Why consider it | Main cost |
| --- | --- | --- |
| Binary `.gbapu` stream | Store the compiler-ready stream or a lightly wrapped version instead of verbose JSON. | Harder to diff and inspect; needs tooling for analysis. |
| JSON plus generated binary cache | Keep readable source and allow a checked or regenerated compact payload. | Cache invalidation and source-of-truth rules. |
| Sub-frame APU scheduler | Use the existing cycle deltas instead of frame grouping. | More runtime cost, timing complexity, and interaction with VBlank work. |
| Direct GBS runtime | Embed or adapt the original driver and call load/init/play. | Requires MBC/banking, timer behavior, memory layout, and driver isolation. |
| Higher-level transcription | Analyze register writes back into notes/instruments/patterns. | Hard, lossy, and song-specific; this is where the removed GBS-to-UGE approach was weak. |
| Authored tracker-first pipeline | Prefer `.uge` or another tracker/source format for new music. | Does not solve faithful playback of existing GBS rips. |
| Standardized register-stream source | Import from a broader external trace format before compiling to the runtime stream. | Still needs a target runtime strategy and may include unsupported hardware events. |

Near-term recommendation: keep `.gbapu.json` as the explicit GBS fidelity path, but avoid expanding it into a general music abstraction. The next worthwhile technical work would be measurement-driven: collect sizes, frame burst lengths, audible drift cases, and loop quality on a small corpus before choosing between a binary stream, sub-frame scheduler, or direct GBS runtime.

## Stability Notes

- The stable format discriminator is `format: "retrosharp.gbapu.v1"`.
- The current compiler accepts the direct file and the `retrosharp.music.v1` envelope variant `format: "gbapu"`.
- Unknown JSON fields are ignored today, but required fields are strict.
- If event timing semantics, loop semantics, or address encoding change, bump the format string instead of silently changing `retrosharp.gbapu.v1`.
