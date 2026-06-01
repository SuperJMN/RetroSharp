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

- `--target nes`: emits an iNES ROM for static background/tile drawing.
- `--target gb`: emits a 32 KiB Game Boy ROM. It supports static background/map setup and a first runtime sprite loop subset with local byte-backed variables, assignment, `if`, `while`, `video_wait_vblank()`, `scroll_set(...)`, `sprite_set(...)`, runtime map column streaming, and simple source-map tile queries for collision.

## What can it do?

Right now, RetroSharp can compile simple programs with:
- Basic arithmetic and logic operations
- Variables and assignments
- Function calls
- Control flow (if/else, loops)
- Multiple data types (int, char, byte, u8, i8, u16, i16, bool)
- Pointers with `ptr<T>` syntax

Example program:
```csharp
int main() 
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

There are also experimental ROM targets for NES and Game Boy under `src/RetroSharp.NES` and `src/RetroSharp.GameBoy`, with runnable samples under `samples/`.

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

See `docs/GameBoyTarget.md` for the current Game Boy subset and short-term checklist.
