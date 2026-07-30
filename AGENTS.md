# AGENTS.md

This is the first file an AI CLI agent should read before changing RetroSharp.

RetroSharp is a .NET 10 multi-project solution for a small C#-like language that compiles directly to NES and Game Boy cartridges. The shared frontend and portable SDK feed target-owned lowerers, with the Game Boy/NES runner as the main acceptance sample.

## Read First

Always read, in order:

1. `AGENTS.md`: repository rules, acceptance policy, and validation.
2. `docs/AgentContext.md`: current authority map, the single task router, code anchors, and known traps.
3. The live issue or specification that defines the requested slice.

Then open only the one route that owns the task. The task router lives in
`docs/AgentContext.md`; this file deliberately does not keep a second copy of
it. Completed roadmaps and per-issue acceptance records live under
`docs/history/` and are background only; they are not active dispatch contracts
unless the task explicitly names them. Do not preload every roadmap.

### Context budget

Startup context is bounded on purpose. A task normally loads only this file,
`docs/AgentContext.md`, the live issue, and one routed owner document. If a
route seems to need several owner documents at once, or an owner document is too
large to hold alongside the code under change, treat that as a signal to split
the document or the task, not to load the whole `docs/` tree. Keep any single
routed document small enough to read next to the code it governs, and move
completed history to `docs/history/` instead of growing an active document.

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

- **Product authority is the named player-visible or audible symptom.** A physical playtest on the affected target/emulator is the closest observer; `NesTestCpu`/`GameBoyTestCpu` provide repeatable regression and safety evidence, not a more precise experience to optimize instead.
- **Classify by provenance before choosing a stop rule.** A *confirmed report* (the user, integrator, or a playtest named a visible/audible defect, e.g. the runner's stuttering scroll) makes the physical observer the acceptance authority: diagnose from the physical scene via `docs/GameBoyRunnerDebugging.md` and `docs/NesTarget.md`, then fix the responsible layer. A deterministic reproduction is a helpful guard, never a precondition — its absence never authorizes closing the work as `NOT_REPRODUCED`. Reserve "reproduce first or hand back" for an *unconfirmed suspicion* nobody has observed.
- **Spend at most two attempts** building the smallest deterministic observer that maps to what the player sees or hears. If it cannot capture the defect: for a confirmed report, fix against the named runner/physical scenario and record the perceptual before/after as evidence; only an unconfirmed symptom may be returned as a bounded investigation. Never invent an easier-to-assert proxy, and never treat a quiet harness as proof a reported defect is absent.
- **Every dispatched gameplay fix carries an immutable acceptance capsule** (fields listed in `docs/AgentContext.md`). Only the user or integrator may change it; a fresh implementer or reviewer must not widen it, redefine smoothness, or add a gate.
- **A metric becomes a gate only when** its physical meaning is named and a known-bad candidate fails while a perceptually good one passes. Logical tick age, queue depth, frame-source choice, exact OAM pose, and incidental off-by-one values stay diagnostic until correlated with visible stutter, corruption, input lag, unsafe hardware writes, or audible dropout.
- **Perceptual terminal:** the named defect is absent in its scenario, corruption and unsafe PPU/OAM writes are zero where applicable, and the focused deterministic guard — or the named physical scenario when no test can capture the defect — is GREEN in two matching runs. After that, precision work, cleaner metrics, extra observers, architecture refinements, and unrelated failures are follow-ups; only new evidence of the named defect, corruption/unsafe writes, or contradictory runs reopens the fix.
- **Prefer good over perfect.** Fix real, observable problems (stutter, input lag, torn or lagging scroll, audio dropout, sustained backlog). Do not chase byte-perfect reproduction, exact cycle counts, or cross-emulator pixel parity once the experience is smooth.
- **Exactness is diagnostic, never a gate.** ROM byte identity, hardcoded SHA-256 digests, exact emitted-byte sequences, and exact CPU-cycle counts are baselines only; do not pin them in tests, and express CPU-cost limits as upper-bound budgets. Tracked sample ROMs are regeneratable — regenerate them when the sample source changes and never block work to preserve a hash.
- **Independent or multi-emulator differential runs are optional forensic diagnostics**, never a product gate, and must not appear in issue, PR, or sample closeout requirements (no FCEUmm/Nestopia/RetroArch/byte/raster parity gate).
- **Validation must change a decision.** Before another diagnostic, minimization, or confirmation run, name the hypothesis, owner decision, or verdict its result can change, and use the cheapest discriminating evidence. Two consecutive experiments that change none require an immediate checkpoint and handoff; adding metrics, rephrasing the hypothesis, or swapping the agent/reviewer does not reset the count.
- **Two matching deterministic runs confirm.** Run a third only on disagreement or a concrete live-issue risk, and run broad/full validation once on the final candidate. After the first perceptually good candidate, allow one review round and one correction round before checkpointing; review findings block the slice only when they show the named perceptual regression, corruption/unsafe writes, a build failure, or a broken in-scope public contract — otherwise they are follow-ups.
- **Classify every broad-validation failure.** It authorizes an edit in the current slice only when causally tied to the acceptance capsule; an inherited, unrelated, exactness-only, or stale-golden failure is reported separately and must not silently expand a completed fix.

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

Runner and Tiled facts — map composition, collision independence, the `Input.Poll()`
tick boundary and public input API, per-target music, DMG `JOYP` reads, and vertical
clamping — live once in `docs/AgentContext.md` under "Runner And Tiled Facts". Build
the runner from `samples/runner/runner.retrosharp.json`, reproduce and isolate runner
bugs with `docs/GameBoyRunnerDebugging.md`, and treat `docs/GameBoyTarget.md` as the
source of truth for the current Game Boy subset and runner milestones.

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
