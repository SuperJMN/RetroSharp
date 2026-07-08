# RetroSharp SDK Architecture

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

## Three Extension Levels

RetroSharp has three separate extension levels:

| Level | Status | Boundary |
| --- | --- | --- |
| Source-only library | Supported today | Package manifests plus source files provide facades and helpers over intrinsics, resource declarations, and SDK semantics already known to the active target. |
| Built-in SDK semantics | Supported today, compiler-owned | `Sdk2DOperation`, `SdkAudioOperation`, resource descriptors, validators, capability models, asset pipelines, and GB/NES lowerers live inside this repo. |
| SDK plugin | Future design boundary | A host-registered SDK extension could bring source facades plus descriptors, validators, importers, capabilities, and per-target lowering hooks without adding those concepts to the language layer. |

The detailed design boundary for the third level is
[`docs/SdkPluginBoundary.md`](SdkPluginBoundary.md). That document is the
implementation handoff for #252; it does not imply a dynamic loader or any
current migration of `RetroSharp.Portable2D`.

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

The current compiler-owned operation and target-intrinsic inventory lives in
`docs/ArchitectureRoadmap.md` under "Compiler-Owned SDK Operation Inventory".
Use that table, especially its target-intrinsic taxonomy buckets
(`core-runtime`, `portable-2d`, `platformer/plugin`, `target-specific`, and
`compat/deprecated`), before adding a new `Sdk2DOperation`,
`SdkAudioOperation`, or `TargetIntrinsicOperation`.
