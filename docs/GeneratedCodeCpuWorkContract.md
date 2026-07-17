# Generated Code CPU-Work Contract

Status: **accepted by GCP-0.2 / GitHub #389 and given an initial executable
report projection by GCP-3.1 / GitHub #402 on 2026-07-17.**

This document fixes the v1 contract for classifying compiler-known CPU work on
the Game Boy and NES targets. GCP-3.1 implements the first build-report
projection of the model as `SdkCpuWorkReport`, attached to the internal Game
Boy and NES ROM build reports. The initial target reports expose stable unknown
coverage, target/profile windows, and the policy status; when the reachable SDK
stream uses retained sprite publication, they also expose the calibrated
`sprite.publish.transfer` detail. They intentionally stay `incomplete` until
remaining descriptors are calibrated, so they do not claim whole-program
cadence or available headroom. The existing `Sdk2DFrameBudget` tile/sprite
checks remain independent current behavior.

The contract deliberately estimates only work whose emitted multiplicity and
target instruction shape the compiler owns. It is not whole-program WCET, does
not assign invented cycle counts to arbitrary user loops, and does not replace
exact-ROM cadence acceptance.

## 1. Accounting boundary

One CPU-work region is the work associated with one logical tick, from the
active completion of one frame boundary to entry into the next frame boundary.
Initialization before the first boundary is excluded. Busy waiting for the
next physical VBlank/NMI edge is idle slack and is also excluded; active work
performed by the frame-boundary runtime, such as an NMI handler, camera commit,
or retained OAM publication, is included.

Generated work that can occur on a later tick remains chargeable even if the
GCP-0.1 observation window happens to see a cheaper steady state. For example,
a one-shot spawn already marked `used` supplies a cheaper lower path, while an
unused eligible spawn plus pool search supplies an upper path. Work explicitly
staged over several physical frames is charged to the bounded phase that runs
in each frame, not as one impossible monolithic sum.

Startup, user work outside a frame-delimited main loop, and a logical tick with
no recognizable frame boundary are reported outside this v1 region rather
than forced into a per-frame number.

## 2. Target windows and native units

All numbers remain in target-native CPU units. A cross-target "work unit" is
not accepted because it would hide the different instruction and memory
costs.

| Target | Unit | Accepted physical-frame window | Blanket reserve | Evidence and assumption |
| --- | --- | ---: | ---: | --- |
| Game Boy | LR35902 T-cycles | 70,224 | 0 | `GameBoyTestCpu.DmgCyclesPerFrame`; normal-speed DMG execution. |
| NES | 6502 CPU cycles | 29,780 | 0 | Conservative whole-cycle floor of the NTSC frame model used by `NesTestCpu`; GCP-0.1 observes about 29,780.5 cycles per frame because rendered frame lengths alternate. |

`Blanket reserve = 0` does not mean runtime work is free or that user code owns
the whole frame. It means the contract does not invent a percentage margin and
then charge the same runtime again. Every emitted service is a contributor once:
input, frame-boundary active work, OAM publication, audio, camera/world
preparation or commit, collision lookup, and generated actor work. Unknown user
work stays explicitly unknown and is never treated as zero headroom.

For the GCP-0.1 fixture, the physical transfer subcomponent is known: 640
T-cycles for the Game Boy OAM DMA and 513..514 CPU cycles for NES DMA. That
supports a partial known lower bound for `sprite.publish`, but not a complete
publication price: call/setup, the Game Boy HRAM wait/return path, and the NES
instruction wrapper are not yet calibrated as one emitted boundary. Subtracting
only the transfer gives arithmetic remainders of 69,584 Game Boy T-cycles and
29,266..29,267 NES CPU cycles. They are **not available budget or headroom**;
the parent contributor still has an unknown upper bound and all other
uncalibrated contributors remain outstanding.

The selected cartridge/runtime profile is part of every calibration key.
Mapper/MBC window switches, fixed-bank trampolines, sequential versus DMA OAM
publication, and packed versus raw world paths may produce different bounds
for the same stable contributor id.

## 3. Cost representation

Every numeric contributor is a checked 64-bit inclusive range in the selected
target's native cycle unit:

- **Exact:** `lower == upper`; every accepted emitted path has the same cost.
- **Bounded:** finite `lower < upper`; branch paths or bounded runtime state
  change the cost, but both limits are calibrated from emitted target code.
- **Unknown:** no numeric upper bound is claimed. The report retains the known
  subtotal and a stable reason instead of substituting zero or infinity.

Each contributor records:

| Field | Contract |
| --- | --- |
| `id` | Stable semantic id from section 5; target implementation names are not ids. |
| `category` | `generated`, `sdk-runtime`, `target-runtime`, or `user`. |
| `basis` | Compiler-known multiplicity such as pool capacity, candidate count, hardware pieces, or one committed edge phase. |
| `count` | Literal/non-negative multiplicity after lowering. |
| `unit-lower` / `unit-upper` | Target/profile-calibrated per-item cycle range; upper is absent for unknown work. |
| `total-lower` / `total-upper` | Checked multiplication followed by checked composition; upper is absent for unknown work. |
| `calibration` | Stable target/profile calibration revision or emitted-code test that owns the numbers. |
| `detail-of` | Optional stable parent id. A detail explains a parent range and is never charged in addition to that parent. |
| `reason` | Required stable reason for unknown work. |

Ranges compose by summing work that may execute in the same physical frame.
Mutually exclusive branches use the minimum and maximum reachable path rather
than summing both. Fixed loops include initialization, condition, body,
increment, exit, helper/trampoline, and bank/window overhead. Hierarchical
breakdowns choose one charged accounting level. A child marked `detail-of`
explains the charged parent range and never enters the root total separately,
preventing a phase and its struct-addressing or transfer detail from being
charged twice. An incomplete parent may use a calibrated child as its known
lower bound while keeping its upper bound absent; later completing the parent
does not add the child a second time.

An unknown contributor does not erase known evidence. Reports retain the known
lower/upper subtotal and list unknown coverage separately. A dynamic user loop
or helper body with an unknown trip count is `user.dynamic-loop`; a target
runtime whose state-dependent bound has not yet been accepted is
`runtime.uncalibrated-state`. Inline helpers inherit the classification of
their expanded body rather than becoming unknown merely because source used a
helper call.

## 4. Composition with existing budgets and runtime systems

CPU cycles are orthogonal to current tile/sprite capacity checks. A 19-tile
Game Boy column remains 19 background writes for `Sdk2DFrameBudget`, while the
instructions that prepare and commit that column are charged once as CPU work.
Passing one axis must not create or subtract a second copy on the other axis.

The following composition rules are locked:

1. `Video.WaitVBlank()` / `Video.WaitFrame()` idle polling is excluded. Active
   handler, commit, and publication paths are target-runtime contributors.
2. `Input.Poll()` is a bounded SDK-runtime contributor when it appears in the
   tick.
3. Camera/world preparation and commit are separate. A frame charges only the
   phases it can execute; a packed edge is not also charged as a raw edge.
4. Retained sprite draw preparation is generated/SDK work. The one OAM
   publication is target-runtime work and is charged once per publishing frame,
   not once per logical sprite.
5. Audio is charged from the selected asset/profile's maximum due per-frame
   body plus bank/window and channel-restore paths. Until that finite descriptor
   exists, it is reported separately as `runtime.uncalibrated-state`; it is not
   hidden in a blanket reserve.
6. Collision uses the selected raw/packed and resident/cold path range. Several
   queries multiply the per-query range only when they can occur in the same
   tick; a cache-fill path and an incompatible cache-hit path are alternative
   bounds, not additive work.
7. User branches whose bodies are finite and target-lowered can contribute
   bounds. A loop with an unknown trip count remains unknown even when its body
   contains known SDK calls.
8. Existing background-write, OAM-count, scanline, and palette diagnostics keep
   their current error policy. CPU-work reporting never weakens or replaces
   them.

## 5. Stable contributor vocabulary

GCP-3.1 may add detail below these ids, but it must not rename them based on a
C# method, partial file, register, or implementation strategy.

| Stable id | Owned work |
| --- | --- |
| `frame.boundary.active` | Non-idle target work at a frame boundary, excluding other named child contributors. |
| `input.poll` | One target input snapshot and held/edge-state update. |
| `camera.position` | Bounded target camera request/logical-position work. |
| `camera.apply` | Camera application excluding separately reported world phase work. |
| `world.prepare` | One selected raw/packed edge preparation phase, including bank/window access. |
| `world.commit` | One selected visible edge commit phase. |
| `collision.aabb` | One camera-relative AABB flags query. |
| `collision.hit-top` | One camera-relative AABB hit-top query. |
| `audio.update` | One selected per-frame audio service path. |
| `sprite.draw` | One logical/metasprite draw preparation, multiplied by emitted hardware pieces when required. |
| `sprite.publish` | One complete retained OAM publication boundary, including call/setup, transfer/wait, and return. |
| `sprite.publish.transfer` | Non-additive physical DMA transfer detail below `sprite.publish`. |
| `actor.spawn.recycle` | Pool-capacity traversal that retires off-window actors. |
| `actor.spawn.scan` | Authored/candidate spawn traversal and `used` check. |
| `actor.spawn.record-read` | Immutable spawn field lookup for one selected record. |
| `actor.spawn.slot-search` | Fixed-pool search and successful/retry assignment path. |
| `actor.phase.update` | One generated actor update traversal. |
| `actor.phase.touch-tiles` | One generated tile-touch traversal and its reachable queries. |
| `actor.phase.land-on-tiles` | One generated landing traversal and its reachable queries. |
| `actor.phase.touch-player` | One generated player-contact traversal. |
| `actor.phase.draw` | One generated draw traversal. |
| `target.struct-array-address` | Target-owned address materialization detail used below a generated phase subtotal. |
| `user.dynamic-loop` | User loop/helper expansion whose tick-local trip count is not statically bounded. |
| `runtime.uncalibrated-state` | Compiler/runtime-owned state-dependent work without an accepted finite descriptor. |

Stable report order is source execution order for root contributors, then
stable id for contributors sharing the same source position. Target-specific
children use the same ordering rule. Tests may assert ids, counts, ranges,
status, and calibration revision; they must not assert private C# symbol names.

## 6. Diagnostic and report policy

GCP-3.1 implements this policy in the shared `SdkCpuWorkReport` model and
attaches it to Game Boy and NES build reports. The current target compilers do
not yet have a complete bounded descriptor whose finite range crosses or
exceeds the frame window, so current production samples compile with status
`incomplete` rather than warning or erroring. There is still no public
RetroSharp source syntax for cycle counts and no public CLI cycle flag; the
report is compiler/tooling metadata.

Let `window` be section 2's target value and let `known-lower` and
`known-upper` be the composed numeric subtotal of calibrated coverage. An
unknown remainder is excluded, but an accepted numeric subcomponent of an
incomplete parent is projected exactly once into that known subtotal. Thus the
known range is not a bound for the whole tick whenever `unknown[]` is non-empty:

| Condition | Compilation/report behavior | Report status |
| --- | --- | --- |
| `known-lower > window` | Error `GCP1001`: the compiler-known work cannot fit even on its cheapest accepted path. | `exceeds` |
| `known-lower <= window < known-upper` | Warning `GCP1002`: at least one accepted compiler-known path can exceed the frame. | `crosses` |
| `known-upper <= window`, no unknowns | No CPU-work diagnostic. | `fits` |
| `known-upper <= window`, unknowns present | No default warning; the explicit report names incomplete coverage and must not claim cadence. | `incomplete` |

The error uses only a lower-bound proof, avoiding rejection from an overly
conservative upper bound. The warning exposes a possible generated-work miss
without pretending it is inevitable. Unknown user work alone does not warn on
every ordinary program, but every requested report states that the numeric
headroom is not a whole-program guarantee. A current exact-ROM acceptance
failure remains authoritative even when a static report says `fits`.

The deterministic report schema is:

```text
target
profile
unit
frame-window
known-lower
known-upper
status
contributors[] { id, category, basis, count, unit-lower, unit-upper,
                 total-lower, total-upper, calibration, detail-of }
unknown[]      { id, reason }
```

An `unknown[]` entry may reference a contributor that still has a numeric
lower bound; it records the absent upper coverage and is not a second charge.

Diagnostic text must name target, selected profile, native unit, window,
known range, and the contributors that make the range cross/exceed it. The
report is tooling/compiler metadata only; there is no public RetroSharp source
syntax for cycles.

The initial GCP-3.1 report uses that schema directly. Game Boy reports
`target=gb`, unit `t-cycles`, and frame window `70,224`; NES reports
`target=nes`, unit `cpu-cycles`, and frame window `29,780`. If the reachable
SDK stream can publish retained sprites, Game Boy adds
`sprite.publish.transfer` at `640..640` below `sprite.publish`, while NES adds
the same stable contributor at `513..514`. Programs with no retained sprite
publication do not claim that transfer. Both reports keep the rest of section
5's generated, SDK-runtime, target-runtime, and user-loop coverage as explicit
`unknown[]` entries.

## 7. Current calibration evidence

The v1 representation is calibrated from emitted ROMs and target test CPUs,
not AST node counts. These existing gates are evidence and starting
descriptors; GCP-3.1 starts their executable projection in the report model.

| Evidence | Accepted fact used by this contract |
| --- | --- |
| `GameBoyTestCpu.DmgCyclesPerFrame` | Exact 70,224 T-cycle frame window. |
| `GameBoyTestCpu` OAM DMA model and `ActorProjectileFunctionalAcceptanceTests` | The model timestamps a 640 T-cycle physical DMA interval from the `$FF46` write. It does not include pre-write `CALL`/page setup or post-transfer return, and it does not independently price the HRAM wait instructions that execute during that interval. |
| `GameBoyWorldPackReaderTests` complete-stage collision gate | Cold lookup <= 70,224; resident <= 2,300; repeated cell <= 300; memo hit <= 900 T-cycles for that emitted profile. |
| `NesTestCpu` frame model | NTSC physical observations average about 29,780.5 CPU cycles; v1 uses the conservative 29,780 whole-cycle floor. |
| `NesTestCpu` OAM DMA model | The physical mapper-0 DMA transfer costs 513 cycles in the model and 513..514 on hardware; the emitted immediate load and `$4014` store wrapper are not included. |
| `NesWorldPackReaderTests` complete-stage gates | Collision cold <= 5,000, resident <= 375, repeated cell <= 120; visual cold <= 7,500 and resident <= 400 CPU cycles for that emitted profile. |
| `NesLargeWorldCameraTests` | Resident complete-stage column preparation <= 13,000 and bounded packed column commit <= 2,136 CPU cycles. |
| NES GCP-2.3 struct-array emitted-code/runtime tests | A direct zero-page index at Actor stride 13 materializes X in 15 bytes / 24 CPU cycles, down from 41 / 68. The accepted one-byte-offset domain is bounded by 33 bytes / 57 cycles at stride 255. A shared `Sprite.Draw` slot cursor charges one materialization for matching runtime fields. |
| `validation/generated-code-performance/baseline.tsv` | Exact ROM/profile/hash plus 20 warm-up and 100-frame cadence classification for the 13 canonical cases per target. |

`baseline.tsv` is the refreshable current snapshot. In a post-GCP-1.1 tree it
contains the constant-cost ROM-lookup results. The pre-GCP-1.1 equality counts
and cadence observations retained in section 9 remain frozen GCP-0.2
calibration evidence rather than a description of the current lookup shape.

Thresholds owned by a particular packed-world or cartridge profile must not be
silently reused for another profile. New descriptors require a focused
emitted-code/runtime test that measures the exact production bytes and records
the calibration revision.

## 8. Calibration completeness and representative subtotals

The current GCP-3.1 executable report does not pretend that every contributor
already has a numeric cycle descriptor. On the exact GCP-0.1 profiles, the only
reusable per-frame numeric subcomponent currently isolated by both emitted
behavior and a target CPU test is the physical OAM transfer. It is non-additive
detail under the still-incomplete `sprite.publish` parent:

| Target/profile | Numeric non-additive detail | Partial transfer-only known lower | Arithmetic remainder after transfer only (not headroom) |
| --- | --- | ---: | ---: |
| `gb` / `gb-rom-only-current` | `sprite.publish.transfer` = one 640 T-cycle DMA | `sprite.publish` >= 640 T-cycles, upper unknown | 69,584 T-cycles |
| `nes` / `nes-mapper-0-current` | `sprite.publish.transfer` = one 513..514-cycle DMA | `sprite.publish` >= 513 CPU cycles, upper unknown | 29,266..29,267 CPU cycles |

Those transfer-only lower bounds are reported and deliberately classified
`incomplete`: they omit the
rest of the publication boundary as well as generated spawn/phase instructions,
input, camera, collision, and logical draw preparation. They prove only the
partial arithmetic and cannot classify either ROM as statically fitting or
state usable remaining budget. The exact-ROM cadence result remains the
classification evidence until later descriptors complete the report.

The boundary between the initial GCP-3.1 report and remaining calibration is:

| Contributor family | What is exact or numeric now | Remaining descriptor needed | Why no complete cycle number is claimed here |
| --- | --- | --- | --- |
| `actor.spawn.recycle` / `actor.spawn.scan` | Literal pool/spawn visit counts from generated `for` bounds. | Target/profile lower and upper cycles per complete traversal, including loop control and active/used branch paths. | These loops are inline in `Main`; there is no isolated emitted symbol or current test-CPU timing boundary. |
| `actor.spawn.record-read` | Exactly 13 field materializations per unused record; the historical conditional equality counts below are derivable from the pre-GCP-1.1 generated expression tree. | Per-record target range covering the historical inline condition ladders and current ROM/window reads after GCP-1.1. | Equality counts are target-neutral work units, not LR35902/6502 cycles; branch direction, inline loads, and bank access decide cycles. |
| `actor.spawn.slot-search` | Literal pool-capacity loop for every eligible unused record. | Empty/full/success target branch range, multiplied by the compiler-known maximum eligible candidates. | The emitted loop continues through its fixed capacity after assignment; source-level candidate count alone is not a cycle price. |
| `actor.phase.*` | Literal pool-capacity visits for `Update`, `TouchTiles`, `LandOnTiles`, and `Draw`; query/draw call-site bounds below. NES GCP-2.3 calibrates direct stride-13 address materialization at 24 CPU cycles and one shared-draw cursor at the same 24-cycle cost. | Target/profile range for inactive/active/kind paths, with `target.struct-array-address` as non-additive child detail and the accepted NES calibration rather than the retired repeated-add shape. | The remaining phase paths inline target operations whose branch composition is not yet a complete descriptor; counting AST statements would still be false precision. |
| `input.poll` | Exactly one call in every canonical tick. | One target-owned finite emitted-path range for the complete input snapshot/update. | Current tests validate values and hardware reads, but do not isolate total operation cycles as a reusable descriptor. |
| `camera.position` / `camera.apply` | Exactly one call each; the GCP-0.1 no-input trace requests no edge. | Separate no-edge and reachable raw/packed prepare/commit ranges for the selected profile. | State, axis, and selected world path change the emitted runtime work; one generic number would double-charge `world.prepare`/`world.commit`. |
| `collision.*` | At most two query call sites per active visible actor in this fixture. Existing complete-stage profile gates provide the section 7 bounds. | Bounds for the canonical fixture's selected raw/packed/profile path, composed by reachable cache state. | Complete-stage MMC3/MBC1 thresholds cannot be silently assigned to the smaller ROM-only/mapper-0 fixture. |
| `sprite.draw` | At most one stable logical draw call site per pool slot and one-piece geometry. | Target/profile inactive/visible draw-preparation range, excluding `sprite.publish`. | Kind zero in an unused slot reaches no draw branch; OAM preparation for a populated slot is inline and is distinct from the calibrated publication-transfer detail. |
| `sprite.publish` | Only child detail `sprite.publish.transfer` is numeric: 640 GB T-cycles or 513..514 NES CPU cycles for these profiles. The parent has that partial lower bound and an unknown upper bound. | Calibrate the complete emitted boundary: call/setup, register write, transfer-overlapped wait where applicable, and return, counting overlap once; add a distinct MMC3 sequential-publication descriptor when selected. | The CPU tests timestamp the DMA transfer itself, not all instructions surrounding publication. The child remains non-additive once the full parent is known. |
| `audio.update` | Count zero in every GCP-0.1 case. | Selected-asset maximum due-body plus bank/channel-restore range when audio is present. | No audio work belongs in the canonical fixture subtotal, and no blanket reserve may stand in for a future asset-specific descriptor. |

This table closes policy without hiding calibration debt: every absent
descriptor enters the GCP-3.1 report as `runtime.uncalibrated-state` or the
matching stable generated/user unknown, keeping status `incomplete`. Adding
the emitted-code timing seams and populating complete target ranges remains
future calibration work. It does not change the accepted unit, range algebra,
stable ids, composition rules, or `GCP1001`/`GCP1002` thresholds.

## 9. GCP-0.1 pre-optimization fixture classification

The canonical source executes `WaitVBlank`, `Input.Poll`, camera position/apply,
spawn activation, `Update`, `TouchTiles`, `LandOnTiles`, and `Draw` in that
order. Let `S` be authored spawn count and `P` pool capacity.

- `wide-spawn-S`: `actor.spawn.scan` has literal count `S`; current
  `actor.spawn.record-read` performs 13 field lookups for each unused record;
  recycle and each actor phase are bounded by `P = 2`. No spawn is close enough
  to claim a slot during the observation window.
- `active-pool-P`: recycle and all four generated phases are bounded by `P`;
  `P` actors are active after warm-up, so touch/land/draw reach their bounded
  collision/sprite paths. The spawn upper path still includes unused-record
  reads and slot search because that path can occur on a valid later tick.

The pre-GCP-1.1 generated lookup shape frozen by GCP-0.2 also gives an exact
target-neutral work calculation. A non-uniform field is a conditional chain
with `S - 1` equality nodes. Evaluating it for every authored index performs
`(S - 1) * (S + 2) / 2` equality checks. Wide-spawn has two non-uniform fields
(`x` and `xHi`), so its upper equality subtotal is
`(S - 1) * (S + 2)`. Active-pool has only non-uniform `x`, so its corresponding
upper subtotal is `(P - 1) * (P + 2) / 2`. These are named generated-work
units; target branch/load cycles remain the descriptor work above.

Every case also has one `input.poll`, one `camera.position`, one
`camera.apply`, zero `audio.update`, one `sprite.publish`, and at most `P`
stable `sprite.draw` call sites per logical tick. The no-input GCP-0.1 trace
requests zero world edges; the static camera descriptor must still represent
its reachable no-edge/edge range.

| Case | Spawn scan visits | Record field materializations (upper) | Lookup equality checks (upper) | Slot-search visits (upper) | Recycle + four phase slot visits | Collision query call sites (upper) | Logical draw call sites (upper) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `wide-spawn-3` | 3 | 39 | 10 | 2 | 10 | 4 | 2 |
| `wide-spawn-8` | 8 | 104 | 70 | 2 | 10 | 4 | 2 |
| `wide-spawn-16` | 16 | 208 | 270 | 2 | 10 | 4 | 2 |
| `wide-spawn-24` | 24 | 312 | 598 | 2 | 10 | 4 | 2 |
| `wide-spawn-32` | 32 | 416 | 1,054 | 2 | 10 | 4 | 2 |
| `wide-spawn-48` | 48 | 624 | 2,350 | 2 | 10 | 4 | 2 |
| `wide-spawn-64` | 64 | 832 | 4,158 | 2 | 10 | 4 | 2 |
| `wide-spawn-96` | 96 | 1,248 | 9,310 | 2 | 10 | 4 | 2 |
| `wide-spawn-128` | 128 | 1,664 | 16,510 | 2 | 10 | 4 | 2 |
| `active-pool-1` | 1 | 13 | 0 | 1 | 5 | 2 | 1 |
| `active-pool-2` | 2 | 26 | 2 | 4 | 10 | 4 | 2 |
| `active-pool-4` | 4 | 52 | 9 | 16 | 20 | 8 | 4 |
| `active-pool-8` | 8 | 104 | 35 | 64 | 40 | 16 | 8 |

The wide-spawn slot-search upper is two visits because its authored records are
200 pixels apart, the activation window is 160 pixels, and at most one record
can be eligible before the fixed two-slot loop. The active-pool upper is
`P * P`: all `P` authored records can be eligible and each assignment loop
visits the complete capacity even after finding a slot. Record materialization,
slot-search, collision, and active branch columns are upper paths; their lower
bound is zero when all spawns are used or all slots are inactive/off-window.
Spawn scan and pool traversal counts remain exact; stable draw call-site counts
retain their literal upper bound regardless of those states.

The final two columns are observed scheduling intervals (`100 / logical ticks`)
from the pre-GCP-1.1 GCP-0.1 snapshot, not static instruction-cost estimates.
`fit` means 100/100; `overrun` means the exact ROM missed at least one
physical-frame cadence gate.

| Case | Compiler-known classification basis | GB observation | NES observation |
| --- | --- | --- | --- |
| `wide-spawn-3` | bounded `S=3`, `P=2`, no unknown generated loop | fit, 1.00 frames/tick | fit, 1.00 frames/tick |
| `wide-spawn-8` | bounded `S=8`, `P=2`, no unknown generated loop | fit, 1.00 | fit, 1.00 |
| `wide-spawn-16` | bounded `S=16`, `P=2`, no unknown generated loop | fit, 1.00 | fit, 1.00 |
| `wide-spawn-24` | bounded `S=24`, `P=2`, no unknown generated loop | fit, 1.00 | fit, 1.00 |
| `wide-spawn-32` | bounded `S=32`, `P=2`, no unknown generated loop | overrun, 2.00 | fit, 1.00 |
| `wide-spawn-48` | bounded `S=48`, `P=2`, no unknown generated loop | overrun, 2.00 | fit, 1.00 |
| `wide-spawn-64` | bounded `S=64`, `P=2`, no unknown generated loop | overrun, 4.00 | overrun, 2.00 |
| `wide-spawn-96` | bounded `S=96`, `P=2`, no unknown generated loop | overrun, 7.14 | overrun, 4.00 |
| `wide-spawn-128` | bounded `S=128`, `P=2`, no unknown generated loop | overrun, 11.11 | overrun, 5.88 |
| `active-pool-1` | bounded `S=P=1`, four phase traversals, one active actor | fit, 1.00 | fit, 1.00 |
| `active-pool-2` | bounded `S=P=2`, four phase traversals, two active actors | fit, 1.00 | fit, 1.00 |
| `active-pool-4` | bounded `S=P=4`, four phase traversals, four active actors | fit, 1.00 | fit, 1.00 |
| `active-pool-8` | bounded `S=P=8`, four phase traversals, eight active actors | overrun, 2.00 | overrun, 2.00 |

Representative calculations anchor the target windows without conflating
scheduling time with emitted work. `wide-spawn-32` on Game Boy observes
7,022,400 / 50 = 140,448 T-cycles per completed tick, while the NES image
observes about 2,978,050 / 100 = 29,780.5 CPU cycles and retains 1:1 cadence.
At `wide-spawn-64`, the intervals are 280,896 Game Boy T-cycles and about
59,560.96 NES CPU cycles. `active-pool-8` similarly takes about two physical
frames per completed tick on both targets. These measurements prove that the
stable contributors need target-owned calibration; they do not become static
whole-program cycle claims.

The table exercises every contract outcome the GCP-3.1 report model needs:
known literal multiplicities, target-specific windows, finite branch ranges,
fixture-specific runtime contributors, and exact-ROM evidence that can disagree
with an incomplete static report. GCP-3.1 therefore implements the model and
policy above without reopening representation or diagnostic policy.
