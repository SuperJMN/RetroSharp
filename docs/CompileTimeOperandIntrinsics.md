# Compile-Time Operand Intrinsics

Status: SAL-8.2 mechanism implemented; SAL-8.3 and SAL-8.4 migrate Game Boy and NES `sprite.Draw` onto the descriptor-role path; SAL-8.5 migrates Game Boy `camera.AabbTiles` and `camera.AabbHitTop`; SAL-8.6 applies the same descriptor-role form to NES `camera.AabbTiles` and `camera.AabbHitTop`; SAL-8.7 migrates Game Boy and NES `music.Play` / `music.Stop` onto the audio target intrinsics; SAL-8.8 migrates Game Boy and NES `audio.Init` onto the `audio_init` void-leaf target intrinsic (no compile-time operands).

This note answers the open SAL-8 question from issue #158: how a target intrinsic can carry operands that must be resolved at compile time, such as a sprite asset id, a constant sprite palette slot, enum collision flags, or a world id, while keeping the language layer target-neutral.

## Decision

Compile-time operands are modeled as typed roles on a single `TargetIntrinsicDescriptor` for the operation.

The source declaration still selects an intrinsic with the existing target-intrinsic attributes:

```csharp
[target("gb")]
[intrinsic("sprite_draw")]
extern void __retrosharp_gb_sprite_draw(...);
```

The target catalog owns the compile-time contract for that intrinsic. The Game Boy and NES descriptors for `sprite_draw` record role-bearing source call slots:

```csharp
TargetIntrinsicDescriptor.SpriteDraw(
    "sprite_draw",
    runtimeArity: 4,
    compileTimeOperands:
    [
        new(slot: 0, role: AssetRef),
        new(slot: 5, role: ConstPaletteSlot),
    ]);
```

Slot indexes refer to the SDK-shaped source call operands before lowering. The slot role tells the SDK/frontend and target boundary to read that argument as compile-time data instead of lowering it as a runtime `i16`/`bool` parameter. One descriptor covers every sprite asset; there is no descriptor per asset and no generated helper per asset.

The initial role set is deliberately small:

- `AssetRef`: an identifier that resolves to target asset metadata such as sprite metasprite geometry and generated tile data.
- `ConstPaletteSlot`: a compile-time integer validated against the target sprite or background palette-slot capability.
- `EnumFlags`: a compile-time enum or constant bitmask, used for values such as `WorldTileFlags`.
- `WorldId`: a compile-time world resource id. The current world id remains `"default"` until multiple worlds exist.

Do not add a general expression-tree operand role. Existing SDK operands such as `SdkByteExpression`, `SdkAabbExtent`, and `SdkStorageLocation` stay at their current limited "constant or storage descriptor" boundary.

## Rejected Forms

Generic-like syntax such as `sprite.Draw<player>(...)` is rejected. It would introduce template/generic grammar pressure into the language for a target-intrinsic problem.

Extra source attribute arguments such as `[intrinsic("sprite_draw", asset: player)]` are rejected for SAL-8. They bind the compile-time operand to an extern declaration instead of a call slot, which pushes toward per-asset declarations or custom attribute expression parsing.

A new compile-time parameter type or marker in the language is rejected. RetroSharp does not need an `asset` type, type parameter, or new ABI category to preserve zero-cost sprite/collision lowering.

## Resolution Flow

`TargetIntrinsicResolver` continues reading `[target]` and `[intrinsic]` from the extern declaration, then resolves the named intrinsic through the active target's `TargetIntrinsicCatalog`. The resolved `TargetIntrinsicDescriptor` is the authoritative source for compile-time operand roles.

The SDK/frontend collector reads call operands using the descriptor:

1. Runtime slots are read as the existing bounded SDK operand forms (`SdkByteExpression`, `bool`-like values, or void parameters).
2. `AssetRef` slots are read as identifiers and resolved to the same asset ids that `ReadDrawLogicalSprite` uses today.
3. `ConstPaletteSlot` and `EnumFlags` slots are read with constant/enum folding and validated at the SDK/target boundary.
4. `WorldId` slots are read as compile-time resource identifiers. The current proof accepts the existing string-literal form such as `"default"` so it does not require new grammar.

Per-target lowerers then emit through the same machinery that current `Sdk2DOperation` lowering uses. For `sprite.Draw`, the lowerer still resolves metasprite geometry from target asset metadata. For collision, `camera.AabbTiles` and `camera.AabbHitTop` still preserve their capability checks and the `255` no-hit result contract.

The parser, AST, ABI, and classic `RetroSharp.Generation.Intermediate` IR do not gain sprite, camera, world, asset, generic, or expression-tree concepts. This is SDK/frontend plus target-intrinsic metadata.

## Implemented Proof

SAL-8.2 adds the descriptor-role machinery and a minimal non-sprite proof intrinsic on Game Boy:

```csharp
[target("gb")]
[intrinsic("world_tile_flags_for_world")]
extern i16 flags_for_world(i16 world, i16 x, i16 y);
```

The active Game Boy descriptor marks slot `0` as `WorldId` and leaves `x`/`y` as the two runtime operands. `flags_for_world("default", x, y)` lowers byte-identically to `world_tile_flags_at(x, y)`. A local variable in that slot is rejected before lowering, proving the compile-time operand is not turned into a runtime argument or temporary.

This proof is deliberately not a new public portable SDK surface. It exists to validate the descriptor role and resolver path that SAL-8.3/SAL-8.4 use for `sprite.Draw`.

SAL-8.3/SAL-8.4 wire Game Boy and NES `sprite.Draw` through the injected SDK library helper:

```csharp
[target("gb")]
[intrinsic("sprite_draw")]
extern void __retrosharp_gb_sprite_draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX, i16 paletteSlot);

[target("nes")]
[intrinsic("sprite_draw")]
extern void __retrosharp_nes_sprite_draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX, i16 paletteSlot);
```

The descriptor marks `spriteId` as `AssetRef` and `paletteSlot` as `ConstPaletteSlot`; X, Y, frame, and flipX remain runtime operands. The SDK/frontend collector resolves that call to the existing `Sdk2DOperation.DrawLogicalSprite`, so target metasprite geometry, palette-slot capability checks, frame-budget checks, and hardware sprite limits continue to use the same path as the legacy `sprite_draw(...)` builtin. Focused and runner-shaped tests assert byte identity against the legacy spelling on both targets.

SAL-8.5 wires Game Boy `camera.AabbTiles` and `camera.AabbHitTop` through the injected SDK library helper:

```csharp
[target("gb")]
[intrinsic("camera_aabb_tiles")]
extern i16 __retrosharp_gb_camera_aabb_tiles(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

[target("gb")]
[intrinsic("camera_aabb_hit_top")]
extern i16 __retrosharp_gb_camera_aabb_hit_top(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);
```

The injected public helpers pass `"default"` for the hidden `WorldId` slot and forward the public operands. The descriptors mark slot `0` as `WorldId` and slot `5` as `EnumFlags`; the collector still parses `screenX`, `worldY`, `width`, and `height` through the existing SDK readers, including `SdkAabbExtent` support for constants and `Sprite.Width(...)`. The result is the same `Sdk2DOperation.CameraAabbTiles` / `CameraAabbHitTop` stream as the legacy compiler-recognized spelling, preserving byte identity, capability diagnostics, and the `255` no-hit contract.

SAL-8.6 wires NES `camera.AabbTiles` and `camera.AabbHitTop` through the same descriptor-role form:

```csharp
[target("nes")]
[intrinsic("camera_aabb_tiles")]
extern i16 __retrosharp_nes_camera_aabb_tiles(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

[target("nes")]
[intrinsic("camera_aabb_hit_top")]
extern i16 __retrosharp_nes_camera_aabb_hit_top(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);
```

`NesTarget.Intrinsics` catalogs both intrinsics with the same `WorldId`/`EnumFlags` slots, so `SdkLibrarySource` injects the `camera.AabbTiles` / `camera.AabbHitTop` helpers for NES too. The NES value-call path resolves the extern intrinsic and re-derives the same `Sdk2DOperation.CameraAabbTiles` / `CameraAabbHitTop` it already emitted from the legacy `camera_aabb_tiles(...)` / `camera_aabb_hit_top(...)` builtin, which remains a compatibility alias. `camera.ScreenAabbTiles` / `camera.ScreenAabbHitTop` stay on the SDK-module/builtin path and are not migrated by SAL-8.6.

SAL-8.7 wires Game Boy and NES `music.Play` / `music.Stop` through the audio target intrinsics:

```csharp
[target("gb")]
[intrinsic("music_play")]
extern void __retrosharp_gb_music_play(i16 theme);

[target("gb")]
[intrinsic("music_stop")]
extern void __retrosharp_gb_music_stop();
```

`music_play` marks slot `0` as `AssetRef`, so the injected `music.Play(theme)` helper forwards the music asset identifier as a compile-time operand; `music_stop` is a void leaf intrinsic. Both targets catalog the two intrinsics, so `SdkLibrarySource` injects `class music { Play, Stop }` for both. The separate `SdkAudioOperationCollector` resolves the extern calls back into `SdkAudioOperation.PlayMusic` / `SdkAudioOperation.StopMusic`, so BGM asset lookup, banking, and emission stay byte-identical to the legacy `music_play(...)` / `music_stop(...)` builtins, which remain compatibility aliases. `music.Asset(...)` is not a class member and still lowers through the SDK module to `music_asset`.

## Operation-Specific Guidance

`sprite.Draw` is the central SAL-8 prototype. The descriptor-role form uses one operation descriptor for all assets and palette slots. The legacy `sprite_draw` builtin remains a transitional alias until a later roadmap item removes it.

Game Boy and NES `camera.AabbTiles` and `camera.AabbHitTop` use the same mechanism for the hidden `WorldId` and `EnumFlags` slots while keeping `SdkAabbExtent` as an SDK/frontend operand shape. Other target-specific collision forms may still record a gap and remain compiler-recognized operations if the composite operands cannot stay byte-identical and zero-cost.

`StreamMapColumn` and `StreamMapRow` stay compiler-emitted. They are effects of camera lowering, not public source calls that should be migrated to source library helpers in SAL-8.

## Golden Characterization

SAL-8.1 pins the current emitted bytes before any lowering changes:

- `Golden_sprite_draw_emission_is_pinned_gb`
- `Golden_collision_aabb_emission_is_pinned_gb`
- `Golden_sprite_draw_emission_is_pinned_nes`
- `Golden_collision_aabb_emission_is_pinned_nes`

These tests compile focused programs and compare full ROM SHA-256 fingerprints. Later SAL-8 slices must keep these goldens byte-identical unless an integrator explicitly accepts a new byte baseline with full target validation.
