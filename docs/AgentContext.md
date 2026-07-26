# AI Agent Project Context

Status: current navigation and execution context.
Last updated: 2026-07-26.

Read this after `AGENTS.md`. It is deliberately short and current. Historical
issue closeouts, commit hashes, ROM hashes, and one-off acceptance measurements
belong in Git history, closed issues, and the relevant acceptance documents;
they are not part of every agent's startup context.

## Authority Order

When sources disagree, use this order:

1. `AGENTS.md` for repository discipline and the product acceptance policy.
2. The live issue or user-provided specification for the requested slice.
3. Current source and executable tests for implemented behavior.
4. The task-owning reference or roadmap selected from the routing table below.
5. Historical roadmap checklists and closed-issue narratives as background only.

Do not infer active work from an unchecked or checked roadmap item. Verify live
issue state when status matters.

## Project Shape

RetroSharp is a .NET 10 compiler for a small C#-like language that emits Game
Boy and NES cartridges directly.

| Layer | Main owner |
| --- | --- |
| Language, parsing, semantic checks, and shared compiler models | `src/RetroSharp.Core`, `src/RetroSharp.Parser`, and the language-specific projects under `src` |
| Portable source package and target-neutral SDK preparation | `sdk/RetroSharp.Portable2D`, `src/RetroSharp.Sdk.Frontend`, and `src/RetroSharp.Core/Sdk` |
| Game Boy cartridge lowering | `src/RetroSharp.GameBoy` |
| NES cartridge lowering | `src/RetroSharp.NES` |
| CLI/project-manifest entry path | `src/RetroSharp.Cli` |
| Behavioral cartridge simulation | `src/RetroSharp.FunctionalAcceptance` and target test projects |
| Acceptance samples | `samples`, classified by `samples/manifest.json` |

The shared runner is the main playable acceptance app. Its presence in both
targets does not make every call in it a portable API contract.

## Route By Task

| Question | Open next |
| --- | --- |
| What layer owns a new concept? | `ArchitectureRoadmap.md` |
| What does portable 2D expose? | `Portable2DSdkV1.md` |
| Where is a deep module or production/test seam? | `AiNavigableArchitecture.md` |
| How do frontend preparation, Actor lowering, and SDK lowering fit together? | `SdkArchitecture.md` |
| What does a target support now? | `GameBoyTarget.md` or `NesTarget.md` |
| How should runner behavior be reproduced? | `GameBoyRunnerDebugging.md` |
| What owns Game Boy runner physical-frame cadence? | `GameBoyRunnerObserverFidelity.md` |
| Is a sample portable evidence? | `samples/README.md` and `samples/manifest.json` |
| What owns functional cadence and transient observations? | `FunctionalRomAcceptance.md` and the scenario-specific acceptance document |
| Is the task about large maps or banking? | `LargeWorldsRoadmap.md` |
| Is the task about generated-code CPU work? | `GeneratedCodePerformanceRoadmap.md` |
| Is the task reviewing historical NES frame scheduling / closed #410? | `NesFrameSchedulingRoadmap.md` |
| Is the task about issue execution or publication? | `AgentExecution.md` |

Load only the selected route. Several roadmap documents contain valuable
completed execution history, but reading all of them up front adds noise and
can make an old constraint look current.

## Decisions To Preserve

- Choose the layer first: language, portable SDK, or target intrinsic.
- The language remains target-neutral. Cameras, sprites, controllers, tilemaps,
  PPU registers, and LCD details do not belong there.
- Portable calls cross an explicit capability check before target lowering.
- Hardware policy and byte mechanics stay target-owned.
- High-level syntax must lower to fixed storage, direct calls and branches, or
  constants. RetroSharp does not gain heap allocation, GC, boxing, delegates,
  closures, RTTI, virtual dispatch, or hidden identity.
- Source-package dot calls such as `Video.Init()` and `Camera.SetPosition(...)`
  are static grouping syntax, not object dispatch.
- `TargetFrontendPreparation.Prepare(...)` is the single owner of the ordered
  shared preparation stages. Target adapters provide catalogs, capabilities,
  assets, resources, and final target-program construction.
- `Sdk2DOperation` and `SdkAudioOperation` are internal compiler models, not the
  public source package and not generic dumping grounds for target behavior.
- Actor Framework lowering is source-to-source work in
  `RetroSharp.Sdk.Frontend`; it must not create actor-specific target
  intrinsics.
- Each target runtime-memory layout is authoritative. Emitters consume named
  layout members rather than recreating addresses.
- Physical frame policy is target-private. NES executable scheduling goes
  through `NesPhysicalFrameScheduler`.
- Game Boy runner cadence investigations use SameBoy's `GB_run_frame` timeline
  as physical-frame authority. `GameBoyTestCpu` remains a behavioral simulator,
  not a physical-frame clock.
- A bug fix iterates against one authority: the in-process behavioral simulator
  (`GameBoyTestCpu`/`NesTestCpu`) that owns its named RED test. Physical
  emulators and MCP transports are diagnostic confirmation only; do not alternate
  oracles mid-fix, because a fix that greens one observer while another stays red
  is not solved.
- Transitional public forms remain supported until an explicit removal slice
  changes their contract.

## High-Leverage Code Anchors

Start at the narrow owner, then inspect its direct caller and focused evidence.

| Concern | Owner | Focused evidence |
| --- | --- | --- |
| Shared source-to-target preparation | `TargetFrontendPreparation` | `TargetFrontendPreparationArchitectureTests`, `CrossTargetFrontendPreparationTests` |
| Actor analysis and generation | `ActorFrameworkLowerer` and its domain state/contribution modules | `ActorFrameworkLoweringPlanTests`, `ActorFrameworkDomainArchitectureTests`, focused Actor suites |
| Game Boy RAM | `GameBoyRuntimeMemoryLayout` | `GameBoyRuntimeMemoryLayoutTests`, `RuntimeMemoryOwnershipArchitectureTests` |
| NES RAM and external ABI | `NesRuntimeMemoryLayout`, `NesRuntimeAbiProjection` | layout/projection tests and `tools/nes/tests/test_runtime_abi.py` |
| Game Boy portable operation emission | `GameBoySdkOperationLowerer` | `GameBoySdk*LoweringTests`, `GameBoySdkLoweringArchitectureTests` |
| NES portable operation emission | `NesSdkOperationLowerer` | `NesSdk*LoweringTests`, `NesSdkLoweringArchitectureTests` |
| Game Boy frame policy | `GameBoyFramePlan` | `GameBoyFramePlanTests` |
| NES executable frame scheduling | `NesPhysicalFrameScheduler`, with `NesFramePlan` as private policy data | scheduler, frame-plan, OAM-publication, and CPU-work tests |
| Functional cartridge observation | `GameBoyFunctionalObservationEngine`, `NesFunctionalObservationEngine` | target observation tests and cross-target scenario acceptance |

The ROM builders link and orchestrate output; they are not the owner of runtime
memory, frontend stages, SDK emission, or scheduling policy.

Classify a regression by its primary observable:

- Collected operations, capabilities, target diagnostics, target emission,
  runtime storage effects, or hardware budgets belong in the matching focused
  target suite.
- Parsing, facade or Actor rewriting, asset import, linker/layout behavior,
  complete cartridge execution, and complete samples belong in integration or
  acceptance suites.
- Exact bytes, cartridge hashes, and exact cycle counts are diagnostic only
  unless a cartridge-format field or ABI encoding is itself the behavior under
  test.

## Runner And Tiled Facts

- Build the runner from `samples/runner/runner.retrosharp.json`; it includes its
  game-owned helper and state files.
- The runner uses the complete `samples/runner/assets/maps/stage1.tmj` and
  `stage1.tsx`. `stage1.playable.tmj` is a smaller historical fixture.
- `World.Load(...)` first produces target-neutral logical map and collision
  facts, then each target resolves graphics and encodes its native tiles.
- Game Boy has one scrolling background tilemap. The authoring `background`
  layer is the visual base; non-empty `world` cells overlay it, while empty
  cells keep the background.
- Collision flags are independent from visual composition. Tileset object
  rectangles or explicit collision data provide those flags.
- If Tiled properties `retrosharpWorldY` and `retrosharpStreamY` move the
  playable band, shift the background layer by the same amount. Otherwise the
  two visual layers become vertically misaligned.
- Complete worlds may be taller or wider than the visible hardware tilemap.
  Startup preloads a viewport and target-owned streaming retains the full map
  facts in ROM.
- `Input.Poll()` is the tick boundary. Public gameplay uses `Input.IsDown`,
  `Input.WasPressed`, `Input.WasReleased`, and `Input.HoldTicks` with
  `Button.*`, plus `Sprite.Width`.
- Original DMG input needs settled `JOYP` row reads. D-pad/A-B bleed is a
  backend/runtime problem before it is sample logic.
- Byte-backed target state can wrap. Clamp vertical runner state before
  collision or reset logic near the top of the scene.

## Known Execution Traps

| Trap | Correct action |
| --- | --- |
| Asking CodeGraph to interpret prose | Query a concrete symbol, file path, or short literal; verify candidates in source and tests |
| Assuming `RetroSharp.Cli --help` exists | Read `src/RetroSharp.Cli/Program.cs` or `README.md`; unknown options fail |
| Running several `dotnet` builds/tests in one checkout | Serialize them and use `-m:1`; shared build outputs can race |
| Testing a stale tracked ROM | Compile once for the scenario and execute that fresh ROM |
| Treating ROM equality or a dry-run diff as the product gate | Diagnose the change, then judge observable behavior and bounded CPU work |
| Treating roadmap history as live status | Check the live issue and current source |
| Editing generated ROMs manually | Regenerate them from their source sample |
| Treating generated screenshots as source | Ignore `samples/runner/*.png` unless the task requests them |
| Applying broad formatting to inherited debt | Format touched files only and run `git diff --check` |
| Fixing hardware/emulator symptoms only in sample code | Inspect target runtime behavior first |
| Debugging the complete runner without isolation | Use `tools/gameboy/runner_diagnostics.py` and locate the first failing step |
| Editing a bug fix before a named RED test fails for it | Reproduce as the cheapest deterministic `*TestCpu` test first; iterate the fix until that test greens twice |
| Alternating oracles while fixing a bug | Iterate against one in-process `*TestCpu` RED; keep physical emulators and MCP transports diagnostic and never swap them in mid-fix |
| Calling local validation “published” | Prove upstream alignment separately, as required by `AGENTS.md` |

In a new worktree, restore before using `--no-restore`. Do not run concurrent
solution tests against the same output directories.

## Validation Routing

Use the reliable commands in `AGENTS.md` as the common gate, then add only the
evidence relevant to the changed owner:

- Parser, semantics, shared SDK, or architecture: focused project tests, then
  the solution.
- Target lowering or runtime: focused target tests plus behavioral simulation
  on a freshly compiled ROM.
- Runner or sample source: regenerate the tracked target artifacts deliberately
  and validate the affected behavior.
- Documentation only: `git diff --check`; run executable checks when commands,
  examples, generated artifacts, or enforced navigation contracts changed.
- External-emulator runs are opt-in diagnostics. Multi-emulator comparison is
  historical forensic evidence and must never be a handoff or closeout gate.

Keep local validation, commit state, push state, and merge state distinct.
