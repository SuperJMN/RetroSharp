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

- For gameplay-performance work, the named player-visible or audible symptom is
  the primary product authority. A physical playtest on the affected target or
  emulator is the closest observer when available. In-process simulation
  (`NesTestCpu` and `GameBoyTestCpu`) protects that experience with repeatable
  evidence; it is not a more precise experience to optimize instead.
- Classify the symptom by provenance before choosing a stop rule. A **confirmed
  report** — the user, the integrator, or a playtest names a visible or audible
  defect (for example the runner's stuttering scroll) — makes the
  physical/perceptual observer the acceptance authority for that defect. A
  deterministic in-process reproduction is then a helpful guard, never a
  precondition: its absence does not mean "no defect" and never authorizes
  closing the work as `NOT_REPRODUCED`. Diagnose the confirmed symptom from the
  physical scene using the target debugging map (`docs/GameBoyRunnerDebugging.md`
  and `docs/NesTarget.md`) and fix the responsible layer. Reserve
  "reproduce first or hand back" for an **unconfirmed suspicion** that no human
  has observed, where that discipline prevents a goose chase.
- Every dispatched gameplay fix must carry an immutable acceptance capsule:
  the player action and scene, target and observer, unwanted visible or audible
  symptom, safety constraints, explicit non-goals, and the terminal verdict.
  Only the user or integrator may change it. A fresh implementer or reviewer
  must not widen it, redefine smoothness, or add a new gate.
- For any symptom, still try the smallest deterministic reproduction that
  observes the same presentation fault, because a GREEN/RED test is the cheapest
  regression guard. A compiled-snippet `GameBoyTestCpu`/`NesTestCpu` test is
  suitable only when its observation maps directly to what the player sees or
  hears. Spend at most two focused attempts building that observer. When it
  cannot capture the defect, the next step depends on provenance: for a
  **confirmed report**, do not hand it back — proceed to fix against the named
  runner/physical scenario and record the perceptual before/after as the
  evidence; only an **unconfirmed** symptom that nobody, including the reporter,
  can reproduce may be returned as a bounded investigation. Do not invent a
  proxy merely because it is easier to assert, and do not treat a quiet
  deterministic harness as proof that a reported defect is absent.
- A new metric becomes a gameplay gate only when its physical meaning is named
  and a known-bad candidate fails while a perceptually good candidate passes.
  Logical tick age, queue depth, frame-source choice, exact OAM pose, and similar
  internal differences remain diagnostic until correlated with visible stutter,
  corruption, input lag, unsafe hardware writes, or audible dropout. An
  incidental nonzero or off-by-one value is not itself a regression.
- A gameplay-performance fix reaches its **perceptual terminal** when the named
  visible or audible defect is absent in its acceptance scenario, corruption
  and unsafe PPU/OAM writes are zero where applicable, and the focused
  deterministic guard — or, when no deterministic test can capture the defect,
  the named physical/perceptual scenario — is GREEN in two matching runs. Once there, precision
  work, cleaner metrics, additional observers, architecture refinements, and
  unrelated failures become follow-ups. They do not reopen the fix. Only new
  evidence of the named perceptual defect, corruption or unsafe writes, or
  contradictory deterministic runs may reopen it.
- Prefer good over perfect. Fix real, observable problems such as stutter, input lag, torn or lagging scroll, audio dropouts, and sustained backlog. Do not chase byte-perfect reproduction, exact cycle counts, or cross-emulator pixel parity once the experience is smooth.
- ROM byte identity, hardcoded SHA-256 digests, exact emitted-byte sequences, and exact CPU-cycle counts are diagnostic baselines, not gates. Do not add tests that pin them. Express CPU-cost limits as upper-bound budgets, not equalities.
- Tracked sample ROMs are regeneratable artifacts. Regenerate them when the sample source changes. Their exact bytes are not a product requirement, so do not block work to preserve a specific hash.
- Independent-emulator or multi-emulator differential runs are optional forensic
  diagnostics, never a product gate, and must not appear in issue, PR, or sample
  closeout requirements. Do not block work on FCEUmm, Nestopia, RetroArch, byte
  parity, or raster parity.
- Validation must change a decision. Before another diagnostic, minimization,
  or confirmation run, name the hypothesis, owner decision, or acceptance
  verdict that its result can change and use the cheapest discriminating
  evidence. Two consecutive experiments that change none of those require an
  immediate checkpoint and handoff; do not reset the count by adding metrics or
  rephrasing the same hypothesis. Replacing the agent or reviewer does not reset
  the count either.
- Two matching deterministic runs are sufficient confirmation. Run a third
  only when the first two disagree or the live issue justifies the extra run
  with a concrete risk. Run broad/full validation once on the final candidate,
  not after every refinement step. After the first perceptually good candidate,
  allow at most one review round and one correction round before checkpointing.
  Review findings block this slice only when they demonstrate the named
  perceptual regression, corruption or unsafe writes, a build failure, or a
  broken public contract in scope; other findings become follow-ups.
- A broad validation failure must be reported and classified. It authorizes an
  edit in the current slice only when causally tied to its acceptance capsule.
  An inherited, unrelated, exactness-only, or stale-golden failure may block
  publication under its own policy, but it must not silently expand a completed
  gameplay fix.

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
