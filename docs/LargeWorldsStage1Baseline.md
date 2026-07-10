# Full `stage1` baseline (LW-0.1)

Status: **frozen on 2026-07-10 by the focused GB/NES baseline tests.**

This report records why the complete authored runner `stage1` cannot run on the
current Game Boy or NES paths. It is measurement and acceptance evidence only:
it does not choose a packed-world format, staging geometry, coordinate ABI,
Game Boy banking policy, or NES mapper.

Run the deterministic report from the repository root:

```bash
dotnet test RetroSharp.sln -m:1 \
  --filter "FullyQualifiedName~FullStage1BaselineTests" \
  --logger "console;verbosity=detailed"
```

The two tests print stable JSON objects in the category order used below. They
normalize `stage1.tmj` into a temporary directory, point an in-memory copy of
the runner source at that temporary map, and delete every temporary map and ROM
after the test. No tracked runner input or output is used as scratch space.

## Acceptance payload

| Fact | Complete `stage1` |
| --- | ---: |
| Authored map | 156x20 source cells at 16x16 px |
| Target-expanded map | 312x40 hardware tiles at 8x8 px |
| Pixel extent | 2496x320 px |
| Visual cells / bytes | 12,480 / 12,480 |
| Collision cells / bytes | 12,480 / 12,480 |
| Visual plus collision bytes | 24,960 |
| Solid collision cells | 788 hardware cells (197 source cells) |
| Acceptance floor | world-pixel Y 304 |

Collision is derived from the `stage1.tsx` tile object groups and expanded with
the visual map, so the reported 12,480 flag bytes cover the whole authored
level. The payload probes retain the runner's target-specific BGM, jump SFX,
sprite sheet, and gameplay code. They do not trim the map, omit collision, or
replace target audio with a no-op.

## Ordered constraints

The order is deliberate and machine-stable. A passing resource category stays
separate from the blockers so pattern pressure is not misreported as a ROM or
coordinate failure.

| Order | Category | Game Boy | NES |
| ---: | --- | --- | --- |
| 1 | `address-width` | **Blocked.** The 312 hardware columns exceed the current `camera_init` range 1..255. | **Blocked.** The current horizontal camera runtime requires a one-byte map width. |
| 2 | `collision-abi` | **Blocked.** The floor hit Y 304 cannot be returned unambiguously while hit-top is an 8-bit value and 255 means no hit. | **Blocked.** The same hit-top ABI and sentinel are used by the runner path. |
| 3 | `rom-capacity` | **Capacity probe passes, runtime acceptance does not.** Redirecting only `World.Load` while leaving the current runner constants unchanged emits a 131,072-byte MBC1 ROM with full map, collision, BGM, and SFX. | **Blocked.** Even after removing audio only for decomposition, code plus full visual/collision data emits 41,907 bytes against 32,762 available PRG bytes. With full audio restored, the `$E980` DPCM block (1,153 bytes) cannot be placed after earlier data ending at `$134FC`. |
| 4 | `tile-patterns` | **Passes.** 6 reserved + 82 background + 60 sprite tiles use 148 of 256 indexes. | **Passes.** 6 reserved + 95 sprite + 90 background tiles use 191 of 256 indexes and 3,056 of 8,192 CHR bytes. |
| 5 | `ram-staging` | **Blocked by missing contract, not measured RAM exhaustion.** Each 12,480-byte visual/flag blob fits one 16 KiB bank, but there is no fixed large-world chunk staging contract. | **Blocked by missing path.** Mapper 0 has no mapper-backed world reader or fixed large-world staging contract. |
| 6 | `vblank` | **Current edge work is bounded.** Visible column and row commits fit the 21-tile write budget; future lookup/decompression still has to happen outside VBlank. | **Current phase work is bounded.** Limits are 32 tile writes and 9 attribute writes; row streaming remains split into bounded phases. |

The 131,072-byte Game Boy result is intentionally called a *capacity probe*.
It keeps `Level.Width = 176` and the current runner runtime constants so the
builder can expose cartridge usage independently. A second probe uses the real
312x40 dimensions and freezes the expected address-width failure. It is not
evidence that the complete level is playable today.

## Target resource facts

| Resource | Game Boy | NES |
| --- | ---: | ---: |
| Generated background patterns | 82 tiles / 1,312 bytes | 90 tiles / 1,440 bytes |
| Runner sprite patterns | 60 tiles / 960 bytes | 95 tiles / 1,520 bytes |
| Target BGM stream | 11,614 bytes | 4,126 bytes |
| DPCM sample blocks | n/a | 1,282 bytes total (129 + 1,153) |
| Jump SFX | 28 bytes | 26 bytes |
| Current target ROM budget | up to 32 MBC1 banks of 16 KiB | 32 KiB PRG minus 6 vector bytes; 8 KiB CHR |

## Protected artifacts

LW-0.1 reads but must not modify:

- `samples/runner/assets/maps/stage1.tmj`, `stage1.tsx`, `stage1.png`, and
  `stage1.playable.tmj`;
- `samples/runner/src/**` and `samples/runner/runner.retrosharp.json`;
- the target BGM, SFX, and sprite source assets under `samples/runner/assets`;
- `samples/runner/bin/runner.gb` and `samples/runner/bin/runner.nes`.

There is deliberately no tracked normalized full map, full-stage1 ROM, packed
world, staging buffer, coordinate widening, compression, mapper selection, or
runner-input switch in this task. Those remain separate dependent work.
