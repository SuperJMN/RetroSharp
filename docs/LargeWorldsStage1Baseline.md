# Full `stage1` baseline (LW-0.1)

Status: **historical baseline, fully resolved by LW-3.5 on 2026-07-12.**

This report preserves the constraints that originally prevented complete
`stage1` from running. The final production paths now consume the exact LW-1.4
packs: the tracked Game Boy runner is a 131,072-byte MBC1 ROM and the tracked
NES runner is an 81,936-byte MMC3/four-screen ROM with full BGM, SFX, DPCM,
collision, and 2-axis streaming. The ordered failures below remain historical
evidence for the decisions that resolved them.

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

Use `--target nes` for the NES payload. `stage1.tmj` now carries the
importer-facing `world` layer and whole-world properties and is the shared
runner input. Historical baseline fixtures may still normalize a temporary
copy so earlier measurements stay reproducible. They freeze identical JSON
plus exact 770 visual and 312 collision stored bytes for both targets.

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
world/background provenance match the raw importers. `World.Load(...)` now
places and reads these packs in production on both targets.

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
| 3 | `rom-capacity` | **Resolved by LW-2.1 through LW-2.5.** The production runner emits a 131,072-byte MBC1 ROM with the complete pack, collision, BGM, and SFX. | **Resolved by LW-3.1 through LW-3.5.** Honest final-link selection emits the complete runner as an 81,936-byte MMC3/four-screen ROM with BGM, SFX, and both DPCM blocks. |
| 4 | `tile-patterns` | **Passes.** 6 reserved + 82 background + 60 sprite tiles use 148 of 256 indexes. | **Passes.** 6 reserved + 95 sprite + 90 background tiles use 191 of 256 indexes and 3,056 of 8,192 CHR bytes. |
| 5 | `ram-staging` | **Implemented.** The production reader uses the accepted bounded chunk/edge staging contract. | **Implemented.** Resident mapper-0 and continued R6 sources share the accepted 338/594-byte bounded staging layout and two immutable edge peers. |
| 6 | `vblank` | **Implemented and bounded.** Lookup/decompression stays outside VBlank; visible commits use at most 19 column or 21 row writes. | **Implemented and bounded.** Lookup/decompression stays outside NMI; limits are 32 tile writes and 9 attribute writes, with row streaming split into bounded phases. |

The frozen NES mapper-0 decomposition still records the original capacity
failure: code plus full visual/collision data used 41,851 bytes against 32,762
available PRG bytes. With audio restored, the `$E980` DPCM block could not
follow data ending at `$134C4`; the earlier address-width probe ended at
`$10A06`. The MMC3 production result above resolves those historical failures
without deleting their measurements.

The original 131,072-byte Game Boy result was intentionally called a *capacity
probe*. One historical probe kept `Level.Width = 176` so the builder could
expose cartridge usage independently; a second used the real 312x40 dimensions
to prove the LW-1.1 address-width path. Those measurements are no longer the
playability claim: the tracked runner and the LW-3.5 production acceptance now
provide that evidence.

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
LW-1.4. Game Boy production acceptance, NES placement/selection/reader/stager,
and the joint runner-input switch have all since landed. This protected-artifact
list describes the historical LW-0.1 slice only.

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
audio-tick evidence. That LW-2.5 slice did not change the shared runner input
or either tracked ROM; LW-3.5 has now performed their joint migration and
deliberate regeneration.

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
| Fixed code/audio runtime/WorldPack reader/aligned DPCM/vectors in banks 6-7 | 4,327 | 16,384 |
| Resident static CHR | 3,056 | 8,192 |
| Physical cartridge | 65,536 PRG + 16,384 CHR | 65,536 PRG + 16,384 CHR |

The separate canonical synthetic acceptance is larger than one 8 KiB window
and reconstructs byte-for-byte through ordered physical R6 banks `0, 3, 4, 5`.
Pack-relative offsets and serialized section order are unchanged. The existing
runner-shaped raw runtime still fails the fixed-region diagnostic because it
retains legacy rows. LW-3.3 adds the reader but deliberately does not migrate
the camera/runner path; that scheduling work belongs to LW-3.4/LW-3.5.
