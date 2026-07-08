# SDK Plugin Boundary

Status: static in-process descriptor boundary implemented for issue #258. No
runtime loader, package feed, or binary plugin ABI is implemented.

RetroSharp already has source-only SDK packages and compiler-owned SDK
semantics. This document defines where a future SDK plugin layer would fit so
new SDK concepts do not leak into the language layer or keep growing the core
operation enums forever. The implemented slice proves the static registry path;
it does not migrate existing `RetroSharp.Portable2D` APIs.

## Layer Decision

Use the smallest extension level that can express the feature:

| Level | Owns | Can provide | Cannot provide |
| --- | --- | --- | --- |
| Source-only library | Package manifest and RetroSharp source files | Public facades, inline helpers, physical namespaces, target-gated source variants, wrappers over known `[intrinsic(...)]` and `[resource(...)]` metadata | New compiler semantics, new resource kinds, new asset importers, new target capabilities, validators, or backend lowering |
| Built-in SDK semantics | `RetroSharp.Sdk.Frontend`, `RetroSharp.Core.Sdk`, and target projects in this repo | `Sdk2DOperation`, `SdkAudioOperation`, `TargetIntrinsicDescriptor`, `SdkResourceDeclarationDescriptor`, resource models, validators, asset pipelines, target capability checks, and GB/NES lowerers | A public package or external ABI; public facade names should still come from source packages |
| SDK plugin | Host-registered static descriptor | Source package files plus namespaced resource/operation descriptors, validators, capability metadata, and explicit per-target lowering hooks | New language syntax, managed object semantics, automatic loader discovery, package-feed resolution, binary ABI, or hidden target runtime services |

The language remains target-neutral in all three levels. A plugin may define
SDK semantics consumed by the SDK frontend, internal SDK model, and target
compilers, but it must not require grammar changes, AST types, classic IR
concepts, heap allocation, GC, RTTI, boxing, delegates, closures, virtual
dispatch, or hidden object identity in RetroSharp source or emitted target code.

## Extension Points

A real SDK plugin needs these extension points. The first static C# API now
covers the descriptor, registry, resource, operation, validator, capability, and
target-lowering-hook pieces; asset importers remain a future extension point:

| Extension point | Purpose | Current built-in analogue |
| --- | --- | --- |
| Source package files and facades | Provide the public source API through the same manifest/source-package path as `RetroSharp.Portable2D`. | `retrosharp-library.json`, `SdkLibraryRegistry`, physical namespace source rewriting |
| Intrinsic and operation descriptors | Declare namespaced operations, runtime arity, return kind, compile-time operand roles, required capabilities, and whether a call is a statement or value fact. | `TargetIntrinsicDescriptor`, `Sdk2DOperation`, `SdkAudioOperation` |
| Resource declaration kinds | Map `[resource("...")]` ids to typed resource declarations with validation and target consumption rules. | `SdkResourceDeclarationDescriptor` and `SdkResourceDeclarationKind` |
| Asset importers | Convert plugin resource declarations and asset roots into target-neutral or target-ready resource models. | Tiled, PNG sprite, palette, animation, music, and SFX import paths. Not implemented in the static v1 proof. |
| Capability declarations | Let targets opt into plugin features and expose limits used by diagnostics. | `Target2DCapabilities`, `TargetAudioCapabilities`, target intrinsic required capabilities |
| Validators | Check resource shape, target support, compile-time operands, operation streams, and cross-operation budgets before lowering. | `Sdk2DOperationValidator`, `SdkAudioOperationValidator`, frame-budget validation |
| Per-target lowering hooks | Let a target that supports the plugin lower a plugin operation or resource without adding a generic core enum member for every plugin concept. The static v1 statement hook receives a minimal target byte emitter. | `GameBoySdkOperationLowerer`, `NesSdkOperationLowerer`, target asset lowerers |
| Compatibility and version metadata | State plugin id, descriptor ABI version, supported compiler range, supported target ids, migration aliases, and non-breaking compatibility policy. | Package `targets`, current docs-only intrinsic taxonomy |

Plugin-owned operations should be namespaced descriptors rather than new
members in `TargetIntrinsicOperation`, `Sdk2DOperation`, or
`SdkAudioOperation` by default. Built-in operations can stay as they are, but a
future plugin proof should show that target lowerers can opt into an operation
descriptor owned by the plugin. That keeps `platformer/plugin`, racing, menu,
or other genre concepts out of the language and out of the central enums unless
they later graduate into built-in semantics deliberately.

## Descriptor Shape

The first ABI shape is descriptor-first and host-registered:

```csharp
SdkPluginDescriptor
  Id: "RetroSharp.Platformer2D"
  Version: "0.1.0"
  RequiredCompilerAbi: "sdk-plugin-static-v1"
  SourcePackage: SdkPluginSourcePackageDescriptor
  ResourceDeclarations: SdkPluginResourceDeclarationDescriptor[]
  Operations: SdkPluginOperationDescriptor[]
  Capabilities: SdkPluginCapabilityDescriptor[]
  Validators: SdkPluginValidatorDescriptor[]
  TargetLoweringHooks: SdkPluginTargetLoweringDescriptor[]
  Compatibility: SdkPluginCompatibilityDescriptor
```

Important constraints:

- The host registers descriptors explicitly when constructing the compiler or
  target compilation pipeline.
- Descriptor ids are namespaced by plugin id, for example
  `RetroSharp.Platformer2D.CameraAabbTiles`.
- A descriptor may point at host-side importer, validator, or lowering hook
  implementations, but those hooks are compiler services, not target runtime
  features.
- Source packages remain the public source surface. The compiler should not
  learn plugin facade names as hard-coded public API strings.
- The host passes `SdkPluginRegistry` explicitly to the target compiler. The
  compiler builds an effective target intrinsic catalog through
  `TargetIntrinsicCatalog.WithSdkPlugins(...)` and an effective source library
  registry through `SdkLibraryRegistry.WithSdkPlugins(...)`.
- Resource ids resolve through `SdkResourceDeclarationRegistry`, so built-in ids
  such as `world_load` stay compatible while plugin ids such as
  `RetroSharp.Platformer2D.CollisionProfile` do not need central table entries.

## First Slice

The first implementation slice after #252 is deliberately small and is now
represented by focused tests:

1. Add a static `SdkPluginDescriptor` model and a host-provided registry.
2. Allow the host to register one plugin descriptor directly in-process; do not
   scan assemblies, load binaries, or read package feeds.
3. Prove one plugin can register:
   - one source package;
   - one resource declaration kind;
   - one validator for that resource or operation;
   - one capability declaration;
   - one per-target lowering hook for a single target.
4. Keep all existing GB/NES output unchanged unless the proof sample opts into
   the plugin.

The current proof is an in-test experimental `RetroSharp.Platformer2D`
descriptor. It supplies a source package with `Platformer.TouchProbe()`, a
namespaced plugin operation descriptor, a validator, a capability id, and a Game
Boy lowering hook that emits target bytes through the static hook emitter. Game
Boy compiles through the hook when the registry is provided; NES fails before
lowering because it does not opt into that hook.

## Platformer2D Example

Issue #253 classifies the camera AABB collision helpers as
`platformer/plugin` bridges. Today they remain compatibility helpers in
`RetroSharp.Portable2D` and rely on compiler-owned `Sdk2DOperation` /
`TargetIntrinsicOperation` entries:

- `Camera.AabbTiles(...)`
- `Camera.AabbHitTop(...)`
- `Camera.ScreenAabbTiles(...)`
- `Camera.ScreenAabbHitTop(...)`

A future `RetroSharp.Platformer2D` plugin could own the next generation of
those helpers:

- Source package: `RetroSharp.Platformer2D` exposes `Platformer.Collision.*`
  facades over plugin operation descriptors.
- Resource kind: optional `platformer_body` or `platformer_collision_profile`
  declarations for named hitbox shapes.
- Asset importer: optional importer for platformer collision metadata, if the
  metadata moves out of Tiled object properties or source constants.
- Capability: `PlatformerAabbCollision` with target limits such as byte-sized
  dimensions, screen-space projection support, and world-page support.
- Validator: rejects hitbox dimensions, flag masks, or projection modes that
  the active target cannot lower.
- Target lowering hook: Game Boy and NES lower the plugin operation to the same
  proven collision routines or to target-specific replacements.

That migration would let platformer-specific collision grow outside
`RetroSharp.Core.Sdk` without adding more `TargetIntrinsicOperation` cases to
core. The current Portable2D helpers should remain until a separate,
non-breaking migration issue provides source compatibility and byte-identity
evidence.

## Relationship To Actor Framework Work

#252 defines the boundary. #251 will decide how to move or isolate
`Actors.*` and `Enemies.*` using that boundary, but #252 does not migrate the
actor framework.

Actor pools, enemy definitions, spawn windows, projectiles, and effects should
not become target intrinsics. They remain source-to-source fixed-storage
lowering unless a future plugin descriptor cleanly owns their metadata,
validation, and target opt-in. Any actor migration must preserve the current
zero-cost shape: fixed arrays, direct branches, generated helpers, no heap, no
virtual dispatch, and no actor-specific backend ABI.

## Non-goals

- No dynamic loader.
- No NuGet, remote feed, transitive dependency graph, or version solving.
- No public binary ABI yet.
- No heap allocation, GC, delegates, closures, RTTI, boxing, virtual dispatch,
  or managed object identity in RetroSharp source or emitted target code.
- No migration of current `RetroSharp.Portable2D` APIs into plugins in #252.
- No changes to Game Boy or NES lowerers for this issue.
- No actor framework migration; that is #251.

## Compatibility Policy

The plugin boundary must be opt-in. Existing projects that load only
`RetroSharp.Portable2D` must keep compiling through the current source package,
resource descriptor, validation, and target lowering paths. A target that does
not register or support a plugin capability should fail before lowering with a
deterministic diagnostic naming the plugin feature and target id.

When a built-in compatibility bridge eventually moves to a plugin, keep the
public source path non-breaking first: provide an aliasing source package,
document the migration, and preserve byte identity for GB/NES samples before
removing any built-in descriptor.
