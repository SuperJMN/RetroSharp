# Generated Code Performance Roadmap (Game Boy and NES)

Status: **active under [GitHub epic #387](https://github.com/SuperJMN/RetroSharp/issues/387)
and [milestone 13](https://github.com/SuperJMN/RetroSharp/milestone/13); discovery
baseline reproduced on 2026-07-17.** The first seeded frontier is
[GCP-0.1 / #388](https://github.com/SuperJMN/RetroSharp/issues/388), and
[GCP-0.2 / #389](https://github.com/SuperJMN/RetroSharp/issues/389) accepts the
CPU-work representation and future diagnostic policy. Later waves remain
intentionally unseeded until that decision is merged into their remote bodies.

This roadmap turns the measured Actor Framework code-generation bottlenecks
into bounded work for Game Boy and NES. It covers compiler-generated work whose
cost the compiler can know from fixed pool capacities, authored spawn tables,
generated phase loops, and target-owned lowering shapes. It does not claim a
complete worst-case execution-time proof for arbitrary user code.

`docs/ArchitectureRoadmap.md` owns the durable language-versus-SDK-versus-target
boundary. `docs/ActorFrameworkRoadmap.md` owns the actor authoring contract and
historical AF tasks. This document owns the generated-code performance
baseline, the `GCP-0.2` CPU-work decision gate and its eventual accepted
contract, the dependency graph, and the `GCP-x.y` task cards. GitHub owns remote
execution state; the parent epic is an integration unit, not an implementation
task.

## 1. Destination

Representative, hardware-valid generated Actor Framework workloads must
complete one logical tick per physical frame on the deterministic Game Boy and
NES test CPUs, without weakening existing functional acceptance or changing
the public source semantics.

The first closeout target is:

- **D1 — wide-spawn cadence:** a 128-entry, low-density authored spawn layer
  with a two-slot pool sustains
  100 logical ticks over 100 observed physical frames after warm-up on both
  targets;
- **D2 — active-pool cadence:** a pool of eight active one-piece actors sustains
  100/100 on both targets;
- **D3 — spatial bound:** the 240-spawn AF-5.10 reference fixture examines at
  most 32 plausible spawn
  records for representative start, middle, end, reverse, and jump windows;
- **D4 — regression safety:** existing exact-ROM runner, actor/projectile,
  audio, and packed-world cadence gates retain their accepted behavior;
- **D5 — static guard:** compiler-owned work that cannot fit the selected
  target's accepted CPU budget produces a deterministic, actionable diagnostic
  rather than a ROM that silently drops logical cadence;
- **D6 — zero-cost invariants:** all accepted optimizations preserve fixed
  storage, deterministic ordering, and the zero-cost language/SDK contract.

The 100/100 figures are deterministic harness gates, not claims about wall-clock
emulator scheduling. `GCP-0.1` owns the canonical fixture and observation
protocol before later tasks may use those figures as acceptance evidence.

## 2. Historical discovery baseline (pre-GCP-1.1)

The 2026-07-17 audit compiled synthetic programs through the current CLI and
ran their exact ROM bytes on the in-process cycle-aware Game Boy and NES test
CPUs. Each measurement used 20 warm-up frames followed by 100 observed physical
frames. `ticks` counts completed frame waits; `longest miss` is the longest run
of physical frames with no completed logical tick.

The audit deliberately isolates two dimensions:

- **wide spawn table:** two pool slots, no nearby spawn eligible for
  activation, and an increasing number of authored records;
- **active pool:** every declared slot active, one-piece actor geometry, and
  pool capacity increasing from one to eight.

These figures are the frozen pre-optimization discovery evidence used to
calibrate GCP-0.2. GCP-0.1 has since replaced the external artifacts with
repository-owned source fixtures and a deterministic report. The refreshable
`validation/generated-code-performance/baseline.tsv` snapshot records the
current emitted ROMs; after GCP-1.1 it therefore differs intentionally from the
historical tables below.

### Wide spawn table

| Authored spawns | GB ticks | GB longest miss | NES ticks | NES longest miss |
| ---: | ---: | ---: | ---: | ---: |
| 3 | 100/100 | 0 | 100/100 | 0 |
| 8 | 100/100 | 0 | 100/100 | 0 |
| 16 | 100/100 | 0 | 100/100 | 0 |
| 24 | 100/100 | 0 | 100/100 | 0 |
| 32 | 50/100 | 1 | 100/100 | 0 |
| 48 | 50/100 | 1 | 100/100 | 0 |
| 64 | 25/100 | 3 | 50/100 | 1 |
| 96 | 14/100 | 6 | 25/100 | 3 |
| 128 | 9/100 | 10 | 17/100 | 5 |

### Active actor pool

| Active one-piece actors | GB ticks | GB longest miss | NES ticks | NES longest miss |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 100/100 | 0 | 100/100 | 0 |
| 2 | 100/100 | 0 | 100/100 | 0 |
| 4 | 100/100 | 0 | 100/100 | 0 |
| 8 | 50/100 | 1 | 50/100 | 1 |

This does not mean every current ROM is slow. The tracked runner and focused
acceptance samples retain their own passing thresholds. The result is narrower:
the generated Actor Framework path has severe scaling cliffs that valid future
content can reach without tripping the current sprite, scanline, or background
write diagnostics.

## 3. Confirmed cost centers

### 3.1 Every activation call scans the complete authored table

`ActorFrameworkLowerer.Actors.Generation.cs` emits a counted loop from zero to
the layer's full spawn count for every `Actors.SpawnLayer(...)` or
`Actors.SpawnWindow(...)` call. Each unused record loads its fields, projects
its position, and, when visible, scans the fixed pool for a free slot.

The one-shot `used[]` contract is not the problem and must remain stable. The
problem is candidate selection: cost currently follows total authored objects
rather than objects plausibly intersecting the camera window. Existing
[AF-5.10 / #244](https://github.com/SuperJMN/RetroSharp/issues/244) owns the
spatial-index behavior and equivalence contract.

### 3.2 Spawn fields lowered to length-dependent conditional ladders before GCP-1.1

`ActorFrameworkLowerer.Actors.cs` generates 13 inline lookup functions per
non-empty spawn layer: `kind`, split X/Y bytes, and eight initial state fields.
When values vary, each helper is a nested `index == n ? value : ...`
conditional chain. Activation therefore pays a length-dependent lookup cost
inside the length-dependent scan. Spatial candidate selection alone is not a
complete fix if each selected record still traverses a table-length branch
ladder.

GCP-1.1 replaces that historical shape with one immutable ROM column for each
varying field. Uniform fields remain direct constants. Game Boy emits one
bounded indexed read (or its bank-preserving read-only-data helper when the
table is banked); NES emits `LDA index; TAX; LDA table,X`. The complete-table
scan remains intentionally unchanged for AF-5.10 / #244.

### 3.3 Dynamic struct-array addressing repeats stride work

`GameBoyRuntimeCompiler.Expressions.cs` and
`NesRuntimeCompiler.Expressions.cs` recalculate `index * stride` for each
runtime `arr[i].field` access. The current `EmitMultiplyA(...)` implementations
use repeated addition proportional to the struct stride. Generated actor phases
touch several fields for the same slot, so the same index/stride work is paid
repeatedly.

This is a general fixed-struct-array lowering concern. Its solution belongs in
the target runtime compilers or a reusable target-private address cursor, not
in a new actor-specific language feature.

### 3.4 Actor phases emit separate full-pool traversals

`Update`, `Draw`, `TouchTiles`, `LandOnTiles`, and `TouchPlayer` each expand to
their own pool loop. Their source-level order is observable and must not be
reordered casually, but generated phases still repeat active checks, address
materialization, camera/projection preparation, and dispatch work that may be
reusable without changing the phase ordering.

The optimization contract is trace equivalence first. Loop fusion is not a
requirement when it changes all-actors-per-phase semantics.

### 3.5 Current frame budgets omit CPU work

`Sdk2DFrameBudget` tracks background tile writes, hardware sprite totals,
sprite size modes, and per-scanline sprite pressure. It does not represent CPU
cycles or compiler-known loop work. A program can therefore pass every current
capability check and still complete only one logical tick every two or more
physical frames.

The CPU-work model must remain honest about its boundary. It may charge exact
or conservative costs for compiler-generated fixed work; it must not present
an incomplete estimate as whole-program WCET.

## 4. Locked architecture rules

These constraints apply to every task in this epic:

1. **No public API change is required.** Existing `Actors.*`, `Enemies.*`, and
   pool phase calls retain their spelling and behavior.
2. **The language stays target-neutral.** CPU cycles, LR35902 registers, 6502
   addressing modes, and ROM window details never become language syntax.
3. **The Actor Framework remains source/static lowering.** No actor-specific
   `Sdk2DOperation`, runtime object model, hidden scheduler, or dynamic dispatch
   is introduced.
4. **Mutable storage stays fixed.** No heap, GC, RTTI, boxing, delegates,
   closures, virtual dispatch, unbounded collections, or hidden object
   identity.
5. **Spawn semantics stay one-shot.** A spawn becomes used only after it claims
   a slot; recycling does not clear it; an eligible spawn that finds a full
   pool remains retryable.
6. **Ordering stays deterministic.** Authored source order remains the
   tie-breaker for equal-position candidates and pool saturation. Actor phase
   ordering remains observably equivalent.
7. **ROM metadata is preferred to RAM replication.** Spatial directories and
   compact record tables belong in ROM. Beyond `used[]`, mutable per-layer
   state remains O(1) unless a later accepted contract says otherwise.
8. **Target work stays target-owned.** Shared lowering may describe fixed work;
   Game Boy and NES own instruction selection, address cursors, and cycle
   calibration.
9. **Measure emitted ROMs.** AST size, generated source size, or instruction
   counts alone are supporting evidence, not runtime acceptance.
10. **Do not weaken existing gates.** A faster synthetic fixture cannot trade
    away runner cadence, exact lifecycle traces, OAM integrity, safe video
    writes, deterministic ROM generation, or current capability diagnostics.

## 5. GCP-0.2 decision gate: CPU-work contract

Decision status: **accepted by GCP-0.2 / #389.** The canonical policy is
[`GeneratedCodeCpuWorkContract.md`](GeneratedCodeCpuWorkContract.md). GCP-0.2
changes no executable validator or ROM bytes; GCP-3.1 owns that implementation.

The accepted representation uses checked inclusive ranges in target-native
cycles: LR35902 T-cycles for Game Boy and 6502 CPU cycles for NES. The physical
windows are 70,224 and a conservative 29,780 respectively. There is no blanket
percentage reserve: active frame service, OAM publication, input, audio,
camera/world phases, collision, and generated actor work are stable
contributors charged once. Busy VBlank waiting is idle slack, while arbitrary
user loops and uncalibrated state-dependent runtime remain explicitly unknown.

Every contributor is `Exact`, `Bounded`, or `Unknown`, keyed by target and
selected cartridge/runtime profile. Fixed generated loops include their
literal multiplicity; ROM lookup bounds include bank/window overhead; mutually
exclusive branch paths form a range rather than an additive sum. CPU cycles
remain orthogonal to the current background-write and sprite/scanline budgets,
so the same emitted work is not charged twice.

The future GCP-3.1 policy is also fixed. A known lower bound above the target
window is error `GCP1001`; a finite range that crosses the window is warning
`GCP1002`; a fitting known range emits no default diagnostic. Unknown work does
not receive a fabricated count or warn on every ordinary program; an explicit
report marks coverage `incomplete` and never claims whole-program cadence.
Stable semantic contributor ids and the deterministic report schema live in
the contract document.

The contract classifies all 13 GCP-0.1 fixtures per target. It preserves the
exact observed 24/32-spawn Game Boy boundary, 48/64-spawn NES boundary, and
four/eight-active-actor boundary as calibration evidence without treating
physical scheduling intervals as static instruction costs. GCP-3.1 can now be
specified and implemented without reopening the cost representation,
composition, unknown-work, or diagnostic-policy decisions.

The representative calculation is explicit rather than implied: the contract
tabulates authored-record visits, 13-field materializations, current conditional
lookup equality counts, pool/slot visits, phase visits, collision call sites,
and logical draws for every canonical scale. Its currently isolated numeric
evidence is only the non-additive OAM transfer detail: 640 Game Boy T-cycles or
513..514 NES CPU cycles. The `sprite.publish` parent remains incomplete until
GCP-3.1 calibrates its call/setup/wait/return boundary, and the remaining
contributor table names the other emitted-code descriptors required instead of
inventing source-level cycle prices.

## 6. Execution contract

### Integrator

- Own the parent epic, milestone, native dependency graph, and merge order.
- Dispatch one unblocked child issue per implementation session.
- Keep this roadmap and GitHub aligned after each merged task.
- Preserve the existing AF-5.10 issue instead of creating a duplicate.
- Seed later implementation waves only when the shared measurement and budget
  contracts make their issue bodies stable.
- Run joint acceptance only after both target paths and the shared actor path
  have merged.

### Implementation agent

- Start from current `master` on `agent/<task-id>-<slug>`.
- Reproduce the issue baseline before editing production code.
- Inspect current CodeGraph owners and focused tests; candidate files below are
  navigation hints, not frozen architecture names.
- Produce one observable result and normally one PR.
- Record ROM-size, generated-shape, cycles/ticks, and semantic-equivalence
  deltas required by the issue.
- Stop rather than absorbing another blocked task or changing a public
  contract silently.

### Stop conditions

Return to the integrator when:

- a proposed optimization changes one-shot, retry, slot-order, or phase-order
  semantics;
- constant-cost spawn records require public pointers or target-specific
  source APIs;
- a cycle estimate cannot distinguish known generated work from arbitrary user
  work;
- a target optimization needs new mutable O(spawns) or O(pool) hidden state;
- the hardware-valid acceptance fixture cannot reach 1:1 cadence without
  weakening an existing correctness gate;
- a task needs to cross another task id to produce its claimed result.

## 7. Dependency graph and waves

```text
GCP-0.1 reproducible baseline
├──> GCP-0.2 CPU-work contract ───────────────────────────────┐
├──> GCP-1.1 constant-cost spawn records ─> AF-5.10 / #244 ──┤
├──> GCP-2.1 shared actor-phase work ─────────────────────────┤
├──> GCP-2.2 Game Boy struct-array addressing ────────────────┤
└──> GCP-2.3 NES struct-array addressing ─────────────────────┤
                                                              v
                                              GCP-3.1 diagnostics
                                                              |
                    #244 + GCP-2.1 + GCP-2.2 + GCP-2.3 + GCP-3.1
                                                              v
                                              GCP-3.2 joint acceptance
```

- **Wave 0 — measurement and contract:** `GCP-0.1`, then `GCP-0.2`.
- **Wave 1 — wide spawn scaling:** `GCP-1.1`, then existing AF-5.10 / #244.
- **Wave 2 — active pool scaling:** `GCP-2.1`, `GCP-2.2`, and `GCP-2.3`
  may proceed independently after `GCP-0.1` and merge before final budgeting.
- **Wave 3 — guard and closeout:** `GCP-3.1`, then `GCP-3.2`.

## 8. Issue-ready task cards

### GCP-0.1: Freeze reproducible generated-code performance baselines

- **Outcome:** repository-owned fixtures and a deterministic report reproduce
  the wide-spawn and active-pool scaling cliffs on exact generated GB/NES ROMs.
- **Layer:** validation and measurement.
- **Depends on:** none.
- **Remote:** [#388](https://github.com/SuperJMN/RetroSharp/issues/388), the
  initial unblocked child of epic #387.
- **Candidate modules:** focused Actor Framework tests; Game Boy and NES test
  CPUs; `src/RetroSharp.FunctionalAcceptance`; a purpose-named performance
  fixture/report module under `validation/` or the target test projects.
- **Work:**
  - [ ] Check in minimal source/map/project fixtures for the wide-spawn and
    active-pool dimensions without checking in synthetic ROM binaries.
  - [ ] Compile exact ROMs through production entry points into temporary test
    output.
  - [ ] Measure 20 warm-up plus 100 observed physical frames on both
    deterministic target CPUs.
  - [ ] Record ticks, longest miss, emitted ROM bytes, and target/profile.
  - [ ] Pin the discovery matrix or a deliberately explained equivalent whose
    workload semantics are identical.
  - [ ] Make boot failure, reset-vector re-entry, or missing frame waits fail
    explicitly rather than appear as a performance result.
- **Acceptance:**
  - [ ] The 24/32-spawn Game Boy boundary, 48/64-spawn NES boundary, and
    four/eight-active-actor boundary are reproduced deterministically.
  - [ ] Repeated runs produce identical ROM hashes and reports.
  - [ ] The report clearly separates physical frames, logical ticks, and CPU
    cycles where available.
  - [ ] Fixtures remain hardware-valid with respect to declared sprite and
    scanline limits.
- **Validation:** focused performance tests on GB/NES; exact fixture builds;
  `dotnet test RetroSharp.sln -m:1`; `git diff --check`.
- **Compatibility:** build `samples/runner/runner.retrosharp.json` for Game Boy
  and NES with its declared source set; this measurement-only task must not
  change tracked runner bytes.
- **Documentation:** update this roadmap if the canonical fixture or observation
  protocol differs from the discovery baseline; no public API doc change.
- **Non-goals:** no production optimization, no public benchmark API, no
  tracked synthetic ROM artifacts, and no replacement of functional ROM
  acceptance.

### GCP-0.2: Define the target CPU-work budget and diagnostic contract

- **Outcome:** one accepted contract says what compiler-known work can be
  charged, in which target-owned units, how unknown user work is represented,
  and when compilation warns or fails.
- **Layer:** shared capability model plus target-specific cost policy.
- **Depends on:** [GCP-0.1 / #388](https://github.com/SuperJMN/RetroSharp/issues/388)
  through a native blocked-by relationship.
- **Remote:** [#389](https://github.com/SuperJMN/RetroSharp/issues/389), child
  of epic #387.
- **Candidate modules:** `Sdk2DFrameBudget`, `Sdk2DOperationCollector`,
  `Sdk2DOperationValidator`, `Target2DCapabilities`, target compilers, target
  acceptance docs, and a focused ADR/contract document if the decision does
  not fit here.
- **Work:**
  - [x] Inventory compiler-generated fixed loops and bounded target SDK/runtime
    operations relevant to the two fixtures.
  - [x] Choose exact cycles, conservative ranges, or named work units per
    category and document calibration evidence.
  - [x] Decide error versus warning/report behavior and the treatment of
    unknown dynamic user work.
  - [x] Define composition with audio and camera/world streaming without
    double-charging existing tile/sprite budgets.
  - [x] Specify a stable diagnostic breakdown suitable for emitted-code tests.
- **Acceptance:**
  - [x] The contract can classify both discovery fixtures without claiming
    whole-program WCET.
  - [x] Game Boy and NES budgets name their target-specific available window
    and reserved runtime assumptions.
  - [x] The accepted policy explains how conservative estimates avoid both
    silent cadence loss and false-precision rejection.
  - [x] `GCP-3.1` can be written without reopening the core policy.
- **Validation:** focused contract/model tests or ADR review; representative
  cost calculations against `GCP-0.1`; `git diff --check`.
- **Compatibility:** no runtime emission changes are expected; still build the
  runner manifest for both targets if the contract patch changes executable
  model or validator code.
- **Documentation:** the accepted decision is recorded in section 5,
  `GeneratedCodeCpuWorkContract.md`, and both target docs.
- **Non-goals:** no complete arbitrary-program timing analyzer and no public
  source syntax for cycles.

### GCP-1.1: Emit constant-cost ROM spawn record lookups

- **Outcome:** loading one authored spawn record has a cost independent of the
  total layer length on both targets.
- **Layer:** Actor Framework asset/lowering plus target-owned ROM data access.
- **Depends on:** [GCP-0.1 / #388](https://github.com/SuperJMN/RetroSharp/issues/388)
  through a native blocked-by relationship.
- **Remote:** [#390](https://github.com/SuperJMN/RetroSharp/issues/390), child
  of epic #387 and native blocker of AF-5.10 / #244.
- **Candidate modules:** `ActorFrameworkLowerer.Actors.cs`,
  `ActorFrameworkLowerer.Actors.Generation.cs`, generated-program
  contributions, Game Boy/NES runtime compilers and cartridge data placement,
  focused `ActorFrameworkActorsTests` suites.
- **Work:**
  - [x] Replace the 13 nested conditional lookup ladders with one compact,
    deterministic record/table representation or an equivalent bounded shape.
  - [x] Keep authored order and all current split-coordinate/initial-field
    values exact.
  - [x] Place immutable payload/index data in ROM without duplicating complete
    spawn records in RAM.
  - [x] Preserve small constant/uniform cases when they are genuinely cheaper
    and prove selection deterministically.
  - [x] Record ROM-size and per-record cycle deltas for both targets.
- **Acceptance:**
  - [x] Per-record lookup instruction/cycle cost does not grow with 16, 64, 128,
    or 240 total records.
  - [x] Reference-versus-new traces agree on every loaded field and authored
    index.
  - [x] No new public source API, dynamic allocation, O(spawns) mutable RAM, or
    bank-unsound executable path is introduced.
  - [x] Current actor sample ROM behavior and deterministic generation remain
    stable unless the issue deliberately records expected byte changes.
- **Validation:** focused shared/frontend and GB/NES emitted-code tests;
  `GCP-0.1` report; actor exact-ROM scenarios; ROM regeneration dry-run;
  `dotnet test RetroSharp.sln -m:1`; `git diff --check`.
- **Compatibility:** compile `samples/runner/runner.retrosharp.json` for both
  targets with its declared source set and run its exact cadence gates when
  spawn/runtime bytes change.
- **Documentation:** update this roadmap and both target docs if emitted spawn
  layout, supported limits, or diagnostics change; public API spelling remains
  unchanged.
- **Non-goals:** spatial candidate selection, spawn reactivation, mapper
  redesign, or active-pool loop optimization.
- **Implementation evidence (2026-07-17):** every varying spawn field is a
  deterministic byte column in cartridge ROM; all-uniform columns still lower
  to immediates. The column payload travels as typed compiler metadata from the
  shared Actor Framework plan to each target; user source cannot declare or
  forge that metadata. Focused 16/64/128/240-record tests compare all 13
  columns in authored order and verify a single fixed instruction shape per
  lookup. The direct Game Boy shape costs 56 cycles; the old 16-record ladder
  ranged from 60 to 608 cycles, so the last-record delta is -552 cycles. NES
  costs 9 cycles
  without an indexed page crossing and 10 with one; the old 16-record ladder
  ranged from 12 to 122 cycles, so the conservative last-record delta is -112
  cycles. For the focused 16-record/two-varying-column wide-spawn probe, Game
  Boy occupied payload falls from 1,986 to 1,646 bytes (-340), and NES fixed
  payload from 2,656 to 2,366 bytes (-290). The refreshed exact GCP-0.1
  snapshot reaches 100/100 through 64 records and 50/100 at 96/128 on Game Boy,
  and 100/100 through 128 on NES; the remaining Game Boy scan cost belongs to
  #244. The tracked actor-framework GB/NES ROMs were deliberately regenerated;
  their exact production scenarios and the shared runner cadence gates pass.

### GCP-1.2 / AF-5.10: Index spawn activation by camera window

- **Outcome:** activation cost follows plausible records near the current
  window rather than the total authored layer.
- **Layer:** Actor Framework asset/lowering.
- **Depends on:** `GCP-1.1`.
- **Remote owner:** existing
  [AF-5.10 / #244](https://github.com/SuperJMN/RetroSharp/issues/244).
- **Contract:** #244 remains authoritative for the 240-spawn fixture,
  at-most-32-record bound, one-shot semantics, reverse/jump/equal-X timelines,
  retry behavior, fixed RAM, ROM directory/index, and reference-trace
  equivalence. Do not create a duplicate GCP issue.
- **Additional epic acceptance:** the `GCP-0.1` 128-spawn cadence fixture reaches
  100/100 on both targets after `GCP-1.1` and #244 are integrated.
- **Compatibility:** compile the shared runner manifest for both targets and run
  the exact actor/runner cadence gates because activation runtime bytes change.
- **Documentation:** update this roadmap, the Actor Framework roadmap, and both
  target docs with the accepted index shape and retained one-shot semantics.

### GCP-2.1: Remove redundant actor-pool phase work without semantic reordering

- **Outcome:** generated actor phases reuse safe per-phase/per-slot facts or
  specialize traversal so repeated full-pool overhead is materially reduced
  while preserving exact phase ordering.
- **Layer:** shared Actor Framework lowering.
- **Depends on:** `GCP-0.1`.
- **Candidate modules:** `ActorFrameworkLowerer.Actors.cs`,
  `ActorFrameworkLowerer.Actors.Generation.cs`,
  `ActorFrameworkLowerer.SharedGeneration.cs`, generated-program
  contributions, focused Actor Framework tests.
- **Work:**
  - [ ] Capture exact reference traces for `Update`, `Draw`, `TouchTiles`,
    `LandOnTiles`, and `TouchPlayer` combinations.
  - [ ] Identify active checks, kind dispatch, camera reads, projections, or
    address facts that can be reused without moving one phase across another.
  - [ ] Optimize only transformations proven equivalent when arbitrary user
    statements appear between phase calls.
  - [ ] Record pool scans, emitted bytes, and cycle deltas at capacities
    one/two/four/eight.
- **Acceptance:**
  - [ ] Actor state, collision/contact calls, draw order, hidden-slot writes,
    and OAM publication traces match the reference exactly.
  - [ ] Eight-active-actor cycle cost improves on both targets and no capacity
    regresses materially without an explicit trade-off accepted by the epic.
  - [ ] No hidden frame scheduler, actor-specific SDK operation, or new mutable
    runtime object is introduced.
- **Validation:** focused actor phase lowering; actor/projectile exact ROMs;
  `GCP-0.1`; full tests and `git diff --check`.
- **Compatibility:** compile `samples/runner/runner.retrosharp.json` for both
  targets with its declared source set and run its exact cadence gates when
  generated actor phase bytes change.
- **Documentation:** update this roadmap and target docs if generated phase cost
  or capability diagnostics change; no public API change is expected.
- **Non-goals:** reordering all-actor phases into per-actor phases merely to
  fuse loops, and target instruction selection owned by `GCP-2.2/2.3`.

### GCP-2.2: Bound Game Boy runtime struct-array addressing cost

- **Outcome:** repeated `arr[i].field` access no longer recomputes a
  stride-proportional repeated-add multiply for each field on Game Boy.
- **Layer:** Game Boy target runtime/compiler lowering.
- **Depends on:** `GCP-0.1`.
- **Candidate modules:** `GameBoyRuntimeCompiler.Expressions.cs`, storage/layout
  modules, `GbBuilder`, Game Boy struct-array emitted-code tests, and
  `ActorFrameworkActorsTests`.
- **Work:** choose a bounded multiply, address cursor, or loop-aware reuse seam;
  cover mixed-width fields and SDK byte-expression indices; preserve register
  liveness and existing storage layout; record bytes/cycles.
- **Acceptance:** lookup cost is bounded independently of struct stride or one
  accepted logarithmic/constant target shape is documented; repeated fields for
  one slot reuse safe address/index work; general struct-array byte/value traces
  remain exact; actor cadence improves without new public semantics.
- **Validation:** Game Boy struct-array emitted bytes and runtime values;
  `GCP-0.1`; actor exact-ROM acceptance; full tests; ROM dry-run;
  `git diff --check`.
- **Compatibility:** compile the shared runner manifest for Game Boy with its
  declared source set and run the exact GB runner cadence gate; compile NES as a
  cross-target frontend sanity check.
- **Documentation:** update this roadmap and `GameBoyTarget.md` if the emitted
  addressing shape, storage constraints, or diagnostics change.
- **Non-goals:** Actor-only special opcodes, storage-layout change without an
  explicit compatibility decision, or NES implementation.

### GCP-2.3: Bound NES runtime struct-array addressing cost

- **Outcome:** repeated `arr[i].field` access no longer recomputes a
  stride-proportional repeated-add multiply for each field on NES.
- **Layer:** NES target runtime/compiler lowering.
- **Depends on:** `GCP-0.1`.
- **Candidate modules:** `NesRuntimeCompiler.Expressions.cs`, storage/layout
  modules, `PrgBuilder`, NES struct-array emitted-code tests, and
  `ActorFrameworkActorsTests`.
- **Work:** choose a bounded multiply, X/address cursor, or loop-aware reuse
  seam; cover mixed-width fields and SDK byte-expression indices; preserve
  zero-page scratch/liveness and existing storage layout; record bytes/cycles.
- **Acceptance:** lookup cost is bounded independently of struct stride or one
  accepted logarithmic/constant target shape is documented; repeated fields for
  one slot reuse safe index work; general struct-array byte/value traces remain
  exact; actor cadence improves without new public semantics.
- **Validation:** NES struct-array emitted bytes and runtime values;
  `GCP-0.1`; actor exact-ROM acceptance; full tests; ROM dry-run;
  `git diff --check`.
- **Compatibility:** compile the shared runner manifest for NES with its
  declared source set and run the exact NES runner cadence gate; compile Game
  Boy as a cross-target frontend sanity check.
- **Documentation:** update this roadmap and `NesTarget.md` if the emitted
  addressing shape, storage constraints, runtime ABI, or diagnostics change.
- **Non-goals:** Actor-only runtime ABI, storage-layout change without an
  explicit compatibility decision, or Game Boy implementation.

### GCP-3.1: Diagnose compiler-known CPU work that exceeds frame budgets

- **Outcome:** compilation reports a deterministic target-specific breakdown
  when bounded generated work exceeds the accepted CPU-work contract.
- **Layer:** shared capability/budget model plus both target validators.
- **Depends on:** `GCP-0.2`, AF-5.10 / #244, `GCP-2.1`, `GCP-2.2`, and
  `GCP-2.3`.
- **Candidate modules:** the modules selected by `GCP-0.2`, target compilers,
  focused budget suites, CLI/report surfaces, and target docs.
- **Work:** implement accepted cost descriptors and composition; charge
  compiler-generated actor/spawn work after optimization; expose stable
  contributor names; handle unknown user work exactly as the accepted contract
  requires; add below/at/above-boundary tests on both targets.
- **Acceptance:** the known failing discovery shapes are either rejected/warned
  with the accepted contributor breakdown before optimization or accepted with
  measured headroom after optimization; current valid samples remain accepted;
  arbitrary user loops are not assigned fabricated exact costs; existing
  tile/sprite diagnostics remain stable.
- **Validation:** focused budget tests; `GCP-0.1`; representative CLI builds;
  target exact-ROM acceptance; full tests; `git diff --check`.
- **Compatibility:** compile `samples/runner/runner.retrosharp.json` for both
  targets with its declared source set and prove the accepted diagnostic policy
  does not reject either current production runner.
- **Documentation:** update section 5, both target docs, and CLI/report docs with
  the implemented policy and stable contributor names.
- **Non-goals:** cycle-perfect analysis of arbitrary programs or replacing
  runtime acceptance with static estimates.

### GCP-3.2: Prove joint GB/NES generated-code cadence acceptance

- **Outcome:** one durable checkpoint binds the optimized emitted shapes,
  target CPU-work diagnostics, and exact GB/NES ROM cadence.
- **Layer:** joint validation and documentation.
- **Depends on:** AF-5.10 / #244, `GCP-2.1`, `GCP-2.2`, `GCP-2.3`, and
  `GCP-3.1`.
- **Candidate modules:** `GCP-0.1` performance fixtures/report, functional ROM
  acceptance adapters, target acceptance suites, this roadmap, target docs,
  and `docs/AgentContext.md`.
- **Work:** run the complete matrix; pin the final 128-spawn and eight-active
  100/100 gates; preserve the #244 record bound; run current runner,
  actor/projectile, audio, packed-world, and deterministic ROM checks; record
  hashes, bytes, cycles/ticks, and any accepted estimator headroom.
- **Acceptance:** every destination bullet in section 1 is evidenced; no exact
  functional scenario regresses; generated ROMs are deterministic; the
  milestone and parent epic can close without an unowned performance gap.
- **Validation:** all focused performance and functional scenarios;
  `tools/gameboy/generate_sample_roms.py --dry-run`; deliberate regeneration
  only if tracked source changed; `dotnet test RetroSharp.sln -m:1`;
  `git diff --check`; appropriate SameBoy/NES emulator parity when emitted
  runtime shapes changed materially.
- **Compatibility:** the shared runner manifest must build for both targets from
  its declared source set and pass its exact target cadence scenarios.
- **Documentation:** record the final measurements, issue/PR links, accepted
  budget headroom, and milestone closeout in this roadmap and AgentContext.
- **Non-goals:** unrelated whole-compiler optimization, builder-size cleanup,
  mapper redesign, or new gameplay features.

## 9. Final acceptance matrix

Section 1 is the canonical source for thresholds. This matrix indexes the proof
that closes each destination clause; it must be updated by reference when a
threshold changes rather than becoming a second independent contract.

| Concern | Required proof |
| --- | --- |
| Measurement | Repository-owned source fixtures compile exact GB/NES ROMs through the canonical observation protocol. |
| Wide spawn cadence | Evidence closes destination **D1**. |
| Active pool cadence | Evidence closes destination **D2**. |
| Spatial bound | Reference-trace evidence closes destination **D3**. |
| Existing ROM acceptance | Runner, actor/projectile, audio-mixed-load, packed worlds, video/OAM safety, and deterministic generation close destination **D4**. |
| Static guard | The implemented target policy and contributor breakdown close destination **D5**. |
| Spawn and phase semantics | One-shot/retry/order traces plus actor state and OAM traces close destination **D6**. |

## 10. Remote tracking

- Milestone: [Generated Code Performance v1 — milestone
  13](https://github.com/SuperJMN/RetroSharp/milestone/13). It owns measurement,
  bounded generated-work optimization, CPU-work diagnostics, and joint GB/NES
  cadence acceptance; it remains open until `GCP-3.2` merges.
- Parent/integrator issue: [#387 — EPIC: Generated Code Performance v1 —
  predictable GB/NES frame cadence](https://github.com/SuperJMN/RetroSharp/issues/387).
  Its native subissues and blocked-by edges own live state; the parent must
  never be dispatched as an implementation task.
- Parent acceptance: every section 1 destination has a closed child/evidence
  link, `GCP-3.2` records the final exact-ROM report, no current functional gate
  regresses, and the roadmap/target docs match the merged implementation.
- Parent non-goals: no direct production implementation, no arbitrary-program
  WCET claim, no public cycle API, and no unrelated builder/mapper cleanup.
- Initial native graph:
  - [#388 — GCP-0.1: freeze reproducible generated-code performance
    baselines](https://github.com/SuperJMN/RetroSharp/issues/388) is the only
    open, unblocked child.
  - [#389 — GCP-0.2: define the target CPU-work budget and diagnostic
    contract](https://github.com/SuperJMN/RetroSharp/issues/389) is blocked by
    #388.
  - [#390 — GCP-1.1: emit constant-cost ROM spawn record
    lookups](https://github.com/SuperJMN/RetroSharp/issues/390) is blocked by
    #388.
  - Existing [#244 — AF-5.10: reduce actor spawn activation scan cost for wide
    maps](https://github.com/SuperJMN/RetroSharp/issues/244) is a native child of
    #387 and is blocked by #390; no duplicate GCP issue exists.
- Later tasks `GCP-2.1` through `GCP-3.2` remain issue-ready here but are not
  seeded until the measurement and CPU-work contracts make their final remote
  bodies stable.

## 11. Durable validation

Run from the repository root:

```bash
git diff --check
tools/gameboy/generate_sample_roms.py --dry-run
dotnet test RetroSharp.sln -m:1
```

Tasks that change emitted tracked sample bytes regenerate them deliberately and
run the matching exact-ROM and emulator/debug workflow. Documentation-only
link updates after issue creation still run `git diff --check` and verify the
native GitHub parent/dependency graph directly.
