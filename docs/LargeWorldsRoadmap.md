# Large Worlds Roadmap (banked map content for Game Boy and NES)

Status: **active; Wave 0 and Wave 1 are seeded for issue execution.**
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

The current derived `stage1.playable.tmj` remains the stable runner input until
the corresponding target acceptance task deliberately switches it. No task may
silently trim the level, stub audio, discard collision, or lower an unsupported
path as a no-op merely to make the ROM fit.

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

## 8. Later waves (not issue-ready yet)

Do not seed these as implementation issues until Wave 0 decisions and the
relevant Wave 1 contracts are merged. The integrator will decompose them using
measured payloads and accepted interfaces.

### Wave 2 — Game Boy MBC1 world data

- Far-pointer chunk placement and fixed-bank readers.
- Bank-safe staging/decompression outside VBlank.
- Bidirectional chunk/bank boundary tests with audio active.
- Full `stage1` Game Boy acceptance and tracked ROM migration.

### Wave 3 — NES mapper-backed world data

- Mapper header/linker/runtime foundation selected by `LW-0.3`.
- Switchable PRG chunk placement and fixed-bank readers.
- CHR-ROM/CHR-RAM or banked-CHR residency policy.
- Streaming and four-screen interaction under measured PPU budgets.
- Full `stage1` NES acceptance with complete BGM.

### Wave 4 — content residency and integration

- Spawn indexing/bucketing coordinated with issue #244.
- Tileset/palette pages for regions with more art than the resident tile budget.
- Multi-level directory/transition policy if required by a real sample.
- Cross-target full-`stage1` validation, documentation, and final budget report.

## 9. Validation matrix

| Concern | Required evidence |
| --- | --- |
| Portable boundary | Architecture tests and no target terms in public world API |
| Address width | 255/256 crossings in both directions |
| Collision | Hit at world Y 304 plus unambiguous no-hit result |
| GB banking | Chunk/bank crossings with BGM active and bank restoration proof |
| NES mapper | Header/layout tests plus behavioral emulator validation |
| Visual parity | Decoded/visible map matches authored Tiled world |
| Runtime budget | Staging outside VBlank; bounded VRAM/PPU writes inside VBlank |
| Small-ROM stability | Representative ROM-only GB and mapper-0 NES outputs checked |
| Full acceptance | Complete `stage1`, full music, no trimming or target no-ops |

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
- Related open issues: [#244](https://github.com/SuperJMN/RetroSharp/issues/244)
  (wide-map spawn activation) and
  [#247](https://github.com/SuperJMN/RetroSharp/issues/247) (broader NES target
  gaps, including mapper-backed levels).

Wave 2 onward is intentionally not seeded. The parent issue remains the
integrator surface; implementation agents receive one child issue, not the
parent or an open-ended request to continue the epic.
