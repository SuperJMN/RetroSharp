# AI Agent Project Context

Status: memory-derived project context for AI CLI agents.
Last updated: 2026-07-15.

This document preserves project knowledge that previously lived only in agent memory and recent runs. It is intentionally practical: it records where to look, which commands have been reliable, and which failure modes should shape future work.

## Recent Baseline

- AIN-3 / #359 projects the authoritative NES runtime layout into a versioned
  JSON sidecar through CLI `--runtime-abi-out`. The contract contains every
  reserved range, named runtime address, intentional alias, probe constant,
  ROM-specific WorldPack runtime region, and compiled user-local symbol. Its
  SHA-256 binding rejects a sidecar for a different ROM before RetroArch or
  NesMcp starts. Python power-on and
  visual-parity tooling must resolve RAM through `tools/nes/runtime_abi.py`,
  not add address constants; the C# cadence harness emits the same projection
  directly. The tracked runner contract is
  `samples/runner/bin/runner.nes.runtime-abi.json` and is regenerated with the
  ROM by `tools/gameboy/generate_sample_roms.py`.
- AIN-4 / #360 makes
  `src/RetroSharp.Sdk.Frontend/TargetFrontendPreparation.cs` the single owner of
  source-package merge, parse, target selection, import validation, Actor
  Framework lowering, facade lowering, `let` inference, and function-contract
  validation. Game Boy and NES provide their own intrinsics, capabilities,
  resources, assets, metasprite geometry, and final video-program construction;
  compile plus both operation collectors must route through each target's one
  `PrepareVideoProgram` adapter. The prepared contract keeps its pre-Actor
  internal program private and exposes only the late actor pool budget behavior that needs
  target-resolved geometry. The architecture guard lives in
  `TargetFrontendPreparationArchitectureTests`, and cross-target public-path
  diagnostics live in `CrossTargetFrontendPreparationTests`.
- AIN-2 / #358 makes `src/RetroSharp.NES/NesRuntimeMemoryLayout.cs` the
  single owner of compiler-reserved NES CPU RAM. Its interface groups
  zero-page locals, camera/runtime scratch, input, audio, OAM, mapper shadows,
  packed-camera, WorldPack scalar/auxiliary state, and the fixed `$0400`
  staging window; declares the three `$0800-$1FFF` CPU RAM mirrors and every
  intentional shared-address role (including `$E4/$E8/$E9`); and validates
  disjoint reserved ranges, the exact 222-byte local limit, and the 594-byte
  staging maximum. NES builders, runtime emitters, and in-repo ABI tests must
  consume this module rather than add private address maps. The focused
  contract lives in `NesRuntimeMemoryLayoutTests`.
- AIN-1 / #357 makes `src/RetroSharp.GameBoy/GameBoyRuntimeMemoryLayout.cs`
  the single owner of compiler-reserved Game Boy RAM. Its interface groups
  named user-local, camera, input, audio, banking, packed-camera, collision,
  and WorldPack addresses; declares every reserved range plus the intentional
  WRAM echo alias; and validates range disjointness, local-storage limits, and
  WorldPack staging limits. Game Boy emitters and runtime helpers must consume
  this module rather than add private `$Cxxx` maps. The focused contract lives
  in `GameBoyRuntimeMemoryLayoutTests`.
- 2026-07-15 runner jump update: shared Game Boy/NES source now uses SMB3-style
  signed 4.4 vertical motion. Standing/walking/running/maximum-speed takeoff is
  `-$38/-$3A/-$3C/-$40`; held A applies `+1` gravity only while velocity is
  below `-$20`, while release or the threshold applies `+5` without clamping,
  and falling caps at `$45`. Exact production-ROM CPU tests read
  integer Y plus the `0..15` subpixel remainder and pin rises of `330/16`,
  `1131/16`, `1361/16`, and `1607/16` pixels, so the standard held jump is
  70.6875 px internally and 71 visible pixels. Both tracked runner ROMs change
  with this source behavior; see `docs/RunnerLandingAcceptance.md`.
- RPH-GB / #353 restores complete-runner Game Boy gameplay cadence on the
  retained packed WorldPack path. The broken exact runner completed 37 of 120
  source ticks and advanced 45 pixels; the fixed build completes 115/120 with
  no two-frame stall and advances 142 pixels in the in-process gate. Collision
  lookup reads raw IDs directly, retains two tagged RLE chunks, serves exact
  and bounded memo hits, and never publishes malformed decodes. SameBoy through
  GameboyMcp independently measured 115 gameplay ticks, 120 audio ticks, 143
  pixels, one additional RLE decode, and zero forbidden VBlank work over the
  same 120-frame held-Right window. See
  `docs/GameBoyRunnerCadenceAcceptance.md`.
- NES packed column attributes must follow physical 30-tile-row nametable
  provenance, not only the portable four-row authored grouping. At nonzero
  camera Y, the 30-row seam otherwise swaps the upper/lower palette quadrants
  while leaving tile IDs correct. The runtime keeps the global table for row
  commits and emits a page-aligned physical table for columns; the complete
  `stage1` probe now measures a 3,158-byte runtime index, 7,310 pinned R7 bytes,
  and 8,999 fixed bytes. The exact runner regression holds RIGHT to visible X
  100 and returns LEFT to X 0 at Y 80. FCEUmm reproduced that path with zero
  visible tile/palette mismatches. See
  `docs/PackedTiledFunctionalAcceptance.md`.
- The horizontal #335 slice adds `tiled-hscroll-short` and
  `tiled-hscroll-full` as collision-free Game Boy/NES target-acceptance
  samples. Their 64x20 and 156x20 fixtures preserve every horizontal column of
  the short/full `stage1` variants, move the bottom 15 authored rows into the
  30-hardware-row camera window, and retain five empty staging rows below it.
  The camera settles at Y 96 on Game Boy and Y 0 on NES for 64 ticks before X
  movement. Their four exact-ROM scenarios keep at least a 0.994 gameplay ratio
  with two-frame-or-better camera visibility, zero authored tile/palette
  mismatches, and zero unsafe video writes. The NES fixes retain prepared-column
  subcell/row-stride state, walk X through 256 with a stable high byte, support
  fast power-of-two chunk-column layouts, and keep the authored palette oracle
  in world coordinates after nametable wrap. See
  `samples/tiled-hscroll/` and `docs/PackedTiledFunctionalAcceptance.md`.
- RPH-3.5 / #339 binds eight exact packed Tiled production scenarios to the
  shared functional runner: `tiled-tall` GB, `tiled-vscroll` GB/NES,
  `tiled-diagonal` GB, `tiled-free-scroll` GB/NES, and `deadzone-follow`
  GB/NES. Game Boy now uses three standard or six diagonal visual cache slots,
  serializes diagonal preparation, keeps decode/directory/bank work outside
  VBlank, and records transitions that complete between sampled frames. NES
  synchronizes the four sprite-side universal palette aliases with the derived
  Tiled background color. Game Boy packed commits retain the previous OAM
  projection instead of starting a second VBlank wait, while the no-audio RLE
  hot path uses a bounded inline LY guard and preserves the full audio-aware
  guard when audio is active. Exact tracked-ROM hashes, reviewed timing budgets,
  zero transient integrity failures, and GameboyMcp/NesMcp checkpoints are in
  `docs/PackedTiledFunctionalAcceptance.md`.
- CSL-3 / #338 binds eight exact production-source GB/NES ROM scenarios to the
  shared #337 functional runner: static drawing, bidirectional cross-target
  camera, GB source vertical scroll, GB/NES source free scroll, and GB Window
  HUD. The deterministic test machines retain every authored visible tile and
  palette (including NES attribute selection plus all four colors),
  source/requested/visible camera state, exact ticks, reset-vector re-entry, and
  cycle-positioned VRAM/PPU/OAM writes. The NES clock uses integer PPU dots and
  rendering-dependent odd-frame skip rather than a rounded CPU frame constant.
  GameboyMcp/NesMcp checkpoints on the
  same emitted bytes independently cover the moving samples and both wrap
  boundaries. See `docs/SimpleSampleFunctionalAcceptance.md`.
- Code baseline immediately before the AF-4.3 documentation closeout:
  `f0398452fd0e3b93d4d77e6aeac5749dbf1322ed`.
- 2026-07-14 runner update: `stage1.tsx` tile 30 marks the existing green
  ledges as `Platform` (56 expanded cells). Shared source lands through
  `Solid | Platform` only while non-rising and only from above; unsupported
  grounded actors fall after walking off, while walls/ceilings remain `Solid`.
  Two-pixel run ticks keep their collision projection anchored to the camera X
  at tick start, so the second B-speed substep cannot repeat the first world
  column and enter the first solid staircase by one pixel on either target.
  The production packs are now GB 2,568 / NES 2,780 bytes
  (770 visual + 326 collision). Real ROM tests also cover the Game Boy packed
  camera collision query at a source column whose `camera + screen` sum exceeds
  255, preserving the complete source-column word instead of truncating it.
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
- RPH-3.4 / #332 restores Game Boy packed-camera cadence without falling back
  to legacy raw rows. `samples/tiled-vscroll/vscroll.rs` reaches source tick 87
  and `SCY=86` after 90 SameBoy frames (baseline 88/87, broken master 11/10),
  completes ten balanced staged lifecycles, and keeps forbidden VBlank
  bank/decode/directory counters at zero. The first cold edge becomes resident
  in 63,248 cycles; automated coverage bounds fourteen cold/chunk/bank edges
  to one 70,224-cycle frame. See `docs/GameBoyPackedCameraCadenceAcceptance.md`.
- RPH-3.3 / #331 gives automatically selected mapper-0 packed-camera images
  the same fixed, mapper-neutral frame-signal NMI handler used by MMC3 while
  retaining mapper-0 reset/IRQ semantics and automatic profile selection.
  `samples/tiled-vscroll/vscroll.rs` remains mapper 0; AprNes/NesMcp measured
  an exact 90-frame NMI delta after startup with `PPUCTRL=$80`, `PPUMASK=$1E`,
  active rendering, source/visible camera progress, and 11 later tile edges.
  See `docs/NesMapper0PackedCameraAcceptance.md`.
- RPH-3.1 / #326 makes the NES packed runner independent of CPU RAM power-on
  policy. Startup clears only the exact `$0326..$03FF` WorldPack/camera control
  block and `$0400..$0651` 594-byte staging layout, then assigns `NoSlot`.
  `tools/nes/verify_runner_power_on_ram.py` proves `$00`, `$FF`, and a
  deterministic nonzero pattern through FCEUmm `(SVN) 3a84a6f`; AprNes/NesMcp
  retains the same 120-frame `15/15/15/15/15` scheduler lifecycle evidence.
- RPH-3.2 / #327 restores deterministic MMC3/four-screen runner streaming in
  FCEUmm without changing gameplay, map content, mapper selection, or collision.
  `tools/nes/verify_runner_visual_parity.py` is the single end-to-end harness:
  it drives the tracked ROM right beyond camera X 300, jumps, and returns left
  through X 256 in AprNes/NesMcp, isolated RetroArch/FCEUmm, and Nestopia. It
  compares all four physical nametables, exact visible tile IDs plus attribute
  palette selectors, authored collision, PPU/lifecycle state, and framebuffers.
  The final transient acceptance retains all 47 commit-centered five-frame
  windows in AprNes/Nes.Mcp 0.0.7, FCEUmm, and Nestopia, plus five complete PPU
  traces with zero PPUDATA writes outside VBlank. The fix bounds the
  32-tile/8-attribute column commit to 2136 CPU cycles and makes packed
  `Video.WaitVBlank()` discard stale coalesced state before waiting for a fresh
  NMI/VBlank edge. RetroArch automation is shared with the power-on harness
  and guards persistent `retroarch.cfg` plus `FCEUmm.opt` by hash.
- The Game Boy vertical camera path is now proven by `samples/source-vscroll/vscroll.rs`,
  a ROM/VRAM acceptance test, and a shared-row-streamer emission fix. Game Boy
  `Camera.SetPosition` can walk up to two same-axis tile crossings per frame and
  commit both exposed edges during `Camera.Apply()`. Game Boy diagonal camera
  movement is proven by `samples/source-free-scroll/freescroll.rs` and
  `samples/tiled-free-scroll/free-scroll.rs` with staggered column/row commits.
  NES has a four-screen free-scroll path with preloaded 64x60
  movement, Tiled diagonal coverage inside that surface, horizontal column streaming
  for wider worlds, and staggered vertical row plus zero-palette attribute streaming
  for source-authored worlds taller than the buffer. NF-10 mapper-backed scale and
  IRQ HUD remain separate in `docs/NesFreeScrollRoadmap.md`.
- The Large Worlds epic now has a dedicated execution source in
  `docs/LargeWorldsRoadmap.md`. It treats the full runner `stage1` design as the
  acceptance target. Waves 0/1, Game Boy `LW-2.1` through `LW-2.5`, NES
  `LW-3.1` through `LW-3.4`, and joint `LW-3.5` are implemented. The target cards are native subissues:
  `LW-2.1`..`LW-2.5` are #296-#300 and `LW-3.1`..`LW-3.5` are #301-#305. All
  belong to milestone 11 under parent #275; do not dispatch the parent as one
  task.
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
  restoring the actual bank shadow LIFO. LW-2.4 adds the target-private packed
  camera scheduler: two immutable peer edge slots move through request,
  prepare, resident, commit, and release; bank/decode work stays outside
  VBlank; each VBlank commits at most one 19-tile column or 21-tile row; and
  malformed/unavailable or wrongly tagged edges defer visible camera advance.
  Same-axis peers preserve order, diagonal peers are column-first and then
  staggered, reversals release only uncommitted resident work, and audio keeps
  one tick per real frame during preparation stalls. The raw camera path and
  shared runner input remain unchanged. LW-2.5 adds the non-destructive complete
  `stage1` acceptance fixture, removes duplicate legacy rows from bank-placed
  packed links, and proves full traversal, all 60 chunks, collision parity,
  16-bit tags/lifecycle, guard-band/VBlank counters, bank restoration, and a
  target-private `$C19D` packed-audio tick counter. The real runner fixture
  selects 128 KiB MBC1 while a smaller full-stage probe stays ROM-only, so the
  evidence comes from the final link rather than the map-only report.
- LW-3.1 adds a target-private forced MMC3/TVROM test profile while leaving the
  public compiler on byte-identical mapper 0 by default. Its section-aware
  `PrgBuilder` relocates the complete callable/runtime/DPCM/vector image to
  physical banks 6-7 at `$C000-$FFFF`, emits 64 KiB PRG plus 16 KiB CHR and
  exact `04 02 48 00` header fields, initializes PRG mode 0, linear resident
  CHR pages, R6/R7 and their shadows, fixed bank helpers, and disabled mapper
  IRQs without writing `$A000`. A generated smoke was loaded through NesMcp
  `auto`/AprNes, entered through the bank-7 reset trampoline at `$FF80` before
  jumping to the `$C000` runtime, switched R6/R7 across distinct physical banks,
  and exposed independent probes at `$2000/$2400/$2800/$2C00`.
- LW-3.2 adds the production mapper-0-first selector and physical data linker.
  A discovered `WorldPack` does not perturb a mapper-0 image that already fits;
  only a real mapper-0 PRG/DPCM layout failure retries MMC3. MMC3 owns ordered
  R6 world banks `0, 3, 4, 5`, pinned R7 bank `1`, boot-only R7 bank `2`, and
  fixed banks `6, 7`. The canonical multi-R6 acceptance pack crosses all four
  non-contiguous R6 segments byte-for-byte without padding or rewritten v1
  offsets. The normalized full-`stage1` placement probe embeds 2,762 pack
  bytes. LW-3.3 adds 2,176 bytes of fixed reader/runtime for a current 4,327
  fixed payload, while pinned/boot/CHR remain 5,012/4,128/3,056. The raw
  runner-shaped path still reports its exact fixed overflow; no runner
  migration is present.
- LW-3.4 adds the NES packed-camera scheduler: two immutable peer edges carry
  16-bit tags through request/prepare/resident/commit/release; preparation and
  raw/RLE/R6 work stays outside VBlank; visible camera state publishes only
  after the matching edge; columns commit at most 32 tiles plus 9 attributes;
  and rows retain their slot for four 8-tile phases plus one attribute phase.
  The normalized full-stage camera probe links and traverses in AprNes with
  BGM/SFX/DPCM active, while the shared runner input and tracked ROMs remain
  unchanged.
- `LW-3.5` / #305 is the completed joint acceptance slice: it migrates the
  shared runner to complete `stage1.tmj`, regenerates both tracked ROMs, and
  proves the final GB/NES production paths together. Issue #244 stays in Wave
  4; Wave 3 only links the mapper-backed slice from #247 and does not absorb
  its unrelated gaps.
- RPH-3 / #321 restores NES complete-stage cadence without source speed
  changes. The canonical runtime uses O(1) pinned plane descriptors, a two-slot
  collision cache plus exact-cell memo, and six visual slots prewarmed for the
  next two viewport columns; total staging remains the accepted 594 bytes. A
  fixed bank-neutral NMI publishes hardware-frame/pending counters only, while
  `Video.WaitVBlank()` keeps camera commit, directories, decoders, mapper work,
  input, audio, and gameplay in mainline code. Paired input snapshots reject
  DPCM-corrupted controller reads. AprNes measures 120 hardware frames = 120
  gameplay/input ticks = 120 audio ticks and 150 source-model X pixels; see
  `docs/NesRunnerCadenceAcceptance.md`.
- The NES 8 KiB R6 window is not a whole-pack size cap. `LW-3.2` places an
  unchanged synthetic `WorldPack` larger than 8 KiB over an explicit ordered
  list of R6-owned continuation segments, whose physical bank ids may be
  non-contiguous because of R7 ownership; `LW-3.3` must read directory and
  raw/RLE payload ranges across those boundaries. `LW-3.4` additionally owns a
  two-gameplay-frame maximum from a valid edge request to residency under an
  R6 crossing with audio/DPCM active. NES v1 still keeps all executable code in
  the fixed 16 KiB region; this epic does not implement code banking.
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
| How do canonical ROM scenarios measure cadence and transient integrity? | `docs/FunctionalRomAcceptance.md` |
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

Suggested next steps for the next agent:
1. Treat Large Worlds Waves 0–3 as complete and read
   `docs/LargeWorldsRoadmap.md` before changing the accepted pack, staging, or
   cartridge contracts.
2. Treat `--world-budget-report` as map-only evidence and remeasure the final
   linked ROM/window layout in every future placement/selection task.
3. Keep HUD/AR-10, broader #247 gaps, #244 spawn-scan cost, Wave 4 art
   residency, and executable-code banking independently scoped.

## Game Boy Runner Lessons

- Normal runner debugging should start with the full app, then use `tools/gameboy/runner_diagnostics.py` to find the first failing diagnostic step before editing code.
- `Input.Poll()` (PascalCase `Input.Poll()`) is the frame/tick input boundary. New gameplay should use `Input.IsDown`, `Input.WasPressed`, `Input.WasReleased`, and `Input.HoldTicks` (and `Sprite.Width`). The button argument is a member of the built-in `Button` enum (`Button.A`, `Button.Right`, ...), defined in the `RetroSharp.Portable2D` source package under `sdk/RetroSharp.Portable2D`. Direct snake_case button calls, direct `sprite_width`, and bare lowercase button identifiers (`a`, `right`, ...) are not public source APIs. `Input.IsDown`/`WasPressed`/`WasReleased` return `bool`; `Input.HoldTicks` returns a count.
- `Input.HoldTicks` saturates at `255` and remains the portable duration-count seam. The current SMB3-style runner jump instead uses `Input.IsDown` plus the live signed vertical velocity: `+1` gravity only below `-$20`, otherwise `+5`, with no release clamp.
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

The runner loads complete `samples/runner/assets/maps/stage1.tmj` directly with
the `stage1.tsx` tileset. Its 156x20 source cells expand to 312x40 hardware
tiles and enter the target-owned packed runtimes. The older
`stage1.playable.tmj` crop and its build tool remain historical fixtures only.

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
- NES limitations: four-screen free scroll uses a 64x60 nametable buffer.
  Packed runtime-streamed columns/rows now carry LW-1.4 palette provenance;
  runtime CHR banking and HUD IRQs remain deferred.
- The shared runner uses a 2-axis camera over complete `stage1.tmj`. `LW-3.5`
  jointly migrated GB/NES, regenerated both tracked ROMs, and retained exact
  full-map visuals, collision, target audio, and mapper/bank restoration.
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

When `samples/runner/src/*.rs`, `stage1.tmj`, the tileset,
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
  --out samples/static-drawing/drawing.gb \
  samples/static-drawing/drawing.rs

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
