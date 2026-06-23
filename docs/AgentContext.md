# AI Agent Project Context

Status: memory-derived project context for AI CLI agents.
Last updated: 2026-06-23.

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
- Dot-call precedence is single-sourced in `RetroSharp.Parser.SdkDotCallResolver`: a receiver in scope shadows a same-named SDK module (lexical scoping), otherwise a known SDK module resolves as an SDK call. Constant folding and semantic analysis both consume it; the folder detects receivers by method signature because it has no variable scope.
- Do not add `Option/Result` or lambdas by default; they were explicitly excluded from the accepted near-term ergonomics direction.

## Portability Lowering Roadmap (epic #106)

Goal: one source program runs the same 2D scroll on Game Boy and NES, with the
language and its classic IR (`RetroSharp.Generation.Intermediate`) framework-neutral,
the 2D framework isolated in `RetroSharp.Core.Sdk` (`Sdk2DOperation`), and on a path
to becoming a library over per-target intrinsics. The GitHub epic #106 implementation
slice is complete after PL-E1; use the newer stabilization issues #119-#122 for the
active framework backlog.

Golden rule (do not violate):
- The language and its classic IR never gain framework concepts (camera/sprite/scroll).
- `Sdk2DOperation` must not grow into a dumping ground; a genre-specific operation
  should be questioned as an intrinsic+library before a compiler-recognized operation.
- End-state: the 2D SDK becomes a library over per-target intrinsics.

Operation-driven lowering pattern (already proven, replicate it):
- The shared collector `RetroSharp.Sdk.Sdk2DOperationCollector` turns source calls
  into target-neutral `Sdk2DOperation` records; `Sdk2DOperationValidator` checks them
  against each target's `Target2DCapabilities` before lowering.
- A per-target lowerer (`GameBoySdkOperationLowerer`, `NesSdkOperationLowerer`) maps an
  operation to target emission. The runtime compiler routes a source call via
  `EmitSdkOperation(op)` instead of re-deriving it from the AST.
- Operand IR is `SdkByteExpression` (`Constant | Variable`). `Variable` carries a typed
  `SdkStorageLocation` (`Local`, recursive `Field`, or `IndexedElement`); targets convert that
  descriptor to their runtime local-map key only at the backend boundary. Do NOT add general
  expression tree cases such as `BinaryOp` to the IR.
- Every step must keep tracked ROMs byte-identical and the full suite green.

Progress (2026-06-14):
- Done: PL-A1 #107 (GB camera apply), PL-B1 #111 (NES lowerer wait/poll),
  PL-B2 #112 (NES camera via shared model), PL-D1 #117 (cross-target scroll acceptance
  in `CrossTargetScrollAcceptanceTests`), PL-C1 #114 (collector moved to
  `RetroSharp.Sdk.Frontend`, out of the language assembly), PL-C3 #116 (layer boundary +
  golden rule documented in ArchitectureRoadmap), PL-A2 #108 / PL-B3 #113 (sprite draw now
  collected as `DrawLogicalSprite` and lowered through GB/NES SDK lowerers), PL-A3 #109
  (Game Boy `map_stream_column(...)` now collected as `StreamMapColumn` and lowered through
  `GameBoySdkOperationLowerer`), PL-A4 #110 (Game Boy runtime lowering now consumes
  `program.SdkOperations` instead of rebuilding migrated operations from AST calls), PL-C2 #115
  (`SdkByteExpression.Variable` now carries a typed storage descriptor instead of an opaque
  formatted string), PL-E1 #118 (parser preserves target/intrinsic extern metadata and GB/NES
  prove a `wait_frame` source helper can lower through target intrinsics with identical bytes).
  Earlier groundwork and audit context came from #101-#105. #103, #104, and #105 remain
  separately tracked design debt rather than open work inside #106.
- Sprite operation decision implemented 2026-06-14: X/Y/Frame are `SdkByteExpression`, FlipX
  is nullable `SdkByteExpression?`, PaletteSlot stays a constant int validated against target
  capabilities, and target lowerers resolve metasprite geometry from `SpriteId` and asset data
  instead of carrying `LogicalSize` in the portable record. `ReadDrawLogicalSprite` mirrors
  `ReadSetCameraPosition`, and the cross-target acceptance now includes logical sprite drawing.
- Stream column operation decision implemented 2026-06-14: target/source columns are
  `SdkByteExpression`, Y/Height remain constants, and Game Boy lowering preserves the existing
  map row table and VRAM write byte shape. This does not add NES runtime nametable streaming.
- Operation stream decision implemented 2026-06-14: `GameBoyRuntimeCompiler` walks the collected
  operation list with a cursor for migrated statement calls and `world_tile_flags_at(...)`; it
  fails if the next operation type does not match the source call or if operations remain
  unconsumed after runtime emission.
- Operand contract decision implemented 2026-06-14: SDK byte variables preserve the
  value-or-location IR boundary while replacing collector/target string formatting conventions
  with typed `Local`, recursive `Field`, and `IndexedElement` descriptors.
- Intrinsics prototype implemented 2026-06-14: `[target("gb"|"nes")] [intrinsic("wait_frame")]`
  extern functions are preserved by the parser and lower through target runtime compilers.
  Source helpers over those externs emit the same bytes as the current `WaitFrame` SDK
  operation. Full SDK migration still needs module packaging, portable target selection, and
  a broader intrinsic catalog.
- Pending in the edited #106 slice: none known after PL-E1.
- Active SDK v1 stabilization backlog after #106: none known after the collision, cross-target
  diagnostic, and logical palette slices landed.
- Camera-relative AABB decision implemented after #106: `camera.AabbTiles(...)` is a
  capability-gated SDK query for fixed-screen actors. Game Boy declares and lowers it through
  `Sdk2DOperation.CameraAabbTiles`; NES declares no collision-query support and rejects it.
- Landing tile-hit decision implemented after #106: `camera.AabbHitTop(...)` is a
  capability-gated SDK query that returns the top world-pixel Y of the first matching tile in a
  caller-defined fixed-screen actor AABB, or `255` when none hit. Game Boy lowers it through
  `Sdk2DOperation.CameraAabbHitTop`; NES rejects it explicitly until collision-query lowering
  exists. The runner uses it to remove the old repeated tile-offset landing probe ladder while
  keeping the downward-velocity gate and `player.Land(...)` policy in source.
- Runner-shaped cross-target validation decision after #106: `CrossTargetScrollAcceptanceTests`
  includes an explicit NES diagnostic for camera-relative AABB collision instead of pretending the
  runner-shaped collision slice is portable today.
- Logical palette decision implemented after #106: `palette.Background(slot, c0, c1, c2, c3)`
  and `palette.Sprite(slot, c0, c1, c2, c3)` declare capability-checked logical palette slots.
  Game Boy lowers background slot `0` to `BGP`, sprite slots `0..1` to `OBP0/OBP1`, and NES
  lowers background/sprite slots `0..3` into its 32-byte palette table. The runner now uses
  `palette.Sprite(0, 0, 0, 1, 3)`, preserving accepted `OBP0 = 0xD0`, without raw
  `objectPalette.Set(...)`.

Suggested next steps for the next agent, in order:
1. If continuing beyond #106 toward SDK-as-library, open new focused issues for module packaging,
   portable target selection, and the remaining intrinsic catalog before migrating more SDK calls.

## Game Boy Runner Lessons

- Normal runner debugging should start with the full app, then use `tools/gameboy/runner_diagnostics.py` to find the first failing diagnostic step before editing code.
- `input.Poll()` is the frame/tick input boundary. New gameplay should use `button_down`, `button_just_pressed`, `button_just_released`, and `button_hold_ticks`.
- `button_hold_ticks` saturates at `255` and is the accepted seam for variable-height jump timing.
- On original DMG hardware, `JOYP` row selection must settle. The backend should select a row, read it several times, use the final sample, and deselect both rows with `0x30`.
- `sprite.Draw(...)` accepts portable `flipX` and palette slot arguments. Do not reintroduce raw OAM attribute bytes through portable sprite calls.
- Mirrored metasprites must preserve logical sprite width, not padded hardware footprint.
- The accepted runner object palette is `0, 0, 1, 3`, which compiles to `OBP0 = 0xD0`.
- Collision over wider sprites should use logical sprite width through helpers such as `sprite_width(...)`; fixed-screen runner actors should use `camera.AabbTiles(...)` for boolean overlap and `camera.AabbHitTop(...)` for landing tile-edge facts so X stays aligned with the visible camera after long scrolls.
- If a platform feels dead even though visual tiles look correct, inspect frame order and state transitions, not just collision geometry.
- Byte-backed Y values can wrap at the top of the scene; clamp before collision/reset logic.
- The runner reset path should restore actor, velocity, animation, facing, jump, and movement state without rebasing the scrolled background.

## Tiled Map Pipeline

The runner's editable level lives at `samples/gameboy-runner/maps/runner.tmj` and uses `samples/gameboy-runner/maps/Super Mario Land 2.tsx`.

Pipeline shape (two phases, after #105 partial extraction):

- Target-neutral phase: `RetroSharp.Core.Sdk.Tiled.LogicalTiledMapImporter.Load(path)` parses the
  Tiled JSON/TSX, resolves tileset descriptors (metadata + image path, no decoded pixels),
  computes geometry and the playable world slice, and resolves collision into portable
  `WorldTileFlags`. It yields a `LogicalTiledMap` of source-tile GID references only.
- Game Boy phase: `GameBoyTiledMapImporter` consumes the `LogicalTiledMap`, enforces GB-specific
  limits (`retrosharpStreamY` 0..31, slice <= 32 rows), decodes tileset PNGs, generates and
  deduplicates 2bpp tile patterns, expands source tiles into 8x8 cells, and composes the
  background under blank world cells. Pixel-level composition stays here because the "blank cell"
  decision depends on the generated pattern.
- Still target-coupled (open in #105): per-pixel layer flattening and GB tile generation/dedup.
  `WorldMap2D` still stores already-lowered target tile ids; NES does not yet consume the neutral
  `LogicalTiledMap`.

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
