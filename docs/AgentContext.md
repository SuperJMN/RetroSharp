# AI Agent Project Context

Status: memory-derived project context for AI CLI agents.
Last updated: 2026-06-13.

This document preserves project knowledge that previously lived only in agent memory and recent runs. It is intentionally practical: it records where to look, which commands have been reliable, and which failure modes should shape future work.

## Recent Baseline

- Code baseline immediately before this documentation pass: `d3eff996ecffeb6d0d159f579127c8d4016022e8`.
- Recent change: `fix: align game boy tiled map backgrounds`.
- That change added `tools/gameboy/generate_sample_roms.py`, documented it in `samples/README.md`, regenerated the tracked Game Boy ROMs, and updated tests around Tiled background/world composition.
- The recent validation proof was `git diff --check`, `tools/gameboy/generate_sample_roms.py --dry-run`, `tools/gameboy/generate_sample_roms.py`, and `dotnet test RetroSharp.sln -m:1`.

## Project Shape

RetroSharp currently has three important work streams:

- Classic compiler pipeline: parser, semantic analysis, intermediate code, Z80 backend, and CLI.
- Direct cartridge targets: `src/RetroSharp.NES` and `src/RetroSharp.GameBoy`.
- Portable 2D SDK architecture: shared concepts are being extracted from Game Boy/NES target experiments into capability-checked SDK operations.

The Game Boy runner is the main acceptance path for playable behavior. It is valuable because it catches real target/runtime issues, but it is not automatically portable API evidence. Use `samples/manifest.json` to check each sample's role.

## Source Of Truth

| Question | Read |
| --- | --- |
| What layer should this feature live in? | `docs/ArchitectureRoadmap.md` |
| What is SDK v1 supposed to expose? | `docs/Portable2DSdkV1.md` |
| What does Game Boy support today? | `docs/GameBoyTarget.md` |
| What does NES support today? | `docs/NesTarget.md` |
| How do we debug with the runner as the GB test app? | `docs/GameBoyRunnerDebugging.md` |
| Which samples are portable evidence? | `samples/README.md` and `samples/manifest.json` |
| How should agents execute roadmap issues? | `docs/AgentExecution.md` |
| What should a generic AI agent read first? | `AGENTS.md` and `llms.txt` |

## Decisions To Preserve

- Keep language, portable SDK, and target intrinsics separate.
- Portable APIs need explicit target capability checks before lowering.
- Game Boy and NES hardware details must not leak into portable samples.
- Transitional APIs can stay while they are documented; remove them only through explicit roadmap work.
- Higher-level syntax is welcome only when it remains zero-cost for 8-bit targets.
- Restricted `class` syntax is source organization over fixed-layout values and static/receiver lowering; it is not managed-object semantics.
- Future receiver-method ergonomics should stay in the plain-struct world, for example `actor.Move(dx, dy)` lowering to a statically resolved helper.
- SDK dot calls such as `video.Init()`, `input.Poll()`, and `camera.SetPosition(x, y)` are static grouping syntax, not object instances.
- Do not add `Option/Result` or lambdas by default; they were explicitly excluded from the accepted near-term ergonomics direction.

## Game Boy Runner Lessons

- Normal runner debugging should start with the full app, then use `tools/gameboy/runner_diagnostics.py` to find the first failing diagnostic step before editing code.
- `input.Poll()` is the frame/tick input boundary. New gameplay should use `button_down`, `button_just_pressed`, `button_just_released`, and `button_hold_ticks`.
- `button_hold_ticks` saturates at `255` and is the accepted seam for variable-height jump timing.
- On original DMG hardware, `JOYP` row selection must settle. The backend should select a row, read it several times, use the final sample, and deselect both rows with `0x30`.
- `sprite.Draw(...)` accepts portable `flipX` and palette slot arguments. Do not reintroduce raw OAM attribute bytes through portable sprite calls.
- Mirrored metasprites must preserve logical sprite width, not padded hardware footprint.
- The accepted runner object palette is `0, 0, 1, 3`, which compiles to `OBP0 = 0xD0`.
- Collision over wider sprites should use logical sprite width through helpers such as `sprite_width(...)` and `collision_aabb_tiles(...)`.
- If a platform feels dead even though visual tiles look correct, inspect frame order and state transitions, not just collision geometry.
- Byte-backed Y values can wrap at the top of the scene; clamp before collision/reset logic.
- The runner reset path should restore actor, velocity, animation, facing, jump, and movement state without rebasing the scrolled background.

## Tiled Map Pipeline

The runner's editable level lives at `samples/gameboy-runner/maps/runner.tmj` and uses `samples/gameboy-runner/maps/Super Mario Land 2.tsx`.

Important current behavior:

- `world.Load(path)` imports finite orthogonal Tiled JSON maps.
- External `.tsj` and `.tsx` tilesets with PNG images are accepted.
- Tiled source cells are expanded into Game Boy 8x8 cells; for example, a 16x16 source tile becomes a 2x2 Game Boy block.
- Generated Game Boy tile patterns are quantized and deduplicated.
- Game Boy has one scrolling background tilemap, so authoring layers are flattened.
- `background` is the visual base.
- Non-empty `world` cells overlay the background at the same Tiled coordinate.
- Empty `world` cells keep the background tile underneath.
- If `retrosharpWorldY` and `retrosharpStreamY` move the playable world slice, the background layer is shifted by the same amount so Tiled layers stay visually aligned.
- Background rows above the streamed world band (GB rows `0..streamY-1`) are emitted as full-width source-map rows and streamed horizontally by the camera, so background decorations above the band scroll with the world instead of freezing/repeating every 32 tiles.
- Collision remains independent from visual composition.
- Tileset `objectgroup` rectangles become solid flags when there is no explicit collision layer.

When `runner.rs`, `runner.tmj`, the tileset, or asset lowering changes, rebuild `samples/gameboy-runner/runner.gb`.

## Known Traps

| Trap | Better action |
| --- | --- |
| Assuming `RetroSharp.Cli --help` works | Inspect `src/RetroSharp.Cli/Program.cs`, `README.md`, or `WARP.md`; unknown options fail. |
| Treating a passing local build as a push | Verify upstream with `git rev-list --left-right --count HEAD...@{u}` and `git ls-remote`. |
| Ignoring submodules before publication | Check `git submodule status --recursive`; current submodules are `libs/6502DotNet` and `libs/Z80DotNet`. |
| Broad formatting-only runs touching old/vendored files | Use targeted formatting and `git diff --check`. |
| Editing generated Game Boy ROMs by hand | Regenerate from source using `tools/gameboy/generate_sample_roms.py`. |
| Treating generated screenshots as source artifacts | Leave `samples/gameboy-runner/*.png` alone unless explicitly requested. |
| Fixing emulator/hardware mismatches only in samples | Check backend/runtime behavior first, especially input and LCD/PPU state. |
| Adding portable APIs without diagnostics | Add or reuse target capability checks before lowering. |
| Debugging the full runner without isolation | Run `tools/gameboy/runner_diagnostics.py` and report the first failing step and scenario. |

## Commands

Run these from the repository root:

```bash
git status --short --branch
git submodule status --recursive
git diff --check
dotnet test RetroSharp.sln -m:1
```

Regenerate tracked Game Boy sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build individual ROM samples:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/gameboy-runner/runner.gb \
  samples/gameboy-runner/runner.rs

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/gameboy-drawing/drawing.gb \
  samples/gameboy-drawing/drawing.rs

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out /tmp/cross-camera.nes \
  samples/cross-target-camera/camera.rs
```

## Validation Expectations

- For parser, semantic, language, or shared SDK changes: run the relevant focused tests and usually the full solution test.
- For Game Boy target or runner changes: run Game Boy tests, rebuild affected `.gb` artifacts, and run the full solution test when practical.
- For sample classification or portable SDK changes: run tests that read `samples/manifest.json`.
- For docs-only changes: run `git diff --check`; run tests only if examples, commands, or generated artifacts changed.

## Publication Expectations

When the user says to push everything, include the full validated dirty tree unless they narrow the scope. For RetroSharp, publication proof should show:

- clean `git status --short --branch`,
- `git rev-list --left-right --count HEAD...@{u}` returning `0 0`,
- local `git rev-parse HEAD` matching the remote branch SHA from `git ls-remote`.

This distinction matters: local validation and remote publication are separate states.
