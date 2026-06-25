# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

Generic AI CLI agents should read `AGENTS.md` first. `docs/AgentContext.md` contains memory-derived project context, known traps, recent changes, and reliable validation commands.

Repository type: .NET 10 multi-project solution (C#) for RetroSharp - a modern C#-like language that compiles to 8-bit architectures. The original compiler path emits intermediate 3-address code and targets Zilog Z80. Experimental cartridge targets also emit NES iNES and Game Boy ROMs from a constrained video/runtime subset.

Common commands
- Restore and build (all projects)
  - Debug: dotnet build RetroSharp.sln -c Debug
  - Release: dotnet build RetroSharp.sln -c Release
- Run all tests (xUnit)
  - dotnet test RetroSharp.sln -m:1
- Run tests for a single project
  - dotnet test src/RetroSharp.Parser.Tests/RetroSharp.Parser.Tests.csproj -c Debug --no-build
- Run a single test
  - By fully-qualified name: dotnet test --filter "FullyQualifiedName~Namespace.ClassName.MethodName"
  - By class: dotnet test --filter "ClassName=Namespace.ClassName"
- Code formatting/linting
  - Whitespace check: git diff --check
  - Prefer targeted `dotnet format` includes for touched files; whole-solution format verification can be noisy because of older or vendored whitespace debt.
- Run the CLI (compile a source file and print IL and Z80 assembly)
  - dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- path/to/source.rs
- Build sample cartridges
  - NES: dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out samples/nes-drawing/drawing.nes samples/nes-drawing/drawing.rs
  - Game Boy static drawing: dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out samples/gameboy-drawing/drawing.gb samples/gameboy-drawing/drawing.rs
  - Game Boy runtime runner: dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out samples/runner/runner.gb samples/runner/runner.rs
  - NES runtime runner: dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out samples/runner/runner.nes samples/runner/runner.rs
- Export GBS to a Game Boy APU trace for faithful Game Boy BGM playback (binary by default; loop auto-detected)
  - dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- gbs-to-gbapu --in path/to/theme.gbs --subsong 1 --seconds 120 --out path/to/theme.gbapu
  - Inspect a trace: dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- gbapu-dump path/to/theme.gbapu
  - Options: --no-auto-loop, --loop-cycle <n>, --emit-json, --gbsplay <path>
- Regenerate tracked sample ROMs
  - tools/gameboy/generate_sample_roms.py --dry-run
  - tools/gameboy/generate_sample_roms.py
- Optional: collect coverage (coverlet collector is referenced in test projects)
  - dotnet test RetroSharp.sln --collect "XPlat Code Coverage"

Prerequisites
- .NET SDK 10.x installed and available on PATH

Repository layout
- src/: All repository-owned projects (apps, libraries, tests)
- libs/: Git submodules and third-party code used by the solution

High-level architecture
The solution started as a classic compiler pipeline with clear stage separation and a backend for Z80. The NES and Game Boy projects are currently target-specific ROM compilers used to validate the portable video/runtime idea before the shared IR grows enough to cover those machines cleanly.

- Frontend (Parsing)
  - RetroSharp.Parser.Antlr4.v2: Antlr4-based parser. The grammar (RetroSharp.g4) is processed via Antlr4BuildTasks at build time to generate lexer/parser code. A small Program.cs is included for quick parsing experiments.
  - RetroSharp.Parser.Antlr4: Earlier Antlr4-based parser variant kept for reference/evolution.
  - RetroSharp.Parser.Model: Shared parser/AST model abstractions (depends on RetroSharp.Core).

- Core and shared domain
  - RetroSharp.Core: Core abstractions used across stages (operators, precedence, binary nodes/trees, helpers). The repo adopts CSharpFunctionalExtensions and Zafiro.Core for functional and utility patterns.

- Semantic analysis
  - RetroSharp.SemanticAnalysis: Consumes parser output and enriches it with semantic information. Downstream of Core and Parser.

- Intermediate representation (IR) and code generation
  - RetroSharp.Generation.Intermediate: Defines the intermediate 3-address code model used across the compiler (e.g., Add, Subtract, Multiply, Divide, And, Or, Assign, Call, Return, Label, Halt). Contains Fragment and supporting types and a CodeFormatter to render IL.
  - RetroSharp (root project): Hosts the compiler orchestration and (historically) IL generation glue. It references the Antlr4 v2 parser and uses functional patterns to produce an IntermediateCodeProgram. Utility types for scope handling live under RetroSharp/Utils.

- Virtual machine (IL execution)
  - RetroSharp.VirtualMachine: Provides a simple VM layer with commands mirroring the IR operations (e.g., AddCommand, AssignConstantCommand, CallCommand, ReturnCommand, HaltCommand). Useful for validating IR execution behavior independently of a hardware backend.

- Backend (Z80)
  - RetroSharp.Z80: Translates the IR into Z80 assembly and related artifacts for the Zilog Z80 target.
  - RetroSharp.Cli: Thin command-line app that ties the pipeline together: reads a RetroSharp source file, compiles to IR, prints IR, generates Z80 assembly, and prints it. Exit reporting is via stderr for success/error messages.

- Cartridge targets
  - RetroSharp.NES: Experimental iNES ROM emitter for constant video API calls and static background/tile drawing.
  - RetroSharp.GameBoy: Experimental 32 KiB DMG ROM emitter. It loads tile/background/map data and can emit LR35902 code for a small runtime subset: byte-backed locals, assignment, `if`, `while`, `video_wait_vblank()`, `scroll_set(...)`, horizontal `camera_*` helpers, `sprite_set(...)`, and runtime map column streaming.

Game Boy planning
- `docs/GameBoyTarget.md` is the source of truth for the currently supported subset and short-term checklist. Keep it updated when adding runtime intrinsics or raising the supported language surface.
- `docs/AgentContext.md` records recent Game Boy runner lessons, Tiled map pipeline behavior, validation expectations, and publication proof conventions for agents.
- `docs/GameBoyRunnerDebugging.md` documents the normal runner-first debugging workflow, including diagnostic samples, emulator cross-checks, and layer selection.

- External projects included in the solution
  - libs/Z80DotNet/*: Z80 processor simulator and tests (e.g., Zexall). Used as a real-world Z80 reference/executor for validation.
  - libs/6502DotNet/Sixty502DotNet/*: Separate external assembler/runtime referenced by the solution (not part of the compiler pipeline itself).

Development notes
- Grammar changes: Building RetroSharp.Parser.Antlr4.v2 will regenerate parser sources via Antlr4BuildTasks. A full solution build will handle this automatically; you can also build that project directly if iterating on the grammar.
- Project targets: Repository-owned projects target net10.0 with nullable and implicit usings enabled.
- Test framework: xUnit across test projects (RetroSharp.*.Tests). Prefer running tests per project when iterating quickly.

Language design decisions (summary)
- Canonical primitive types: i8, u8, i16, u16, bool and ptr<T> (16-bit). Aliases like byte/sbyte/short/ushort may be accepted by the frontend but always lower to canonical types. int/long are rejected on 8-bit targets with a friendly diagnostic.
- Struct field access: Always use '.' both for value and pointer cases. A single implicit dereference is allowed in semantics so that p: ptr<S> can use p.x as shorthand for (*p).x. More than one level of indirection requires explicit '*'. Offsets are computed at compile time; codegen emits base+offset loads/stores.
- Rationale: These choices make cost and ABI explicit, map directly to 8/16-bit registers, and keep code predictable for constrained hardware while remaining familiar to C# users.

See docs/RetroSharp.Language.md for the full specification and examples.
