# Full `stage1` baseline (LW-0.1)

Status: **frozen on 2026-07-10 and refreshed after LW-1.5 by the focused GB/NES baseline, WorldPack parity tests, and opt-in CLI report.**

This report records why the complete authored runner `stage1` cannot run on the
current Game Boy or NES paths. It began as measurement and acceptance evidence;
the later ADRs now fix the packed-world format, staging geometry, and coordinate
ABI. The report still does not choose Game Boy banking policy or a NES mapper,
and the inspectable LW-1.4 packs are not connected to production readers.

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

For an importer-ready Tiled map, the same production measurement seam is
available explicitly through the CLI without writing a ROM:

```bash
dotnet run --no-launch-profile --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb --world-budget-report \
  samples/tiled-free-scroll/free-scroll.tmj
```

Use `--target nes` for the NES payload. The full authored `stage1.tmj` remains
an authoring acceptance asset rather than the runner input: it does not carry
the importer-facing layer name and whole-world properties. The focused CLI
acceptance tests therefore copy it to a temporary directory, non-destructively
normalize that temporary JSON, run each target report twice, and remove the
copy. They freeze identical JSON plus exact 770 visual and 312 collision stored
bytes for both targets; no tracked map or ROM is modified.

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

## LW-1.4 packed result

| Packed fact | Game Boy | NES |
| --- | ---: | ---: |
| Serialized `WorldPack` | 2,550 bytes | 2,762 bytes |
| Raw-fallback envelope | 7,708 bytes | 7,920 bytes |
| Chunks / visual IDs / collision profiles | 60 / 53 / 2 | 60 / 53 / 2 |
| Preserved generated patterns | 82 | 90 |

These are canonical header + profiles + target expansions + directory +
per-plane raw/RLE bytes. Two fresh compilations are byte-identical, and decoded
tiles, collision flags, target pattern data, NES palette slots, and NES
world/background provenance match the current raw importers. The numbers are
packing evidence only; `World.Load(...)` continues to use the existing raw
production path.

Collision is derived from the `stage1.tsx` tile object groups and expanded with
the visual map, so the reported 12,480 flag bytes cover the whole authored
level. The full-payload and no-audio decomposition probes retain the runner's
sprite sheet and gameplay code; the full-payload probe also retains the
target-specific BGM and jump SFX. They do not trim the map or omit collision.
The focused LW-1.1 address-width probe is scoped separately below.

## Ordered constraints

The order is deliberate and machine-stable. A passing resource category stays
separate from the blockers so pattern pressure is not misreported as a ROM or
coordinate failure.

| Order | Category | Game Boy | NES |
| ---: | --- | --- | --- |
| 1 | `address-width` | **Passes after LW-1.1.** The 312 hardware columns and word camera/source-edge positions compile without truncation. | **Passes after LW-1.1.** The 312 hardware columns reach the existing PRG-capacity failure without camera/source-column truncation. |
| 2 | `collision-abi` | **Passes after LW-1.2.** The floor returns `30 01` and no hit returns `FF FF`. | **Passes after LW-1.2.** The same complete word result is returned through the NES ABI. |
| 3 | `rom-capacity` | **Capacity probe passes, runtime acceptance does not.** Both the unchanged runner payload with redirected `World.Load` and the real 312x40 camera-dimension probe emit a 131,072-byte MBC1 ROM with full map, collision, BGM, and SFX. | **Blocked.** Even after removing audio only for decomposition, code plus full visual/collision data emits 41,851 bytes against 32,762 available PRG bytes. With full audio restored, the `$E980` DPCM block (1,153 bytes) cannot be placed after earlier data ending at `$134C4`. |
| 4 | `tile-patterns` | **Passes.** 6 reserved + 82 background + 60 sprite tiles use 148 of 256 indexes. | **Passes.** 6 reserved + 95 sprite + 90 background tiles use 191 of 256 indexes and 3,056 of 8,192 CHR bytes. |
| 5 | `ram-staging` | **Contract accepted; production path still missing.** The `WorldPack` ADR fixes bounded chunk/edge staging, but no target runtime reader or staging implementation consumes it yet. | **Contract accepted; production path still missing.** The same bounded staging contract exists, but mapper 0 has no mapper-backed world reader or staging implementation. |
| 6 | `vblank` | **Current edge work is bounded.** Visible column and row commits fit the 21-tile write budget; future lookup/decompression still has to happen outside VBlank. | **Current phase work is bounded.** Limits are 32 tile writes and 9 attribute writes; row streaming remains split into bounded phases. |

The 131,072-byte Game Boy result is intentionally called a *capacity probe*.
One probe keeps `Level.Width = 176` and the current runner runtime constants so
the builder can expose cartridge usage independently. A second probe uses the
real 312x40 dimensions and proves the LW-1.1 address-width path. Neither is
evidence that the complete level is playable today: collision widening is now
complete through LW-1.2, while packing and a production runtime reader remain
separate work.

The focused NES LW-1.1 runtime probe keeps the complete 312x40 map, target BGM
and SFX, a 312-cell camera width, and a word camera request of 1,888 pixels. It
historically excluded gameplay collision queries owned by LW-1.2; the later
LW-1.2 focused probes cover those queries separately. The LW-1.1 probe passes
logical address lowering and reaches the cartridge-capacity blocker: the
`$E980` DPCM block cannot be placed after music data ending at `$10A06`.

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

LW-0.1 deliberately added no tracked normalized full map, full-stage1 ROM,
packed world, staging buffer, collision-result widening, compression, mapper
selection, or runner-input switch. Collision-result widening later landed in
LW-1.2, and deterministic inspection packs plus raw/RLE serialization landed in
LW-1.4. Game Boy production acceptance and NES physical placement/selection
have since landed; the NES fixed-bank reader/stager and joint runner-input
switch remain separate dependent work.

## LW-2.5 Game Boy follow-up

The Game Boy blockers frozen above are now closed by LW-2.1 through LW-2.5.
The production packed reader/stager uses the accepted 298-byte current layout,
keeps bank/directory/decode work outside VBlank, and commits at most one
19-column or 21-row edge. A non-destructive full-stage fixture now proves the
complete map and resources on the real final linker: the runner-shaped build
selects 131,072-byte MBC1, while a smaller full-stage traversal build remains
32 KiB ROM-only. Both use the exact 2,550-byte pack; neither uses the map-only
budget report as a profile decision.

The acceptance reconstructs all 60 chunks, checks full visual/collision
parity and the Y=304 / `FFFF` ABI, crosses column 256 in both directions,
traverses both axes, and records lifecycle, write-bound, guard-band, bank, and
audio-tick evidence. It does not change the shared runner input or either
tracked runner ROM; their joint migration remains LW-3.5.

## LW-3.2 NES placement follow-up

LW-3.2 preserves the raw mapper-0 measurements above as historical baseline
facts, but the production selector now attempts that exact mapper-0 image first
and retries MMC3 only after a real PRG/DPCM layout failure. The normalized
full-`stage1` placement probe includes the 95-tile runner sprite, 90 generated
background tiles, BGM, SFX, and both DPCM blocks. Its final linker report is:

| NES final section | Used | Capacity |
| --- | ---: | ---: |
| Canonical `WorldPack` in R6 bank 0 | 2,762 | 8,192 |
| Pinned R7 runtime/BGM/SFX in bank 1 | 5,012 | 8,192 |
| Boot-only palette/nametables in R7 bank 2 | 4,128 | 8,192 |
| Fixed code/audio runtime/aligned DPCM/vectors in banks 6-7 | 2,151 | 16,384 |
| Resident static CHR | 3,056 | 8,192 |
| Physical cartridge | 65,536 PRG + 16,384 CHR | 65,536 PRG + 16,384 CHR |

The separate canonical synthetic acceptance is larger than one 8 KiB window
and reconstructs byte-for-byte through ordered physical R6 banks `0, 3, 4, 5`.
Pack-relative offsets and serialized section order are unchanged. The existing
runner-shaped raw runtime still fails the fixed-region diagnostic because it
retains legacy rows; replacing those accesses belongs to LW-3.3. LW-3.2 does
not migrate the runner or regenerate either tracked ROM.
