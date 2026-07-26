# RetroSharp Architecture Overview

Status: current active architecture reference. Last updated: 2026-07-26.

This is the routed answer to "what layer owns a new concept?". It holds the
active layer model, capability model, shared SDK operation model, golden rule,
shared world-map resource, and agent task contract. Roadmap iterations and the
`AR-x.y` implementation backlog live in [`ArchitectureRoadmap.md`](ArchitectureRoadmap.md).

## Goals

- Keep the RetroSharp language small, explicit, and target-neutral.
- Define a portable 2D SDK for common tile/sprite machines.
- Keep raw machine access available through target intrinsics.
- Make Game Boy and NES the first compatibility pair for the portable 2D contract.
- Preserve the Game Boy runner as the acceptance sample while reducing target-specific leakage.
- Fail early when a feature does not fit a target's declared capabilities.

## Non-goals

- No hidden VM, heap, GC, exceptions, or dynamic runtime.
- No promise of unlimited sprites, free palette changes, or arbitrary VRAM writes.
- No automatic physics engine in the portable SDK.
- No portable guarantee for scanline effects, mapper-specific tricks, or machine-specific register behavior.
- No requirement that every target supports HUD, diagonal scrolling, or software sprites.

## Layer Model

The layer model is enforced by `src/RetroSharp.Architecture.Tests`: language projects must not reference SDK-frontend or target assemblies, language source must stay free of portable-SDK/target domain terms, portable SDK projects must not reference concrete targets, and any non-target raw hardware terminology must be explicitly allowlisted.

### Language

The language defines source syntax, types, data layout, and ABI. It must not know about cameras, sprites, tilemaps, or controller semantics.

Language work belongs here:

- Fixed-width primitives: `u8`, `i8`, `u16`, `i16`, `bool`.
- Internal addressability and ABI policy for static data, ROM data, RAM data, and target intrinsics. Public gameplay source should use SDK/resource facades instead of `ptr<T>`, raw buffers, or hardware addresses.
- `struct`, `enum`, fixed-size arrays, constants, casts, operators, and structured control flow. The current cartridge path already has the first zero-cost slice of type aliases, top-level and block-local constants with optional type annotations, decimal/hex/binary integer literal spellings with `_` separators and width suffixes, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, enums, local structs with named and shorthand initializer lists, fixed-size local arrays of scalar values or mixed-width structs, byte-array initializer lists and initializer-inferred lengths, per-element struct-array initializer lists, constant or runtime indices, struct-array field access such as `actors[i].x`, explicit casts to scalar local types, byte and direct 16-bit arithmetic/compare/assignment, bitwise compound assignment, statement-only `++`/`--`, half-open range membership expressions, `if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, post-test `do while`, `while (true)` infinite loops, short-circuit logical conditions and byte-backed 0/1 logical value expressions including unary `!`, byte-backed conditional value expressions, inline statement helpers, inline single-return expression helpers, expression-bodied helpers, named arguments and default parameter values for inline helpers, counted `for` loops, half-open range `for` loops, and `break`/`continue`; the broader shared ABI/layout work remains outside this slice.
- Functions, parameters, returns, calling convention, and attributes.
- Memory placement attributes such as `[section]`, `[bank]`, `[zeropage]`, or `[align]`.
- Target attributes such as `[target("gb")]` or `[intrinsic]`.
- Namespaces or modules if needed to separate SDK and intrinsics.
- Zero-cost high-level ergonomics such as SDK namespaced dot calls, struct receiver methods, immutable `let` locals, switch expressions, static pipeline syntax, purity/inline contracts, and restricted class syntax, provided they lower to direct calls, branches, local storage, static helper calls, or constants. These features must not require heap allocation, delegates, closure objects, boxing, virtual dispatch, runtime interface tables, or hidden object identity.

### Portable 2D SDK

The portable SDK defines game-facing concepts that can be lowered to multiple machines when the target has enough capability.

SDK work belongs here:

- Frame boundary: wait for frame, begin/end frame if needed.
- Tick-based input: poll, down, just pressed, just released, hold ticks.
- Logical resources: tilesets, maps, sprite sheets, palettes, animations, collision flags.
- Tile/world API: read tile id and tile flags from world data.
- Camera API: set viewport position, apply scroll, stream map columns/rows.
- Logical sprites: variable logical sizes, metasprite lowering, flip, palette slot, animation frame.
- Collision queries: tile flags and AABB checks.
- Optional HUD API with capability-gated modes.

### Target Intrinsics

Intrinsics expose machine-specific operations and registers. They are valid, but they are not portable SDK APIs.

Intrinsic work belongs here:

- Game Boy PPU registers, `SCX`, `SCY`, `JOYP`, OAM, DMA, palette registers, LCD modes.
- NES PPU registers, NMI, OAM DMA, attribute table details, sprite size modes, mapper/banking details.
- Raw tilemap writes, raw sprite/OAM writes, hardware flags, and scanline timing.
- Low-level routines needed by SDK lowering but not promised as portable calls.

## Current API Classification

| API | Layer | Notes |
| --- | --- | --- |
| `WorldMap2D` | Portable SDK resource | Dimensions plus per-tile collision `WorldTileFlags`. Holds no target tile numbers. |
| `WorldTileGrid` | Target render data | Already-lowered per-target background tile numbers, produced and consumed per target. |
| `WorldTileFlags` | Portable SDK resource | Defines `Empty`, `Solid`, `Hazard`, and `Platform`. |
| `World.Column(...)` | Portable SDK resource/setup candidate | Current source authoring call for one world column of tile ids. |
| `World.Flags(...)` | Portable SDK resource/setup candidate | Current source authoring call for one world column of collision flags. |
| `World.Map(...)` | Portable SDK resource/setup candidate | Current Game Boy call builds the initial visible tilemap from declared world columns. |
| `GameBoyRomCompiler.CollectSdkOperations(...)` | Compiler architecture boundary | Observes portable `Sdk2DOperation` records before Game Boy lowering. |
| `Video.WaitVBlank()` | Portable SDK candidate | Rename or alias to frame terminology later. |
| `Input.Poll()` | Portable SDK | Current tick boundary. |
| `Input.IsDown(...)` | Portable SDK | Good shared input semantics. |
| `Input.WasPressed(...)` | Portable SDK | Good shared input semantics. |
| `Input.WasReleased(...)` | Portable SDK | Good shared input semantics. |
| `Input.HoldTicks(...)` | Portable SDK | Available duration-count seam; the current SMB3-style runner jump uses `Input.IsDown` plus a source-owned signed-velocity threshold and no jump cut. |
| `Button.*` arguments | Portable SDK | Input helpers accept enum members; bare lowercase button names are no longer public source. |
| `Sprite.Asset(...)` | Portable SDK candidate | Needs target-neutral asset contract and palette slots. |
| `Sprite.Draw(...)` | Portable SDK | Game Boy and NES lower it through compile-time-operand target intrinsics supplied by the source package. |
| `Sprite.Width(...)` | Portable SDK | Source-package helper over a compile-time `sprite_width` target intrinsic. Add `sprite_height(...)`. |
| `sprite_set(...)` | Target intrinsic/transitional | Raw hardware sprite write. |
| `scroll_set(...)` | Target intrinsic/transitional | Raw scroll register concept. Portable API should be camera based. |
| `Camera.Init(...)` | Portable SDK | Source-package helper over the `camera_init` target intrinsic. |
| `Camera.SetPosition(...)` | Portable SDK camera | Position-based camera API; Game Boy supports X and Y within the declared write budget, while NES supports four-screen X/Y movement with horizontal column streaming, vertical row streaming, and staggered diagonal edge updates. |
| `Camera.Apply()` | Portable SDK candidate | Valid concept, but should apply SDK camera state. |
| `camera_move_right()` | Transitional SDK helper | Replace with `Camera.SetPosition(x, y)`. |
| `camera_move_left()` | Transitional SDK helper | Replace with `Camera.SetPosition(x, y)`. |
| `camera_tile_column_at(...)` | Transitional SDK helper | Move collision and map reads to world coordinates. |
| `camera_span_tile_at(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_tile(...)` | Transitional SDK helper | Replace with world collision/tile flag API. |
| `camera_span_has_flags(...)` | Transitional SDK helper | Legacy camera-span collision bridge. |
| `Camera.AabbTiles(...)` | Portable SDK capability-gated query | Camera-relative AABB bridge for fixed-screen actors and projected world-space actors on long maps; requires target support for `CameraRelativeAabb`. |
| `Camera.AabbHitTop(...)` | Portable SDK capability-gated query | Camera-relative AABB tile-hit bridge. Returns an aligned world-pixel `i16` top or `-1`/`0xFFFF` for no hit; byte destinations remain compatible only for active worlds up to 32 hardware rows. Requires target support for `CameraRelativeAabbHitTop`. |
| `World.Column(...)` | Portable SDK resource/setup | Compact source authoring for one world column; use `World.Load(...)` for editable Tiled maps. |
| `map_tile_at(...)` | Portable SDK candidate | Reads generated world tile-id rows. |
| `map_flags_at(...)` | Portable SDK candidate | Reads generated world flag rows. |
| `map_stream_column(...)` | Target intrinsic/transitional | SDK camera should own streaming. |
| `tilemap_set(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill(...)` | Target intrinsic/transitional | Useful setup primitive, not a full portable map contract. |
| `tilemap_fill_column(...)` | Target intrinsic/transitional | Camera streaming implementation detail. |
| `Palette.Set(...)` | Target intrinsic/transitional | Portable SDK should use logical palette slots. |
| `ObjectPalette.Set(...)` | Game Boy intrinsic/transitional | Exposes GB object palette model. |
| `palette_background(...)` | Portable SDK capability-gated declaration | Declares a logical background palette slot with four target color values. |
| `palette_sprite(...)` | Portable SDK capability-gated declaration | Declares a logical sprite palette slot with four target color values. |

## Capability Model

Every portable 2D API must be checked against the target descriptor before it is lowered.

Target descriptors should start with:

```text
Target2DCapabilities:
  name
  screenPixels
  screenTiles
  tileSize
  backgroundBufferTiles
  scrollAxes
  supportsFineScrollX
  supportsFineScrollY
  maxBackgroundTileWritesPerFrame
  maxAttributeWritesPerFrame
  staggerCameraMovementStreams
  spriteCount
  spriteSizeModes
  maxSpritesPerScanline
  spritePaletteSlots
  backgroundPaletteSlots
  supportsSpriteFlipX
  supportsSpriteFlipY
  supportsWindowHud
  supportsSplitScrollHud
  supportsSpriteHud
```

Capability failures should be explicit compiler errors unless the API call requests a documented fallback.

Example:

```text
Target 'nes' does not support Window HUD. Use SplitScroll, SpriteHud, or disable HUD for this target.
```

Capability checks should use the shared targeting helpers so diagnostics stay stable. `TargetCapabilityChecks.RequireHudMode(...)` is the first reusable check and formats unsupported-feature messages through `TargetCapabilityErrorFormatter`.

## Shared SDK Operation Model

Portable 2D calls should be represented as semantic operations before target lowering. The first shared model lives under `RetroSharp.Core.Sdk`:

- `Sdk2DOperation.WaitFrame`
- `Sdk2DOperation.PollInput`
- `Sdk2DOperation.DrawLogicalSprite`
- `Sdk2DOperation.SetCameraPosition`
- `Sdk2DOperation.ApplyCamera`
- `Sdk2DOperation.StreamMapColumn`
- `Sdk2DOperation.StreamMapRow`
- `Sdk2DOperation.ReadWorldTile`
- `Sdk2DOperation.ReadWorldTileFlags`
- `Sdk2DOperation.CameraAabbTiles`
- `Sdk2DOperation.CameraAabbHitTop`
- `Sdk2DOperation.SetHudTile`

`Sdk2DOperationValidator` validates operations against `Target2DCapabilities` before target-specific lowering. The records carry SDK-level concepts only: no Game Boy addresses, NES registers, emitted opcodes, or backend labels. `SetCameraPosition` charges a background-tile-write budget only on targets that stream background tiles at runtime (`CameraMovementStreamsBackground`): for them horizontal movement can require one streamed column and vertical movement one streamed row of the target's backing background buffer. Diagonal movement must either fit the combined write count or use a target-declared staggered camera stream policy (`StaggersCameraMovementStreams`) where each committed edge fits the frame budget. Targets that fine-scroll within a pre-loaded background buffer set `CameraMovementStreamsBackground = false` and are charged no streaming cost for a camera position set. The explicit streaming operations (`StreamMapColumn`, `StreamMapRow`) require the matching `RuntimeBackgroundStreamingAxes` flag, are per-operation budget-checked, and `ValidateFrameBudget(...)` rejects combined explicit background-stream writes, unsupported sprite size modes, total hardware sprites, and constant-Y sprite scanline counts that exceed one frame's target budget.

`GameBoyRomCompiler.CollectSdkOperations(...)` is the first observable operation-creation boundary. It parses the current Game Boy source subset and returns the portable operations detected before `GameBoyRomBuilder` lowers anything to ROM bytes. The boundary recognizes frame/input, camera, HUD tile, world flag reads, logical sprite draw, and map-column streaming operations; raw or transitional calls such as `sprite_set(...)`, `scroll_set(...)`, direction-specific camera helpers, and raw tilemap writes remain on the direct target path until later roadmap tasks move them deliberately.

The collector itself is target-neutral and lives in a dedicated SDK-frontend assembly, `RetroSharp.Sdk.Frontend` (namespace `RetroSharp.Sdk`): `Sdk2DOperationCollector` (with `SdkCallReader` for argument parsing) walks the parsed main block and inlined user functions for any target. It references the parser and `Core` but is **not** part of the language assembly, so SDK call-name knowledge does not live in the language front-end. Both Game Boy and NES run this one collector and then validate the resulting operations through `Sdk2DOperationValidator` against their own `Target2DCapabilities` before lowering, so the portable boundary is no longer Game Boy-only. The same collector also exposes `CollectFrameBudgets(...)`, a control-flow-aware budget pass that treats branches as alternatives and frame waits/input polls as frame boundaries. Targets supply resolved metasprite geometry for `Sprite.Draw(...)`, allowing total hardware sprite checks and per-scanline checks when Y is constant; runtime Y positions remain outside static scanline proof.

Each target has one stateful lowerer that maps an `Sdk2DOperation` to substantive target emission: `GameBoySdkOperationLowerer` and `NesSdkOperationLowerer`. Their feature partials own frame/input, logical-sprite, camera/streaming, and collision emission directly. The runtime compilers route migrated statement and value calls through the collected operation stream and expose only small concrete operand/storage contexts, so an operation drives emission instead of being reconstructed from the AST. Raw and transitional target calls may reuse lowerer-owned internal helpers without becoming new portable operations. Architecture guards enforce this direction from compiled operation-entry symbols and IL call edges rather than feature-file names or private emitter names; a lowerer-to-runtime backedge is an ownership failure.

Byte facts use `SdkByteExpression` and logical camera/world operands use `SdkWordExpression`; both retain the `Constant | Variable` shape. A variable carries a typed `SdkStorageLocation` that targets resolve only at the backend boundary. `Camera.AabbTiles` carries byte screen X plus word world Y. `Camera.AabbHitTop` carries the same AABB shape and returns the complete aligned world top, or `0xFFFF` for no hit. Game Boy `HL` and NES `A:X` materialize every `I16` intrinsic result; screen-relative hit-top remains byte-range but zero-extends to a complete word.

The IR stays at the immediate-value-or-storage-location level without gaining general source syntax trees. Both targets retain a flattened operation list for capability validation. During runtime emission, Game Boy consumes a reachability-aligned `Sdk2DProgram` with separate main and named-subroutine streams, while NES consumes its reachability-aligned flat `SdkOperationStream`. Target-owned stream readers handle migrated statement and value calls, and both targets route camera-AABB value operations through the same lowerer instance used for statement operations. The stream readers fail if a source call and the next collected operation disagree, or if collected operations remain after emission.

The SDK-as-library slice is now in place. Project manifests load
`RetroSharp.Portable2D` through `libraries`, while standalone source can declare
`import RetroSharp.Portable2D;` as the explicit source-level form. Unknown imports
fail compilation instead of being ignored. Hosts compile without the SDK unless
it is imported or supplied as a host/project library, and can provide a custom
`SdkLibraryRegistry` so other import paths inject source-level SDK
libraries. `SdkLibraryRegistry.FromDirectories(...)`, the CLI `--lib-path <path>`
option, and project-manifest `libraryPaths`/`libraries` now provide the first
local-package MVP: each package directory has a `retrosharp-library.json`
manifest with `import`, `sources`, optional `targets`, and optional physical
namespace fields; project `libraryPaths` discovers packages while project
`libraries` names the import paths injected through the same registry path as
`RetroSharp.Portable2D`. A `--lib-path` can also point at a directory whose
direct children are package directories. This MVP is deliberately source-only:
no version solving, package feed, transitive dependency graph, binary library
ABI, per-package asset root, or target-backend plugin model is promised yet.
Project and library manifests can also enable `namespaceMode: "physical"` with a
`rootNamespace` and `sourceRoot`; this derives compile-time namespaces from
source folders and rewrites path-qualified type names, top-level function calls,
and static references to unique internal symbols before target lowering, without
runtime metadata or dispatch. Files can use C#-style `using Root.Player;`
directives to open physical namespaces for unqualified type names, top-level
function calls, and static references; code-file `using` is name resolution,
while library loading belongs in manifests or the explicit source-level `import`
transition path. Each cartridge target exposes a
declarative `TargetIntrinsicCatalog` instead
of a one-off intrinsic switch; Game
Boy and NES currently catalog `video_init`, `video_present`, `wait_frame`, the
`wait_vblank` alias, `poll_input`, button predicates, `audio_init`,
`audio_update`, `music_play`, `music_stop`, `sfx_play`, `camera_init`,
`camera_set_position`, `camera_apply`, `sprite_draw`, `sprite_width`,
`animation_frame`, and the camera AABB intrinsics (Game Boy additionally
catalogs `world_tile_flags_at` and `world_tile_flags_for_world`).
`RetroSharp.Sdk.Frontend` resolves imported
SDK libraries through the registry and supplies target-selected library source
for explicitly imported target compilations. The built-in
`RetroSharp.Portable2D` library is the manifest-backed source package under
`sdk/RetroSharp.Portable2D`; it defines
`Video`, `Input`, `Audio`, `Camera`, `Sprite`, `World`, `Music`, and `Sfx` helpers whose `Video.WaitVBlank()`,
`Input.Poll()`, `Audio.Update()`, `Camera.Init(...)`, `Camera.SetPosition(x, y)`,
`Camera.Apply()`, `Sprite.Width(...)`, `Sprite.Draw(...)`, `Animation.Frame(...)`, and
catalog-gated helpers such as Game Boy `Camera.AabbTiles(...)` /
`Camera.AabbHitTop(...)` call `[intrinsic(...)] extern` declarations, and those helpers
emit the same bytes as the previous SDK operation path. The `audio_update` intrinsic
is collected by the separate `SdkAudioOperationCollector` (Game Boy lowers it from the
audio operation stream, NES emits it inline), so the shared `Sdk2DOperation` collectors
consume but ignore it. The `camera_set_position`/`camera_apply` intrinsics route through
the existing `SetCameraPosition`/`ApplyCamera` collection and emission, so their scroll-axis
inference, capability checks, and frame-budget accounting are unchanged. Actor-framework expansion
now emits calls to imported target-intrinsic extern functions for sprite draw,
animation frame, and screen-space camera AABB queries, so generated actor code no
longer depends on public SDK names being hard-coded in the compiler.
`TargetProgramSelector` filters
`[target("gb")]` / `[target("nes")]` function variants before constant folding
or function indexing, so a portable helper can name one target-specific extern
and let the active target select the matching declaration.
#200 makes public SDK facades package-only at the public-name layer. PascalCase
public facades such as `Video.WaitVBlank`, `Input.Poll`, `Audio.Init`,
`Audio.Update`, `Camera.SetPosition`, `Camera.Apply`, `Sprite.Draw`, and
`Camera.AabbTiles` must be supplied by source packages; flat target-intrinsic
names are implementation IDs rather than public source calls.
SDKLIB-5 applies the same public-name rule to resource declarations:
`Sprite.Asset`, `World.Load`, `Music.Asset`, `Sfx.Asset`, `Palette.Background`, `Palette.Sprite`,
and `Animation.Clip` are source-package methods annotated with `[resource(...)]`.
The frontend resolves that metadata to a generic resource declaration descriptor
instead of making targets key off the public facade name. Flat declaration calls
are no longer public compatibility aliases; target-specific asset import, target
variant resolution, palette validation, and clip validation stay in the existing
GB/NES resource pipelines behind the package metadata.

The library can also carry **capability-gated, value-returning** members. Game Boy
catalogs a `world_tile_flags_at` intrinsic and exposes `World.TileFlagsAt(x, y)` — a
two-argument query that returns the tile flags as a value, lowering byte-identically
to the existing `Sdk2DOperation.ReadWorldTileFlags` path. The package marks that
helper `[target("gb")]`, so NES target selection removes it before function
indexing and intrinsic resolution. This proves the pattern extends from void leaf calls to argument-taking,
value-returning queries (parameterized `inline` helpers substitute their arguments
into the operation operands without introducing temporaries, so the bytes match).

SAL-8.2 adds the missing compile-time operand role mechanism to `TargetIntrinsicDescriptor`
without changing parser, AST, ABI, or classic IR. SDKLIB-3 broadens that descriptor into the
complete target-intrinsic contract: intrinsic id, runtime arity, return kind, compile-time
operand roles, and required target capabilities. Descriptors can now mark source call slots
as `AssetRef`, `ConstPaletteSlot`, `EnumFlags`, or `WorldId`, and the SDK/frontend resolver
separates those compile-time operands from runtime operands before operation collection while
validating extern return types against the descriptor. The
minimal proof is a Game Boy `world_tile_flags_for_world` intrinsic whose `WorldId` slot lowers
byte-identically to `World.TileFlagsAt(x, y)` for `"default"` while rejecting runtime locals
in that slot.

SAL-8.3 applies that mechanism to Game Boy `Sprite.Draw`, and SAL-8.4 applies the same pattern
to NES: the `RetroSharp.Portable2D` helper calls a target intrinsic `[intrinsic("sprite_draw")]`
extern, each target descriptor marks the asset id as `AssetRef` and the palette slot as
`ConstPaletteSlot`, and the collector turns the resolved call back into
`Sdk2DOperation.DrawLogicalSprite`. This keeps metasprite resolution, capability validation,
frame-budget validation, and emission aligned with the common sprite draw operation on both
targets.

SAL-8.5 applies the descriptor-role form to Game Boy `Camera.AabbTiles` and
`Camera.AabbHitTop`. The injected camera helpers pass a hidden `"default"` `WorldId`, keep
`screenX`, `worldY`, `width`, and `height` in the substituted call expression, and mark the
`flags` slot as `EnumFlags`. The collector resolves the extern call back into the existing
`Sdk2DOperation.CameraAabbTiles` / `CameraAabbHitTop`, preserving `SdkAabbExtent` parsing
(including `Sprite.Width(...)`), capability checks, byte identity, and the `255` no-hit
contract.

SAL-8.6 applies the same descriptor-role form to NES `Camera.AabbTiles` and
`Camera.AabbHitTop`, closing the last Game Boy/NES asymmetry for camera-relative AABB
collision. `NesTarget.Intrinsics` catalogs `camera_aabb_tiles` and `camera_aabb_hit_top`
with the same `WorldId`/`EnumFlags` slots, so `RetroSharp.Portable2D` declares the same
`Camera.AabbTiles` / `Camera.AabbHitTop` helpers for NES. The NES value-call path resolves the
extern intrinsic and re-derives the same operation shape used by camera AABB lowering, so
`Golden_collision_aabb_emission_is_pinned_nes` stays byte-identical.

SAL-8.9 extends the same descriptor-role form to screen-space camera collision on both targets:
Game Boy and NES catalog `camera_screen_aabb_tiles` and `camera_screen_aabb_hit_top` with the
same hidden `WorldId` and `EnumFlags` slots, `RetroSharp.Portable2D` declares `Camera.ScreenAabbTiles`
/ `Camera.ScreenAabbHitTop` helpers, and the collector/emitter re-derive the same
`Sdk2DOperation.CameraScreenAabbTiles` / `CameraScreenAabbHitTop` as the common
screen-space camera collision operation. All four camera-relative collision
queries now reach their operations through compile-time-operand intrinsics on both targets;
the actor framework's generated `Camera.ScreenAabb*` calls stay byte-identical.

The migration boundary remains deliberate, and the SAL-6 feasibility spike (epic
#139) refined it with evidence rather than assumption. Wrapping the heavy calls in
ordinary parameterized `inline` helpers is **byte-identical** for `Camera.SetPosition()`,
`Camera.Apply()`, and `Sprite.Draw()` (regression tests
`Inline_helper_wrapping_camera_set_position_is_byte_identical` and
`Inline_helper_wrapping_sprite_draw_and_camera_apply_is_byte_identical`). So the
inline/operand mechanics are not the blocker: the collected operation stream is identical
whether a call arrives directly or through an inlined helper, which means the cross-call
streaming/frame-budget state is preserved.

The remaining friction is at the **extern-intrinsic boundary**, not the language:
- `Camera.Init()` / `Camera.SetPosition()` / `Camera.Apply()` carry only fixed `i16`/void operands,
  so they are package helpers over target intrinsics: both targets catalog `camera_init`
  (arity 3), `camera_set_position` (arity 2), and `camera_apply` (arity 0). `RetroSharp.Portable2D`
  declares the `Camera` static methods, and the collector/emitter route them to the existing
  camera initialization, position, and apply emission paths.
- `Sprite.Draw()` mixes **compile-time** operands (the asset id, the constant palette slot)
  with runtime ones (X/Y/frame/flipX). Game Boy and NES now use the compile-time-operand
  descriptor form, so the public `Sprite.Draw(...)` helper lives in the source package
  while still collecting to the same capability-checked `Sdk2DOperation`.
- Internal streaming (`StreamMapColumn`/`StreamMapRow`) stays compiler-emitted. Camera-relative
  collision still collects to SDK operations, but Game Boy and NES public `Camera.AabbTiles`,
  `Camera.AabbHitTop`, `Camera.ScreenAabbTiles`, and `Camera.ScreenAabbHitTop` now reach those
  operations through compile-time-operand intrinsics.

Net decision: the library pattern now covers frame/input/audio leaf calls, camera initialization,
BGM control
(`Music.Play` / `Music.Stop`), one-shot SFX playback (`Sfx.Play`), a capability-gated value query (`World.TileFlagsAt`), the
camera position/apply pair, `Sprite.Width`, `Sprite.Draw`, `Animation.Frame` on Game Boy and NES,
plus all four Game Boy and NES camera-relative AABB collision queries (world-Y and screen-space forms).
Streaming internals and non-migrated target-specific collision forms remain compiler-recognized
until their compile-time-operand intrinsic migrations are proven. Not everything must become a library. The SAL-8 design note
([`docs/CompileTimeOperandIntrinsics.md`](CompileTimeOperandIntrinsics.md)) chooses the narrow
descriptor-role form for those future intrinsics and records the byte-identity goldens that
later SAL-8 slices must preserve.

## Compiler-Owned SDK Operation Inventory

Last reviewed for #253 on 2026-07-08. This inventory lists internal compiler
operation records and target-intrinsic operations, not public source-package
facade names. Public APIs still live in `sdk/RetroSharp.Portable2D`; the rows
below explain which compiler-owned paths those helpers currently rely on.

SDK plugin descriptors are intentionally outside this inventory. The #258 static
plugin path registers namespaced `SdkPluginOperationDescriptor` values through
`SdkPluginRegistry` and exposes them to a target through
`TargetIntrinsicCatalog.WithSdkPlugins(...)`. A plugin-owned feature should add a
namespaced descriptor and target hook, not a new `Sdk2DOperation`,
`SdkAudioOperation`, or `TargetIntrinsicOperation` entry, unless it deliberately
graduates into built-in SDK semantics. The first proof hook is intentionally
small: statement operations receive a `SdkPluginTargetLoweringContext` with
validated operand metadata and a minimal target byte emitter. Targets grant
plugin capabilities per hook (`ProvidedCapabilities`) and are host-registered by
the CLI through `--sdk-plugin <id>` or a manifest `plugins` array; the reference
descriptor is `RetroSharp.Sdk.Plugins.Platformer2D.Platformer2DPlugin`.

### SDK 2D Operations

| Operation | Classification | Current path and next decision |
| --- | --- | --- |
| `Sdk2DOperation.WaitFrame` | Stable SDK operation | Collected from package helpers over `wait_frame` / `wait_vblank` and lowered per target. Keep as a shared frame-boundary operation. |
| `Sdk2DOperation.PollInput` | Stable SDK operation | Collected from `poll_input`; keep as the tick/input boundary operation. |
| `Sdk2DOperation.DrawLogicalSprite` | Stable SDK operation over target intrinsics | Public `Sprite.Draw(...)` resolves through compile-time `AssetRef` and `ConstPaletteSlot` intrinsic operands, then collects to this shared operation for budget validation and target lowering. |
| `Sdk2DOperation.SetCameraPosition` | Stable SDK operation | Public `Camera.SetPosition(x, y)` is package-backed over `camera_set_position`; keep scroll-axis inference and frame-budget accounting here. |
| `Sdk2DOperation.ApplyCamera` | Stable SDK operation | Public `Camera.Apply()` is package-backed over `camera_apply`; keep as the camera commit boundary. |
| `Sdk2DOperation.StreamMapColumn` | Internal compiler-emitted operation | Camera/runtime streaming uses this after capability and write-budget checks. Do not expose it as a portable public API until a target-neutral streaming contract exists. |
| `Sdk2DOperation.StreamMapRow` | Internal compiler-emitted operation | Same policy as `StreamMapColumn`; row streaming stays compiler-owned while camera movement owns map exposure. |
| `Sdk2DOperation.ReadWorldTile` | Migration candidate, not public-stable | Represents tile-id reads, but the public SDK should prefer collision flags or a named tile-id contract before exposing it broadly. Keep compiler-owned until `World.TileAt`/tile-id semantics are designed. |
| `Sdk2DOperation.ReadWorldTileFlags` | Stable capability-gated operation where cataloged | Game Boy `World.TileFlagsAt(x, y)` reaches this path through `world_tile_flags_at` / `world_tile_flags_for_world`. NES currently lacks the world-tile-flag capability and target-selected helper. |
| `Sdk2DOperation.CameraAabbTiles` | Platformer-shaped SDK bridge over target intrinsics | Public camera-relative collision bridge; `WorldId` and `EnumFlags` are compile-time intrinsic operands, with capability checks before lowering. Keep as a compatibility bridge in Portable2D now; move toward a future platformer/plugin boundary only through a non-breaking migration issue. |
| `Sdk2DOperation.CameraAabbHitTop` | Platformer-shaped SDK bridge over target intrinsics | Same bridge policy as `CameraAabbTiles`; returns a complete word with `-1` as no hit. Keep with the current bridge until a plugin boundary can own platformer collision helpers. |
| `Sdk2DOperation.CameraScreenAabbTiles` | Platformer-shaped SDK bridge over target intrinsics | Screen-space actor collision bridge for generated actor code; keep capability-gated and byte-identity tested. Do not replace this with actor-specific intrinsics. |
| `Sdk2DOperation.CameraScreenAabbHitTop` | Platformer-shaped SDK bridge over target intrinsics | Screen-space hit-top bridge; keep alongside `CameraScreenAabbTiles` until a platformer/plugin package can provide the helper contract. |
| `Sdk2DOperation.SetHudTile` | Next migration candidate, keep compiler-owned for now | HUD modes are still optional and capability-sensitive, and NES declares no HUD mode. Next agent-sized action: introduce a `hud_set_tile` target-intrinsic descriptor for the supported Game Boy HUD modes, route package HUD helpers through it, and prove the existing `SetHudTile` bytes or emitted operation stream are unchanged. Do not add broader HUD concepts in that slice. |

### SDK Audio Operations

| Operation | Classification | Current path and next decision |
| --- | --- | --- |
| `SdkAudioOperation.InitializeAudio` | Stable audio SDK operation | Public `Audio.Init()` resolves through `audio_init`, is collected by the audio operation stream where needed, and validates against target audio capabilities. |
| `SdkAudioOperation.PlayMusic` | Stable audio SDK operation over target intrinsics | `Music.Play(...)` uses compile-time `AssetRef` and remains a semantic BGM operation, not a 2D operation. |
| `SdkAudioOperation.PlaySoundEffect` | Stable audio SDK operation over target intrinsics | `Sfx.Play(...)` uses compile-time `AssetRef` and stays separate from BGM state. |
| `SdkAudioOperation.UpdateAudio` | Stable audio SDK operation | `Audio.Update()` uses `audio_update`; Game Boy consumes it from the audio stream, while NES may emit inline at the target boundary. |
| `SdkAudioOperation.StopMusic` | Stable audio SDK operation | `Music.Stop()` uses `music_stop`; keep as an audio operation rather than folding it into generic 2D SDK state. |

### Target Intrinsic Operations

Target intrinsic descriptors are the sanctioned escape hatch for target-backed
package helpers. A new `TargetIntrinsicOperation.*` member is compiler-owned
surface area: it needs a target catalog entry, capability requirements when
applicable, resolver coverage for compile-time operands, target lowering, and a
matching docs update.

The catalog taxonomy is documentation-only today; it does not remove aliases or
rename ids. Use it to decide where new work belongs:

- `core-runtime`: low-level lifecycle, frame, input, and audio leaves that most
  cartridge targets can reasonably implement, sometimes behind audio
  capabilities.
- `portable-2d`: built-in 2D SDK concepts that stay capability-checked in the
  shared compiler model: camera state, logical sprites, animation data, and
  world flags.
- `platformer/plugin`: genre-shaped helpers that are useful enough to keep as
  current Portable2D compatibility bridges, but should not be forced into the
  core forever. A future plugin boundary can own these through a separate,
  non-breaking migration. New platformer-shaped experiments should prefer
  `SdkPluginOperationDescriptor` plus explicit target hooks over growing this
  enum.
- `target-specific`: raw hardware or target-only escape hatches. The current
  GB/NES `TargetIntrinsicOperation` catalog has no preferred raw-hardware id in
  this bucket; legacy flat calls such as `sprite_set(...)`, `scroll_set(...)`,
  `tilemap_set(...)`, `tilemap_fill(...)`, `tilemap_fill_column(...)`,
  `Palette.Set(...)`, and `ObjectPalette.Set(...)` remain target-specific or
  transitional outside the catalog.
- `compat/deprecated`: ids kept for source/package compatibility. New samples
  and docs should prefer the replacement id, but removals require a separate
  migration issue.

Do not introduce `Actors.*`, `Enemies.*`, pool, spawn, behavior, projectile, or
effect-specific target intrinsics in this catalog. Those frameworks are
source-to-source sugar over fixed storage and should keep lowering through the
portable-2d and platformer/plugin bridge intrinsics already listed below.

| Bucket | Operation | Catalog id(s) | Targets | Current status |
| --- | --- | --- | --- | --- |
| `core-runtime` | `TargetIntrinsicOperation.InitializeVideo` | `video_init` | GB, NES | Target setup leaf for package `Video.Init()`. Keep narrow unless a broader portable video lifecycle is designed. |
| `core-runtime` | `TargetIntrinsicOperation.PresentVideo` | `video_present` | GB, NES | Target presentation leaf; not currently a public SDK lifecycle promise beyond package helper use. |
| `core-runtime` | `TargetIntrinsicOperation.WaitFrame` | `wait_frame` | GB, NES | Preferred portable frame-boundary primitive. Feeds `Sdk2DOperation.WaitFrame` when collected. |
| `compat/deprecated` | `TargetIntrinsicOperation.WaitFrame` | `wait_vblank` | GB, NES | Compatibility alias for the same wait-frame operation. Keep non-breaking; prefer `wait_frame` for new target helpers and docs. |
| `core-runtime` | `TargetIntrinsicOperation.PollInput` | `poll_input` | GB, NES | Tick/input boundary. Feeds `Sdk2DOperation.PollInput` when collected. |
| `core-runtime` | `TargetIntrinsicOperation.ButtonDown` | `button_down` | GB, NES | Value-returning input predicate under package `Input.*`; not an `Sdk2DOperation` because it is ordinary expression input. |
| `core-runtime` | `TargetIntrinsicOperation.ButtonJustPressed` | `button_just_pressed` | GB, NES | Edge-trigger input predicate with the same policy as `button_down`. |
| `core-runtime` | `TargetIntrinsicOperation.ButtonJustReleased` | `button_just_released` | GB, NES | Release-edge input predicate with the same policy as `button_down`. |
| `core-runtime` | `TargetIntrinsicOperation.ButtonHoldTicks` | `button_hold_ticks` | GB, NES | Held-duration input predicate; returns an `i16` tick count. |
| `core-runtime` | `TargetIntrinsicOperation.InitializeAudio` | `audio_init` | GB, NES | Capability-gated audio runtime setup. Feeds `SdkAudioOperation.InitializeAudio` where the target consumes audio streams. |
| `core-runtime` | `TargetIntrinsicOperation.UpdateAudio` | `audio_update` | GB, NES | Capability-gated audio tick. Feeds `SdkAudioOperation.UpdateAudio` or target inline emission. |
| `core-runtime` | `TargetIntrinsicOperation.PlayMusic` | `music_play` | GB, NES | BGM leaf with compile-time `AssetRef`; feeds `SdkAudioOperation.PlayMusic`. |
| `core-runtime` | `TargetIntrinsicOperation.StopMusic` | `music_stop` | GB, NES | BGM stop leaf; feeds `SdkAudioOperation.StopMusic`. |
| `core-runtime` | `TargetIntrinsicOperation.PlaySoundEffect` | `sfx_play` | GB, NES | One-shot SFX leaf with compile-time `AssetRef`; feeds `SdkAudioOperation.PlaySoundEffect`. |
| `portable-2d` | `TargetIntrinsicOperation.InitializeCamera` | `camera_init` | GB, NES | Camera setup for package `Camera.Init(...)`; target-backed while position/apply semantics stay shared. |
| `portable-2d` | `TargetIntrinsicOperation.SetCameraPosition` | `camera_set_position` | GB, NES | Feeds `Sdk2DOperation.SetCameraPosition`; keep capability and frame-budget logic in the shared operation. |
| `portable-2d` | `TargetIntrinsicOperation.ApplyCamera` | `camera_apply` | GB, NES | Feeds `Sdk2DOperation.ApplyCamera`; keep as the camera commit boundary. |
| `portable-2d` | `TargetIntrinsicOperation.CameraVerticalScrollMax` | `camera_vertical_scroll_max` | GB, NES | Value helper over `Camera.VerticalScrollMax()`; folds to the per-target `worldHeight - screenHeight` scroll bound so callers can clamp their own camera Y and keep sprite/background alignment. |
| `portable-2d` | `TargetIntrinsicOperation.DrawLogicalSprite` | `sprite_draw` | GB, NES | Uses compile-time `AssetRef` and `ConstPaletteSlot`; feeds `Sdk2DOperation.DrawLogicalSprite`. |
| `portable-2d` | `TargetIntrinsicOperation.ReadSpriteWidth` | `sprite_width` | GB, NES | Value helper over `Sprite.Width(...)`; keep out of `Sdk2DOperation` unless sprite geometry reads become a validated frame operation. |
| `portable-2d` | `TargetIntrinsicOperation.ReadAnimationFrame` | `animation_frame` | GB, NES | Value helper over `Animation.Frame(...)`; keep as a source-package helper over target data tables. |
| `portable-2d` | `TargetIntrinsicOperation.ReadWorldTileFlags` | `world_tile_flags_at`, `world_tile_flags_for_world` | GB | Capability-gated world flag read. Other targets must opt in through capabilities and target-selected helpers. |
| `platformer/plugin` | `TargetIntrinsicOperation.CameraAabbTiles` | `camera_aabb_tiles` | GB, NES | Suspect platformer-shaped collision bridge. Keep in Portable2D for compatibility now; move only through a future plugin-boundary migration. |
| `platformer/plugin` | `TargetIntrinsicOperation.CameraAabbHitTop` | `camera_aabb_hit_top` | GB, NES | Same bridge policy as `camera_aabb_tiles`; returns a complete word with `-1` as no hit while landing policy stays in source/framework code. |
| `platformer/plugin` | `TargetIntrinsicOperation.CameraScreenAabbTiles` | `camera_screen_aabb_tiles` | GB, NES | Screen-space actor/projectile collision bridge; useful today, but not a reason to add actor-specific target intrinsics. |
| `platformer/plugin` | `TargetIntrinsicOperation.CameraScreenAabbHitTop` | `camera_screen_aabb_hit_top` | GB, NES | Screen-space hit-top bridge; keep capability-gated until a platformer/plugin package can own the helper contract. |

Resource declarations are deliberately separate from this table:
`Sprite.Asset`, `World.Load`, `Music.Asset`, `Sfx.Asset`,
`Palette.Background`, `Palette.Sprite`, and `Animation.Clip` are package
methods annotated with `[resource(...)]` and resolved through generic resource
descriptors. They do not add runtime SDK operations by themselves.

## Layer Boundary and Golden Rule

`Sdk2DOperation` is an opinionated 2D-game framework model (camera, sprites, tilemap streaming, HUD) used as an intermediate representation on the cartridge path. It is deliberately **isolated** in `RetroSharp.Core.Sdk`. The language frontend (`RetroSharp.Parser`, `RetroSharp.Parser.Model`, and `RetroSharp.SemanticAnalysis`) stays framework-neutral: it must not gain cameras, sprites, scroll, tilemaps, or controller concepts.

Golden rule for anyone extending this area:

1. The language frontend never gains framework concepts. Game-facing concepts live in the SDK layer or in target intrinsics, never in the grammar, AST typing, semantic model, or language ABI.
2. `Sdk2DOperation` must not become a dumping ground. Before adding a new operation, ask whether the feature is genuinely a shared portable primitive or whether it should be a target intrinsic plus a library helper. Genre-specific operations are a warning sign.
3. The operand IR (`SdkByteExpression`) stays at the level of "immediate value" or "storage location". Typed storage descriptors such as `Local`, `Field`, and `IndexedElement` are acceptable because they describe storage, not expression evaluation. Do not add `BinaryOp` or other general expression-tree cases: that would pull source syntax into the portable IR.
4. End-state: the 2D SDK should migrate from compiler-recognized operations toward a library written in the language over per-target intrinsics, so the framework becomes optional and replaceable. Until the language has per-target intrinsics and modules, the operation model is the accepted pragmatic bridge.

Where each piece lives:

- Source syntax and AST: `RetroSharp.Parser` (language assembly, framework-neutral).
- Operation collection from source: `RetroSharp.Sdk.Frontend` (SDK-frontend assembly).
- Portable operation records, validator, capabilities, world/Tiled model: `RetroSharp.Core.Sdk` and `RetroSharp.Core.Targeting`.
- Per-target lowering: `GameBoySdkOperationLowerer` / `NesSdkOperationLowerer`; target runtime compilers own syntax/control-flow traversal and stream consumption, not migrated portable SDK emission.
- Actor Framework lowering: enter through the single `ActorFrameworkLowerer` plan, then follow actor/spawn/projectile/effect domain state and generation symbols; generated program/name facts join through one ordered domain contribution catalog.
- Ownership validation: `RetroSharp.Architecture.Tests` resolves compiled symbols and IL dependency edges; use exact source paths only for an intentionally physical module contract.

## Shared World Map Resource

The first portable world resource lives under `RetroSharp.Core.Sdk`:

- `WorldMap2D`
- `WorldTileFlags`

`WorldMap2D` stores dimensions plus per-tile collision `WorldTileFlags` only — it is the portable collision resource and deliberately carries no background tile numbers. Already-lowered, target-specific background tile numbers live in a separate `WorldTileGrid` (also under `RetroSharp.Core.Sdk`, but explicitly target render data, not a portable promise), which each target's importer produces and that target's rendering path consumes. Keeping the two apart means the portable collision map is not conflated with target render payload.

Interpreting Tiled collision data into portable `WorldTileFlags` is target-neutral and lives in `RetroSharp.Core.Sdk.Tiled.TiledCollisionFlags`. It reads Tiled JSON/XML objectgroups, `retrosharpCollision`/`retrosharpFlags` custom properties, and collision-layer GIDs without any Game Boy or NES specifics, so collision modeling is shared rather than owned by a target backend. The Game Boy Tiled importer still owns the genuinely target-specific work (Game Boy tile quantization, deduplication, single-tilemap flattening) and calls this shared collision interpretation.

The first Game Boy integration is `World.Column(...)`, `World.Flags(...)`, and
`World.Map(width, streamY, height)`. `World.Map(...)` builds a `WorldMap2D`,
derives the initial visible background rows from it, regenerates the streaming
ROM column tables, and generates parallel collision flag tables from the same
resource. `World.Column(...)` and `World.Flags(...)` remain the compact
source-authored path; editable runner-level maps should use `World.Load(...)`.

## Agent Task Contract

Use the task breakdown below as issue-sized implementation units. Before starting any task, the agent should inspect the current code paths because candidate file names are guidance, not a mandate.

See `AgentExecution.md` for the operational workflow, GitHub issue seeding commands, execution waves, and agent roles.

Every implementation task must include:

- Scope: one task id or a small contiguous group of task ids.
- Layer decision: language, portable SDK, or target intrinsic.
- Candidate files: expected code/docs/tests to inspect or edit.
- Verification: exact build, unit test, ROM build, or sample check.
- Compatibility check: whether `samples/runner/runner.retrosharp.json` still builds with its declared source set.
- Documentation check: update this roadmap or target docs if public API changes.

General rules for agents:

- Do not add a new portable SDK call without a capability check path.
- Do not expose raw Game Boy or NES hardware bits through portable samples.
- Do not move gameplay behavior into the language layer.
- Keep transitional APIs working until the roadmap explicitly removes them.
- Prefer adding shared SDK concepts before duplicating behavior in a target builder.
