# Large Worlds Roadmap (banked map content for Game Boy and NES)

Status: **complete; Waves 0 and 1, Game Boy `LW-2.1` through `LW-2.5`, NES
`LW-3.1` through `LW-3.4`, and joint acceptance `LW-3.5` are implemented.**
Last updated: 2026-07-14.

Acceptance policy: acceptance is judged by observable gameplay fluidity, not byte-for-byte output. The byte-identical ROM, hardcoded SHA-256, exact CPU-cycle, and cross-emulator parity criteria mentioned below are diagnostic baselines rather than gates. Tracked ROMs are regeneratable artifacts. See the Acceptance Policy in `AGENTS.md`.

Post-epic content revision: the runner now authors 56 one-way `Platform` cells
on its existing green ledges. This does not change WorldPack v1 or the completed
banking graph; it adds one collision profile and changes the current production
pack measurements to GB 2,568 / NES 2,780 bytes, with 770 visual and 326
collision stored bytes. Historical LW checkpoint numbers below remain the
evidence captured when those milestones landed.

This roadmap is the executable plan for levels that exceed the legacy
one-byte world addressing and current monolithic ROM-data budgets. It coordinates the
portable world contract, target-owned map packing, Game Boy MBC1 data access,
NES mapper-backed data, collision, art residency, and acceptance validation.

`docs/ArchitectureRoadmap.md` owns the durable language-versus-SDK-versus-target
boundary. This file owns the detailed Large Worlds task graph. GitHub owns
remote state and review; the parent epic is not an implementation unit.

Navigation after AIN-1 through AIN-8: candidate `GameBoyRomBuilder.cs` and
`NesRomBuilder.cs` paths in the completed task cards are historical landing
records. Current runtime addresses come from `GameBoyRuntimeMemoryLayout` /
`NesRuntimeMemoryLayout`; source/runtime traversal lives in the target
`*RuntimeCompiler*` modules; portable SDK emission lives in the target
`*SdkOperationLowerer*` modules; and cartridge placement/byte assembly uses the
purpose-named layout and builder modules. Start from `docs/SdkArchitecture.md`
and the target guide rather than a historical candidate line.

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

`LW-3.5` migrated the shared runner from the historical derived
`stage1.playable.tmj` input to the complete map and regenerated both tracked
ROMs from one manifest. No target trims the level, stubs audio, discards
collision, or lowers the packed path as a no-op merely to make the ROM fit.
The consolidated artifact hashes, probe commands, and SameBoy/AprNes MCP
observations are recorded in
[`docs/LargeWorldsStage1Checkpoint.md`](LargeWorldsStage1Checkpoint.md).

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
  bytes. Game Boy and NES packed/banked readers and staged camera schedulers are
  implemented.
- NES still emits mapper 0 with 32 KiB PRG and 8 KiB CHR by default. `LW-3.1`
  adds the forced MMC3/TVROM foundation and `LW-3.2` adds mapper-0-first
  selection plus mapper-backed level-data placement, and `LW-3.3` adds the
  fixed-bank production pack reader without migrating the runner.
- `Camera.AabbHitTop(...)` now exposes a complete world-pixel word with `-1`
  as no hit; screen-relative hit-top retains its byte-range `255` sentinel.
- Tiled 16x16 cells expand into target-owned 8x8 visuals and collision cells;
  production `WorldPack` payloads preserve them as 60 random-access chunks
  instead of retaining the legacy 24,960-byte raw grid.
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
9. **NES v1 banks data, not executable code.** `WorldPack` and other target
   data may occupy ordered R6/R7 sections, but every callable instruction,
   return target, handler, helper, DPCM byte, and vector remains in the fixed
   `$C000-$FFFF` region. Automatic executable-code banking is a separate future
   architecture decision.

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

## 8. Target production waves — Game Boy acceptance next, NES ready

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

1. Game Boy `LW-2.1` through `LW-2.5` are complete.
2. `LW-3.1` is complete. Continue with
   [LW-3.2 / #302](https://github.com/SuperJMN/RetroSharp/issues/302) from the
   merged `LW-3.1` foundation; keep the NES chain strictly ordered through
   `LW-3.3 -> LW-3.4`.
3. Target-local tasks in different chains may overlap after their own merged
   dependencies. Never run two tasks that edit the same builder/runtime in
   parallel, and do not change `WorldPack` v1 or a public SDK/Core contract to
   make a target task fit.
4. Run `LW-3.5` only after both `LW-2.5` and `LW-3.4` merge. It is the sole
   owner of the joint runner-input migration and both tracked runner ROMs.

### Wave 2 — Game Boy MBC1 world data

#### LW-2.1: Add the fixed-bank MBC1 world-read foundation

- Status: **implementation complete through
  [#296](https://github.com/SuperJMN/RetroSharp/issues/296); issue closure is
  tracked on GitHub.**
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
- Implemented foundation:
  - `$C1FA` is the authoritative actual-visible-bank shadow; `$C11C` remains
    executable program-bank state. Every generated `$2000` selection updates
    `$C1FA` first. Every MBC1 layout initializes the actual hardware/shadow to
    bank 1, while layouts without a program tail leave `$C11C` untouched.
  - The fixed-bank byte reader accepts banks `1..31`, distinguishes bank-zero
    miss from unsupported-bank/window error through its private status/flags
    ABI, saves the actual entry bank on the hardware stack, and restores it
    LIFO on every exit, including nested and interrupt-like re-entry.
  - WRAM is formalized as user `$C000-$C0DF`, runtime `$C0E0-$C14C`, fixed
    world scalar/tag/validation state `$C1F0-$C1FC`, audio `$C210-$C214`, and staging
    `$C300-$C529`; both 298/554-byte shapes fit and 555 bytes fail.
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

- Status: **implementation complete through
  [#297](https://github.com/SuperJMN/RetroSharp/issues/297); issue closure is
  tracked on GitHub.**
- Layer: Game Boy target placement/linking.
- Dependencies: [LW-2.1 / #296](https://github.com/SuperJMN/RetroSharp/issues/296)
  (native blocked-by) and merged `LW-1.4`/`LW-1.5`.
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
- Implemented placement:
  - `World.Load(...)` retains the exact serialized GB pack beside its historical
    lowered map. A pack-only final link first tries the bytes inline in the
    ordinary 32 KiB image, so a fitting world stays ROM-only. Only an actual
    linked overflow retries as MBC1.
  - MBC1 packs are placed after executable program-tail banks in explicit
    continuation segments. A relative offset is translated as
    `linear = (baseAddress - $4000) + offset`, then
    `bank = baseBank + linear / $4000` and
    `address = $4000 + linear % $4000`; serialized bytes and v1 relative
    offsets are unchanged.
  - The target-private final build result records the selected profile, padded
    ROM size, stable physical ranges and owners, CPU-window addresses, and
    occupied banks. Pack, generated art/tilemap data, code, BGM, and SFX ranges
    are copied and tested through that report rather than byte-searching.
  - Packed links emit no legacy expanded map rows, flags, background stream
    rows, or row-pointer tables. LW-2.2 originally retried the unchanged raw
    compatibility layout when camera/collision lowering still referenced one
    of those labels; LW-2.3 replaces the packed collision dependency with the
    production reader rather than emitting both representations.
  - Full normalized `stage1` remains exactly 2,550 bytes with 60 chunks, 770
    stored visual bytes, 312 stored collision bytes, and a 49-byte largest
    combined stored chunk. A valid synthetic pack proves final-ROM continuation
    across 16 KiB windows, and data-only MBC1 builds initialize the existing
    fixed-bank foundation.
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

- Status: **implementation complete through
  [#298](https://github.com/SuperJMN/RetroSharp/issues/298); issue closure is
  tracked on GitHub.**
- Layer: Game Boy production runtime reader, decode, and collision lowering.
- Dependencies: [LW-2.2 / #297](https://github.com/SuperJMN/RetroSharp/issues/297)
  (native blocked-by), merged `LW-1.1`, and merged `LW-1.2`.
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
- Implemented reader:
  - The compiler semantically validates the exact v1 header, collision
    profiles, directory, raw/RLE planes, decoded ID bounds, and encoded
    consumption before linking. At runtime, startup compares the v1 header
    byte-for-byte and fingerprints every linked WorldPack byte with the
    position-sensitive 32-bit check introduced by #332. Staged RLE rechecks
    stored consumption, packet fit, and IDs; staged raw decoding validates
    every ID before cache publication and checks it again before visual
    expansion or collision-profile lookup. A failed validation is
    cached as malformed and halts before `Main`, while successful validation
    is cached before camera, audio, or user state becomes visible.
  - Fixed-bank random lookup maps complete hardware coordinates to the clipped
    8x8 source-metatile directory, decodes only the requested plane, and resolves
    Game Boy visual expansions or collision profiles independently. The reader
    accepts v1 metatile axes without imposing a power-of-two format change.
  - Two visual, two collision, and two edge slots remain disjoint at 298 bytes
    for current one-byte IDs and 554 bytes for the two-byte maximum. No complete
    level is copied to WRAM.
  - Every helper and bank write executes in bank 0. Success, miss, bounds,
    malformed, cross-window, nested, and interruptible reads restore the actual
    entry bank plus `$C1FA` LIFO while leaving `$C11C` unchanged.
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

- Status: **implemented by [#299](https://github.com/SuperJMN/RetroSharp/issues/299).**
- Cadence follow-up: [#332](https://github.com/SuperJMN/RetroSharp/issues/332)
  keeps the production packed route, validates the complete pack before LCD
  enable, reuses edge lookup/decode/bank state, and bounds each measured cold,
  chunk-boundary, or bank-sensitive request-to-resident transition to one DMG
  frame. SameBoy evidence is recorded in
  [`GameBoyPackedCameraCadenceAcceptance.md`](GameBoyPackedCameraCadenceAcceptance.md).
- Production functional follow-up:
  [#339](https://github.com/SuperJMN/RetroSharp/issues/339) binds the exact
  tracked vertical, diagonal, free-scroll, and dead-zone GB/NES cartridges to
  the shared runner. Game Boy uses three standard or six diagonal visual cache
  slots, serializes diagonal preparation, and retains zero forbidden VBlank
  work; NES also proves derived Tiled palette aliases. See
  [`PackedTiledFunctionalAcceptance.md`](PackedTiledFunctionalAcceptance.md).
- Layer: Game Boy camera runtime and VBlank integration.
- Dependencies: [LW-2.3 / #298](https://github.com/SuperJMN/RetroSharp/issues/298)
  (native blocked-by).
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
  - The packed scheduler owns two immutable 21-byte peer slots and explicit
    `requested -> preparing -> resident -> committing -> released` metadata.
    Same-axis peers drain in crossing order across consecutive VBlanks; the raw
    compatibility scheduler keeps its existing two-edge single-apply behavior.
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
    exceeds 21 tile writes. The implementation also instruments directory work
    and reserves LY 136-153 as a guard band around sensitive packed reads.
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

- Status: **implementation complete through
  [#300](https://github.com/SuperJMN/RetroSharp/issues/300); issue closure is
  tracked on GitHub.**
- Layer: Game Boy behavioral acceptance and compatibility.
- Dependencies: [LW-2.4 / #299](https://github.com/SuperJMN/RetroSharp/issues/299)
  (native blocked-by).
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
  - Regress the implemented packed scheduler under that full traversal: 16-bit
    edge tags, request/prepare/resident/commit/release ownership, 19-column /
    21-row write bounds, zero forbidden bank/directory/decode work in VBlank,
    the LY 136-153 guard band, safe deferral/reversal, and one BGM tick per
    real frame.
  - Retain ROM-only byte identity for representative small programs and prove
    final-link selection rather than relying on `--world-budget-report`.
  - Record durable emulator evidence, but do not regenerate or change either
    tracked shared runner ROM in this task.
- Implemented acceptance:
  - A temporary normalized fixture redirects only the composed test source to
    the complete 156x20 map. The shared runner manifest/source/input and both
    tracked runner ROMs remain byte-identical.
  - The real runner final link selects the 128 KiB MBC1 profile with the exact
    2,550-byte pack, 11,614-byte BGM, 28-byte SFX, and 2,368 art bytes; the
    smaller traversal probe stays ROM-only, proving final-link selection.
  - Runtime tests reconstruct all 60 visual/collision chunks, preserve Y=304 /
    `FFFF`, traverse horizontally in both directions and vertically, and cover
    column 256, 19/21 writes, lifecycle/tag ownership, deferral/reversal, the
    guard band, bank restoration, and per-frame audio ticks.
  - The packed banked emitter no longer appends duplicate legacy rows, and the
    test CPU implements the `LD E,B` opcode reached by real packed collision
    gameplay. `$C19D` records packed audio ticks for durable emulator evidence.
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
  - Full-stage instrumentation keeps every packed-scheduler invariant from
    `LW-2.4` intact: matching 16-bit tags, at most 19 column or 21 row writes,
    zero forbidden VBlank work, and no visible advance before a resident commit.
  - Existing ROM-only golden outputs remain byte-identical.
- Validation: full-stage compile/final-link report; focused Game Boy tests;
  packed-scheduler counters/guard-band assertions; scripted external emulator
  traversal and screen/memory/bank/audio evidence;
  `tools/gameboy/generate_sample_roms.py --dry-run`; full solution tests;
  `git diff --check`.
- Non-goals: no shared runner-input migration, tracked ROM regeneration, NES
  work, spawn indexing (#244), content residency, or unrelated #247 gaps.
- Stop conditions: stop if acceptance needs trimming, audio removal/no-op,
  runner input changes, tracked ROM edits, Wave 4 spawn/art work, or behavior
  cannot be proven in the selected Game Boy backend.

### Wave 3 — NES mapper-backed world data

#### LW-3.1: Add the MMC3/TVROM linker and fixed-runtime foundation

- Status: **implementation complete; the target-private forced profile,
  fixed-runtime linker foundation, focused tests, and AprNes/NesMcp acceptance
  are implemented.**
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

- Status: **implementation complete; canonical/multi-R6 placement, section
  ownership, diagnostics, and final-link selection are implemented and
  validated.**
- Layer: NES physical placement, final linking, and profile diagnostics.
- Dependencies: [LW-3.1 / #301](https://github.com/SuperJMN/RetroSharp/issues/301)
  (native blocked-by) and merged `LW-1.4`/`LW-1.5`.
- Outcome: carry the canonical NES pack into final PRG sections, perform
  section-aware DPCM relocation, and choose mapper 0 versus MMC3 from the real
  final link rather than the map-only report.
- Scope:
  - Embed the exact 2,762-byte full-`stage1` pack (770 visual, 312 collision,
    60 chunks, maximum current chunk payload 49) and keep the 7,920-byte
    raw-fallback envelope within one 8,192-byte R6 world bank (272 bytes spare).
  - Treat 8,192 bytes as the size of one R6 CPU window, not as a maximum
    `WorldPack` length. Place a larger canonical pack unchanged across an
    ordered list of R6-owned continuation segments; the physical bank ids may
    be non-contiguous because pinned/boot R7 ownership is independent.
  - Translate each 32-bit pack-relative offset through that ordered segment
    layout without inserting serialized padding or rewriting v1 offsets, and
    report the logical segment-to-physical-bank mapping with its owner/range.
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
- Implemented evidence:
  - The linker attempts the byte-identical historical mapper-0 image first.
    A `World.Load(...)` that still fits therefore has no inserted pack bytes;
    only a real PRG/DPCM layout failure retries the internal MMC3 profile.
  - MMC3 physical ownership is R6 world banks `0, 3, 4, 5`, pinned R7 bank
    `1`, boot-only R7 bank `2`, and fixed banks `6, 7`. Pack offsets translate
    over the ordered R6 list, so the 8 KiB window is not a pack-length cap.
  - The complete normalized `stage1` placement probe embeds the exact 2,762
    bytes and remeasures 5,012 pinned-R7 bytes, 4,128 boot-R7 bytes, 2,151
    fixed payload bytes including aligned DPCM/vectors, and 3,056 resident CHR
    bytes. Physical PRG is 65,536 bytes and physical CHR is 16,384 bytes.
  - The existing full runner-shaped raw runtime still reports the fixed-region
    overflow that requires `LW-3.3`; no NES pack reader, camera integration,
    runner migration, CHR banking, or IRQ HUD was added here.
- Candidate files: `src/RetroSharp.NES/NesTiledWorldImporter.cs`,
  `src/RetroSharp.NES/NesRomBuilder.cs`, NES layout records,
  `src/RetroSharp.NES.Tests/FullStage1BaselineTests.cs`, and
  `src/RetroSharp.NES.Tests/NesRomCompilerTests.cs`.
- Acceptance:
  - Extracted PRG bytes reproduce `CompileWorldPack`; every section is
    deterministic, in bounds, non-overlapping, and diagnosed by owner/window.
  - A canonical synthetic pack larger than 8,192 bytes reconstructs byte for
    byte after crossing at least one R6 continuation boundary; its relative
    offsets and section order are unchanged, and no single-window pack-length
    restriction is introduced.
  - Small final images that fit mapper 0 remain byte-identical mapper 0;
    forced-test mode still proves MMC3, while automatic selection occurs only
    from an honest final-link need.
  - The linker accepts the measured windows or reports the exact PRG, fixed,
    DPCM, CHR, nametable, coordinate, or collision constraint that failed.
- Validation: exact embedded-pack/deterministic rebuild; synthetic multi-R6
  continuation/reconstruction; mapper-0 golden; forced and automatic profile
  selection; section overlap/window/DPCM boundary tests; map-only CLI comparison
  proving `selectedProfile` is not authoritative; full NES tests;
  `git diff --check`.
- Non-goals: no runtime reader, camera integration, CHR banking, IRQ HUD,
  runner migration, or public/Core cartridge fields.
- Stop conditions: stop if selection masks a non-PRG failure, forces MMC3 when
  final mapper 0 fits, constrains every valid pack to one R6 window, rewrites
  canonical bytes/offsets, violates any accepted PRG/CHR/window/DPCM bound, or
  needs a public mapper/bank option.

#### LW-3.3: Implement the fixed-bank NES WorldPack reader

- Status: **published as [#303](https://github.com/SuperJMN/RetroSharp/issues/303);
  implementation complete; issue closure is tracked on GitHub.**
- Layer: NES fixed runtime reader, decode, collision, and bank restoration.
- Dependencies: [LW-3.2 / #302](https://github.com/SuperJMN/RetroSharp/issues/302)
  (native blocked-by) and merged `LW-1.2`.
- Outcome: use one v1 reader for resident mapper-0 sources and far R6 sources,
  with bounded slots and exact R6/shadow restoration on all paths.
- Scope:
  - Validate v1 before staging, then implement directory lookup, raw/RLE decode,
    visual expansion/provenance, collision-profile lookup, and separate
    visual/collision slots.
  - Support an ordinary resident PRG source without mapper writes and an MMC3
    R6 source through fixed code; R7 remains pinned for BGM/SFX/runtime data.
  - Consume the ordered R6 continuation layout from `LW-3.2` and translate
    every 32-bit pack-relative byte/range through it. Header sections,
    directory lookup, encoded visual/collision planes, and chunk payloads may
    cross an 8 KiB physical boundary without changing the canonical pack.
  - Save the actual R6 entry bank LIFO and restore both hardware and software
    shadow on success, miss, bounds/malformed error, nesting, and NMI
    interruption. NMI/IRQ never writes mapper registers or reads R6.
  - Prove 338 bytes for current one-byte IDs and 594 bytes at the v1 maximum;
    preserve world Y 304 and `0xFFFF` no-hit behavior.
- Implemented evidence:
  - `NesWorldPackRuntime` validates canonical metadata before publishing a
    slot, reads resident mapper-0 bytes without mapper writes, and translates
    32-bit pack-relative offsets through ordered physical R6 banks `0, 3, 4,
    5` from fixed code.
  - Shared raw/element-RLE decoders fill two independent visual and collision
    slot pairs; visual lookup returns the NES tile plus palette/provenance byte,
    while collision lookup resolves the selected profile/subcell byte.
  - Actual 6502 execution tests cover directory and plane ranges on both sides
    of an 8 KiB continuation, two-byte IDs, malformed payloads, miss/bounds,
    nested reads, and an NMI injected after R6 selection. Every path restores
    hardware R6 and `$0324`; R7 and the fixed handlers are untouched.
  - The exact payload buffers remain 338 bytes for one-byte IDs and 594 bytes
    for two-byte IDs. The normalized full-`stage1` final-link probe now measures
    4,327 fixed bytes (2,176 reader bytes over the LW-3.2 placement-only probe),
    5,012 pinned R7 bytes, 4,128 boot R7 bytes, and 3,056 resident CHR bytes.
  - Deterministic decoder measurements, with validation already cached, are
    15,883 raw / 7,488 RLE cycles resident and 27,198 raw / 9,193 RLE cycles
    through R6; `LW-3.4` owns the later scheduling budget and PPU commits.
  - A generated 81,936-byte mapper-4 probe (`a68a7bb26d37c274c981c6e5f7c90394580cb7e4ded89bb8e6c526eb50f3123b`)
    completes in AprNes at frame 167 / cycle 4,998,679 with validation `01`,
    visual/collision statuses `00/00`, tile/metadata/collision `14/04/00`, R6
    restored to entry bank `00`, and pinned R7 `01`. A write watchpoint over
    `$8000-$8001` remains quiet for the following 120 frames while APU writers
    continue across `$4000-$4017`; a further deterministic 60-frame input
    timeline preserves the same bank and result markers.
- Candidate files: `src/RetroSharp.NES/NesRomBuilder.cs`, NES runtime/layout
  records, `src/RetroSharp.NES.Tests/NesWorldLoadTests.cs`,
  `src/RetroSharp.NES.Tests/NesLargeWorldCameraTests.cs`, and
  `src/RetroSharp.NES.Tests/NesMusicTests.cs`.
- Acceptance:
  - Mapper-0 resident and MMC3 far fixtures decode identical raw/RLE visual and
    collision results; malformed data fails before staging or camera advance.
  - A synthetic pack larger than 8,192 bytes proves directory lookup plus raw
    and RLE plane reads on both sides of, and across, an R6 continuation
    boundary. Physical bank ids need not be contiguous; pack-relative offsets
    remain the only serialized addresses.
  - Bank logs prove R6 plus shadow restoration on every exit and nested path,
    while R7, audio cadence, fixed code, DPCM, handlers, and vectors remain
    unchanged.
  - RAM use never exceeds the accepted two visual/two collision/two edge
    staging contract or copies the whole world.
- Validation: shared forced-codec/malformed corpus; resident/far parity;
  multi-R6 directory/plane/payload boundary fixtures; Y-304/no-hit;
  nested/error/NMI restoration and audio tests; deterministic raw/RLE decode
  cycle measurements for the `LW-3.4` latency budget; AprNes bank
  watchpoints/state/timeline probes; full NES tests; `git diff --check`.
- Non-goals: no PPU edge scheduler, runner migration, runtime CHR banking,
  mapper IRQ/HUD, spawn indexing, or public API change.
- Stop conditions: stop if stack depth/restoration cannot be proven, a valid
  v1 pack must be rewritten/padded or limited to one R6 window, any call or
  return target lands in R6/R7, NMI must write a mapper register, a slot exceeds
  594 bytes, whole-level RAM is required, or v1 must change.

#### LW-3.4: Integrate staged four-screen NES streaming

- Status: **implementation complete through
  [#304](https://github.com/SuperJMN/RetroSharp/issues/304); issue closure is
  tracked on GitHub.**
- Layer: NES camera runtime, staging scheduler, and PPU/NMI integration.
- Dependencies: [LW-3.3 / #303](https://github.com/SuperJMN/RetroSharp/issues/303)
  (native blocked-by).
- Outcome: prepare immutable peer edge slots outside VBlank/NMI and reuse the
  proven four-screen commit phases without bank/decode work in the frame-critical
  section.
- Scope:
  - Convert packed-path column/row requests into prepare/resident/commit/release
    phases with exact axis/direction/world-edge tags, deferral, reversal, reuse,
    and diagonal staggering.
  - Use the implemented `LW-2.4` lifecycle and publish-after-resident semantics
    as a behavioral reference, not a code dependency: keep 16-bit world-edge
    tags, reject wrong-tag work, preserve committing-slot immutability, and
    expose no partial camera advance on deferral or reversal.
  - Preserve the current bound of at most 32 column tile writes, or four
    8-tile row phases followed by a separate phase of at most 9 attribute
    writes; a row remains resident until every phase completes.
  - Expand the NES two-byte target record so palette and world/background
    provenance produce the same tiles and attributes as LW-1.4.
  - Keep all R6 selection, directory reads, and raw/RLE decode outside
    VBlank/NMI; keep R7 pinned, mapper IRQ disabled, static resident CHR only,
    and never write `$A000` mirroring.
  - Instrument request, resident, and commit frames. With worst-case accepted
    raw/RLE data, an R6 continuation crossing, and BGM/SFX/DPCM active, a valid
    requested edge must become resident within at most two normal gameplay
    frames; diagonal staggering must not starve either axis.
  - Cover bidirectional 255/256, chunk/R6-bank boundaries, diagonal movement,
    reversals, missing/malformed input, and active BGM/SFX/DPCM.
- Implemented runtime:
  - Two target-private 41-byte edge payloads keep immutable peer metadata and
    16-bit axis/direction/world-edge/orthogonal tags through
    request/prepare/resident/commit/release. Pending column and row descriptors
    are independent, correctly tagged resident peers alternate without axis
    starvation, and reversals release only uncommitted resident work.
  - Preparation performs validation, R6 selection, directory access, raw direct
    lookup or bounded RLE decode, tile expansion, and LW-1.4 attribute planning
    before VBlank. Target expansion and palette/provenance lookup tables remain
    pinned in R7; camera-only packed links omit duplicate legacy map data.
  - Visible camera coordinates publish only after the matching pending axis
    clears. A column commits at most 32 tile bytes plus at most 9 attributes; a
    row remains committing across four 8-tile phases and one attribute phase.
    The critical section records any R6, directory, or decode work as a contract
    violation.
  - The normalized full-`stage1` camera probe links as mapper 4 / TVROM with an
    8,989-byte fixed payload, 7,310 pinned R7 bytes, 4,128 boot R7 bytes, and
    1,536 resident CHR bytes. The unchanged full runner payload still overflows
    and remains owned solely by `LW-3.5`.
  - AprNes/NesMcp traversed 236 packed edges to visible X 1888 with matching
    lifecycle counts, no pending axis, zero R6/directory/decode work in commit,
    a final 30-tile/8-attribute phase, four distinct nametable hashes, BGM/SFX
    plus DPCM register writes, and no `$A000` mirroring write.
- Candidate files: `src/RetroSharp.NES/NesRomBuilder.cs`, NES camera/runtime
  state, `src/RetroSharp.NES.Tests/NesLargeWorldCameraTests.cs`,
  `src/RetroSharp.NES.Tests/NesRomCompilerTests.cs`, and
  `src/RetroSharp.NES.Tests/NesWorldLoadTests.cs`.
- Acceptance:
  - Camera state advances only when the correctly tagged immutable edge is
    resident; unavailable or malformed data defers movement without a partial
    nametable/attribute commit.
  - Wrong-axis/direction/world-edge tags and reversals retain the same
    observable safety contract proven by `LW-2.4`, within NES-owned PPU phases
    and budgets.
  - Instrumentation proves no mapper/decode work in VBlank/NMI and every tile
    and attribute phase stays within its exact bound.
  - The same instrumentation proves the two-frame request-to-resident limit in
    worst-case single-axis and diagonal traversal; exceeding it is a failed
    temporal contract, not a silently acceptable camera deferral.
  - AprNes screen/nametable evidence matches LW-1.4 visual and palette
    provenance across all four screens with uninterrupted audio/DPCM.
- Validation: focused bidirectional, diagonal, reversal, deferral, phased-row,
  attribute-provenance, chunk/bank-boundary, request/resident latency, and audio
  tests; AprNes via NesMcp watchpoints/timelines/screens/state with frame
  counters; full NES tests; `git diff --check`.
- Non-goals: no extra slot/dynamic RAM, runtime CHR banking, `$A000` mirroring,
  mapper IRQ/HUD, runner migration, spawn indexing, or unrelated #247 work.
- Stop conditions: stop if staging exceeds 594 bytes/two peers, a commit
  exceeds 32 tiles/four 8-tile row phases/9 attributes, art needs runtime CHR
  residency, mapper/decode work enters VBlank/NMI, worst-case preparation cannot
  meet the two-frame residency bound, or four-screen behavior cannot be proven
  in AprNes.

#### LW-3.5: Migrate the shared runner and prove joint full-stage1 acceptance

- Status: **complete; [#305](https://github.com/SuperJMN/RetroSharp/issues/305)
  migrated and jointly validated the shared runner.**
- Layer: cross-target acceptance, shared sample integration, and tracked
  artifacts.
- Dependencies: [LW-2.5 / #300](https://github.com/SuperJMN/RetroSharp/issues/300)
  and [LW-3.4 / #304](https://github.com/SuperJMN/RetroSharp/issues/304)
  (both native blocked-by).
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
| Canonical pack | Current runner: GB 2,568 bytes and NES 2,780 bytes; each has 770 stored visual bytes, 326 stored collision bytes, 60 chunks, and raw/RLE plus malformed-data coverage. Historical LW-1.4 checkpoint: GB 2,550 / NES 2,762. |
| Address width | 255 -> 256 and 256 -> 255 crossings on both targets |
| Collision | Hit at world Y 304 plus unambiguous `0xFFFF` no-hit result |
| GB placement/restoration | Deterministic MBC1 far placement; every exit/nested/audio-active path restores the actual entry bank and shadow |
| GB runtime budget | 298-byte direct reader / 362-byte standard packed camera / 554-byte diagonal and v1 maximum staging; bank/directory/decode outside VBlank with LY 136-153 guarded; at most 19 column or 21 row writes in VBlank |
| NES cartridge | Final-link mapper-0-first selection; forced MMC3 header `04 02 48 00`; PRG <=65,536, physical CHR 16,384, resident CHR <=8,192, fixed code/DPCM/vectors <=16,384 |
| NES windows/restoration | Current `WorldPack` raw-fallback <=7,924/8,192 R6; packed-camera pinned data <=7,310/8,192 R7 (5,012-byte LW-3.2 placement baseline); boot data <=4,128/8,192 R7; every R6 exit restores hardware and shadow while R7 stays pinned |
| NES runtime budget | 594-byte canonical staging (six visual, two collision, two edge slots); bank/directory/decode outside VBlank/NMI; fixed NMI only accounts hardware/pending frames; at most 32 column tiles or four 8-tile row phases, then at most 9 attributes |
| Behavioral backends | Game Boy interrupt/restoration/visual traversal in SameBoy/GameboyMcp; generated mapper-4 + four-screen `0x48` ROM in AprNes through NesMcp `auto` |
| Visual/audio parity | Decoded and visible tiles/collision match LW-1.4; GB BGM/SFX and NES BGM/SFX/DPCM remain complete and frame-correct |
| Small-ROM stability | Representative ROM-only GB and mapper-0 NES outputs remain byte-identical |
| Final shared acceptance | The shared manifest loads complete `stage1`; tracked GB/NES ROMs are regenerated together with no trimming or target no-ops; short/full jump probes prove one-way traversal and landing on both targets |

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
- Wave 2 native subissues (five implementation-complete; milestone 11):
  - [#296 — LW-2.1: add the fixed-bank MBC1 world-read foundation](https://github.com/SuperJMN/RetroSharp/issues/296)
  - [#297 — LW-2.2: place serialized WorldPacks in physical MBC1 banks](https://github.com/SuperJMN/RetroSharp/issues/297)
  - [#298 — LW-2.3: implement the fixed-bank WorldPack reader and decoder](https://github.com/SuperJMN/RetroSharp/issues/298)
  - [#299 — LW-2.4: integrate staged edges with Game Boy camera streaming](https://github.com/SuperJMN/RetroSharp/issues/299)
  - [#300 — LW-2.5: prove full stage1 on Game Boy without migrating the shared runner](https://github.com/SuperJMN/RetroSharp/issues/300)
- Wave 3 native subissues (all implementation-complete; milestone 11):
  - [#301 — LW-3.1: add the MMC3/TVROM linker and fixed-runtime foundation](https://github.com/SuperJMN/RetroSharp/issues/301)
  - [#302 — LW-3.2: place WorldPack/data sections and select the final NES profile](https://github.com/SuperJMN/RetroSharp/issues/302)
  - [#303 — LW-3.3: implement the fixed-bank NES WorldPack reader](https://github.com/SuperJMN/RetroSharp/issues/303)
  - [#304 — LW-3.4: integrate staged four-screen NES streaming](https://github.com/SuperJMN/RetroSharp/issues/304)
  - [#305 — LW-3.5: migrate the shared runner and prove joint full-stage1 acceptance](https://github.com/SuperJMN/RetroSharp/issues/305)
- Related open issues: [#244](https://github.com/SuperJMN/RetroSharp/issues/244)
  (Wave 4 wide-map spawn activation) and
  [#247](https://github.com/SuperJMN/RetroSharp/issues/247) (broader NES target
  gaps). Update #247 only with a link to the Wave 3 mapper-backed slice; its
  unrelated gaps remain open and are not duplicated here.

All ten Wave 2/3 issues are native subissues of #275 with the dependency graph
recorded above. Game Boy `LW-2.1` through `LW-2.5`, NES `LW-3.1` through
`LW-3.4`, and joint `LW-3.5` are complete. The parent remains the integrator
history for the completed Large Worlds execution chain.
