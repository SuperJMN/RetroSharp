# RetroSharp

RetroSharp is a C#-inspired language, compiler, and zero-cost 2D framework for
8-bit game systems.

I'm making this in my free time to learn about compilers and old-school game
hardware. It was supposed to make me happier; it has also managed to reach the
shaved-head milestone.

See [`docs/ProjectOverview.md`](docs/ProjectOverview.md) for the human-facing
overview of the language, framework, targets, and project objectives.

## Project shape

RetroSharp has three layers that should stay separate:

- **Language**: target-neutral syntax, types, storage, constants, helpers,
  control flow, and zero-cost source ergonomics.
- **Portable 2D SDK**: capability-checked game APIs for frames, input, worlds,
  cameras, sprites, palettes, music, animation, and actors.
- **Target intrinsics and lowering**: Game Boy, NES, Z80, and future
  hardware-specific behavior.

The language should not know what a sprite, camera, tilemap, joypad, or palette
register is. Those concepts belong in the SDK or in explicit target intrinsics.

## Compilation paths

RetroSharp's original compiler path uses a multi-stage pipeline:

1. **Parser**: Uses ANTLR4 to parse RetroSharp source code into an AST
2. **Semantic Analysis**: Validates types, scopes, and semantics
3. **Intermediate Code Generation**: Produces platform-agnostic 3-address code (IL)
4. **Backend**: Translates IL to target architecture (currently Zilog Z80)

The repository now also contains early cartridge targets that compile a constrained RetroSharp video subset directly to ROMs:

- `--target nes`: emits an iNES mapper 0 ROM for static background/tile drawing plus tick-based input, logical sprites, horizontal camera streaming, four-screen 2-axis camera movement, Tiled `World.Load(...)`, runtime animation helpers, camera-relative runner collision, and VGM/VGZ-sourced 2A03 BGM playback. HUD and generic world-space collision are still not implemented.
- `--target gb`: emits a 32 KiB ROM-only Game Boy cartridge when the program fits, or an MBC1 banked ROM when large music assets need more space. It supports static background/map setup and a first runtime sprite loop subset with local byte-backed variables, assignment, `if`/`else if`/`else`, `while`, `loop`, `for`, half-open range `for`, `Video.WaitVBlank()`, tick-based input polling, `Scroll.Set(...)`, position-based camera X/Y scrolling with runtime row/column streaming, `Sprite.Set(...)`, simple source-map tile queries for collision, joypad button queries, hUGETracker `.uge` BGM playback, `.gbapu`/`.gbapu.json` APU trace playback, and VGM/VGZ-sourced DMG playback through the same compact on-ROM trace repack. Bank selection for banked BGM data is emitted by the runtime; source code keeps using `Music.Asset(...)`, `Music.Play(...)`, and `Audio.Update()`.

The actor framework acceptance slice lives in `samples/actor-framework`. It shows
fixed actor pools, declarative `Enemies.Def(...)` metadata, Tiled object-layer
spawns, runtime camera-window activation, and Game Boy/NES lowering without heap
allocation or runtime dispatch. The roadmap is `docs/ActorFrameworkRoadmap.md`.

## Language surface

Right now, RetroSharp can compile simple programs with:
- Basic arithmetic and logic operations
- Variables and assignments
- Decimal, hexadecimal (`0x2A`), binary (`0b1010_0000`), `_`-separated, and width-suffixed (`255u8`, `0x1234u16`) integer literals
- Type aliases normalized at compile time
- Compound assignment with `+=`, `-=`, `&=`, `|=`, and `^=`, plus statement-only `++` and `--`
- Top-level and block-local constants with optional type annotations folded at compile time, including constant boolean and conditional expressions
- `sizeof(type)` folded at compile time for primitive, pointer, enum, and plain struct types
- `offsetof(type, field)` folded at compile time for direct fields of plain struct types
- `countof(array)` folded at compile time for fixed-size local arrays
- Top-level enums with qualified members folded at compile time
- Plain local structs with named and shorthand initializer lists plus field access in the current cartridge targets
- Restricted static classes with fixed-layout fields, instance methods lowered to receiver helpers, and static constants/methods lowered without heap allocation or dispatch
- Fixed-size local byte arrays with initializer lists, initializer-inferred lengths, constant index access, and byte-backed runtime index access in the current cartridge targets
- Function calls, including inline helper calls with parameters, named arguments, default parameter values, single-return expression helpers, and `=>` expression-bodied helpers in the current cartridge targets
- Control flow (`if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, `while`, `do while`, `loop`, `for`, half-open range `for`, `break`, `continue`)
- Half-open range membership expressions such as `tile in 1..4`, lowered like `tile >= 1 && tile < 4`
- Short-circuit `&&`/`||` and unary `!` conditions, plus byte-backed 0/1 logical value expressions, in the current cartridge targets
- Conditional value expressions (`condition ? whenTrue : whenFalse`) lowered to direct branches in the current cartridge targets
- Byte-backed bitwise `&`, `|`, and `^`, plus folded constant `~` masks, in the current cartridge targets
- Explicit casts such as `(u8)expr` as validated zero-cost expression markers in the current cartridge targets
- Canonical fixed-width types (`u8`, `i8`, `u16`, `i16`, `bool`) plus source aliases where the current target accepts them
- Pointers with `ptr<T>` syntax in the language model; full cartridge-target pointer/member lowering remains planned

Example program:
```csharp
i16 Main()
{
    return 2 * 3 * 4;
}
```

## Framework surface

The portable 2D SDK gives game code a common vocabulary for tile-and-sprite
machines while keeping each target's limits explicit. Current SDK-shaped code
can start with `import RetroSharp.Portable2D;` and use frame/input calls, Tiled
`World.Load(...)`, collision flags, camera-relative AABB queries, logical
palettes, logical sprites, animation, music declarations, and fixed-pool actor
framework sugar. Game Boy and NES still auto-import that SDK for older samples;
unknown imports are rejected. Compiler hosts can switch to explicit-only SDK
imports and provide an `SdkLibraryRegistry` for additional source-level SDK
libraries.

Portability is capability-based, not magic. A shared source file is portable
only when each selected target can support the requested scrolling mode, sprite
budget, palette slots, audio format, runtime tile writes, and asset shape. See
[`docs/Portable2DSdkV1.md`](docs/Portable2DSdkV1.md) for the current API
reference.

## Project objectives

- Keep the language small, explicit, and independent from any one machine.
- Let the framework make small real 2D games practical without hiding hardware
  budgets.
- Keep raw machine access available through target intrinsics.
- Make Game Boy and NES the first compatibility pair for portable SDK work.
- Prefer zero-cost ergonomics over managed-runtime features: no heap, GC, RTTI,
  boxing, virtual dispatch, closures, delegates, or hidden object identity.

## Which platforms does it compile for?

The original backend targets the **Zilog Z80** processor - one of the most iconic 8-bit CPUs of all time! The Z80 powered legendary systems like:

- Nintendo Game Boy
- Amstrad CPC
- MSX computers
- TRS-80
- And many arcade machines

There are also experimental ROM targets for NES and Game Boy under `src/RetroSharp.NES` and `src/RetroSharp.GameBoy`, with runnable samples under `samples/`. Sample portability is tracked in `samples/manifest.json`; only samples marked `portable-sdk` are treated as evidence for cross-target SDK behavior.

## Installation

RetroSharp is distributed as a .NET tool:

```bash
dotnet tool install --global RetroSharp
```

Then use it to compile your programs:

```bash
retroSharp myprogram.rs
```

Build the Game Boy runner sample from source:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/runner.gb \
  samples/runner/runner.rs
```

Export a GBS subsong into a Game Boy APU trace when you want faithful playback of the original register writes:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  gbs-to-gbapu \
  --in path/to/theme.gbs \
  --subsong 1 \
  --seconds 120 \
  --out path/to/theme.gbapu
```

This writes a compact binary `.gbapu` (use `--emit-json` for a JSON debug view, or `gbapu-dump
<file>` to print the register writes). The loop is auto-detected and the capture is trimmed to one
loop body (`--no-auto-loop`/`--loop-cycle` override). Use the result directly with
`Music.Asset(...)`. The source keeps cycle deltas for every supported APU register write; the ROM
compiler maps them onto DMG VBlank frames, removes redundant non-trigger writes, deduplicates
repeated frame groups into a pool, and stores full Wave RAM uploads as compact block commands for
`Audio.Update()` playback.

The helper requires `gbsplay` on `PATH` unless `--gbsplay <path>` is supplied. GBS files are not loaded directly by `Music.Asset(...)`. See [`docs/GameBoyApuTraceFormat.md`](docs/GameBoyApuTraceFormat.md) for the format shape, runtime use, limitations, and future alternatives.

Build the first cross-target camera sample for both cartridge targets:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out /tmp/cross-camera.gb \
  samples/cross-target-camera/camera.rs

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out /tmp/cross-camera.nes \
  samples/cross-target-camera/camera.rs
```

The sample sources have been migrated to the current language surface: symbolic `const` values and enums for static data, aliases where they clarify byte-backed values, `let` for immutable frame-local values, `inline`/`pure` helper contracts, SDK dot-calls, restricted static classes and receiver methods where they clarify ownership, `switch` expressions, pipelines, `loop` for infinite runtime loops, and compound mutation syntax where it maps directly to the old explicit assignments.

## AI agent orientation

AI CLI agents should start with `AGENTS.md`. `llms.txt` provides a compact index, and `docs/AgentContext.md` preserves recent project memory, known traps, reliable commands, and publication expectations.

See `docs/ProjectOverview.md` for the reader-facing project overview, `docs/Portable2DSdkV1.md` for the portable 2D SDK v1 reference, `samples/README.md` for sample layer classification, `docs/RetroSharp.Language.md` for the language v1 surface, `docs/GameBoyTarget.md` for the current Game Boy subset, `docs/NesTarget.md` for the current NES subset, `docs/ArchitectureRoadmap.md` for the persistent language/SDK/intrinsics architecture roadmap, `docs/ActorFrameworkRoadmap.md` for the actor framework slice, `docs/CameraVerticalScrollRoadmap.md` for the AR-5 vertical scroll execution plan, and `docs/AgentExecution.md` for the autonomous issue/agent workflow.
