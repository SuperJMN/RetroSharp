# RetroSharp SDK Architecture

For the concise deep-module owner map, common change routes, focused test seams,
and reproducible CodeGraph probes, start with
[`AiNavigableArchitecture.md`](AiNavigableArchitecture.md).

This document names the current SDK boundary so the source-only package model
does not get confused with a compiler plugin system.

## Pieces

| Piece | Path | Role |
| --- | --- | --- |
| Public portable package | `sdk/RetroSharp.Portable2D` | The built-in source-only package loaded by manifests, `import RetroSharp.Portable2D;`, and the same package resolver used for external libraries. |
| SDK frontend | `src/RetroSharp.Sdk.Frontend` | Import resolution, source package loading, physical namespace rewriting, public facade lowering, resource declaration discovery, and collection of SDK operations from parsed source. |
| Internal SDK model | `src/RetroSharp.Core/Sdk` | The internal compiler model shared by targets: `Sdk2DOperation`, `SdkAudioOperation`, resource descriptors, validators, target intrinsic descriptors, and target-neutral asset/resource types. |
| Targets | `src/RetroSharp.GameBoy` and `src/RetroSharp.NES` | Target descriptors, asset lowering, operation lowering, runtime emission, and ROM builders. |

`RetroSharp.Core.Sdk` is intentionally not the public SDK package. It is the
internal compiler model that targets and the SDK frontend share. It can describe
portable operations and validate target capabilities, but it should not contain
the public facade source for classes such as `Video`, `Input`, `Camera`,
`Sprite`, or `World`.

## Frontend Preparation Boundary

`TargetFrontendPreparation` in `RetroSharp.Sdk.Frontend` is the single owner of
the ordered source-to-target-neutral preparation sequence. It applies one plugin
registry to the target intrinsic, library, and resource registries, then runs
source-package merge, parse, target selection, import validation, Actor Framework
lowering, source-package facade lowering, `let` inference, and function-contract
validation in that order.

The resulting internal `PreparedTargetProgram` carries the validated lowered
program and the same effective intrinsics, resource declarations, capabilities,
and base directory used during preparation. It also retains the staged internal
Actor Framework lowering plan, whose analyzed facts let Game Boy and NES provide
resolved metasprite geometry for the existing post-asset actor pool budget check
without rediscovering directives or retaining the selected pre-Actor program.

Actor Framework generation keeps that one lowering plan and the public
`ActorFrameworkLowerer` interface, but its internal gameplay policy and mutable
facts are local to feature-owned partial modules in `RetroSharp.Sdk.Frontend`.
The actor, spawn, projectile, effect, and generated-call state modules own their
lookup dictionaries, insertion-order lists, and generated-call counters; the
root state retains only target/capability, role/intrinsic, camera configuration,
and domain orchestration facts. `ActorFrameworkLowerer.Actors.cs`,
`.Projectiles.cs`, and `.Effects.cs` own directive policy for actors/Tiled
spawns, projectile pools/definitions, and effect pools/definitions respectively,
together with rewrite dispatch and the declarations assembled directly from
those directives. Each paired `.Generation.cs` file owns the deeper domain
lowering, validation, and AST builders invoked by that dispatch. The neutral
`ActorFrameworkLowerer.SharedGeneration.cs` centralizes cross-domain pool
dispatch, camera projection, and stable sprite-draw primitives.
`ActorFrameworkLowerer.GeneratedProgram.cs` consumes one ordered domain
contribution catalog for generated structs, constants, functions, and names
before name-collision checks. Adding a domain therefore adds one contribution
instead of parallel aggregation chains. The root `ActorFrameworkLowerer.cs`
retains only the public interface, one staged plan, AST traversal and rewriting,
and primitives shared across the entire lowering pipeline; the modules do not
introduce target-specific actor intrinsics or separate public lowering entry
points.

For ownership changes, start in `ActorFrameworkDomainArchitectureTests` and the
compiled-symbol helpers in `RetroSharp.Architecture.Tests`, then follow the
domain state/contribution symbol into its feature partial. Target SDK ownership
uses the same compiled-symbol and IL-call-edge guard: runtime compilers may
consume streams and route operations, but the lowerer must not call back into a
runtime compiler. Exact paths remain tests only when physical separation itself
is the contract. The physical contracts are the six documented Game Boy roots
and eight NES roots for layout, frame policy/execution, OAM publication,
runtime, stream, lowerer, and byte building; NES separates the validated
`NesFramePlan` and scheduler-owned `NesOamPublicationSchedule` from the
executable `NesPhysicalFrameScheduler`. Feature partial names are
deliberately rename-safe. Compiled owner types must be
declared in those roots and absent from `GameBoyRomBuilder.cs` or
`NesRomBuilder.cs`. Every focused SDK test method, plus intentional focused
frontend callers, declares compiled `RetroSharp.TestOwnership` metadata, while
executable-member enumeration and IL traversal are shared by all symbol guards.

The concrete compiler adapters remain responsible for target catalogs and
capabilities, final `GameBoyVideoProgram` / `NesVideoProgram` construction,
resource and asset materialization, SDK/audio capability checks, runtime policy,
and ROM building. `CompileSource`, `CollectSdkOperations`, and
`CollectSdkAudioOperations` on each target all enter through that target's one
`PrepareVideoProgram` adapter. The shared preparation boundary deliberately ends
at the validated lowered program; the existing post-contract constant fold stays
inside final video-program construction.

## Three Extension Levels

RetroSharp has three separate extension levels:

| Level | Status | Boundary |
| --- | --- | --- |
| Source-only library | Supported today | Package manifests plus source files provide facades and helpers over intrinsics, resource declarations, and SDK semantics already known to the active target. |
| Built-in SDK semantics | Supported today, compiler-owned | `Sdk2DOperation`, `SdkAudioOperation`, resource descriptors, validators, capability models, asset pipelines, and GB/NES lowerers live inside this repo. |
| SDK plugin | Static in-process registry supported for the first proof | A host-registered SDK extension can bring source facades plus namespaced resource/operation descriptors, validator/capability metadata, and explicit per-target lowering hooks without adding those concepts to the language layer or central operation enums. |

The detailed design boundary for the third level is
[`docs/SdkPluginBoundary.md`](SdkPluginBoundary.md). That document is the
implementation handoff for #252 and #258; it does not imply a dynamic loader or
any current migration of `RetroSharp.Portable2D`.

## Current Pluggability

RetroSharp supports source-only SDK libraries. A package directory contains a
`retrosharp-library.json` manifest and RetroSharp source files. Projects can load
packages through `libraryPaths` plus `libraries`, and standalone builds can use
repeated `--lib-path <path>` options together with source-level imports. A
library path can point either at one package directory or at a directory whose
direct children are package directories.

This is deliberately source-only:

- It can provide public facade classes and helpers.
- It can wrap target intrinsics already known to the active target catalog.
- It can use `namespaceMode: "physical"` for compile-time namespaces.
- It can be gated to target ids with the package manifest `targets` list.

It is not a compiler plugin ABI. A source-only library does not add new
compiler semantics: it does not add new `Sdk2DOperation` records, target
capability validators, resource importers, or backend lowerers. Those still live
in the compiler and target assemblies until RetroSharp has a proven need for a
stable plugin surface.

The first SDK plugin surface is deliberately static and host-provided. A host can
construct an `SdkPluginDescriptor`, register it in `SdkPluginRegistry`, and pass
that registry to a target compiler. The SDK frontend then exposes the plugin's
source package through `SdkLibraryRegistry.WithSdkPlugins(...)`, resource ids
resolve through `SdkResourceDeclarationRegistry`, and each target receives an
effective `TargetIntrinsicCatalog` built with `WithSdkPlugins(...)`. A plugin
operation remains a namespaced descriptor such as
`RetroSharp.Platformer2D.TouchProbe`; it does not require a new
`Sdk2DOperation`, `SdkAudioOperation`, or `TargetIntrinsicOperation` member.
Targets opt in by registering a matching `SdkPluginTargetLoweringDescriptor`.
For the static v1 proof, statement hooks receive a minimal target byte emitter
through `SdkPluginTargetLoweringContext`; broader target services remain outside
this slice. Targets without that hook fail before lowering with a diagnostic
naming the plugin feature and target id. A hook also grants capabilities: an
operation's `RequiredCapabilities` must be covered by the hook's
`ProvidedCapabilities`, otherwise the target fails before lowering with a
capability-specific diagnostic.

The host, not the compiler, decides which plugins are active. The RetroSharp CLI
resolves plugin ids to descriptors from a static known-plugins table and passes
the registry through `--sdk-plugin <id>` (repeatable) or a project manifest
`"plugins": [...]` array. The reference plugin lives in
`src/RetroSharp.Sdk.Plugins.Platformer2D` (`Platformer2DPlugin.Create()`),
outside the compiler core. Builds that do not request a plugin stay
byte-identical.

Some compiler-owned frontend transforms are entered through package-declared
metadata rather than public-name switches. The actor framework is the current
example: `sdk/RetroSharp.Portable2D/src/actors.rs` declares the public
`Actors.*` and `Enemies.*` facade methods with `sdk_role("...")` attributes.
`ActorFrameworkLowerer` consumes roles such as `actor_pool`,
`actor_spawn_layer`, `actor_spawn_window`, and `actor_enemy_def`; it does not
derive those public entry points from the names `Actors.Pool` or
`Enemies.Def`. The semantic lowering remains compiler-owned because it generates
fixed storage, spawn tables, direct dispatch, validation, and helper functions.
This is not the SDK plugin layer from #252.

## Dependency Direction

The dependency direction should stay:

```text
source package -> RetroSharp.Sdk.Frontend -> RetroSharp.Core.Sdk -> targets
```

In project terms, `RetroSharp.Sdk.Frontend` references `RetroSharp.Core` and the
parser. `RetroSharp.Core` must not reference `RetroSharp.Sdk.Frontend`, because
that would make the target-neutral model depend on source loading and parser
concerns.

## When To Add Compiler Semantics

Keep a feature in a source-only package when it is a thin facade, a helper, or a
compile-time namespace organization over existing intrinsics and SDK operations.

Move a feature into the internal SDK model only when targets must validate or
lower a new portable semantic operation. That change should update the matching
operation model, target capability checks, backend lowerers, and public SDK
documentation in the same patch.

Use the SDK plugin layer when a feature needs new compiler semantics but should
not become a built-in RetroSharp operation. The current static plugin ABI can
prove source package + descriptor + validator + target hook flows in-process,
but it is not a dynamic plugin loader, package manager, binary ABI, or migration
mechanism for existing `RetroSharp.Portable2D` APIs.

The current compiler-owned operation and target-intrinsic inventory lives in
`docs/ArchitectureRoadmap.md` under "Compiler-Owned SDK Operation Inventory".
Use that table, especially its target-intrinsic taxonomy buckets
(`core-runtime`, `portable-2d`, `platformer/plugin`, `target-specific`, and
`compat/deprecated`), before adding a new `Sdk2DOperation`,
`SdkAudioOperation`, or `TargetIntrinsicOperation`.
