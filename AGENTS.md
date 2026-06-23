# AGENTS.md

This is the first file an AI CLI agent should read before changing RetroSharp.

RetroSharp is a .NET 10 multi-project solution for a small C#-like language that targets 8-bit systems. The original compiler path emits intermediate code and Z80 assembly. The current fast-moving proving ground is the direct NES/Game Boy cartridge compiler path, especially the Game Boy runner sample.

## Read First

Use this order when you need project context:

1. `AGENTS.md`: repo rules, validation, and agent workflow.
2. `docs/AgentContext.md`: memory-derived context, known traps, and recent changes.
3. `README.md`: project summary and basic examples.
4. `docs/ArchitectureRoadmap.md`: language vs portable 2D SDK vs target-intrinsics boundary.
5. `docs/Portable2DSdkV1.md`: current portable SDK surface and capability expectations.
6. `docs/GameBoyTarget.md` and `docs/NesTarget.md`: target-specific supported subsets.
7. `docs/GameBoyRunnerDebugging.md`: normal debugging workflow with the Game Boy runner as test app.
8. `samples/README.md` and `samples/manifest.json`: sample classification and portability rules.
9. `docs/AgentExecution.md`: GitHub issue/roadmap execution workflow.

`WARP.md` remains a tool-specific guide. `llms.txt` is a compact index for agents and RAG systems.

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
- Treat generated Game Boy ROMs as tracked artifacts when their source sample changes. Regenerate them deliberately.
- Generated screenshots under `samples/runner/*.png` are not source artifacts unless a task explicitly asks for them.

## Architecture Rules

- Decide the layer first: language, portable 2D SDK, or target intrinsic.
- The language layer must stay target-neutral. Do not add cameras, sprites, controllers, or tilemap concepts there.
- Portable SDK APIs must be capability-checked before target lowering.
- Raw Game Boy/NES hardware details belong in target intrinsics or target lowering, not portable samples.
- Keep transitional APIs working until the roadmap explicitly removes them.
- Prefer zero-cost ergonomics. Restricted classes, receiver methods, SDK dot calls, `let`, helper calls, and other high-level source forms are acceptable only when they lower to static data, direct calls, direct branches, fixed storage, or constants. Do not introduce heap allocation, GC, RTTI, boxing, delegates, closures, virtual dispatch, or hidden object identity.

## Reliable Commands

Run from the repository root.

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
```

Regenerate tracked Game Boy sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build representative samples:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/runner.gb \
  samples/runner/runner.rs

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out /tmp/cross-camera.nes \
  samples/cross-target-camera/camera.rs
```

The RetroSharp CLI itself does not implement `--help`; unknown options fail. Verify supported options from `README.md`, `WARP.md`, or `src/RetroSharp.Cli/Program.cs`.

Avoid broad formatting-only churn. Whole-solution `dotnet format RetroSharp.sln --verify-no-changes --no-restore` has been noisy in this repo because of older or vendored whitespace debt; prefer targeted formatting for touched files plus `git diff --check`.

## Game Boy Runner Notes

- `samples/runner/runner.rs` is a target-acceptance sample, not proof that every API it uses is portable.
- Use `docs/GameBoyRunnerDebugging.md` when reproducing or isolating runner bugs.
- `docs/GameBoyTarget.md` is the source of truth for the current Game Boy subset and runner milestones.
- The runner now uses `world.Load(...)` over `samples/runner/maps/runner.tmj` and the external `Super Mario Land 2.tsx` tileset.
- Game Boy has one scrolling background tilemap. Tiled `background` and `world` authoring layers are flattened at compile time: background is the visual base, non-empty world cells overlay it, and empty world cells keep the background tile under them.
- Collision is independent from visual composition. Tileset `objectgroup` rectangles or explicit collision data produce world flags.
- `input.Poll()` is the tick boundary. Prefer `button_down`, `button_just_pressed`, `button_just_released`, and `button_hold_ticks` over direct `button_pressed` for new gameplay code.
- Original DMG hardware needs settled `JOYP` row reads. If d-pad input bleeds into A/B behavior, treat it as backend/runtime behavior first, not as sample logic.
- Byte-backed target values can wrap. Clamp vertical runner state before collision/reset code when working near the top of the scene.

## Publication Workflow

Only commit or push when asked. When asked to push:

1. Re-check `git status --short --branch`.
2. Re-check `git submodule status --recursive`.
3. Run relevant validation.
4. Commit the intended tree.
5. Push the configured upstream.
6. Verify `git rev-list --left-right --count HEAD...@{u}` is `0 0`.
7. Verify `git rev-parse HEAD` matches `git ls-remote origin refs/heads/master` when publishing `master`.

Do not describe local validation as publication unless the remote proof is complete.
