# Large Worlds Roadmap (banked map content for Game Boy and NES)

Status: **active; Waves 0 and 1 are complete, and Waves 2 and 3 are
issue-ready for first publication.**
Last updated: 2026-07-10.

This roadmap is the executable plan for levels that exceed the legacy
one-byte world addressing and current monolithic ROM-data budgets. It coordinates the
portable world contract, target-owned map packing, Game Boy MBC1 data access,
NES mapper-backed data, collision, art residency, and acceptance validation.

`docs/ArchitectureRoadmap.md` owns the durable language-versus-SDK-versus-target
boundary. This file owns the detailed Large Worlds task graph. GitHub owns
remote state and review; the parent epic is not an implementation unit.

## 1. Outcome

The first acceptance target is the complete authored runner `stage1` level:

- source map: `samples/runner/assets/maps/stage1.tmj`;
- 156x20 source cells at 16x16 pixels;
- 312x40 hardware cells at 8x8 pixels (2496x320 pixels);
- full Game Boy and NES background music retained;
- collision remains correct at world-pixel Y values beyond 255;
- movement remains correct in both directions across hardware-tile column 255;
- small programs continue to emit ROM-only Game Boy or mapper-0 NES output
  when they fit.

The current derived `stage1.playable.tmj` remains the stable shared runner input
through `LW-2.5`; only joint acceptance task `LW-3.5`, after both target paths
are ready, may switch it to the complete map and regenerate both tracked ROMs.
No task may silently trim the level, stub audio, discard collision, or lower an
unsupported path as a no-op merely to make the ROM fit.

## 2. Baseline and root constraints

The full level expands to 12,480 hardware cells. The current target paths store
one visual tile byte plus one collision-flag byte per hardware cell, before
code, audio, initial tilemaps/nametables, attributes, pointer tables, or art:

| World | Hardware cells | Raw tile ids + flags |
| --- | ---: | ---: |
| Full `stage1` | 312x40 = 12,480 | 24,960 bytes |
| `stage1.playable` | 176x30 = 5,280 | 10,560 bytes |

Current blockers are independent and must not be conflated:

- Viewport streaming already exists. Map size is not the same problem as the
  32x32 Game Boy background buffer or NES nametable surface.
- LW-1.1 widens shared camera operands and both targets' logical map-column,
  camera, edge-tag, and row/column streaming state; hardware scroll writes stay
  bytes. Packed/banked world reads are still absent.
- NES still emits mapper 0 with 32 KiB PRG and 8 KiB CHR; mapper-backed level
  data is not implemented.
- `Camera.AabbHitTop(...)` now exposes a complete world-pixel word with `-1`
  as no hit; screen-relative hit-top retains its byte-range `255` sentinel.
- Tiled 16x16 cells are expanded into repeated 8x8 visual and flag cells. The
  tile patterns are deduplicated, but the world grid itself is not packed as
  metatiles or chunks.
- Wide-map actor activation still scans all authored spawns each frame; GitHub
  issue #244 owns that adjacent runtime-cost problem.

## 3. Locked architecture decisions

These constraints apply to every task in the epic:

1. **The language stays target-neutral.** Banks, mappers, ROM windows, PPU
   addresses, and MBC registers never become language syntax or public portable
   SDK arguments.
2. **`World.Load(...)` stays the authoring boundary.** The compiler may lower it
   to a target-specific packed `WorldPack`, but gameplay source does not select
   chunks or banks manually.
3. **World coordinates are wider than hardware scroll registers.** Logical
   dimensions, camera coordinates, and chunk selection may use fixed 16-bit
   state; SCX/SCY and PPUSCROLL remain target-owned low-byte registers.
4. **Large worlds live in ROM, not as full RAM copies.** Runtime RAM cost is
   bounded by fixed descriptors and two peer edge-staging buffers.
5. **Streaming work is split by phase.** Bank selection, lookup, decompression,
   and staging happen outside VBlank where possible; VBlank commits only the
   bounded VRAM/PPU write shape declared by target capabilities.
6. **Map packing is target-owned.** The portable layer owns world semantics and
   collision meaning. Game Boy and NES own generated tile ids, palettes,
   cartridge placement, decompression helpers, and residency policy.
7. **Small output remains stable.** New packing/banking is selected only when
   required or explicitly tested. Existing small sample ROMs should stay
   byte-identical unless a task declares otherwise.
8. **No hidden dynamic runtime.** No heap, GC, RTTI, boxing, delegates, closures,
   virtual dispatch, unbounded collections, or runtime asset discovery.

## 4. Decision gates

Wave 0 must close these questions before target implementation begins:

- Metatile identity and whether collision is metadata, a per-cell override, or
  both.
- Chunk geometry and ordering for horizontal, vertical, and 2-axis worlds.
- Random-access compression granularity; a whole-level sequential stream is
  not acceptable for camera/collision reads.
- Fixed staging-buffer sizes and when a chunk/edge becomes resident.
- NES v1 cartridge profile (MMC1, UxROM, or another measured choice), including
  emulator support, fixed-bank runtime placement, PRG/CHR policy, and
  four-screen interaction.
- Public/internal representation for a collision hit beyond world Y 255 without
  introducing managed-object semantics.
- Compatibility policy for existing `WorldMap2D`, `WorldTileGrid`, and tracked
  small-ROM output.

An ADR task records a decision and proof. It must not smuggle the production
implementation into the same PR.

## 5. Agent execution contract

### Integrator

- Own the parent epic, dependency graph, roadmap status, and merge order.
- Dispatch only child issues whose dependencies are merged on `master`.
- Give each implementation run one issue URL and this roadmap section.
- Do not ask an implementation agent to "continue the epic" after its issue is
  complete.
- Re-scope or add a child issue when new work crosses a declared layer or
  target boundary.
- Keep GitHub issue state and this roadmap aligned after each merge.

### Implementation agent

- Start a fresh task/session for one child issue.
- Work on `agent/<task-id>-<slug>` from current `master`.
- Read the real code path before editing and reproduce/measure the baseline.
- Produce one reviewable PR for one observable result.
- Preserve unrelated work and do not start a dependent task automatically.
- Run the issue-specific validation plus `git diff --check`.
- Update target/public docs in the same PR when behavior changes.

### Stop conditions

Stop and return to the integrator when:

- a task needs a different public contract than its accepted ADR;
- a shared change would require target hardware details in portable source;
- the chosen chunk cannot be read/decompressed within the measured runtime
  budget;
- a bank switch can occur while executing code from the same switchable window;
- a proposed NES mapper is unsupported by the selected behavioral test backend;
- preserving small-ROM output requires an unrelated rewrite;
- the issue would need to absorb another task id to reach its claimed outcome.

### Model and scope defaults

Project `.codex/config.toml` selects `gpt-5.6-sol` with `xhigh` reasoning for
new trusted-repo sessions. Mechanical follow-ups may override the effort, but
architecture, ABI, banking, mapper, and integration tasks should retain
`xhigh`. Parallel work starts only after shared contracts merge; two agents must
not edit the same builder or shared format concurrently.

## 6. Wave 0 — measured decisions

Wave 0 is sequential at its entry point. `LW-0.1` lands first; `LW-0.2`,
`LW-0.3`, and `LW-0.4` may then proceed independently.

### LW-0.1: Freeze the complete `stage1` acceptance baseline and budget report

- Status: **complete; baseline frozen by the GB/NES focused tests and
  [`LargeWorldsStage1Baseline.md`](LargeWorldsStage1Baseline.md).**
- Layer: validation and documentation.
- Dependencies: none.
- Candidate files: `samples/runner/assets/maps/stage1.tmj`,
  `samples/runner/assets/maps/stage1.playable.tmj`, runner project/constants,
  target compiler tests, a focused reporting tool/test, target docs.
- Work:
  - Record exact source/hardware dimensions and per-target current ROM budgets.
  - Add a deterministic, non-destructive way to compile or analyze the full
    authored map and capture every current failure in order.
  - Separate addressing, collision ABI, PRG/ROM capacity, tile-pattern, and
    VBlank limits in the report.
  - Preserve the current tracked runner ROMs and playable map.
- Acceptance:
  - One command or focused test produces a stable baseline for both GB and NES.
  - Failures name the responsible layer instead of reporting only final ROM
    overflow or runtime corruption.
  - The baseline proves that the full level, music, and collision are the final
    acceptance payload.
- Verification:
  - `dotnet test RetroSharp.sln -m:1 --filter
    "FullyQualifiedName~FullStage1BaselineTests" --logger
    "console;verbosity=detailed"` emits the deterministic GB/NES facts.
  - `tools/gameboy/generate_sample_roms.py --dry-run` shows no unintended
    tracked-ROM changes.
  - `git diff --check`.
- Non-goals: no production mapper, banking, coordinate, compression, or runner
  migration implementation.

### LW-0.2: Decide the `WorldPack` metatile, chunk, compression, and staging format

- Status: **complete; format accepted by
  [`WorldPackFormatV1.md`](WorldPackFormatV1.md) with reproducible full-`stage1`
  cost coverage.**
- Layer: portable SDK asset model and target-lowering architecture.
- Dependencies: `LW-0.1`.
- Candidate files: logical Tiled importer, `WorldMap2D`, `WorldTileGrid`, both
  target importers/builders, `docs/ArchitectureRoadmap.md`.
- Work:
  - Compare current 8x8 raw rows with source-cell metatiles and target-owned
    expansion tables using the measured `stage1` payload.
  - Select chunk geometry and deterministic ordering for v1.
  - Select random-access compression boundaries and fixed staging buffers.
  - Define portable metadata versus target-owned packed payload.
  - Record compatibility and versioning rules for the existing world path.
- Acceptance:
  - An accepted ADR defines the binary/logical shape precisely enough for
    `LW-1.3` to implement without reopening core decisions.
  - Worst-case ROM and RAM costs are calculated for full `stage1`.
  - The design supports direct collision and camera edge lookup without
    whole-level decompression.
- Verification: docs links resolve; calculations/tests used by the ADR are
  reproducible; `git diff --check`.
- Non-goals: no production packer or target runtime reader.

### LW-0.3: Select the NES Large Worlds v1 cartridge profile

- Status: **complete; mapper 4 / TVROM-style four-screen profile accepted by
  [`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md) with
  reproducible layout and emulator-capability evidence.**
- Layer: NES target architecture and validation.
- Dependencies: `LW-0.1`.
- Candidate files: `NesRomBuilder`, `NesTarget`, NES acceptance tests,
  `docs/NesFreeScrollRoadmap.md`, NesMcp/ADNES capability surface.
- Work:
  - Measure MMC1, UxROM, and MMC3-family fit for full `stage1`, fixed runtime
    code, switchable PRG data, CHR policy, mirroring/four-screen, audio, and HUD
    follow-ups.
  - Verify the chosen first mapper can be behaviorally tested in the selected
    emulator backend before production implementation.
  - Define mapper-0 fallback/selection rules for small programs.
  - Keep mapper-backed levels separate from the later IRQ-HUD decision if one
    mapper is not required for both.
- Acceptance:
  - One mapper/profile is selected for v1 with an explicit reason and rejected
    alternatives.
  - Fixed/switchable bank ownership, vectors, bank restoration, PRG/CHR shape,
    and emulator proof are specified.
  - Issue #247 is updated or linked without claiming its unrelated NES gaps are
    solved.
- Verification: focused mapper/emulator probe or existing capability evidence;
  NES tests used by the decision; `git diff --check`.
- Non-goals: no complete mapper linker/runtime or HUD implementation.

### LW-0.4: Define the 16-bit world-coordinate and collision-hit contract

- Status: **complete; coordinate and collision contract accepted by
  [`WorldCoordinateCollisionContract.md`](WorldCoordinateCollisionContract.md)
  with reproducible cross-target layout evidence.**
- Layer: portable SDK contract with target lowering.
- Dependencies: `LW-0.1`.
- Candidate files: camera SDK source, `Sdk2DOperation`/intrinsic descriptors,
  world/collision records, runner collision code, GB/NES builders and tests.
- Work:
  - Define logical camera position, map dimensions, hardware-tile coordinates,
    and chunk-local coordinates across the 255 boundary.
  - Replace or version the world-pixel `255` no-hit contract with fixed-layout
    wider data or a screen-relative fact that remains unambiguous.
  - Specify compatibility for byte-backed callers and existing samples.
  - Keep hardware scroll bytes and target implementation details internal.
- Acceptance:
  - The ADR traces full `stage1` positions including hardware-tile column 256
    and floor Y 304 through camera, streaming, collision query, and caller
    resolution.
  - The representation has explicit byte layout and no managed runtime cost.
  - `LW-1.1` and `LW-1.2` can implement independently against the accepted
    contract.
- Verification: representative lowering sketches or tests prove the byte layout
  is implementable on GB and NES; `git diff --check`.
- Non-goals: no production coordinate/collision migration.

## 7. Wave 1 — shared foundation

Wave 1 lands shared contracts before either target receives a production banked
world reader. `LW-1.1` and `LW-1.2` may run in parallel after `LW-0.4`; packing
tasks follow the dependency graph below.

### LW-1.1: Implement 16-bit world dimensions and camera-position lowering

- Status: **complete; shared word camera operands and GB/NES logical
  source-column movement/addressing are implemented with 255 <-> 256 boundary
  coverage while target scroll writes remain bytes.**
- Layer: portable SDK and GB/NES lowering.
- Dependencies: `LW-0.4`.
- Candidate files: SDK camera source/operations, target intrinsic descriptors,
  `GameBoyRomBuilder`, `NesRomBuilder`, camera acceptance tests and docs.
- Work:
  - Carry accepted 16-bit logical dimensions/positions through collection and
    target lowering while keeping hardware scroll writes byte-sized.
  - Preserve current byte-backed source compatibility.
  - Add boundary tests for 255 -> 256 and 256 -> 255 movement.
- Acceptance:
  - Both targets can represent and move across a map column above 255 without a
    banked-data implementation or runtime corruption.
  - Existing small camera samples retain their documented behavior and expected
    ROM shape unless the ADR explicitly allows a change.
- Verification: focused shared/GB/NES camera tests; representative samples;
  full solution tests when shared production code changes; `git diff --check`.
- Non-goals: no chunk packing, mapper, or full `stage1` migration.

### LW-1.2: Implement the widened collision-hit contract

- Status: **complete; world collision Y operands and hit results use complete
  words on GB/NES, the runner uses `i16`/`-1`, screen-hit byte compatibility
  remains intact, and unsafe tall-world narrowing is diagnosed.**
- Layer: portable SDK collision contract and GB/NES lowering.
- Dependencies: `LW-0.4`.
- Candidate files: Portable2D camera/collision helpers, operation/intrinsic
  descriptors, actor/runner collision callers, target builders/tests and docs.
- Work:
  - Implement the accepted hit/no-hit representation with fixed storage.
  - Migrate a focused caller and preserve existing byte-range compatibility.
  - Add floor-hit coverage at world Y 304 and a no-hit case.
- Acceptance:
  - A caller can distinguish a hit at Y 304 from no hit on GB and NES.
  - No heap object, dynamic dispatch, or target hardware value leaks into the
    public API.
- Verification: focused SDK/target collision tests; runner-shaped acceptance;
  full solution tests when shared production code changes; `git diff --check`.
- Non-goals: no general physics engine, chunk packing, or spawn policy change.

### LW-1.3: Add the target-neutral `WorldPack` model

- Status: **complete; Core now models and validates the accepted v1 descriptor,
  directory, clipped chunk coverage, collision profiles, coordinates, and
  opaque target-expansion bytes while retaining explicit legacy adapters.**
- Layer: portable SDK asset model.
- Dependencies: `LW-0.2`, `LW-0.4`.
- Candidate files: Core SDK world/Tiled records, target import boundaries, Core
  tests, architecture guards and docs.
- Work:
  - Add the accepted descriptor, chunk directory, coordinate, collision, and
    target-payload seams without encoding GB/NES storage details in Core.
  - Keep `WorldMap2D`/`WorldTileGrid` compatibility explicit.
  - Validate dimensions, chunk coverage, offsets, and malformed descriptors.
- Acceptance:
  - Core can describe full `stage1` as deterministic chunks without assigning a
    mapper bank or MBC address.
  - Architecture tests prove target details do not leak into the portable model.
- Verification: focused Core and architecture tests; full solution tests;
  `git diff --check`.
- Non-goals: no Tiled packer, compression encoder, or runtime target reader.

### LW-1.4: Compile Tiled worlds into deterministic `WorldPack` chunks

- Status: **complete; the shared Tiled plan assigns canonical visual/collision
  IDs, Core writes canonical raw/element-RLE chunks, and GB/NES expose
  inspectable target payloads without changing production world readers. Full
  `stage1` serializes to 2,550 bytes on GB and 2,762 bytes on NES.**
- Layer: shared Tiled asset pipeline with target-owned payload generation.
- Dependencies: `LW-1.3`.
- Candidate files: logical Tiled importer, GB/NES Tiled importers, world-pack
  tests, full `stage1` fixture/report.
- Work:
  - Convert source cells into the accepted metatile/chunk structure.
  - Preserve background/world composition, collision semantics, target PNG
    variants, tile deduplication, and deterministic ordering.
  - Emit target payloads for inspection without switching production runtime
    readers yet.
- Acceptance:
  - Full `stage1` packs successfully for both targets within the ADR's expected
    size envelope.
  - Decoding test data reconstructs the same visible tiles and collision flags
    as the current raw importer for representative maps.
- Verification: focused pack round-trip/parity tests; baseline budget report;
  full solution tests; `git diff --check`.
- Non-goals: no production GB bank reader, NES mapper runtime, or runner switch.

### LW-1.5: Add deterministic world/cart budget diagnostics

- Status: **complete; `--world-budget-report` emits opt-in deterministic GB/NES
  JSON from the real LW-1.4 payloads, with categorized profile diagnostics and
  no mapper/banker selection.**
- Layer: validation and compiler diagnostics.
- Dependencies: `LW-0.1`, `LW-1.3`.
- Candidate files: CLI/compiler reporting seam, target builders, capability
  diagnostics, tests and target docs.
- Work:
  - Report world cells/metatiles/chunks, packed visual/collision bytes, target
    tile count, staging RAM, PRG/ROM data, bank count, and relevant frame budget.
  - Produce actionable overflow diagnostics naming the resource and limit.
  - Keep normal successful CLI output stable unless an explicit report option
    is selected.
- Acceptance:
  - Full `stage1` and one small sample produce deterministic, testable reports.
  - A failing resource identifies whether addressing, ROM/PRG, CHR/tile count,
    staging RAM, or VBlank budget is responsible.
- Verification: focused CLI/compiler tests; representative GB/NES builds;
  `git diff --check`.
- Non-goals: no automatic mapper/bank selection beyond reporting accepted
  requirements.

## 8. Target production waves — issue-ready

The CLI `--world-budget-report` is a map-only analysis path: it serializes the
selected target `WorldPack` and reports map resources without writing or
linking a ROM. Its `romBytes`/`romPrgBytes` values therefore do not prove final
cartridge placement and its `selectedProfile` does not choose a banker or
mapper. For full `stage1`, the current GB report's 3,862 `romBytes` is the
2,550-byte pack plus generated background patterns, while NES reports the
2,762-byte pack as `romPrgBytes`; neither is a final linked-ROM total. The
production final link must first attempt the existing ROM-only GB or mapper-0
NES layout and select banking only when an actual address/window or capacity
requirement demands it.

Recommended execution and merge order:

1. Start `LW-2.1` and `LW-3.1` from the same current `master`; they may proceed
   in parallel because they own separate target builders and tests.
2. Keep each target chain strictly ordered:
   `LW-2.1 -> LW-2.2 -> LW-2.3 -> LW-2.4 -> LW-2.5` and
   `LW-3.1 -> LW-3.2 -> LW-3.3 -> LW-3.4`.
3. Target-local tasks in different chains may overlap after their own merged
   dependencies. Never run two tasks that edit the same builder/runtime in
   parallel, and do not change `WorldPack` v1 or a public SDK/Core contract to
   make a target task fit.
4. Run `LW-3.5` only after both `LW-2.5` and `LW-3.4` merge. It is the sole
   owner of the joint runner-input migration and both tracked runner ROMs.

### Wave 2 — Game Boy MBC1 world data

#### LW-2.1: Add the fixed-bank MBC1 world-read foundation

- Status: **issue-ready; not started.**
- Layer: Game Boy linker/runtime foundation and validation.
- Dependencies: merged `LW-1.1` through `LW-1.5` and the accepted
  [`WorldPack` v1 contract](WorldPackFormatV1.md).
- Outcome: establish a fixed-bank, target-private MBC1 far-read protocol and a
  formal WRAM reservation for packed-world staging while preserving the exact
  ROM-only output path.
- Scope:
  - Keep `GameBoyRomBuilder.Build` trying the current 32 KiB ROM-only layout
    first (32,432-byte payload limit), then the existing low-five-bit MBC1
    profile of at most 32 16-KiB banks.
  - Add fixed-bank bank-select/read helpers whose entry-bank value is saved
    LIFO and restored on every return. `ProgramCurrentBankAddress` (`$C11C`)
    remains the executable program-bank shadow; it is not reused as proof of
    the actual bank visible at an arbitrary reader entry.
  - Reserve the accepted two visual/two collision/two edge staging area in
    WRAM: 298 bytes for current one-byte IDs and 554 bytes at the v1 maximum,
    plus only fixed scalar slot state owned by the target runtime.
  - Keep bank/window/register details internal to Game Boy lowering; this task
    does not place or read a `WorldPack` yet.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, Game Boy
  runtime/layout records in that target project,
  `src/RetroSharp.GameBoy.Tests/GameBoyBankingRoadmapTests.cs`, and
  `src/RetroSharp.GameBoy.Tests/GameBoyRomCompilerTests.cs`.
- Acceptance:
  - Representative small programs remain byte-identical 32 KiB ROM-only
    cartridges; no new public mapper/bank argument exists.
  - Fixed helpers execute only from bank 0, support bounded nested reads, and
    restore the actual entry bank and its shadow on success, miss, and error.
  - The WRAM layout rejects overlap with user variables, runtime state, audio
    state (`$C210-$C214` today), stack, or the complete 554-byte maximum.
  - Existing program-tail, read-only-data, audio, and trampoline behavior stays
    compatible with the new authoritative bank-state protocol.
- Validation: byte-golden ROM-only tests; focused banking/layout tests through
  `GameBoyTestCpu`; nested and interrupted restoration probes; full Game Boy
  tests; `git diff --check`.
- Non-goals: no `WorldPack` placement, decoder, camera integration, runner
  change, upper MBC1 bank bits/mode, or public/Core banking abstraction.
- Stop conditions: stop if a helper would execute from the switchable window,
  the payload needs upper MBC1 bits or another mode, fixed bank 0 overflows,
  the 554-byte reservation cannot be proven disjoint, or a public banking type
  would be required.

#### LW-2.2: Place serialized WorldPacks in physical MBC1 banks

- Status: **issue-ready; not started.**
- Layer: Game Boy target placement/linking.
- Dependencies: `LW-2.1` and merged `LW-1.4`/`LW-1.5`.
- Outcome: carry the canonical serialized Game Boy `WorldPack` into the final
  ROM, assign deterministic far locations after program banks without
  overlapping audio, and select MBC1 only when the final link requires it.
- Scope:
  - Embed the exact bytes returned by `GameBoyTiledMapImporter.CompileWorldPack`
    and translate only pack-relative offsets into target-private bank/window
    locations.
  - Place full `stage1` deterministically: 2,550 serialized bytes, including
    770 stored visual bytes, 312 stored collision bytes, and 60 chunks (largest
    current stored chunk payload 49 bytes); continue to validate the 7,708-byte
    raw-fallback envelope.
  - Allocate whole-blob or explicit continuation-safe placements after program
    tail banks, with no overlap with the 11,614-byte BGM, 28-byte SFX, tile
    data, or other read-only placements.
  - Suppress duplicate legacy expanded rows, flags, and row-pointer tables only
    on the packed production path; keep the existing raw path byte-identical.
  - Make final link output report the selected layout and every occupied bank;
    do not treat the map-only CLI report or the 128 KiB capacity probe as bytes
    used or a banking decision.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyTiledMapImporter.cs`,
  `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, target program/layout records,
  `src/RetroSharp.GameBoy.Tests/FullStage1BaselineTests.cs`, and
  `src/RetroSharp.GameBoy.Tests/GameBoyBankingRoadmapTests.cs`.
- Acceptance:
  - Extracting the embedded bytes reproduces `CompileWorldPack` byte for byte.
  - A synthetic pack crossing a 16 KiB bank boundary has deterministic,
    non-overlapping far locations, and normal audio placement remains intact.
  - A small packed world that fits the original final ROM remains ROM-only;
    an actual final-link far address/capacity need selects the accepted MBC1
    layout with actionable bank/window diagnostics.
- Validation: exact embedded-pack and deterministic-rebuild tests; synthetic
  boundary/overlap tests; ROM-only golden; full Game Boy tests; map-only CLI
  report comparison; `git diff --check`.
- Non-goals: no format/version change, decoder, collision reader, frame
  streaming, runner migration, or public/Core target-placement fields.
- Stop conditions: stop if placement requires changing `WorldPack` v1, leaks a
  bank/window into Core or source APIs, overlaps program/audio/art, or cannot
  preserve exact ROM-only output.

#### LW-2.3: Implement the fixed-bank WorldPack reader and decoder

- Status: **issue-ready; not started.**
- Layer: Game Boy production runtime reader, decode, and collision lowering.
- Dependencies: `LW-2.2`, merged `LW-1.1`, and merged `LW-1.2`.
- Outcome: read and validate v1 packs through fixed-bank code, decode raw/RLE
  visual or collision planes into bounded slots, and answer collision lookups
  without copying the whole world to RAM.
- Scope:
  - Validate header, target stride, section/directory bounds, clipped chunk
    shape, IDs, codecs, and RLE before staging or exposing camera state.
  - Implement random-access directory/chunk reads, raw and element-RLE decode,
    visual expansion/profile lookup, and separate visual/collision paths using
    two visual plus two collision slots.
  - Prove the current 298-byte staging use and the 554-byte maximum, including
    forced raw/RLE, malformed, 49-byte chunk, and cross-bank fixtures.
  - Restore the actual entry bank and its shadow on every success, miss, bounds
    error, malformed-data result, and bounded nested call while 11,614-byte BGM
    plus 28-byte SFX playback remains active.
  - Preserve the word collision ABI: a floor at world Y 304 returns `30 01` and
    no hit returns `FF FF`.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, target runtime
  state/layout records, `src/RetroSharp.GameBoy.Tests/GameBoyBankingRoadmapTests.cs`,
  `src/RetroSharp.GameBoy.Tests/GameBoyLargeWorldCameraTests.cs`, and
  `src/RetroSharp.GameBoy.Tests/GameBoyMusicTests.cs`.
- Acceptance:
  - Raw and RLE chunks reconstruct the exact LW-1.4 tiles and collision flags;
    malformed data fails before a slot or camera coordinate becomes visible.
  - Visual lookup never requires collision decode and collision lookup never
    requires visual decode or whole-level RAM.
  - Bank-switch traces prove entry-bank restoration across all exits, nesting,
    direction changes, and active audio.
- Validation: shared `WorldPack` corpus plus target forced-codec/malformed tests;
  `GameBoyTestCpu` bank traces; Y-304/no-hit probes; music/tempo regression;
  SameBoy/GameboyMcp interrupt-capable restoration scenario; full Game Boy
  tests; `git diff --check`.
- Non-goals: no camera-edge scheduler, VBlank commit change, runner input
  switch, art residency, spawn indexing, or public API change.
- Stop conditions: stop if the reader/return path would live in a switchable
  bank, a chunk exceeds a declared slot, restoration cannot cover every exit,
  whole-level RAM is required, or v1/coordinate contracts must change.

#### LW-2.4: Integrate staged edges with Game Boy camera streaming

- Status: **issue-ready; not started.**
- Layer: Game Boy camera runtime and VBlank integration.
- Dependencies: `LW-2.3`.
- Outcome: prepare banked/decompressed peer edges outside VBlank and let
  `Camera.Apply()` commit only immutable resident slots within the existing
  21-tile write bound.
- Scope:
  - Replace packed-path legacy row-table reads inside `Camera.Apply()` with
    request, prepare, resident, commit, and release phases.
  - Preserve two same-axis crossings in order, diagonal column-first then
    alternating/staggered commits, slot reuse, reversals, and deferral when the
    required edge is not resident; never expose unstaged camera state.
  - Cover 255 -> 256 and 256 -> 255 logical crossings, horizontal/vertical
    chunk and bank boundaries, and retained peer-slot coordinates/directions.
  - Keep all bank selection, directory reads, and decode outside VBlank; the
    VBlank phase performs at most 21 tile writes and no bank switch.
  - Keep BGM at one tick per frame throughout stalls, reversals, diagonal
    staging, and consecutive crossings.
- Candidate files: `src/RetroSharp.GameBoy/GameBoyRomBuilder.cs`, target camera
  runtime state, `src/RetroSharp.GameBoy.Tests/GameBoyLargeWorldCameraTests.cs`,
  `src/RetroSharp.GameBoy.Tests/GameBoyVerticalScrollAcceptanceTests.cs`, and
  `src/RetroSharp.GameBoy.Tests/GameBoyRunnerAudioTempoTests.cs`.
- Acceptance:
  - Every committed edge was previously resident with the matching
    axis/direction/world-edge tag; malformed or unavailable input defers the
    crossing without advancing visible camera state.
  - Two-edge and diagonal schedules retain their current observable ordering,
    and reversals never mutate a committing slot.
  - Instrumentation proves zero bank/decode work inside VBlank and no commit
    exceeds 21 tile writes.
- Validation: focused bidirectional, two-edge, diagonal, reversal, deferral,
  chunk/bank-boundary, and audio-tempo tests; VRAM inspection in
  `GameBoyTestCpu`; external SameBoy/GameboyMcp visual/timeline acceptance;
  full Game Boy tests; `git diff --check`.
- Non-goals: no third edge slot, dynamic RAM, runner migration, actor activation
  changes, palette/art residency, or NES changes.
- Stop conditions: stop if correctness needs a third slot, bank/decode work in
  VBlank, more than 21 writes, exposure of an unstaged edge, or degraded audio
  cadence.

#### LW-2.5: Prove full stage1 on Game Boy without migrating the shared runner

- Status: **issue-ready; not started.**
- Layer: Game Boy behavioral acceptance and compatibility.
- Dependencies: `LW-2.4`.
- Outcome: prove the complete authored `stage1` on the production packed MBC1
  path while leaving `samples/runner/runner.retrosharp.json` and its shared
  `World.Load(...)` input unchanged for NES.
- Scope:
  - Build a non-destructive full-`stage1` fixture from the 156x20 authored map
    (312x40 hardware tiles) with the real runner gameplay, 11,614-byte BGM,
    28-byte SFX, 148 resident tile indexes / 2,368 art bytes, and complete
    collision.
  - Exercise long bidirectional traversal, chunk/bank boundaries, hardware
    column 255, floor Y 304/no-hit, resets, audio, and visual/collision parity.
  - Retain ROM-only byte identity for representative small programs and prove
    final-link selection rather than relying on `--world-budget-report`.
  - Record durable emulator evidence, but do not regenerate or change either
    tracked shared runner ROM in this task.
- Candidate files: focused Game Boy full-stage acceptance fixtures/tests,
  `src/RetroSharp.GameBoy.Tests/FullStage1BaselineTests.cs`,
  `src/RetroSharp.GameBoy.Tests/GameBoyLargeWorldCameraTests.cs`, and Game Boy
  target documentation. The shared runner source/manifest/ROMs are read-only.
- Acceptance:
  - The non-destructive fixture builds and runs the complete untrimmed map with
    exact LW-1.4 visual/collision parity, complete BGM/SFX, and correct movement
    and collision across all named boundaries.
  - SameBoy/GameboyMcp evidence proves stable frames, audio cadence, bank
    restoration, and no stale/unstaged edges during a scripted traversal.
  - Existing ROM-only golden outputs remain byte-identical.
- Validation: full-stage compile/final-link report; focused Game Boy tests;
  scripted external emulator traversal and screen/memory/bank evidence;
  `tools/gameboy/generate_sample_roms.py --dry-run`; full solution tests;
  `git diff --check`.
- Non-goals: no shared runner-input migration, tracked ROM regeneration, NES
  work, spawn indexing (#244), content residency, or unrelated #247 gaps.
- Stop conditions: stop if acceptance needs trimming, audio removal/no-op,
  runner input changes, tracked ROM edits, Wave 4 spawn/art work, or behavior
  cannot be proven in the selected Game Boy backend.

### Wave 3 — NES mapper-backed world data

#### LW-3.1: Add the MMC3/TVROM linker and fixed-runtime foundation

- Status: **issue-ready; not started.**
- Layer: NES target linker/cartridge/runtime foundation and validation.
- Dependencies: accepted
  [`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md) and
  merged `LW-1.1` through `LW-1.5`.
- Outcome: add target-private PRG sections/configurable bases and a forced-test
  MMC3/TVROM link mode without changing the default mapper-0 bytes.
- Scope:
  - Model 64 KiB PRG as eight 8 KiB banks and emit 16 KiB physical CHR with one
    static resident 8 KiB set. The forced profile header begins
    `4E 45 53 1A 04 02 48 00`.
  - Bootstrap MMC3 PRG mode 0, R6 at `$8000-$9FFF`, pinned R7 at
    `$A000-$BFFF`, fixed code/DPCM/handlers/vectors at `$C000-$FFFF`, linear
    resident CHR pages, bank shadows, and disabled/acknowledged mapper IRQs.
  - Keep every callable instruction, return target, handler, vector, bank
    helper, and DPCM byte out of R6/R7; never write `$A000` mirroring on the
    hardwired four-screen profile.
  - Make `PrgBuilder`/DPCM/vector placement section-aware while preserving the
    exact mapper-0 32 KiB PRG / 8 KiB CHR header and body when it fits.
- Candidate files: `src/RetroSharp.NES/NesRomBuilder.cs`, NES target
  program/layout records, `src/RetroSharp.NES.Tests/NesRomCompilerTests.cs`, and
  `src/RetroSharp.NES.Tests/NesLargeWorldsCartridgeProfileAnalysisTests.cs`.
- Acceptance:
  - Mapper-0 golden ROMs are byte-identical; no gameplay-source mapper argument
    exists.
  - A forced synthetic profile has exact `04 02 48 00` header bytes, 65,536
    PRG bytes, 16,384 CHR bytes, four distinct nametables, fixed vectors, R6/R7
    shadows, PRG mode 0, and IRQs disabled.
  - AprNes through NesMcp `auto` loads, steps, switches R6/R7, and observes the
    combined generated mapper-4 + four-screen behavior.
- Validation: mapper-0 byte golden; header/section/vector/bootstrap/DPCM tests;
  generated `0x48` AprNes smoke with bank and four-nametable probes; full NES
  tests; `git diff --check`.
- Non-goals: no automatic profile selection, `WorldPack` placement/reader,
  streaming, runtime CHR banking, IRQ HUD, runner change, or public mapper API.
- Stop conditions: stop if fixed code plus aligned DPCM and six vectors exceeds
  16,384 bytes, callable code/returns land in R6/R7, the generated combined
  profile fails AprNes, mapper-0 identity changes, or the accepted profile must
  change.

#### LW-3.2: Place WorldPack/data sections and select the final NES profile

- Status: **issue-ready; not started.**
- Layer: NES physical placement, final linking, and profile diagnostics.
- Dependencies: `LW-3.1` and merged `LW-1.4`/`LW-1.5`.
- Outcome: carry the canonical NES pack into final PRG sections, perform
  section-aware DPCM relocation, and choose mapper 0 versus MMC3 from the real
  final link rather than the map-only report.
- Scope:
  - Embed the exact 2,762-byte full-`stage1` pack (770 visual, 312 collision,
    60 chunks, maximum current chunk payload 49) and keep the 7,920-byte
    raw-fallback envelope within one 8,192-byte R6 world bank (272 bytes spare).
  - Place pinned R7 audio/runtime data within 5,012/8,192 bytes, boot-only R7
    nametable/palette data within 4,128/8,192, and fixed code/audio runtime plus
    aligned DPCM plus six vectors within the accepted 13,639/16,384 estimate;
    report the remeasured production totals rather than treating the proxy as a
    guaranteed final fit.
  - Keep total PRG at or below 65,536 bytes, physical CHR exactly 16,384 bytes,
    and the resident static CHR set at or below 8,192 bytes (full `stage1` uses
    191 indexes / 3,056 bytes).
  - Attempt the exact current mapper-0 final link first, even for a packed
    world. Select MMC3 only for a real banked address or mapper-0 PRG/window
    overflow; do not promote to hide coordinate, collision, CHR, palette,
    fixed-region, DPCM, or nametable errors.
  - Treat the current raw no-audio failure (41,851 bytes against 32,762
    available PRG bytes) and the ADR's older 41,907-byte proxy as historical
    measurements; remeasure the production final link after the new reader and
    section layout land.
  - Relocate 64-byte-aligned DPCM from final fixed addresses, keep it below
    `$FFFA`, and emit per-section/window/profile diagnostics.
- Candidate files: `src/RetroSharp.NES/NesTiledWorldImporter.cs`,
  `src/RetroSharp.NES/NesRomBuilder.cs`, NES layout records,
  `src/RetroSharp.NES.Tests/FullStage1BaselineTests.cs`, and
  `src/RetroSharp.NES.Tests/NesRomCompilerTests.cs`.
- Acceptance:
  - Extracted PRG bytes reproduce `CompileWorldPack`; every section is
    deterministic, in bounds, non-overlapping, and diagnosed by owner/window.
  - Small final images that fit mapper 0 remain byte-identical mapper 0;
    forced-test mode still proves MMC3, while automatic selection occurs only
    from an honest final-link need.
  - The linker accepts the measured windows or reports the exact PRG, fixed,
    DPCM, CHR, nametable, coordinate, or collision constraint that failed.
- Validation: exact embedded-pack/deterministic rebuild; mapper-0 golden;
  forced and automatic profile selection; section overlap/window/DPCM boundary
  tests; map-only CLI comparison proving `selectedProfile` is not authoritative;
  full NES tests; `git diff --check`.
- Non-goals: no runtime reader, camera integration, CHR banking, IRQ HUD,
  runner migration, or public/Core cartridge fields.
- Stop conditions: stop if selection masks a non-PRG failure, forces MMC3 when
  final mapper 0 fits, violates any accepted PRG/CHR/window/DPCM bound, or needs
  a public mapper/bank option.

#### LW-3.3: Implement the fixed-bank NES WorldPack reader

- Status: **issue-ready; not started.**
- Layer: NES fixed runtime reader, decode, collision, and bank restoration.
- Dependencies: `LW-3.2` and merged `LW-1.2`.
- Outcome: use one v1 reader for resident mapper-0 sources and far R6 sources,
  with bounded slots and exact R6/shadow restoration on all paths.
- Scope:
  - Validate v1 before staging, then implement directory lookup, raw/RLE decode,
    visual expansion/provenance, collision-profile lookup, and separate
    visual/collision slots.
  - Support an ordinary resident PRG source without mapper writes and an MMC3
    R6 source through fixed code; R7 remains pinned for BGM/SFX/runtime data.
  - Save the actual R6 entry bank LIFO and restore both hardware and software
    shadow on success, miss, bounds/malformed error, nesting, and NMI
    interruption. NMI/IRQ never writes mapper registers or reads R6.
  - Prove 338 bytes for current one-byte IDs and 594 bytes at the v1 maximum;
    preserve world Y 304 and `0xFFFF` no-hit behavior.
- Candidate files: `src/RetroSharp.NES/NesRomBuilder.cs`, NES runtime/layout
  records, `src/RetroSharp.NES.Tests/NesWorldLoadTests.cs`,
  `src/RetroSharp.NES.Tests/NesLargeWorldCameraTests.cs`, and
  `src/RetroSharp.NES.Tests/NesMusicTests.cs`.
- Acceptance:
  - Mapper-0 resident and MMC3 far fixtures decode identical raw/RLE visual and
    collision results; malformed data fails before staging or camera advance.
  - Bank logs prove R6 plus shadow restoration on every exit and nested path,
    while R7, audio cadence, fixed code, DPCM, handlers, and vectors remain
    unchanged.
  - RAM use never exceeds the accepted two visual/two collision/two edge
    staging contract or copies the whole world.
- Validation: shared forced-codec/malformed corpus; resident/far parity;
  Y-304/no-hit; nested/error/NMI restoration and audio tests; AprNes bank
  watchpoints/state/timeline probes; full NES tests; `git diff --check`.
- Non-goals: no PPU edge scheduler, runner migration, runtime CHR banking,
  mapper IRQ/HUD, spawn indexing, or public API change.
- Stop conditions: stop if stack depth/restoration cannot be proven, any call
  or return target lands in R6/R7, NMI must write a mapper register, a slot
  exceeds 594 bytes, whole-level RAM is required, or v1 must change.

#### LW-3.4: Integrate staged four-screen NES streaming

- Status: **issue-ready; not started.**
- Layer: NES camera runtime, staging scheduler, and PPU/NMI integration.
- Dependencies: `LW-3.3`.
- Outcome: prepare immutable peer edge slots outside VBlank/NMI and reuse the
  proven four-screen commit phases without bank/decode work in the frame-critical
  section.
- Scope:
  - Convert packed-path column/row requests into prepare/resident/commit/release
    phases with exact axis/direction/world-edge tags, deferral, reversal, reuse,
    and diagonal staggering.
  - Preserve the current bound of at most 32 column tile writes, or four
    8-tile row phases followed by a separate phase of at most 9 attribute
    writes; a row remains resident until every phase completes.
  - Expand the NES two-byte target record so palette and world/background
    provenance produce the same tiles and attributes as LW-1.4.
  - Keep all R6 selection, directory reads, and raw/RLE decode outside
    VBlank/NMI; keep R7 pinned, mapper IRQ disabled, static resident CHR only,
    and never write `$A000` mirroring.
  - Cover bidirectional 255/256, chunk/R6-bank boundaries, diagonal movement,
    reversals, missing/malformed input, and active BGM/SFX/DPCM.
- Candidate files: `src/RetroSharp.NES/NesRomBuilder.cs`, NES camera/runtime
  state, `src/RetroSharp.NES.Tests/NesLargeWorldCameraTests.cs`,
  `src/RetroSharp.NES.Tests/NesRomCompilerTests.cs`, and
  `src/RetroSharp.NES.Tests/NesWorldLoadTests.cs`.
- Acceptance:
  - Camera state advances only when the correctly tagged immutable edge is
    resident; unavailable or malformed data defers movement without a partial
    nametable/attribute commit.
  - Instrumentation proves no mapper/decode work in VBlank/NMI and every tile
    and attribute phase stays within its exact bound.
  - AprNes screen/nametable evidence matches LW-1.4 visual and palette
    provenance across all four screens with uninterrupted audio/DPCM.
- Validation: focused bidirectional, diagonal, reversal, deferral, phased-row,
  attribute-provenance, chunk/bank-boundary, and audio tests; AprNes via NesMcp
  watchpoints/timelines/screens/state; full NES tests; `git diff --check`.
- Non-goals: no extra slot/dynamic RAM, runtime CHR banking, `$A000` mirroring,
  mapper IRQ/HUD, runner migration, spawn indexing, or unrelated #247 work.
- Stop conditions: stop if staging exceeds 594 bytes/two peers, a commit
  exceeds 32 tiles/four 8-tile row phases/9 attributes, art needs runtime CHR
  residency, mapper/decode work enters VBlank/NMI, or four-screen behavior
  cannot be proven in AprNes.

#### LW-3.5: Migrate the shared runner and prove joint full-stage1 acceptance

- Status: **issue-ready; not started.**
- Layer: cross-target acceptance, shared sample integration, and tracked
  artifacts.
- Dependencies: `LW-2.5` and `LW-3.4`.
- Outcome: switch the shared runner from `stage1.playable.tmj` to the complete
  authored `stage1.tmj`, regenerate both tracked ROMs, and prove the final GB
  and NES production paths together.
- Scope:
  - Own the only shared runner manifest/source/constants/map-input migration in
    Waves 2/3 and regenerate both `samples/runner/bin/runner.gb` and
    `samples/runner/bin/runner.nes` from the same commit.
  - Preserve the complete 156x20 / 312x40 map, exact LW-1.4 packs (GB 2,550;
    NES 2,762; both 770 visual + 312 collision and 60 chunks), target BGM/SFX,
    NES DPCM, collision at Y 304, and movement across column 255 both ways.
  - Exercise honest automatic final-link selection plus forced MMC3 layout
    acceptance; do not assert that full `stage1` must use MMC3 if the completed
    production image genuinely fits mapper 0.
  - Capture behavioral GB and NES evidence, final-link/window reports, visual
    parity, and small-ROM ROM-only/mapper-0 byte-identity checks.
- Candidate files: `samples/runner/runner.retrosharp.json`,
  `samples/runner/src/**`, runner map/constants only as required,
  `samples/runner/bin/runner.gb`, `samples/runner/bin/runner.nes`, GB/NES runner
  acceptance tests, and target/runner documentation.
- Acceptance:
  - One shared runner source builds both tracked ROMs against complete
    `stage1`; neither target trims content, disables audio/DPCM/collision, or
    substitutes a no-op.
  - GameboyMcp/SameBoy and NesMcp/AprNes scripted traversals prove stable
    visuals, all four NES nametables, collision/no-hit, bidirectional boundary
    movement, audio cadence, restoration, and bounded commits.
  - The generator dry-run is clean after deliberate regeneration; the full
    solution and small-ROM byte-golden suites pass.
- Validation: focused full-stage and runner suites; automatic/forced final-link
  reports; external scripted traversals with screens/timelines/watchpoints;
  `tools/gameboy/generate_sample_roms.py`; rerun it with `--dry-run`;
  `dotnet test RetroSharp.sln -m:1`; `git diff --check`.
- Non-goals: no #244 spawn-index implementation, Wave 4 tileset/palette
  residency, IRQ HUD, multi-level transitions, or closure of unrelated #247
  gaps.
- Stop conditions: stop if either target cannot build or behave with the same
  complete input, if acceptance needs content/audio/collision degradation, if
  the mapper profile must change, or if #244/Wave 4 is required to prove the
  stated world-reader behavior.

### Wave 4 — content residency and integration

- Spawn indexing/bucketing remains owned by issue #244; Waves 2/3 do not
  duplicate it.
- Tileset/palette pages for regions with more art than the resident tile budget.
- Multi-level directory/transition policy if required by a real sample.
- Post-migration cross-target regression and a final budget report after any
  Wave 4 residency feature is adopted.

## 9. Validation matrix

| Concern | Required evidence |
| --- | --- |
| Portable boundary | Architecture tests; no bank, mapper, window, register, PPU, or MBC term in the public SDK/Core world contract |
| Canonical pack | GB 2,550 bytes and NES 2,762 bytes; each has 770 stored visual bytes, 312 stored collision bytes, 60 chunks, and raw/RLE plus malformed-data coverage |
| Address width | 255 -> 256 and 256 -> 255 crossings on both targets |
| Collision | Hit at world Y 304 plus unambiguous `0xFFFF` no-hit result |
| GB placement/restoration | Deterministic MBC1 far placement; every exit/nested/audio-active path restores the actual entry bank and shadow |
| GB runtime budget | 298-byte current / 554-byte maximum staging; bank/decode outside VBlank; at most 21 tile writes in VBlank |
| NES cartridge | Final-link mapper-0-first selection; forced MMC3 header `04 02 48 00`; PRG <=65,536, physical CHR 16,384, resident CHR <=8,192, fixed code/DPCM/vectors <=16,384 |
| NES windows/restoration | `WorldPack` raw-fallback <=7,920/8,192 R6; pinned data <=5,012/8,192 R7; boot data <=4,128/8,192 R7; every R6 exit restores hardware and shadow while R7 stays pinned |
| NES runtime budget | 338-byte current / 594-byte maximum staging; bank/decode outside VBlank/NMI; at most 32 column tiles or four 8-tile row phases, then at most 9 attributes |
| Behavioral backends | Game Boy interrupt/restoration/visual traversal in SameBoy/GameboyMcp; generated mapper-4 + four-screen `0x48` ROM in AprNes through NesMcp `auto` |
| Visual/audio parity | Decoded and visible tiles/collision match LW-1.4; GB BGM/SFX and NES BGM/SFX/DPCM remain complete and frame-correct |
| Small-ROM stability | Representative ROM-only GB and mapper-0 NES outputs remain byte-identical |
| Final shared acceptance | `LW-2.5` proves GB without changing the runner input; `LW-3.5` then migrates complete `stage1` jointly and regenerates both tracked ROMs with no trimming or target no-ops |

Durable closeout commands remain:

```bash
dotnet test RetroSharp.sln -m:1
tools/gameboy/generate_sample_roms.py --dry-run
git diff --check
```

Tasks that change tracked sample source deliberately run the non-dry-run ROM
generator and validate the resulting ROMs through the appropriate Game Boy/NES
debug workflow.

## 10. Remote tracking

- Milestone: [Large Worlds v1 — full stage1](https://github.com/SuperJMN/RetroSharp/milestone/11).
- Parent/integrator issue: [#275 — EPIC: Large Worlds v1 — scalable banked map streaming for GB/NES](https://github.com/SuperJMN/RetroSharp/issues/275).
- Wave 0 native subissues:
  - [#276 — LW-0.1: freeze full stage1 acceptance baseline and budget report](https://github.com/SuperJMN/RetroSharp/issues/276)
  - [#277 — LW-0.2: decide WorldPack metatile, chunk, compression, and staging format](https://github.com/SuperJMN/RetroSharp/issues/277)
  - [#279 — LW-0.3: select the NES Large Worlds v1 cartridge profile](https://github.com/SuperJMN/RetroSharp/issues/279)
  - [#278 — LW-0.4: define 16-bit world-coordinate and collision-hit contract](https://github.com/SuperJMN/RetroSharp/issues/278)
- Wave 1 native subissues:
  - [#282 — LW-1.1: implement 16-bit world dimensions and camera-position lowering](https://github.com/SuperJMN/RetroSharp/issues/282)
  - [#280 — LW-1.2: implement the widened collision-hit contract](https://github.com/SuperJMN/RetroSharp/issues/280)
  - [#281 — LW-1.3: add the target-neutral WorldPack model](https://github.com/SuperJMN/RetroSharp/issues/281)
  - [#284 — LW-1.4: compile Tiled worlds into deterministic WorldPack chunks](https://github.com/SuperJMN/RetroSharp/issues/284)
  - [#283 — LW-1.5: add deterministic world and cartridge budget diagnostics](https://github.com/SuperJMN/RetroSharp/issues/283)
- Wave 2 and Wave 3 native subissues: links are pending the first publication
  of this issue-ready plan. Do not invent issue numbers in documentation; add
  the real links after the native subissues are created under #275.
- Related open issues: [#244](https://github.com/SuperJMN/RetroSharp/issues/244)
  (Wave 4 wide-map spawn activation) and
  [#247](https://github.com/SuperJMN/RetroSharp/issues/247) (broader NES target
  gaps). Update #247 only with a link to the Wave 3 mapper-backed slice; its
  unrelated gaps remain open and are not duplicated here.

The parent issue remains the integrator surface. After first publication,
implementation agents receive one native `LW-2.x` or `LW-3.x` child issue, not
the parent or an open-ended request to continue the epic; no created child is
started as part of planning/publication.
