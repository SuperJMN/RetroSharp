# AI-Navigable Architecture

Status: current owner map and change router.
Last updated: 2026-07-26.

Use this document when a compiler or cartridge-runtime change crosses more than
one module. Start at the deep module that owns the decision, follow its small
interface to one production adapter, and open only the focused evidence for
that concern. Historical refactor measurements belong in Git history rather
than this navigation interface.

## Deep-Module Map

| Concern | Authority and interface | Production route | Focused evidence |
| --- | --- | --- | --- |
| Game Boy runtime RAM | `GameBoyRuntimeMemoryLayout`: reserved ranges, named addresses, aliases, and validation | Builders, runtime compilers, packed runtimes, and lowerers consume named domain groups | `GameBoyRuntimeMemoryLayoutTests`, `RuntimeMemoryOwnershipArchitectureTests` |
| NES runtime RAM and external ABI | `NesRuntimeMemoryLayout`; `NesRuntimeAbiProjection.Serialize(...)` projects it without another address map | NES emission consumes the layout; CLI emits the ROM-bound sidecar; `tools/nes/runtime_abi.py` reads the projection | layout/projection tests, Python ABI tests, runtime-memory architecture tests |
| Shared frontend preparation | `TargetFrontendPreparation.Prepare(...)` returns one `PreparedTargetProgram` after the ordered target-neutral stages | `GameBoyRomCompiler.PrepareVideoProgram(...)` and `NesRomCompiler.PrepareVideoProgram(...)` supply target catalogs, assets, and final construction | `TargetFrontendPreparationArchitectureTests`, `CrossTargetFrontendPreparationTests` |
| Actor analysis and generation | `ActorFrameworkLowerer` and one `ActorFrameworkLoweringPlan`; domain state modules own mutable facts; `ActorFrameworkDomains.Contributions` is the generated-program seam | `TargetFrontendPreparation` analyzes once, lowers through the plan, and retains only facts needed for the late pool-budget check | lowering-plan tests, domain architecture tests, focused Actor suites |
| Game Boy portable SDK emission | `GameBoySdkOperationLowerer.Emit(...)`; its context supplies operand/storage mechanics only | `GameBoyRuntimeCompiler` owns one lowerer and routes the collected stream through `GameBoySdkStreamReader` | Game Boy operation/lowering suites and lowerer architecture tests |
| NES portable SDK emission | `NesSdkOperationLowerer.Emit(...)`; its context supplies operand/storage mechanics only | `NesRuntimeCompiler` owns one lowerer and routes the collected stream through `NesSdkStreamReader` | NES operation/lowering suites and lowerer architecture tests |
| Game Boy physical frame policy | `GameBoyFramePlan` | Target emission consumes the selected static plan | `GameBoyFramePlanTests` and target CPU-work tests |
| NES physical frame scheduling | `NesPhysicalFrameScheduler` is executable authority; `NesFramePlan` is validated private policy; `NesOamPublicationSchedule` owns sequential publication bytes and cost | Builders, runtime compilers, and lowerers receive the scheduler; lowerer partials implement closed byte mechanics without selecting phases | scheduler, frame-plan, OAM-publication, CPU-work, and architecture tests |
| Functional cartridge observation | `GameBoyFunctionalObservationEngine` and `NesFunctionalObservationEngine` project target-native events into semantic frame observations | Scenario runners consume observations without embedding target register rules | observation-engine tests and cross-target scenario acceptance |

## Physical Navigation Roots

Exact paths are architecture facts only where physical separation improves
locality.

Game Boy roots:

- `GameBoyRomLayout.cs`
- `GameBoyFramePlan.cs`
- `GameBoyRuntimeCompiler.cs`
- `GameBoySdkStreamReader.cs`
- `GameBoySdkOperationLowerer.cs`
- `GbBuilder.cs`

NES roots:

- `NesCartridgeLayout.cs`
- `NesPhysicalFrameScheduler.cs`
- `NesFramePlan.cs`
- `NesOamPublicationSchedule.cs`
- `NesRuntimeCompiler.cs`
- `NesSdkStreamReader.cs`
- `NesSdkOperationLowerer.cs`
- `PrgBuilder.cs`

`GameBoyRomBuilder.cs` and `NesRomBuilder.cs` are link/orchestration modules, not
owners of those extracted concerns. Feature-partial file names are navigation
hints rather than contracts.

## Guard Strategy

Prefer compiled symbols and IL edges for semantic ownership:

- one runtime-memory authority per target;
- one ordered shared frontend path;
- Actor domain state and contribution ownership;
- no target-lowerer backedge into a runtime compiler;
- one executable NES scheduling authority;
- a deliberate SDK operation inventory.

Use exact paths only for the physical roots above and their ROM-builder
non-owner check. Do not make private helper names, test method names, line
counts, documentation phrases, or file-fragment allowlists into architecture
contracts.

Explicit `RetroSharp.TestOwnership` metadata is reserved for the small number of
focused frontend exceptions that a compiled call-edge guard cannot classify
from suite locality. Ordinary target lowering tests are owned by their focused
suite and primary observable.

## Common Change Routes

### Add Or Move Target Runtime State

1. Start in the target `*RuntimeMemoryLayout` domain group.
2. Update range, alias, and overlap validation plus the focused layout test.
3. Consume the named member from emitters; do not create another address
   constant.
4. For NES external observations, extend `NesRuntimeAbiProjection` and the
   Python reader.
5. Run the runtime-memory ownership guard before target regressions.

### Add Or Reorder A Frontend Stage

1. Start at `TargetFrontendPreparation.Prepare(...)`.
2. Keep catalogs, resources, assets, capabilities, and final target-program
   construction in the two target adapters.
3. Update the architecture test for stage order and exclusive ownership.
4. Add one cross-target public-path diagnostic when behavior is user-visible.

Target compilers and monolithic test helpers must not reconstruct the shared
merge/parse/select/Actor/facade/inference/contract sequence.

### Add Actor Framework Behavior

1. Start in the matching Actor domain policy module and its generation module.
2. Put mutable facts in that domain state, not the root state.
3. Add one `GeneratedProgramContribution` when the domain contributes generated
   artifacts.
4. Retain the single lowering plan and shared syntax primitives.
5. Add focused domain behavior and keep symbol ownership green.

### Change A Portable SDK Operation

1. Confirm that the concept belongs to the portable SDK rather than the
   language or one target intrinsic.
2. Change collection and capability validation in the shared SDK model.
3. Implement target behavior in the matching lowerer feature module.
4. Keep only expression/storage mechanics in the lowering context.
5. Prove the primary observable: collected operation, diagnostic, runtime state,
   hardware budget, or behavior on a freshly compiled ROM.

Assert exact bytes only when an encoding, header, ABI field, or instruction
shape is the behavior being specified. Cartridge length, whole-ROM equality,
hashes, and exact cycle equalities are not general regression gates.

### Change NES Physical-Frame Work

1. Start at `NesPhysicalFrameScheduler`.
2. Extend a closed scheduler command only when new video-safe mechanics must
   cross its seam.
3. Change `NesFramePlan` for immutable profile facts, windows, bounds, or finite
   staging deadlines.
4. Prove emitted ordering and CPU-work projection through focused scheduler
   tests.
5. Run representative behavior on the freshly compiled NES ROM. A sidecar or
   ROM diff is useful diagnosis, not acceptance by itself.

### Change Functional Acceptance

1. Compile the scenario once.
2. Give that exact fresh ROM to the target adapter and observation engine.
3. Assert semantic behavior, cadence, transient integrity, and upper-bound
   budgets.
4. Keep target register interpretation in the observation engine rather than
   the scenario runner.

## Test Locality

Put a regression where its asserted behavior is owned:

- Operations, target capabilities or diagnostics, emission, runtime storage
  effects, and hardware budgets go in the matching focused target suite.
- Parsing, source-package or Actor rewriting, asset composition, linker/layout,
  whole-cartridge execution, and complete samples go in integration or
  acceptance suites.
- Cross-module acceptance may cover the same feature only when it asserts a
  distinct end-to-end behavior.
- A monolithic compiler suite must not duplicate a focused lowering regression.

This keeps test files navigable without maintaining a parallel manual taxonomy.

## CodeGraph Navigation

When `.codegraph/` exists, use concrete identifiers and exact paths. Start with
the smallest lookup that can find the owner:

```bash
codegraph query -p . "NesPhysicalFrameScheduler"
codegraph node -p . NesPhysicalFrameScheduler
codegraph callers -p . NesPhysicalFrameScheduler
codegraph node -p . --file src/RetroSharp.NES/NesPhysicalFrameScheduler.cs
```

For an unknown exact name, query a short literal first. Treat ranked exploration
results as candidates, then verify the selected source, compiled guard, and
focused tests. Do not preload every target backend or maintain a frozen list of
dozens of probes.

## Validation

Run the focused owner tests first, then the relevant target project and the
solution gate from `AGENTS.md`. For architecture-only changes, a ROM dry-run can
locate output drift; it does not replace behavioral simulation and does not
require preserving a tracked ROM byte-for-byte.
