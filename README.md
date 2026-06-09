# RetroSharp

RetroSharp is a modern C#-like language that compiles to 8-bit architectures, created for "The Joy of Learning®".

I'm making this in my free time to learn about compilers and some old-school topics. It combines the familiar syntax of C# with the nostalgic charm of retro computing. It's supposed to make me happier, but more often that not, it's making me get bald faster 🤣

## How does it work?

RetroSharp's original path uses a multi-stage compilation pipeline:

1. **Parser**: Uses ANTLR4 to parse RetroSharp source code into an AST
2. **Semantic Analysis**: Validates types, scopes, and semantics
3. **Intermediate Code Generation**: Produces platform-agnostic 3-address code (IL)
4. **Backend**: Translates IL to target architecture (currently Zilog Z80)

The repository now also contains early cartridge targets that compile a constrained RetroSharp video subset directly to ROMs:

- `--target nes`: emits an iNES ROM for static background/tile drawing plus narrow tick-based input, logical sprite, and horizontal camera runtime subsets.
- `--target gb`: emits a 32 KiB Game Boy ROM. It supports static background/map setup and a first runtime sprite loop subset with local byte-backed variables, assignment, `if`/`else if`/`else`, `while`, `loop`, `for`, half-open range `for`, `video_wait_vblank()`, tick-based input polling, `scroll_set(...)`, horizontal `camera_*` helpers, `sprite_set(...)`, runtime map column streaming, simple source-map tile queries for collision, and joypad button queries.

## What can it do?

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
i16 main()
{ 
    return 2 * 3 * 4; 
}
```

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
  --out samples/gameboy-runner/runner.gb \
  samples/gameboy-runner/runner.rs
```

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

The sample sources have been migrated to the current language v1 style: symbolic `const` values and enums for static data, aliases where they clarify byte-backed values, `loop` for infinite runtime loops, and compound mutation syntax where it maps directly to the old explicit assignments.

See `docs/Portable2DSdkV1.md` for the portable 2D SDK v1 reference, `samples/README.md` for sample layer classification, `docs/RetroSharp.Language.md` for the language v1 surface, `docs/GameBoyTarget.md` for the current Game Boy subset, `docs/NesTarget.md` for the current NES subset, `docs/ArchitectureRoadmap.md` for the persistent language/SDK/intrinsics architecture roadmap, and `docs/AgentExecution.md` for the autonomous issue/agent workflow.
