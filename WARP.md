# WARP.md

This file provides guidance to WARP (warp.dev) when working with RetroSharp.
Generic AI CLI agents should read `AGENTS.md` first. The repository is a .NET
10 multi-project solution for a C#-like language and zero-cost 2D framework
that compiles directly to Game Boy and NES cartridges.

## Common commands

Run from the repository root:

```bash
dotnet build RetroSharp.sln -c Debug
dotnet test RetroSharp.sln -m:1
git diff --check
tools/gameboy/generate_sample_roms.py --dry-run
```

Prefer focused test projects while iterating. Use targeted formatting for
touched files; whole-solution formatting can expose unrelated historical
whitespace debt.

Build representative cartridges:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/bin/runner.gb \
  samples/runner/runner.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --runtime-abi-out samples/runner/bin/runner.nes.runtime-abi.json \
  --out samples/runner/bin/runner.nes \
  samples/runner/runner.retrosharp.json
```

The CLI has no `--help` implementation. Standalone source builds require
`--target gb` or `--target nes`; project manifests must declare `target` or
`targets` unless the CLI supplies an override.

## Architecture

- `RetroSharp.Core`: target-neutral compiler and SDK domain types.
- `RetroSharp.Parser` and `RetroSharp.Parser.Model`: ANTLR parser and AST.
- `RetroSharp.SemanticAnalysis`: target-neutral semantic analysis.
- `RetroSharp.Sdk.Frontend`: source packages, `TargetFrontendPreparation`, SDK
  operation collection, Actor Framework analysis, and source-to-source lowering.
- `RetroSharp.GameBoy`: Game Boy descriptors, target lowering, runtime, and ROM
  construction.
- `RetroSharp.NES`: NES descriptors, target lowering, runtime, ABI projection,
  and ROM construction.
- `RetroSharp.Cli`: target selection, project manifests, source libraries,
  plugins, world-budget reports, and cartridge output.

The language frontend must not gain camera, sprite, tilemap, controller, or raw
hardware concepts. Portable SDK operations are capability-checked before each
target-owned lowerer emits bytes. See `docs/SdkArchitecture.md` and
`docs/ArchitectureRoadmap.md` for the authoritative seams.

## Samples and generated artifacts

- `samples/runner/runner.retrosharp.json` is the shared GB/NES target-acceptance
  project.
- `samples/manifest.json` classifies portable and target-specific evidence.
- When runner source, maps, tilesets, or target asset lowering changes, run
  `tools/gameboy/generate_sample_roms.py` and commit the tracked ROM changes.
- Screenshots under `samples/runner/*.png` are not source artifacts unless a
  task explicitly requests them.

## Retired Z80 path

The former three-address IR, Z80 backend, debug projects, and two external
submodules are not part of the active solution. Their exact recovery commit,
fork relationships, pinned SHAs, and former roles are preserved in
`docs/LegacyZ80Compiler.md`.

## Development notes

- Repository-owned projects target .NET 10 with nullable reference types and
  implicit usings enabled.
- xUnit is the test framework.
- Public gameplay source stays at the SDK/resource level; buffers, registers,
  DMA, banking, and hardware addresses stay behind target intrinsics/lowering.
- Preserve zero-cost semantics: no heap, GC, RTTI, boxing, delegates, closures,
  virtual dispatch, or hidden object identity.
