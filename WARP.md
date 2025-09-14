# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

Repository type: .NET 8 multi-project solution (C#) for RetroSharp - a modern C#-like language that compiles to 8-bit architectures, emitting intermediate 3-address code and targeting Zilog Z80.

Common commands
- Restore and build (all projects)
  - Debug: dotnet build RetroSharp.sln -c Debug
  - Release: dotnet build RetroSharp.sln -c Release
- Run all tests (xUnit)
  - dotnet test RetroSharp.sln -c Debug --no-build
- Run tests for a single project
  - dotnet test src/RetroSharp.Parser.Tests/RetroSharp.Parser.Tests.csproj -c Debug --no-build
- Run a single test
  - By fully-qualified name: dotnet test --filter "FullyQualifiedName~Namespace.ClassName.MethodName"
  - By class: dotnet test --filter "ClassName=Namespace.ClassName"
- Code formatting/linting (uses .NET analyzers/formatting)
  - Fix: dotnet format RetroSharp.sln
  - Check only (CI-friendly): dotnet format RetroSharp.sln --verify-no-changes
- Run the CLI (compile a source file and print IL and Z80 assembly)
  - dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- path/to/source.rs
- Optional: collect coverage (coverlet collector is referenced in test projects)
  - dotnet test RetroSharp.sln --collect "XPlat Code Coverage"

Prerequisites
- .NET SDK 8.x installed and available on PATH

Repository layout
- src/: All repository-owned projects (apps, libraries, tests)
- libs/: Git submodules and third-party code used by the solution

High-level architecture
The solution is structured as a classic compiler pipeline with clear stage separation and a backend for Z80.

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

- External projects included in the solution
  - libs/Z80DotNet/*: Z80 processor simulator and tests (e.g., Zexall). Used as a real-world Z80 reference/executor for validation.
  - libs/6502DotNet/Sixty502DotNet/*: Separate external assembler/runtime referenced by the solution (not part of the compiler pipeline itself).

Development notes
- Grammar changes: Building RetroSharp.Parser.Antlr4.v2 will regenerate parser sources via Antlr4BuildTasks. A full solution build will handle this automatically; you can also build that project directly if iterating on the grammar.
- Project targets: All projects target net8.0 with nullable and implicit usings enabled.
- Test framework: xUnit across test projects (RetroSharp.*.Tests). Prefer running tests per project when iterating quickly.

Language design decisions (summary)
- Canonical primitive types: i8, u8, i16, u16, bool and ptr<T> (16-bit). Aliases like byte/sbyte/short/ushort may be accepted by the frontend but always lower to canonical types. int/long are rejected on 8-bit targets with a friendly diagnostic.
- Struct field access: Always use '.' both for value and pointer cases. A single implicit dereference is allowed in semantics so that p: ptr<S> can use p.x as shorthand for (*p).x. More than one level of indirection requires explicit '*'. Offsets are computed at compile time; codegen emits base+offset loads/stores.
- Rationale: These choices make cost and ABI explicit, map directly to 8/16-bit registers, and keep code predictable for constrained hardware while remaining familiar to C# users.

See docs/RetroSharp.Language.md for the full specification and examples.

