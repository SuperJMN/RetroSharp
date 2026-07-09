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
- `--target gb`: emits a 32 KiB ROM-only Game Boy cartridge when the program fits, or an MBC1 banked ROM when large music assets need more space. It supports static background/map setup and a first runtime sprite loop subset with local byte-backed variables, assignment, `if`/`else if`/`else`, `while`, `for`, half-open range `for`, `Video.WaitVBlank()`, tick-based input polling, `Scroll.Set(...)`, position-based camera X/Y scrolling with runtime row/column streaming, `Sprite.Set(...)`, simple source-map tile queries for collision, joypad button queries, hUGETracker `.uge` BGM playback, `.gbapu`/`.gbapu.json` APU trace playback, and VGM/VGZ-sourced DMG playback through the same compact on-ROM trace repack. Bank selection for banked BGM data is emitted by the runtime; source code keeps using `Music.Asset(...)`, `Music.Play(...)`, and `Audio.Update()`.

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
- `sizeof(type)` folded at compile time for primitive, reserved internal pointer-size marker, enum, and plain struct types
- `offsetof(type, field)` folded at compile time for direct fields of plain struct types
- `countof(array)` folded at compile time for fixed-size local arrays
- Top-level enums with qualified members folded at compile time
- Plain local structs with named and shorthand initializer lists plus field access in the current cartridge targets
- Restricted static classes with fixed-layout fields, instance methods lowered to receiver helpers, and static constants/methods lowered without heap allocation or dispatch
- Fixed-size local byte arrays with initializer lists, initializer-inferred lengths, constant index access, and byte-backed runtime index access in the current cartridge targets
- Function calls, including inline helper calls with parameters, named arguments, default parameter values, single-return expression helpers, and `=>` expression-bodied helpers in the current cartridge targets
- Control flow (`if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, `while`, `do while`, `for`, half-open range `for`, `break`, `continue`)
- Half-open range membership expressions such as `tile in 1..4`, lowered like `tile >= 1 && tile < 4`
- Short-circuit `&&`/`||` and unary `!` conditions, plus byte-backed 0/1 logical value expressions, in the current cartridge targets
- Conditional value expressions (`condition ? whenTrue : whenFalse`) lowered to direct branches in the current cartridge targets
- Byte-backed bitwise `&`, `|`, and `^`, plus folded constant `~` masks, in the current cartridge targets
- Explicit casts such as `(u8)expr` as validated zero-cost expression markers in the current cartridge targets
- Canonical fixed-width types (`u8`, `i8`, `u16`, `i16`, `bool`) plus source aliases where the current target accepts them
- Public gameplay code stays at the SDK/resource level; raw buffers, hardware addresses, pointer member access, and `ptr<T>` APIs remain reserved for internal SDK/backend addressability policy

Example program:
```csharp
i16 Main()
{
    return 2 * 3 * 4;
}
```

## Framework surface

The portable 2D SDK gives game code a common vocabulary for tile-and-sprite
machines while keeping each target's limits explicit. Projects load
`RetroSharp.Portable2D` from manifest `libraries` and then use frame/input
calls, Tiled `World.Load(...)`, collision flags, camera-relative AABB queries,
logical palettes, logical sprites, animation, music declarations, and
fixed-pool actor framework sugar. Standalone source files can still declare
`import RetroSharp.Portable2D;`; unknown imports are rejected. Compiler hosts
can supply explicit library imports and an `SdkLibraryRegistry` for additional
source-level SDK libraries. The CLI can also discover local source-only
libraries with `--lib-path`.
A library package is a directory with `retrosharp-library.json` and one or more
RetroSharp source files:

```json
{
  "import": "Acme.Wait",
  "rootNamespace": "Acme.Wait",
  "sourceRoot": "src",
  "namespaceMode": "physical",
  "sources": [
    "src/api.rs",
    "src/timing/rules.rs"
  ],
  "targets": [ "gb", "nes" ]
}
```

`--lib-path` may point at one package directory or at a directory containing
package subdirectories. This MVP deliberately does not include package
versioning, remote feeds, transitive dependencies, binary libraries, or target
backend plugins.

Source-only libraries cannot add new compiler semantics. When a feature needs a
new namespaced operation, resource, capability, validator, or per-target
lowering hook, the host registers a static in-process **SDK plugin** instead.
Pass `--sdk-plugin <id>` (repeatable) or list `"plugins": ["<id>"]` in a project
manifest; unknown ids are rejected before compiling. A registered plugin is
enforced per target: a target lowers a plugin operation only when it provides a
matching lowering hook and grants the operation's required capabilities,
otherwise the build fails before lowering with a diagnostic naming the plugin
feature and target id. The experimental reference plugin is
`RetroSharp.Platformer2D` (`Platformer.GroundProbe()`), which lowers on Game Boy
and is unsupported on NES. There is no dynamic plugin loader or binary plugin
ABI. See [`docs/SdkPluginBoundary.md`](docs/SdkPluginBoundary.md).

When a library package opts into `namespaceMode: "physical"`, folder names under
`sourceRoot` become compile-time namespaces just like project sources. Root
source files commonly act as the package's public facade after `import`, while
helper files under folders can reuse names without colliding with the game or
with other packages.

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
  --out samples/runner/bin/runner.gb \
  samples/runner/runner.retrosharp.json
```

For games split across several source files, use a RetroSharp project manifest.
It is a small JSON file owned by RetroSharp, not an MSBuild project:

```json
{
  "targets": [ "gb", "nes" ],
  "outputs": {
    "gb": "bin/runner.gb",
    "nes": "bin/runner.nes"
  },
  "rootNamespace": "Runner",
  "sourceRoot": "src",
  "namespaceMode": "physical",
  "sources": [
    "src/Program.rs",
    "src/player/State.rs",
    "src/camera/State.rs"
  ],
  "libraryPaths": [
    "lib"
  ],
  "libraries": [
    "RetroSharp.Portable2D"
  ]
}
```

Then build the project file directly:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- retrosharp.json
```

Project paths are resolved relative to the JSON file. `--target`, `--out`, and
additional `--lib-path` options still work as command-line overrides. Use
project `sources` for code that belongs to the game itself; use
`retrosharp-library.json` packages when code should behave like an external,
imported dependency. `libraryPaths` tells the CLI where to discover local
packages; `libraries` names the package import paths that this project loads.
Source-level `import` still works as the explicit transitional form, but
C#-style project code normally declares loaded libraries in the manifest and
uses `using` only for namespace lookup inside source files.

When `namespaceMode` is `physical`, RetroSharp derives zero-cost source
namespaces from the path under `sourceRoot`: `src/player/State.rs` belongs to
`Runner.Player`, while `src/camera/State.rs` belongs to `Runner.Camera`. The
compiler lowers those names to unique internal symbols, so folders can contain
same-named helper classes such as `Rules`; source can refer to static constants
with path-qualified names like `Player.Rules.Start`.

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

The sample sources have been migrated to the current language surface: symbolic `const` values and enums for static data, aliases where they clarify byte-backed values, `let` for immutable frame-local values, `inline`/`pure` helper contracts, SDK dot-calls, restricted static classes and receiver methods where they clarify ownership, `switch` expressions, pipelines, `while (true)` for infinite runtime loops, and compound mutation syntax where it maps directly to the old explicit assignments.

## AI agent orientation

AI CLI agents should start with `AGENTS.md`. `llms.txt` provides a compact index, and `docs/AgentContext.md` preserves recent project memory, known traps, reliable commands, and publication expectations.

See `docs/ProjectOverview.md` for the reader-facing project overview, `docs/Portable2DSdkV1.md` for the portable 2D SDK v1 reference, `samples/README.md` for sample layer classification, `docs/RetroSharp.Language.md` for the language v1 surface, `docs/GameBoyTarget.md` for the current Game Boy subset, `docs/NesTarget.md` for the current NES subset, `docs/ArchitectureRoadmap.md` for the persistent language/SDK/intrinsics architecture roadmap, `docs/LargeWorldsRoadmap.md` for the full-`stage1` packed-world/banking/mapper execution plan, `docs/ActorFrameworkRoadmap.md` for the actor framework slice, `docs/CameraVerticalScrollRoadmap.md` for the AR-5 vertical scroll execution plan, and `docs/AgentExecution.md` for the autonomous issue/agent workflow.
