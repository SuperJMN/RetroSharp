# AI Agent Project Context

Status: memory-derived project context for AI CLI agents.
Last updated: 2026-07-10.

This document preserves project knowledge that previously lived only in agent memory and recent runs. It is intentionally practical: it records where to look, which commands have been reliable, and which failure modes should shape future work.

## Recent Baseline

- Code baseline immediately before the AF-4.3 documentation closeout:
  `f0398452fd0e3b93d4d77e6aeac5749dbf1322ed`.
- Recent change: `feat(actors): generated-name guards and codegen robustness`.
- The actor framework first scrolling platformer slice is landed on
  `feature/actor-framework`: `samples/actor-framework/actors.rs` builds for Game
  Boy and NES from the manifest and exercises fixed actor pools, declarative
  enemy definitions, Tiled object-layer spawns, runtime camera-window activation,
  camera-relative draw/collision/player contact, animation, and metasprite-aware
  pool budget checks.
- The current closeout validation expectation is `git diff --check` and
  `dotnet test RetroSharp.sln -m:1`, with tracked sample ROMs left
  byte-identical for docs-only work.
- The Game Boy vertical camera path is now proven by `samples/gameboy-vscroll/vscroll.rs`,
  a ROM/VRAM acceptance test, and a shared-row-streamer emission fix. Game Boy
  `Camera.SetPosition` can walk up to two same-axis tile crossings per frame and
  commit both exposed edges during `Camera.Apply()`. Game Boy diagonal camera
  movement is proven by `samples/nes-free-scroll/freescroll.rs` and
  `samples/tiled-free-scroll/free-scroll.rs` with staggered column/row commits.
  NES has a four-screen free-scroll path with preloaded 64x60
  movement, Tiled diagonal coverage inside that surface, horizontal column streaming
  for wider worlds, and staggered vertical row plus zero-palette attribute streaming
  for source-authored worlds taller than the buffer. NF-10 mapper-backed scale and
  IRQ HUD remain separate in `docs/NesFreeScrollRoadmap.md`.
- The Large Worlds epic now has a dedicated execution source in
  `docs/LargeWorldsRoadmap.md`. It treats the full runner `stage1` design as the
  acceptance target. Waves 0/1 are complete; Game Boy `LW-2.1` through `LW-2.3`
  are implemented, and the remaining Wave 2/3 cards are published as native
  subissues: `LW-2.1`..`LW-2.5` are #296-#300 and `LW-3.1`..`LW-3.5` are
  #301-#305. All belong to milestone 11 under parent #275; do not dispatch the
  parent as one task.
- LW-1.3 adds the target-neutral `RetroSharp.Core.Sdk.WorldPack` model: the v1
  header/directory and exact clipped coverage are validated with checked
  relative offsets, collision profiles and decoded IDs stay portable, target
  expansion records remain opaque bytes, and tooling can explicitly
  materialize the unchanged `WorldMap2D`/`WorldTileGrid` compatibility types.
  That milestone deliberately stopped before packing/codecs; cartridge
  placement and target runtime readers remain later Large Worlds tasks.
- LW-1.4 adds the production Tiled-to-pack planning and canonical raw/element-RLE
  serializer used by tooling/tests. GB and NES inspection paths preserve their
  historical pattern/CHR dedup order while mapping lexicographic authoring IDs
  to target expansion records. Full normalized `stage1` is 2,550 bytes on GB
  and 2,762 on NES with raw-importer parity; production readers, banking,
  mappers, and the runner input remain unchanged.
- LW-1.5 adds the explicit `--world-budget-report` CLI analysis seam for an
  importer-ready `.tmj`. It emits deterministic JSON from the real LW-1.4
  payload without writing a ROM, freezes full normalized `stage1` at 770 visual
  plus 312 collision stored bytes on both targets, uses 554-byte GB and 594-byte
  NES accepted staging maxima, and keeps mapper-0 versus future MMC3/TVROM plus
  current/future/resident CHR budgets distinct. Reporting never selects a
  banker, mapper, production reader, or runner input. Its ROM/PRG number is
  map-only, not a final-link total; only the production link may preserve
  ROM-only/mapper-0 or select a banked profile.
- LW-2.2 carries the exact Game Boy serialized pack into pack-only final links,
  tries 32 KiB ROM-only inline placement first, and uses target-private
  continuation segments after program-tail banks only when the real link needs
  MBC1. Its internal final-build report owns selected-profile and physical
  range evidence. LW-2.3 adds the fixed-bank production validator/reader:
  startup validates every raw/RLE plane before `Main`, caches the result, and
  packed collision/visual lookups decode only their own bounded slot while
  restoring the actual bank shadow LIFO. Camera edge residency remains LW-2.4.
- Fresh Large Worlds implementation conversations should start with
  [LW-2.1 / #296](https://github.com/SuperJMN/RetroSharp/issues/296) and
  [LW-3.1 / #301](https://github.com/SuperJMN/RetroSharp/issues/301) in parallel
  only when both branches start from the same current `master`, then keep each
  target chain sequential. `LW-2.5` / #300 proves full `stage1` on Game Boy
  through a non-destructive fixture and must not change the shared runner input.
  Only `LW-3.5` / #305, after #300 and `LW-3.4` / #304, migrates the shared
  runner and regenerates both tracked ROMs. Issue #244 stays in Wave 4; Wave 3
  only links the mapper-backed slice from #247 and does not absorb its unrelated
  gaps.
- The NES four-screen background flicker (#130, stale scroll on streaming frames)
  is fixed and the issue is closed. `dd58910` ("fix: stabilize NES camera streaming")
  drains one pending camera stream phase at VBlank entry in `Video.WaitVBlank()`
  before sprite DMA, restores PPUCTRL/PPUSCROLL before rendering resumes, and splits
  a runtime row into four 8-tile `$2007` phases (plus a separate attribute phase) so
  no single VBlank overruns budget. Regression tests live in `NesRomCompilerTests`
  (`Nes_video_wait_vblank_applies_pending_camera_scroll_before_sprite_dma`,
  `Nes_runtime_row_streaming_is_split_across_vblanks`).

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
| How do we implement vertical camera scroll (AR-5)? | `docs/CameraVerticalScrollRoadmap.md` |
| How do we implement free 2-axis scroll on NES? | `docs/NesFreeScrollRoadmap.md` |
| How do we scale Tiled worlds beyond one-byte/monolithic ROM limits? | `docs/LargeWorldsRoadmap.md` |
| What should a generic AI agent read first? | `AGENTS.md` and `llms.txt` |

## Decisions To Preserve

- Keep language, portable SDK, and target intrinsics separate.
- Portable APIs need explicit target capability checks before lowering.
- Game Boy and NES hardware details must not leak into portable samples.
- Transitional APIs can stay while they are documented; remove them only through explicit roadmap work.
- Higher-level syntax is welcome only when it remains zero-cost for 8-bit targets.
- Restricted `class` syntax is source organization over fixed-layout values and static/receiver lowering; it is not managed-object semantics.
- Future receiver-method ergonomics should stay in the plain-struct world, for example `actor.Move(dx, dy)` lowering to a statically resolved helper.
- SDK dot calls such as `Video.Init()`, `Input.Poll()`, and `Camera.SetPosition(x, y)` are static grouping syntax, not object instances.
- Static dot-call lowering is driven by declared source-package/static methods. `DeclaredStaticMethodIndex` maps declared `Type_Method` functions to `Type.Method(...)`; remaining dot-calls are receiver methods. There is no compiler registry of public SDK facade names.
- Do not add `Option/Result` or lambdas by default; they were explicitly excluded from the accepted near-term ergonomics direction.
- The actor framework is source-to-source sugar in `RetroSharp.Sdk.Frontend`.
  `Actors.Pool`, `Actors.SpawnLayer`, `Actors.SpawnWindow`, `Enemies.Def`, called
  `Enemies.*` helpers now enter through `RetroSharp.Portable2D` `sdk_role("...")`
  metadata rather than public-name switches. Pool receiver helper calls remain a
  compiler-owned semantic boundary and lower before target emission to fixed
  `Actor` arrays, constants, generated spawn helpers, `used[]`, direct `kind`
  branches, and existing SDK calls such as `Sprite.Draw`, `Camera.AabbTiles`,
  `Camera.AabbHitTop`, `Camera.ScreenAabbTiles`, `Camera.ScreenAabbHitTop`,
  and `Animation.Frame`. Do not add actor-specific target intrinsics for this slice.

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
- Project manifests load `RetroSharp.Portable2D` through `libraries`, while
  `import RetroSharp.Portable2D;` remains the explicit source-level form for
  standalone files. New SDK library work should go through `SdkLibraryRegistry`
  and manifest/import plumbing rather than adding global compiler knowledge.

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
  Earlier groundwork and audit context came from #101-#105. #103 is resolved, and
  #200 retires the registry-era public facade map: source-package static methods
  now supply SDK dot-call declarations, and constant folding/semantic analysis use
  the declared static method index plus receiver-method lowering. #104 and #105
  remain separately tracked design debt rather than open work inside #106.
- Sprite operation decision implemented 2026-06-14: X/Y/Frame are `SdkByteExpression`, FlipX
  is nullable `SdkByteExpression?`, PaletteSlot stays a constant int validated against target
  capabilities, and target lowerers resolve metasprite geometry from `SpriteId` and asset data
  instead of carrying `LogicalSize` in the portable record. `ReadDrawLogicalSprite` mirrors
  `ReadSetCameraPosition`, and the cross-target acceptance now includes logical sprite drawing.
- Stream column operation decision implemented 2026-06-14 and extended on NES in #124:
  target/source columns are `SdkByteExpression`, Y/Height remain constants, Game Boy lowering
  preserves the existing map row table and VRAM write byte shape, and NES lowers horizontal
  column streaming into `$2006`/`$2007` writes across a two-nametable buffer.
- Operation stream decision implemented 2026-06-14: `GameBoyRuntimeCompiler` walks the collected
  operation list with a cursor for migrated statement calls and `World.TileFlagsAt(...)`; it
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
- SAL-8 compile-time operand intrinsics landed after the initial prototype: target intrinsic
  descriptors can mark call slots as `AssetRef`, `ConstPaletteSlot`, `EnumFlags`, or
  `WorldId` while leaving the language, parser AST, ABI, and classic IR target-neutral.
  SDKLIB-3 makes those descriptors the full target contract by carrying the intrinsic id,
  runtime arity, return kind, compile-time operand roles, and required capabilities; the
  resolver validates extern return types and names the intrinsic id in compile-time operand
  diagnostics.
  `Sprite.Draw(...)` now comes from the `RetroSharp.Portable2D` source package over role-bearing `sprite_draw`
  intrinsics on both Game Boy and NES, collecting to the same `Sdk2DOperation.DrawLogicalSprite`.
  Game Boy and NES `Camera.AabbTiles(...)` and
  `Camera.AabbHitTop(...)` also come from package helpers over target intrinsics with a hidden
  `"default"` world id and compile-time flag mask, still collecting to the same camera AABB
  SDK operations and preserving `Sprite.Width(...)` extents and the `255` no-hit contract.
  SAL-8.9 then migrated `Camera.ScreenAabbTiles(...)` /
  `Camera.ScreenAabbHitTop(...)` to the same intrinsic path on both targets (catalogued as
  `camera_screen_aabb_*` with hidden `WorldId`/`EnumFlags`), so all four camera-relative
  collision queries share the intrinsic path; the actor framework's generated `Camera.ScreenAabb*`
  calls stay byte-identical (`actors.gb`/`actors.nes` ROMs unchanged).
  SAL-8.7 migrated Game Boy and NES `Music.Play(...)` / `Music.Stop()` to `Music`
  helpers in `RetroSharp.Portable2D` over `music_play` (compile-time `AssetRef` theme) / `music_stop` target intrinsics,
  collecting to the same `SdkAudioOperation.PlayMusic`/`StopMusic`; direct backend aliases for those
  calls are no longer public source APIs.
  One-shot FX now follow the same pattern: `Sfx.Asset(...)` is a source-package
  `[resource("sfx_asset")]` declaration, `Sfx.Play(...)` wraps the `sfx_play`
  target intrinsic with a compile-time `AssetRef`, and both Game Boy and NES
  lower it separately from BGM. Both targets give an effect priority over the BGM
  on its channel (channel 1 / square+sweep on GB, pulse 1 on NES): the SFX trace is
  filtered to that channel (GB: `$FF10-$FF14`, dropping globals `NR50/NR51/NR52` and
  other channels; NES: `$4000-$4003`, dropping `$4010/$4015/$4017`), the BGM player
  suppresses *and shadows* its own channel writes while an effect is active, and the
  full shadowed channel state is restored when the effect ends so the BGM melody is
  not left carrying the effect's residue. GB shadows/restores `NR10-NR14` (page-aligned
  shadow at `$C200` so the address is `$C200 + register offset`; `NR14` restored with
  its trigger to reload the `NR12` envelope). NES shadows/restores `$4000-$4003` (a
  descending-order restore loop; because all four stores land in one frame before the
  next APU sequencer clock, the order still yields the BGM's final state). Both must
  restore the whole channel, not just the sweep, because the BGM can go many frames
  without rewriting the duty/volume register (GB `NR11`/`NR12`, NES `$4000`), so that
  residue would otherwise stick on the melody. Game Boy
  advances active SFX streams from `Audio.Update()` after BGM writes (its trace has
  per-order-entry frame waits, so it rings out naturally); NES plays SFX as a flat
  per-frame one-shot trace ticked once per `Audio.Update()` after BGM, arms the
  effect via a zero-page cursor, a ring-out linger counter, and an `SfxActive` flag
  (never touching the BGM tick/order state), keeps owning the channel for the linger
  frames so the note rings out fully, and shares one APU body writer with the BGM
  engine to fit NROM. The NES SFX data is emitted after the DPCM samples so it does
  not shrink the DPCM window.
  SAL-8.8 completed the `audio` class by migrating `Audio.Init()` to a void-leaf `audio_init`
  target intrinsic on both targets (collecting `SdkAudioOperation.InitializeAudio`), with the
  `Audio.Init(...)` builtin kept as an alias.
  SDKLIB-4 retired the simple runtime facades `Video.WaitVBlank`, `Input.Poll`,
  `Audio.Init`, `Audio.Update`, `Camera.SetPosition`, and `Camera.Apply` from
  hard-coded public-name lowering; those public names now come from SDK source
  packages.
  SDKLIB-5 moved resource declarations behind source-package contracts:
  `Sprite.Asset`, `World.Load`, `Music.Asset`, `Sfx.Asset`, `Palette.Background`,
  `Palette.Sprite`, and `Animation.Clip` are declared in `RetroSharp.Portable2D`
  with `[resource(...)]` metadata and resolved to generic resource declaration
  descriptors instead of target-specific public-name switches.
  SDKLIB-6 moved remaining complex package-backed facades out of hard-coded
  public-name lowering: `Camera.Init`, all four `Camera.*Aabb*` helpers,
  `Sprite.Width`, `Sprite.Draw`, `Animation.Frame`, `Music.Play`/`Stop`, and `Sfx.Play`
  are now provided by `RetroSharp.Portable2D` source methods over target intrinsics.
  Actor-framework generated calls now use imported target-intrinsic extern
  functions discovered from the source package, so generated actor code no longer
  relies on public SDK names being hard-coded in the compiler. Flat declaration calls
  such as `Sprite.Asset(...)`, `World.Load(...)`, `Music.Asset(...)`, and `Sfx.Asset(...)`
  are package-provided resource declarations, not compatibility aliases.
  Internal stream operations (`StreamMapColumn`/`StreamMapRow`) remain compiler-emitted effects
  of camera lowering, not public source calls.
- Pending in the edited #106 slice: none known after PL-E1.
- Active SDK v1 stabilization backlog after #106: none known after the collision, cross-target
  diagnostic, and logical palette slices landed.
- Camera-relative AABB decision implemented after #106 and extended to NES runner parity:
  `Camera.AabbTiles(...)` is a capability-gated SDK query for camera-relative AABBs; its
  `screenX` operand may be literal or byte-backed. Game Boy and NES both declare and lower it
  through `Sdk2DOperation.CameraAabbTiles`.
- Landing tile-hit decision implemented after #106: `Camera.AabbHitTop(...)` is a
  capability-gated SDK query that returns the top world-pixel Y of the first matching tile in a
  caller-defined camera-relative AABB, or `-1` (`0xFFFF`) when none hit. Its world-Y operand and
  result are complete words; Game Boy returns the result through `HL` and NES through `A:X`.
  Byte destinations remain compatible only while the active world is at most 32 hardware rows.
  The runner uses an explicit `i16` local and keeps the downward-velocity gate and
  `player.Land(...)` policy in source.
- Runner-shaped NES parity decision: `samples/runner/runner.retrosharp.json` is the shared Game Boy/NES runner
  project. It lists `src/main.rs` plus helper/state code from `samples/runner/src`. It declares
  `assets/music/runner.vgz`, which resolves to per-target VGM/VGZ variants and
  gives both Game Boy and NES real frame-driven BGM lowering. `NesRunnerAcceptanceTests`
  enforces that the shared source builds for NES, while `CrossTargetScrollAcceptanceTests`
  verifies that runner-shaped camera-relative collision lowers on both targets.
- Logical palette decision implemented after #106: `Palette.Background(slot, c0, c1, c2, c3)`
  and `Palette.Sprite(slot, c0, c1, c2, c3)` declare capability-checked logical palette slots.
  Color values are logical tones `0..3`. Game Boy lowers background slot `0` to `BGP` and sprite
  slots `0..1` to `OBP0/OBP1`; NES lowers background/sprite slots `0..3` into fixed grayscale
  entries by default; NES Tiled backgrounds can derive a universal background color plus up to
  four background palette slots and initial attribute bytes from placed tiles in the selected
  tileset PNG, and PNG sprite assets can derive the hardware sprite palette for the draw
  slot that uses them while preserving the background universal color. The runner now uses
  `Palette.Sprite(0, 0, 0, 1, 3)`, preserving accepted `OBP0 = 0xD0`, without raw
  `ObjectPalette.Set(...)`.
- Actor framework runtime activation decision on branch `feature/actor-framework`: `Actors.SpawnLayer`
  and `Actors.SpawnWindow` no longer materialize Tiled spawns as active slots at compile time.
  They generate ROM-table spawn helpers plus a fixed per-layer `used[]` byte array and should be
  called after `Camera.SetPosition(...)` each frame. Slots recycle when actors leave the activation
  window or source code clears `active`; authored spawns are one-shot and do not respawn after a
  successful activation. Capacity diagnostics use the maximum simultaneous spawns in the declared
  camera-relative window, not total layer count.
- Actor framework closure state on branch `feature/actor-framework`: AF-5.1..AF-5.8 are landed,
  making Iteration 14 feature-complete for the first scrolling platformer slice. Open follow-ups
  are non-blocking robustness/scale items: AF-5.9 decide one-shot versus reactivation spawn policy
  and AF-5.10 reduce O(spawns)/frame activation scans.

Suggested next steps for the next agent, in order:
1. For the active cross-target scale frontier, read the exact published card in
   `docs/LargeWorldsRoadmap.md`; begin with
   [LW-2.4 / #299](https://github.com/SuperJMN/RetroSharp/issues/299) and/or
   [LW-3.1 / #301](https://github.com/SuperJMN/RetroSharp/issues/301), not an
   open-ended request to continue #275.
2. Treat `--world-budget-report` as map-only evidence and remeasure the final
   linked ROM/window layout in every placement/selection task.
3. Keep each GB/NES chain sequential and keep target details out of public
   SDK/Core. Do not migrate the shared runner before `LW-3.5`; HUD/AR-10,
   broader #247 gaps, and #244 spawn-scan cost remain independently scoped.

## Game Boy Runner Lessons

- Normal runner debugging should start with the full app, then use `tools/gameboy/runner_diagnostics.py` to find the first failing diagnostic step before editing code.
- `Input.Poll()` (PascalCase `Input.Poll()`) is the frame/tick input boundary. New gameplay should use `Input.IsDown`, `Input.WasPressed`, `Input.WasReleased`, and `Input.HoldTicks` (and `Sprite.Width`). The button argument is a member of the built-in `Button` enum (`Button.A`, `Button.Right`, ...), defined in the `RetroSharp.Portable2D` source package under `sdk/RetroSharp.Portable2D`. Direct snake_case button calls, direct `sprite_width`, and bare lowercase button identifiers (`a`, `right`, ...) are not public source APIs. `Input.IsDown`/`WasPressed`/`WasReleased` return `bool`; `Input.HoldTicks` returns a count.
- `Input.HoldTicks` saturates at `255` and is the accepted seam for variable-height jump timing.
- On original DMG hardware, `JOYP` row selection must settle. The backend should select a row, read it several times, use the final sample, and deselect both rows with `0x30`.
- `Sprite.Draw(...)` accepts portable `flipX` and palette slot arguments. Do not reintroduce raw OAM attribute bytes through portable sprite calls.
- Sprite PNG paths can be generic. `Sprite.Asset(player, "assets/player.png", w, h)` resolves to a target variant such as `assets/player.gb.png` or `assets/player.nes.png` when present, then falls back to the requested PNG.
- Mirrored metasprites must preserve logical sprite width, not padded hardware footprint.
- The accepted runner object palette is `0, 0, 1, 3`, which compiles to `OBP0 = 0xD0`.
- Collision over wider sprites should use logical sprite width through helpers such as `Sprite.Width(...)`; runner actors should project current screen X/Y from actor/player world position minus camera position, then pass byte-backed screen coordinates to `Camera.AabbTiles(...)`/`Camera.AabbHitTop(...)` or the fully screen-space `Camera.ScreenAabbTiles(...)`/`Camera.ScreenAabbHitTop(...)` forms so collision stays aligned with the visible camera after long scrolls.
- If a platform feels dead even though visual tiles look correct, inspect frame order and state transitions, not just collision geometry.
- Clamp vertical runner state before collision/reset logic when it can cross the top of the scene.
- The runner reset path should restore actor, velocity, animation, facing, jump, and movement state without rebasing the scrolled background.

## Tiled Map Pipeline

The runner loads `samples/runner/assets/maps/stage1.playable.tmj`, derived from
the complete editable `samples/runner/assets/maps/stage1.tmj` design and
`stage1.tsx` tileset by `tools/runner/build_stage1_playable_map.py`. The complete
156x20 source map is the Large Worlds acceptance payload; the current 88x15
derived map remains the stable runtime input until a target acceptance task
switches it deliberately.

Pipeline shape (two phases, after #105 partial extraction):

- Target-neutral phase: `RetroSharp.Core.Sdk.Tiled.LogicalTiledMapImporter.Load(path)` parses the
  Tiled JSON/TSX, resolves tileset descriptors (metadata + image path, no decoded pixels),
  computes geometry and the playable world slice, and resolves collision into portable
  `WorldTileFlags`. It yields a `LogicalTiledMap` of source-tile GID references only. PNG decoding
  (`RetroSharp.Core.Imaging.PngImage`) is shared in Core; Game Boy still uses the shared luminance
  quantizer for canonical 8x8 four-tone patterns.
- Per-target phase: `GameBoyTiledMapImporter` and `NesTiledWorldImporter` both consume the
  `LogicalTiledMap`, resolve target PNG variants for tileset images (`tiles.gb.png`/`tiles.nes.png`
  when present), decode tileset PNGs, and encode them into their own 2bpp tile byte layout (Game Boy
  interleaved planes from luminance tones; NES planar planes from derived palette indexes),
  deduplicating and composing the background under blank world cells. `World.Load(path)` therefore
  lowers on both Game Boy and NES from the same source while keeping the editable `.tsx` pointed at
  one baseline PNG.
- LW-1.1 carries `Camera.SetPosition(...)` through shared `SdkWordExpression`
  operands and both target lowerings. GB/NES map widths and logical
  source-column cursors support `1..4096`, byte-backed callers zero-extend, and
  hardware scroll writes remain bytes. This does not add packed/banked world
  storage or widen collision hits.
- NES limitations: four-screen free scroll uses a 64x60 nametable buffer, and runtime-streamed row attributes currently refresh
  as palette slot 0 rather than carrying full Tiled palette provenance. Mapper-backed scale and HUD
  IRQs are still deferred to NF-10.
- The shared runner currently uses a horizontal camera over the 88x15 source
  `stage1.playable.tmj` map, expanding to 176x30 hardware tiles. The complete
  156x20 source `stage1` design expands to 312x40 hardware tiles and is retained
  as the explicit Large Worlds acceptance target instead of being silently
  trimmed by future target work. `LW-2.5` must prove it through a separate
  non-destructive Game Boy fixture; the shared input stays on the playable map
  until `LW-3.5` jointly migrates GB/NES and regenerates both tracked ROMs.
- Portable/target split (#105 collision resource done): `WorldMap2D` now stores only dimensions and
  per-tile `WorldTileFlags` (portable collision); already-lowered target background tile numbers live
  in a separate `WorldTileGrid` owned by each target. Per-pixel layer flattening stays per target
  because the blank-cell decision depends on the generated pattern.

Important current behavior:

- `World.Load(path)` imports finite orthogonal Tiled JSON maps.
- External `.tsj` and `.tsx` tilesets with PNG images are accepted; target backends can substitute
  `.gb.png` or `.nes.png` variants for the tileset image while Tiled keeps editing against the base PNG.
- Tiled source cells are expanded into Game Boy 8x8 cells; for example, a 16x16 source tile becomes a 2x2 Game Boy block.
- Generated Game Boy tile patterns are quantized and deduplicated.
- Game Boy has one scrolling background tilemap, so authoring layers are flattened.
- `background` is the visual base.
- Non-empty `world` cells overlay the background at the same Tiled coordinate.
- Empty `world` cells keep the background tile underneath.
- If `retrosharpWorldY` and `retrosharpStreamY` move the playable world slice, the background layer is shifted by the same amount so Tiled layers stay visually aligned.
- Game Boy Tiled maps loaded through `World.Load(...)` can keep a world taller than the 32-row background buffer. Startup clips the initial preload to the circular tilemap, while the full imported height stays in ROM row tables for vertical camera streaming.
- Background rows above the streamed world band (GB rows `0..streamY-1`) are emitted as full-width source-map rows and streamed horizontally by the camera, so background decorations above the band scroll with the world instead of freezing/repeating every 32 tiles.
- Collision remains independent from visual composition.
- Tileset `objectgroup` rectangles become solid flags when there is no explicit collision layer.

When `samples/runner/src/*.rs`, `stage1.playable.tmj`, `stage1.tmj`, the tileset,
or GB/NES asset lowering changes, rebuild the tracked runner ROMs with
`tools/gameboy/generate_sample_roms.py`.

## Known Traps

| Trap | Better action |
| --- | --- |
| Assuming `RetroSharp.Cli --help` works | Inspect `src/RetroSharp.Cli/Program.cs`, `README.md`, or `WARP.md`; unknown options fail. |
| Treating a passing local build as a push | Verify upstream with `git rev-list --left-right --count HEAD...@{u}` and `git ls-remote`. |
| Ignoring submodules before publication | Check `git submodule status --recursive`; current submodules are `libs/6502DotNet` and `libs/Z80DotNet`. |
| Broad formatting-only runs touching old/vendored files | Use targeted formatting and `git diff --check`. |
| Editing generated Game Boy ROMs by hand | Regenerate from source using `tools/gameboy/generate_sample_roms.py`. |
| Treating generated screenshots as source artifacts | Leave `samples/runner/*.png` alone unless explicitly requested. |
| Fixing emulator/hardware mismatches only in samples | Check backend/runtime behavior first, especially input and LCD/PPU state. |
| Assuming fast GB diagonal scroll garbage is a throughput limit | The bottom-edge init (`CameraBottomBackgroundRow/SourceRow`) must seed a visible screen-height below the top, not the clamped buffer height; a wrong seed streams downward crossings into the top band. |
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

Regenerate tracked sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build individual ROM samples:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/bin/runner.gb \
  samples/runner/runner.retrosharp.json

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
