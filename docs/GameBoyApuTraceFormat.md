# Game Boy APU Trace Format

Status: experimental analysis document for `retrosharp.gbapu.v1`.

`*.gbapu.json` is RetroSharp's current Game Boy register-trace music format. It is a target-specific asset format for the Game Boy BGM runtime, not a portable music format and not an editable tracker module.

The format exists to preserve the audible output of an existing Game Boy music driver without embedding that driver in the generated ROM. It records timed writes to the DMG APU registers and Wave RAM, then the Game Boy compiler repacks those writes into a compact frame-driven stream consumed by `audio.Update()`.

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
  --seconds 60 \
  --loop-cycle 0 \
  --out path/to/theme.gbapu.json
```

Generation flow:

1. Read the GBS header for title, author, copyright, subsong count, and entry-point metadata.
2. Validate `--subsong`, `--seconds`, `--loop-cycle`, and `--gbsplay`.
3. Run `gbsplay -q -o iodumper -t <seconds> -f 0 -g 0 <input.gbs> <subsong> <subsong>`.
4. Parse lines shaped like `00000010 ff30=12`.
5. Keep only supported APU register and Wave RAM writes.
6. Write a `retrosharp.gbapu.v1` JSON file with timing deltas and GBS metadata.

The helper uses a 4,194,304 Hz clock and 60 frames per second. `durationCycles` is `clockHz * seconds`. `loopCycle` is provided by the caller; the exporter does not detect musical loop points.

Example generated during development:

- Source: `/home/jmn/Descargas/GB Music/Super Mario Land 2/DMG-L6J.gbs`
- Subsong: `1`
- Capture duration: `2` seconds
- JSON size: `34155` bytes
- Event count: `401`

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
void main() {
    video.Init();
    music.Asset(stage_theme, "music/stage.gbapu.json");
    audio.Init();
    music.Play(stage_theme);
    loop {
        video.WaitVBlank();
        audio.Update();
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

`music.Asset(...)` can point directly to `.gbapu.json` or to a `retrosharp.music.v1` envelope whose `platforms.gb.format` is `gbapu`.

## Compiler Packing

The JSON file is not stored directly in ROM. `GameBoyMusicAssetCompiler` compiles it into a compact byte stream with this shape:

```text
00 loopOffsetLo loopOffsetHi
group*
00
```

The leading `00` marks the asset as an APU trace stream, distinguishing it from the `.uge` row-stream header where byte 0 is ticks-per-row. `loopOffset` is measured from the first byte after this three-byte header.

Each group is:

```text
commandCount command* waitAfter
```

Each command is one of:

```text
registerOffset value
FF waveByte0 ... waveByte15
```

Register commands store only the low high-RAM offset. For example, address `FF24` becomes offset `24`. `FF` is reserved as the Wave RAM block command because valid APU offsets are in `10..26` and `30..3F`.

Current packing rules:

- Events are accumulated from `deltaCycles` into absolute cycles.
- Absolute cycles are rounded to runtime frames through `frame = (cycles * framesPerSecond + clockHz / 2) / clockHz`.
- Consecutive events in the same frame become one group.
- A group split is forced when `loopCycle` is crossed, so the loop target can land on a group boundary.
- Redundant writes to the same register with the same value are removed when they are not considered side-effectful.
- `NR52` writes are always preserved.
- Trigger writes are preserved when bit 7 is set on `NR14`, `NR24`, `NR34`, or `NR44`.
- Contiguous 16-byte writes to `FF30..FF3F` are packed as one Wave RAM block command.
- `waitAfter` is the frame delay until the next group, or until `durationCycles` after the last group.
- Waits longer than 255 frames currently fail.
- More than 255 command groups in one frame currently fail.
- Loop offsets beyond 65535 bytes currently fail because the present ROM-only runtime stores a 16-bit loop offset.

## Runtime Playback

`audio.Init()` enables the APU through `NR52`, routes channels through `NR51`, sets master volume through `NR50`, clears the BGM state, and clears the row cache shared with `.uge` playback.

`music.Play(theme)` records the music data pointer and sets the active music kind:

- `.uge` assets use the row-stream path.
- `.gbapu.json` assets use the APU trace path and reset the current pointer to the first group.

`audio.Update()` should be called once per frame after `video.WaitVBlank()`. For APU traces it:

1. Checks whether the current wait counter is zero.
2. If waiting, decrements the counter and exits.
3. If ready, reads the current group command count.
4. A zero command count is the loop sentinel; the runtime resets the pointer to the compiled loop offset.
5. Register commands use `LDH (C),A` to write dynamic APU offsets.
6. Wave block commands write 16 bytes to `FF30..FF3F`.
7. The group `waitAfter` value becomes the next wait counter.
8. If `waitAfter` is zero, the runtime immediately processes the next group in the same `audio.Update()` call.

The runtime is therefore frame-driven. The source trace keeps cycle precision, but today that precision is used only to choose frame groups.

## Current Problems

The format is useful as a quick fidelity path, but it has real design debt.

1. It is target-specific.
   The format stores DMG APU writes. It is not portable to NES or other future targets without a separate translation layer.

2. It is not editable music.
   A `.gbapu.json` file has register writes, not notes, instruments, patterns, song order, or musical intent. It is a preservation/playback format, not an authoring format.

3. JSON is verbose.
   The source is easy to inspect and diff, but large for generated register data. The compiler packs it aggressively, so the checked-in source size can be much larger than the actual ROM payload.

4. Loop points are manual.
   `--loop-cycle` is not inferred. A wrong loop cycle can restart at a musically bad point, and the compiler can only align it to a command group.

5. Sub-frame timing is currently lost at playback.
   The JSON keeps cycle deltas, but the runtime collapses writes into frame groups. Fast arpeggios, timer-driven effects, and dense driver tricks can be audibly different until a sub-frame scheduler exists.

6. Runtime bursts can become expensive.
   Zero-wait groups execute in one `audio.Update()` call. That avoids accidental one-frame gaps, but a dense trace can create a long write burst in VBlank-adjacent code.

7. The optimizer is conservative but not formally proven.
   It preserves trigger writes and `NR52`, and removes repeated non-trigger writes with the same value. That is reasonable for normal APU state writes, but this format has no explicit side-effect annotations beyond hardcoded register rules.

8. It depends on `gbsplay` behavior.
   Generation fidelity depends on `gbsplay -o iodumper`, the selected subsong, the capture duration, and the external tool's interpretation of the GBS driver.

9. It drops non-APU context.
   The exported trace ignores writes outside supported APU and Wave RAM addresses. That is intentional for post-driver playback, but it means the resulting file cannot explain or reconstruct driver state, timers, bank switching, or source song structure.

10. It is constrained by the current 32 KiB ROM-only target.
    The compiled stream has a 16-bit loop offset and no banked data path. Longer traces can hit size limits before the target grows MBC support.

11. It uses a nominal 60 FPS grouping rate.
    The exporter writes `framesPerSecond: 60`, and the compiler groups cycle timestamps against that value. That is simple and close enough for short experiments, but long captures should be checked for drift against the actual runtime frame cadence.

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
