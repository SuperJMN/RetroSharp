# AI-Navigable Architecture

Status: acceptance map for AIN-9 / #365, amended by the test-locality audit in
AIN-11 / #377. Measurements are descriptive, not size gates.

Use this document when a compiler or cartridge-runtime change needs a fresh
navigation path. Start from the owning deep module below, cross its small seam,
and then open only the production caller and focused tests for the concern.
Keep the language, portable 2D SDK, and target-intrinsic layers separate.

## Deep-module map

| Concern | Authority and small interface | Production path | Focused evidence |
| --- | --- | --- | --- |
| Game Boy runtime RAM | `src/RetroSharp.GameBoy/GameBoyRuntimeMemoryLayout.cs`: `ReservedRanges`, `NamedAddresses`, intentional aliases, `Validate()`, `ValidateUserLocalBytes(...)`, and `ValidateWorldPackStagingBytes(...)` | `GameBoyRomBuilder`, `GameBoyRuntimeCompiler*`, `GameBoyPackedCameraRuntime`, `GameBoyWorldPackRuntime`, and `GameBoySdkOperationLowerer*` consume its domain groups | `GameBoyRuntimeMemoryLayoutTests` plus `RuntimeMemoryOwnershipArchitectureTests` |
| NES runtime RAM and external ABI | `src/RetroSharp.NES/NesRuntimeMemoryLayout.cs` owns CPU RAM; `NesRuntimeAbiProjection.Serialize(...)` projects it without creating another map | NES builder/runtime/lowerer modules consume the layout; CLI `--runtime-abi-out` emits the ROM-bound sidecar; `tools/nes/runtime_abi.py` is the external-probe reader | `NesRuntimeMemoryLayoutTests`, `NesRuntimeAbiProjectionTests`, `tools/nes/tests/test_runtime_abi.py`, and `RuntimeMemoryOwnershipArchitectureTests` |
| Shared frontend preparation | `TargetFrontendPreparation.Prepare(...)` returns one internal `PreparedTargetProgram` after the ordered target-neutral stages | `GameBoyRomCompiler.PrepareVideoProgram(...)` and `NesRomCompiler.PrepareVideoProgram(...)` provide target catalogs/assets and are the only target adapters; compile, 2D collection, and audio collection reuse them | `TargetFrontendPreparationArchitectureTests` and `CrossTargetFrontendPreparationTests` |
| Actor Framework analysis and generation | `ActorFrameworkLowerer.Analyze(...)`, `Lower(...)`, and one `ActorFrameworkLoweringPlan`; Actor/Spawn/Projectile/Effect/GeneratedCall state modules own mutable facts; `ActorFrameworkDomains.Contributions` is the ordered generated-program seam | `TargetFrontendPreparation` analyzes once, lowers through the plan, and retains that plan only for late metasprite-aware pool-budget validation | `ActorFrameworkLoweringPlanTests`, `ActorFrameworkDomainArchitectureTests`, and the `ActorFrameworkActors`, `ActorFrameworkProjectiles`, `ActorFrameworkEffects`, and `ActorFrameworkCrossDomain` suites |
| Game Boy portable SDK emission | `GameBoySdkOperationLowerer.Emit(Sdk2DOperation)` and feature partials; `GameBoySdkLoweringContext` supplies only operand/storage primitives | `GameBoyRuntimeCompiler` owns one lowerer and routes its collected `Sdk2DProgram` through `GameBoySdkStreamReader`; the lowerer must not call back into the runtime compiler | `GameBoySdkLoweringArchitectureTests`, `GameBoySdkOperationBoundaryTests`, and the `GameBoySdk{FrameInput,Sprite,Animation,CameraRuntime,CameraStreaming,CollisionRuntime,Collision}LoweringTests` suites |
| NES portable SDK emission | `NesSdkOperationLowerer.Emit(Sdk2DOperation)` and feature partials; `NesSdkLoweringContext` supplies only operand/storage primitives | `NesRuntimeCompiler` owns one lowerer and routes its collected `SdkOperationStream` through `NesSdkStreamReader`; the lowerer must not call back into the runtime compiler | `NesSdkLoweringArchitectureTests`, `NesSdkOperationBoundaryTests`, and `NesSdk{FrameInput,Sprite,CameraStreaming,Collision}LoweringTests` |

The target cartridge modules are deliberately physical as well as conceptual.
For Game Boy, the five documented physical navigation roots for
layout/placement, runtime compilation, SDK stream reading, SDK emission, and
byte building are `GameBoyRomLayout.cs`,
`GameBoyRuntimeCompiler.cs`, `GameBoySdkStreamReader.cs`,
`GameBoySdkOperationLowerer.cs`, and `GbBuilder.cs`. The NES equivalents are
`NesCartridgeLayout.cs`, `NesRuntimeCompiler.cs`, `NesSdkStreamReader.cs`,
`NesSdkOperationLowerer.cs`, and `PrgBuilder.cs`. Feature partials are the next
navigation hop, but their file names are not architecture contracts; the ROM
builders are link/orchestration modules, not the owner of those extracted
concerns.

## Guard taxonomy

Semantic guards inspect compiled symbols and IL edges. Runtime-memory owners,
frontend-stage ownership, Actor Framework state/contributions, target-lowerer
backedges, the SDK operation inventory, and test-suite ownership all belong in
this category. Focused lowering suites declare `RetroSharp.TestOwnership`
metadata, so method and source-file renames do not require architecture-test
edits. Focused tests that intentionally exercise frontend stages declare the
same metadata on the calling method instead of relying on a source fragment.

Physical guards use exact repository paths only for the ten navigation roots
listed above. Each path assertion carries the physical invariant it protects.
Feature-partial names, test method names, and private declaration text are not
physical contracts.

## Common change paths

### Add or move target runtime state

1. Start in the target `*RuntimeMemoryLayout` domain group and reserve or move
   the address there.
2. Update its range/alias validation and focused layout test.
3. Consume the named layout member from emitters. Do not add a private address
   constant to a runtime compiler, builder, packed runtime, or lowerer.
4. For NES observations, extend `NesRuntimeAbiProjection` and
   `tools/nes/runtime_abi.py`; never add a second Python RAM map.
5. Run `RuntimeMemoryOwnershipArchitectureTests` before target regressions.

### Add or reorder a frontend stage

1. Start at `TargetFrontendPreparation.Prepare(...)`.
2. Keep target-specific catalogs, resources, assets, and final video-program
   construction in the two `PrepareVideoProgram(...)` adapters.
3. Update `TargetFrontendPreparationArchitectureTests` so the ordered stage and
   exclusive ownership are structural facts.
4. Add the public-path diagnostic to `CrossTargetFrontendPreparationTests`.

Target compilers and monolithic compiler regression files must not reconstruct
the merge/parse/select/Actor/facade/inference/contract sequence themselves.

### Add an Actor Framework domain behavior

1. Start in the matching `ActorFrameworkLowerer.<Domain>.cs` policy module and
   its `.Generation.cs` implementation.
2. Put mutable facts in the matching domain state, not `ActorFrameworkState`.
3. If the domain contributes generated artifacts, add one
   `GeneratedProgramContribution` to `ActorFrameworkDomains.Contributions`.
4. Keep the single lowering plan/interface and shared syntax primitives.
5. Add focused domain tests and preserve the compiled-symbol ownership guards.

### Migrate or change a portable SDK operation

1. Confirm the operation belongs to the portable SDK, not the language or a
   target intrinsic, in `docs/ArchitectureRoadmap.md`.
2. Update collection/validation in the SDK model and capability layer.
3. Implement target emission in the matching `*SdkOperationLowerer` feature
   partial; keep only operand/storage mechanics in `*SdkLoweringContext`.
4. Route the collected stream through the existing runtime compiler reader.
5. Pin emitted bytes in the focused lowering suite and keep the lowerer-to-
   runtime backedge guard green.

Classify a target SDK regression by its primary observable, not by the public
entry point used to arrange it:

- If the assertion pins collected SDK operations, capability validation or a
  diagnostic for that operation, target-emitted bytes, runtime storage effects,
  or that operation's target hardware budget, it belongs in the matching
  focused lowering suite. This remains true when `CompileSource(...)` is the
  smallest realistic way to arrange the program.
- If the value of the test is the complete path across source parsing, facade
  or Actor rewriting, asset import, linker/layout, generated cartridge data,
  emulator execution, or a complete sample, it belongs in the target compiler
  or acceptance suite.
- A monolithic compiler test must not duplicate a focused lowering regression.
  Cross-module acceptance may cover the same public feature only when it
  asserts a distinct end-to-end contract.

The AIN-11 audit applied this rule to every remaining Game Boy and NES compiler
regression. It moved 33 Game Boy and 5 NES emission-, capability-, and
budget-specific regressions into the focused suites above. The monoliths retain
frontend/helper parity, asset-pipeline composition, linker/runtime, and complete
scenario coverage; shared PNG and sprite-fixture support lives in neutral test
support modules rather than either monolithic suite.

## CodeGraph probes

When `.codegraph/` exists, run this exact file-mode probe from the repository
root before opening a complete backend. File mode is deliberate: semantic
search can rank a high-fan-out production symbol ahead of a reflection-only or
Python test edge. `--symbols-only` loads each exact module and its dependents
without dumping a complete backend:

```bash
files=(
  src/RetroSharp.GameBoy/GameBoyRuntimeMemoryLayout.cs
  src/RetroSharp.GameBoy/GameBoyRuntimeCompiler.cs
  src/RetroSharp.GameBoy.Tests/GameBoyRuntimeMemoryLayoutTests.cs
  src/RetroSharp.NES/NesRuntimeMemoryLayout.cs
  src/RetroSharp.NES/NesRuntimeAbiProjection.cs
  src/RetroSharp.NES/NesRuntimeCompiler.cs
  src/RetroSharp.NES.Tests/NesRuntimeMemoryLayoutTests.cs
  src/RetroSharp.NES.Tests/NesRuntimeAbiProjectionTests.cs
  tools/nes/runtime_abi.py
  tools/nes/tests/test_runtime_abi.py
  src/RetroSharp.Sdk.Frontend/TargetFrontendPreparation.cs
  src/RetroSharp.GameBoy/GameBoyRomCompiler.cs
  src/RetroSharp.NES/NesRomCompiler.cs
  src/RetroSharp.Architecture.Tests/TargetFrontendPreparationArchitectureTests.cs
  src/RetroSharp.NES.Tests/CrossTargetFrontendPreparationTests.cs
  src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.cs
  src/RetroSharp.Sdk.Frontend/ActorFrameworkLowerer.GeneratedProgram.cs
  src/RetroSharp.GameBoy.Tests/ActorFrameworkLoweringPlanTests.cs
  src/RetroSharp.Architecture.Tests/ActorFrameworkDomainArchitectureTests.cs
  src/RetroSharp.GameBoy/GameBoySdkOperationLowerer.cs
  src/RetroSharp.GameBoy.Tests/GameBoySdkAnimationLoweringTests.cs
  src/RetroSharp.GameBoy.Tests/GameBoySdkCameraRuntimeLoweringTests.cs
  src/RetroSharp.GameBoy.Tests/GameBoySdkCollisionRuntimeLoweringTests.cs
  src/RetroSharp.GameBoy.Tests/GameBoySdkFrameInputLoweringTests.cs
  src/RetroSharp.GameBoy.Tests/GameBoySdkOperationBoundaryTests.cs
  src/RetroSharp.Architecture.Tests/GameBoySdkLoweringArchitectureTests.cs
  src/RetroSharp.NES/NesSdkOperationLowerer.cs
  src/RetroSharp.NES.Tests/NesSdkFrameInputLoweringTests.cs
  src/RetroSharp.NES.Tests/NesSdkOperationBoundaryTests.cs
  src/RetroSharp.Architecture.Tests/NesSdkLoweringArchitectureTests.cs
)

for path in "${files[@]}"; do
  codegraph node -p . --file "$path" --symbols-only
done
```

The AIN-9 acceptance run used the then-current 901-file index and all 27 file
probes returned the requested symbol map. AIN-11 adds the three new focused
Game Boy suite probes, bringing the reproducible recipe to 30 files. Together
they locate each authority, its production route, and its focused C# or Python
tests without loading either complete ROM builder. The direct frame/input
lowerer tests expose an explicit constructor edge; the boundary/architecture
files pin the end-to-end stream seam and compiled ownership guard separately.

## Final measurements

The baseline is commit `7260e70`, immediately before the AIN epic. These line
counts describe locality gained; they are not acceptance thresholds and must
not become arbitrary size gates.

| Former hotspot | Baseline | AIN-9 tree | AIN-11 tree |
| --- | ---: | ---: | ---: |
| `GameBoyRomBuilder.cs` | 10,948 | 1,138 | 1,138 |
| `NesRomBuilder.cs` | 8,890 | 1,528 | 1,528 |
| root `ActorFrameworkLowerer.cs` | 4,530 | 622 | 622 |
| `GameBoyRomCompilerTests.cs` | 8,429 | 6,483 | 5,337 |
| `NesRomCompilerTests.cs` | 5,110 | 3,503 | 3,173 |

The final tree has 11 Game Boy SDK-lowerer modules, 8 NES SDK-lowerer modules,
7 focused Game Boy SDK-lowering suites, 4 focused NES SDK-lowering suites,
14 Actor Framework modules, and 27 architecture `[Fact]`/`[Theory]`
declarations. Validation results and exact runner hashes belong in the closing
PR/epic record because they are execution evidence, not permanent design limits.

## Acceptance commands

```bash
dotnet test src/RetroSharp.Architecture.Tests/RetroSharp.Architecture.Tests.csproj -m:1
python3 -m unittest discover -s tools/nes/tests -v
tools/gameboy/generate_sample_roms.py --dry-run
dotnet test RetroSharp.sln -m:1
git diff --check
```

Build the representative runner outputs into a temporary directory and compare
them byte-for-byte with `samples/runner/bin/runner.gb`, `runner.nes`, and
`runner.nes.runtime-abi.json`. Do not regenerate tracked ROMs for this
architecture-only acceptance slice unless the dry run proves that they changed.
