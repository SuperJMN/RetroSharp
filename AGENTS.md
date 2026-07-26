# AGENTS.md

This is the first file an AI CLI agent should read before changing RetroSharp.

RetroSharp is a .NET 10 multi-project solution for a small C#-like language that compiles directly to NES and Game Boy cartridges. The shared frontend and portable SDK feed target-owned lowerers, with the Game Boy/NES runner as the main acceptance sample.

## Read First

Always read:

1. `AGENTS.md`: repository rules, acceptance policy, and validation.
2. `docs/AgentContext.md`: current authority map, code anchors, and known traps.
3. The live issue or specification that defines the requested slice.

Then open only the route that owns the task. Do not preload every roadmap.
Completed roadmaps preserve design history; they are not active dispatch
contracts unless the task explicitly names them.

| Task | Additional context |
| --- | --- |
| Project or language orientation | `README.md`, then `docs/RetroSharp.Language.md` if syntax is in scope |
| Layer placement or portable SDK surface | `docs/ArchitectureRoadmap.md`, `docs/Portable2DSdkV1.md` |
| Frontend, Actor Framework, or target lowering ownership | `docs/AiNavigableArchitecture.md`, `docs/SdkArchitecture.md` |
| Game Boy or NES behavior | `docs/GameBoyTarget.md` or `docs/NesTarget.md` |
| Runner reproduction | `docs/GameBoyRunnerDebugging.md` |
| Sample portability | `samples/README.md`, `samples/manifest.json` |
| Large maps, banking, or mappers | `docs/LargeWorldsRoadmap.md` |
| Generated-code performance | `docs/GeneratedCodePerformanceRoadmap.md` |
| Historical NES physical-frame scheduling / closed #410 | `docs/NesFrameSchedulingRoadmap.md` |
| GitHub roadmap execution | `docs/AgentExecution.md` |
| Archived Z80 compiler history | `docs/LegacyZ80Compiler.md` |

`llms.txt` is a compact index for agents and RAG systems.

## Local Source Code

The Zafiro ecosystem source is available locally. If Zafiro internals matter, inspect source directly instead of guessing from package metadata:

- Zafiro core: `/mnt/fast/Repos/Zafiro`
- Zafiro.Avalonia: `/mnt/fast/Repos/Zafiro.Avalonia`

## Repository Discipline

- Start with `git status --short --branch` and `git submodule status --recursive`.
- Do not revert or overwrite unrelated local changes.
- Inspect the real source path before editing; candidate file names in docs are guidance, not a substitute for reading code.
- Keep changes scoped to the requested layer and behavior.
- If public behavior, supported syntax, SDK calls, target capabilities, or sample workflows change, update the matching docs in the same patch.
- Treat generated Game Boy and NES runner ROMs as tracked artifacts when their source sample changes. Regenerate them deliberately.
- Generated screenshots under `samples/runner/*.png` are not source artifacts unless a task explicitly asks for them.

## Architecture Rules

- Decide the layer first: language, portable 2D SDK, or target intrinsic.
- The language layer must stay target-neutral. Do not add cameras, sprites, controllers, or tilemap concepts there.
- Portable SDK APIs must be capability-checked before target lowering.
- Raw Game Boy/NES hardware details belong in target intrinsics or target lowering, not portable samples.
- Keep transitional APIs working until the roadmap explicitly removes them.
- Prefer zero-cost ergonomics. Restricted classes, receiver methods, SDK dot calls, `let`, helper calls, and other high-level source forms are acceptable only when they lower to static data, direct calls, direct branches, fixed storage, or constants. Do not introduce heap allocation, GC, RTTI, boxing, delegates, closures, virtual dispatch, or hidden object identity.

## Acceptance Policy

The goal is a good in-game experience: smooth scrolling and movement, responsive controls, and music without stuttering. Acceptance is judged by that observable gameplay fluidity, not by byte-for-byte output. Aim to do it well, not perfectly. A ROM that plays well is correct even if its bytes move between builds.

- A bug fix is *solved* when its named reproduction — the smallest deterministic
  in-process behavioral test that fails because of the defect — flips from RED to
  GREEN and stays green across two matching runs. It is not solved because the
  ROM subjectively feels fluid. Write that reproduction before editing, prefer a
  compiled-snippet `GameBoyTestCpu`/`NesTestCpu` test in the style of
  `GameBoyRunnerLandingTests`, and iterate the fix against that single test.
  Fluidity is the end-of-loop guard, run once on the final candidate, not the
  target the fix loop iterates against. If the defect cannot be expressed as a
  failing deterministic test within the reproduction budget, stop and hand it
  back as an investigation carrying that reproduction attempt; do not keep
  editing against the subjective fluidity signal.
- The product gate is in-process behavioral simulation (`NesTestCpu` and `GameBoyTestCpu`): movement, jumps, landing, camera follow, collisions, audio cadence, deterministic execution, and absence of sustained backlog. Validate behavior on the freshly compiled ROM, not on a committed golden.
- Prefer good over perfect. Fix real, observable problems such as stutter, input lag, torn or lagging scroll, audio dropouts, and sustained backlog. Do not chase byte-perfect reproduction, exact cycle counts, or cross-emulator pixel parity once the experience is smooth.
- ROM byte identity, hardcoded SHA-256 digests, exact emitted-byte sequences, and exact CPU-cycle counts are diagnostic baselines, not gates. Do not add tests that pin them. Express CPU-cost limits as upper-bound budgets, not equalities.
- Tracked sample ROMs are regeneratable artifacts. Regenerate them when the sample source changes. Their exact bytes are not a product requirement, so do not block work to preserve a specific hash.
- `tools/nes/verify_runner_visual_parity.py` defaults to an optional AprNes-only
  physical smoke check. Its historical multi-emulator differential is an
  explicit forensic replay, not a product gate, and must not appear in issue,
  PR, or sample closeout requirements. Do not block work on FCEUmm, Nestopia,
  RetroArch, byte parity, or raster parity.
- Validation must change a decision. Before another diagnostic, minimization,
  or confirmation run, name the hypothesis, owner decision, or acceptance
  verdict that its result can change and use the cheapest discriminating
  evidence. Two consecutive experiments that change none of those require an
  immediate checkpoint and handoff; do not reset the count by adding metrics or
  rephrasing the same hypothesis.
- Two matching deterministic runs are sufficient confirmation. Run a third
  only when the first two disagree or the live issue justifies the extra run
  with a concrete risk. Run broad/full validation once on the final candidate,
  not after every refinement step.

## Reliable Commands

Run from the repository root.

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
```

Regenerate tracked sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build representative samples:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/bin/runner.gb \
  samples/runner/runner.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --runtime-abi-out samples/runner/bin/runner.nes.runtime-abi.json \
  --out samples/runner/bin/runner.nes \
  samples/runner/runner.retrosharp.json
```

The RetroSharp CLI itself does not implement `--help`; unknown options fail. Verify supported options from `README.md` or `src/RetroSharp.Cli/Program.cs`.

Avoid broad formatting-only churn. Whole-solution `dotnet format RetroSharp.sln --verify-no-changes --no-restore` has been noisy in this repo because of older or vendored whitespace debt; prefer targeted formatting for touched files plus `git diff --check`.

## Runner Notes

- `samples/runner/runner.retrosharp.json` is the shared Game Boy/NES runner target-acceptance project, not proof that every API it uses is portable. It lists `src/main.rs` plus helper/state files under `samples/runner/src`; direct runner builds should use the project manifest instead of treating game-owned code as a local library.
- NES and Game Boy both use per-target VGM/VGZ runner music variants via `assets/music/runner.vgz`; do not treat NES audio calls as no-ops.
- Use `docs/GameBoyRunnerDebugging.md` when reproducing or isolating runner bugs.
- `docs/GameBoyTarget.md` is the source of truth for the current Game Boy subset and runner milestones.
- The runner uses `World.Load(...)` over complete `samples/runner/assets/maps/stage1.tmj` and `stage1.tsx`. The older `stage1.playable.tmj` crop is a historical/smaller fixture only; do not substitute it for joint runner acceptance.
- Game Boy has one scrolling background tilemap. Tiled `background` and `world` authoring layers are flattened at compile time: background is the visual base, non-empty world cells overlay it, and empty world cells keep the background tile under them.
- Collision is independent from visual composition. Tileset `objectgroup` rectangles or explicit collision data produce world flags.
- `Input.Poll()` (PascalCase `Input.Poll()`) is the tick boundary. Use `Input.IsDown`, `Input.WasPressed`, `Input.WasReleased`, and `Input.HoldTicks` with `Button.*` enum members, plus `Sprite.Width`. The direct `button_pressed` read, snake_case `button_*`/`sprite_width` calls, and bare lowercase button identifiers are not public source APIs.
- Original DMG hardware needs settled `JOYP` row reads. If d-pad input bleeds into A/B behavior, treat it as backend/runtime behavior first, not as sample logic.
- Byte-backed target values can wrap. Clamp vertical runner state before collision/reset code when working near the top of the scene.

## Branching and Publication Workflow

Prefer a clean branch-based workflow over working directly on `master`. Commit freely on feature branches; treat pushing as the guarded step.

Recommended flow:

1. Start every slice from an up-to-date `master` on a dedicated branch named `agent/<short-slug>` (for example `agent/music-play-stop-intrinsics`).
2. Make focused, self-contained commits with descriptive messages. Follow the existing convention when a slice maps to a roadmap item (for example `SAL-8.7: migrate gb/nes Music.Play/Stop to audio target intrinsics`).
3. Run the relevant validation before each merge (`dotnet test RetroSharp.sln -m:1`, `git diff --check`, and regenerate tracked ROMs when their source changed).
4. When the slice is validated and it is time to land it, integrate into `master` **by default via a pull request**: push the branch, open a PR (`gh pr create --base master`), and merge it (`gh pr merge <number> --squash --delete-branch`). This PR + merge flow is the default whenever no other integration strategy is specified. A local fast-forward merge (`git merge --ff-only <branch>`) is only for when it is explicitly requested; use `--no-ff` when you want to preserve the branch boundary.
5. Keep unrelated local changes intact: never revert or overwrite work you did not author for this task.

Use git worktrees when you need real parallelism — several independent slices in flight at once, or a long build/test running in one tree while you edit another. Create one with `git worktree add ../RetroSharp-<slug> -b agent/<slug>` so each workstream has its own branch and working directory instead of thrashing a single checkout. Remove finished trees with `git worktree remove`.

Push only when asked (opening the PR above is the guarded "land it" step). When asked to push or land:

1. Re-check `git status --short --branch`.
2. Re-check `git submodule status --recursive`.
3. Run relevant validation.
4. Commit the intended tree.
5. Push the configured upstream.
6. Verify `git rev-list --left-right --count HEAD...@{u}` is `0 0`.
7. Verify `git rev-parse HEAD` matches `git ls-remote origin refs/heads/master` when publishing `master`.

Do not describe local validation as publication unless the remote proof is complete.
