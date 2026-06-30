# Compile-Time Operand Intrinsics

Status: SAL-8.1 design note and golden-characterization contract.

This note answers the open SAL-8 question from issue #158: how a target intrinsic can carry operands that must be resolved at compile time, such as a sprite asset id, a constant sprite palette slot, enum collision flags, or a world id, while keeping the language layer target-neutral.

## Decision

Compile-time operands are modeled as typed roles on a single `TargetIntrinsicDescriptor` for the operation.

The source declaration still selects an intrinsic with the existing target-intrinsic attributes:

```csharp
[target("gb")]
[intrinsic("sprite_draw")]
extern void __retrosharp_gb_sprite_draw(...);
```

The target catalog owns the compile-time contract for that intrinsic. A future descriptor for `sprite_draw` should record role-bearing source call slots, for example:

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

`TargetIntrinsicResolver` should continue reading `[target]` and `[intrinsic]` from the extern declaration, then resolve the named intrinsic through the active target's `TargetIntrinsicCatalog`. The resolved `TargetIntrinsicDescriptor` is the authoritative source for compile-time operand roles.

The SDK/frontend collector reads call operands using the descriptor:

1. Runtime slots are read as the existing bounded SDK operand forms (`SdkByteExpression`, `bool`-like values, or void parameters).
2. `AssetRef` slots are read as identifiers and resolved to the same asset ids that `ReadDrawLogicalSprite` uses today.
3. `ConstPaletteSlot` and `EnumFlags` slots are read with constant/enum folding and validated at the SDK/target boundary.
4. `WorldId` slots are read as compile-time resource identifiers, defaulting to `"default"` for current world/collision operations.

Per-target lowerers then emit through the same machinery that current `Sdk2DOperation` lowering uses. For `sprite.Draw`, the lowerer still resolves metasprite geometry from target asset metadata. For collision, `camera.AabbTiles` and `camera.AabbHitTop` still preserve their capability checks and the `255` no-hit result contract.

The parser, AST, ABI, and classic `RetroSharp.Generation.Intermediate` IR do not gain sprite, camera, world, asset, generic, or expression-tree concepts. This is SDK/frontend plus target-intrinsic metadata.

## Operation-Specific Guidance

`sprite.Draw` is the central SAL-8 prototype. The descriptor-role form must allow one operation descriptor to cover all assets and palette slots. The legacy `sprite_draw` builtin remains a transitional alias until a later roadmap item removes it.

`camera.AabbTiles` and `camera.AabbHitTop` are allowed to prototype the same mechanism for `EnumFlags` and `WorldId`, but they may record a gap and remain compiler-recognized operations if the composite operands cannot stay byte-identical and zero-cost.

`StreamMapColumn` and `StreamMapRow` stay compiler-emitted. They are effects of camera lowering, not public source calls that should be migrated to source library helpers in SAL-8.

## Golden Characterization

SAL-8.1 pins the current emitted bytes before any lowering changes:

- `Golden_sprite_draw_emission_is_pinned_gb`
- `Golden_collision_aabb_emission_is_pinned_gb`
- `Golden_sprite_draw_emission_is_pinned_nes`
- `Golden_collision_aabb_emission_is_pinned_nes`

These tests compile focused programs and compare full ROM SHA-256 fingerprints. Later SAL-8 slices must keep these goldens byte-identical unless an integrator explicitly accepts a new byte baseline with full target validation.
