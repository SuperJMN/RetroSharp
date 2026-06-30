# RetroSharp Project Overview

RetroSharp is a C#-inspired language, compiler, and zero-cost 2D framework for
8-bit game systems. The current practical target is small cartridge-style games
for Game Boy and NES, while the older compiler path still proves the classic
parser, semantic analysis, intermediate-code, and Z80 backend pipeline.

The project has three parts that should stay distinct:

- The language: source syntax, types, storage, control flow, and zero-cost
  abstractions.
- The portable 2D SDK: game-facing APIs for frames, input, tile worlds, cameras,
  sprites, palettes, music, animation, and actor-style platformer structure.
- Target intrinsics and lowerers: the machine-specific work needed to turn SDK
  intent into Game Boy, NES, or future 8-bit hardware behavior.

The goal is not to hide 8-bit constraints. RetroSharp should make them explicit,
checked, and pleasant to work with.

## Language

The language is deliberately small and close to the hardware. It borrows a
C#-like shape where that improves readability, but every accepted feature has to
lower to predictable 8-bit code.

Current language work focuses on:

- Fixed-width primitive types such as `u8`, `i8`, `u16`, `i16`, and `bool`.
- Plain structs, enums, constants, type aliases, fixed arrays, and field access.
- Structured control flow: `if`, `switch`, `while`, `do while`, `loop`, `for`,
  half-open ranges, `break`, and `continue`.
- Zero-cost helpers: `inline`/`pure` source helpers, named/default arguments,
  expression-bodied helpers, receiver methods, restricted static classes, and
  SDK dot calls.
- Explicit compile-time facts such as `sizeof(type)`, `offsetof(type, field)`,
  and `countof(array)`.

The language layer must stay target-neutral. It must not learn what a camera,
sprite, tilemap, joypad, palette register, or scanline is. Those concepts belong
in the framework or in target intrinsics.

See [RetroSharp.Language.md](RetroSharp.Language.md) for the detailed preview
surface.

## Framework

The framework is the portable 2D SDK layer. It gives game code a vocabulary for
common tile-and-sprite machines without promising that every target behaves the
same internally.

The SDK currently covers:

- Frame and input calls such as `video.WaitVBlank()` and `input.Poll()`.
- Tick-based button helpers: down, just pressed, just released, and hold ticks.
- Tiled world loading, tile flags, collision facts, and camera-relative AABB
  queries.
- Position-based camera movement through `camera.SetPosition(x, y)` and
  `camera.Apply()`.
- Logical sprites, frame animation, flip, logical palette slots, and
  target-specific PNG variants.
- Music declarations and per-frame audio update, with Game Boy `.uge` /
  `.gbapu` support and VGM/VGZ input for Game Boy and NES.
- An actor framework slice for scalable platformer enemies using fixed pools,
  generated spawn helpers, direct branches, and existing SDK calls.

Portability is capability-based. A source program can be shared across targets
only when each target declares enough capability for the requested SDK calls,
sprite budgets, palette slots, scrolling modes, music format, and runtime tile
writes. Unsupported combinations should fail at compile time with explicit
diagnostics.

See [Portable2DSdkV1.md](Portable2DSdkV1.md) for the API reference and
[ActorFrameworkRoadmap.md](ActorFrameworkRoadmap.md) for the actor-framework
direction.

## Targets

RetroSharp has two active cartridge targets:

- Game Boy: a direct ROM compiler that emits ROM-only or MBC1 cartridges,
  supports the runner sample, Tiled `world.Load(...)`, scrolling, sprites,
  logical palettes, input, and Game Boy music playback.
- NES: a direct iNES ROM compiler that shares the portable SDK operation path
  where possible, supports the runner source as target acceptance, and proves
  scrolling, sprites, Tiled worlds, VGM/VGZ BGM, and many framework contracts.

The original compiler path still parses RetroSharp, analyzes it, emits
platform-agnostic intermediate code, and lowers that path to Z80 assembly. That
path matters because it keeps the language/compiler core honest, even though the
fastest-moving gameplay work now happens in the direct cartridge targets.

See [GameBoyTarget.md](GameBoyTarget.md) and [NesTarget.md](NesTarget.md) for
the current supported subsets.

## Objectives

RetroSharp should grow in a narrow, testable direction:

- Keep the language compact, explicit, and independent from any one console.
- Make the framework pleasant enough to build small real games, especially
  scrolling platformers, without hiding hardware budgets.
- Move shared game concepts into capability-checked SDK APIs instead of leaking
  raw target details into portable samples.
- Keep raw machine access available through target intrinsics for the cases
  where a game really needs hardware-specific behavior.
- Prefer zero-cost ergonomics over managed-runtime features. No heap, GC, RTTI,
  boxing, virtual dispatch, closures, delegates, or hidden object identity.
- Use the Game Boy and NES runners as practical acceptance tests while keeping
  sample classifications honest about what is portable and what is target-only.

In short: RetroSharp is a learning project, a compiler experiment, and a small
game framework. It should feel higher-level at the source level, but the emitted
program should still look like something an 8-bit machine can actually afford.
