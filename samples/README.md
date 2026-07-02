# RetroSharp Samples

Samples are classified by architectural layer in `samples/manifest.json`.

Current samples use the active language surface where it helps readability without hiding target cost: symbolic `const` values, enums for tile/flag names and zero-cost constant groups, aliases for byte-backed values, immutable `let` locals, `inline`/`pure` helper contracts, SDK dot-calls, actor framework pool/definition sugar, restricted static classes, receiver methods, `switch` expressions, pipelines, `loop` for intentional infinite loops, and compound mutation syntax for direct state updates.

Until RetroSharp grows a dedicated `module` or `const group` syntax, samples may group static configuration with enums such as `Level.Width` or `Player.StartX`. Treat that as a compile-time naming pattern, not runtime object state. Use restricted `class` syntax or the equivalent `struct` plus receiver methods only for mutable state that behaves like a real value, such as `PlayerState.Reset(...)` or `EnemyState.Step()`. The flat style remains valid when a sample needs to show every local and helper call directly.

| Layer | Meaning |
| --- | --- |
| `portable-sdk` | A source sample that is allowed to prove cross-target SDK portability. It must not call target intrinsics or transitional helpers. |
| `target-intrinsic` | A target-specific sample that demonstrates raw setup or hardware-shaped calls. It is not evidence that the API is portable. |
| `target-capability-spike` | A target-specific spike for a capability-gated SDK feature whose cross-target contract is not complete yet. |
| `target-acceptance` | A target-specific acceptance sample for a runnable scenario. It can use transitional calls while they are explicitly documented. |

The portable quarantine check in `RetroSharp.Core.Tests` reads the manifest and rejects transitional or target-intrinsic calls inside `portable-sdk` samples.

For game-owned source organization, samples can use a `retrosharp.json` or
`*.retrosharp.json` project manifest that lists multiple source files and local
`libraryPaths`. That manifest is not a new architectural layer: it just defines
the compilation unit. Source-only libraries remain the separate mechanism for
code that should be imported as a package.

`samples/gameboy-vscroll/vscroll.rs` is a Game Boy-only `target-acceptance` sample for vertical camera movement over source-authored columns. `samples/tiled-tall/tall.rs` is the Game Boy-only `target-acceptance` sample for vertical camera movement over a tall Tiled map loaded with `World.Load(...)`, proving row streaming into its wrapped 32-row background buffer. `samples/tiled-vscroll/vscroll.rs` is the Game Boy/NES `target-acceptance` sample for vertical camera movement over a 40x60 Tiled map loaded with `World.Load(...)`: Game Boy builds the same source, and NES proves the Tiled source enters the four-screen vertical path across all four nametables. `samples/tiled-diagonal/diag.rs` is the Game Boy-only `target-acceptance` sample for diagonal camera movement over a 40x40 Tiled map loaded with `World.Load(...)`, proving both wrapped columns and rows are streamed from the imported map. `samples/tiled-free-scroll/free-scroll.rs` is the Game Boy/NES `target-acceptance` sample for diagonal camera movement over a 50x60 Tiled `World.Load(...)` map: NES proves the imported map populates the four-screen surface across both axes, and Game Boy keeps diagonal Tiled streaming coverage. `samples/deadzone-follow/deadzone.rs` is the Game Boy/NES `target-acceptance` sample for 2-axis dead-zone camera following over a 64x60 Tiled `World.Load(...)` map: the scripted player point moves inside a central band before the camera advances by at most one pixel per frame on both axes. `samples/nes-free-scroll/freescroll.rs` is a Game Boy/NES `target-acceptance` sample for diagonal camera movement over source-authored columns: NES proves preloaded four-screen free scroll over a bounded 64x60 surface, and Game Boy proves staggered one-edge-per-VBlank streaming over source-authored columns. The shared `samples/runner/runner.retrosharp.json` project is the richer playable runner acceptance path: it lists `src/main.rs` plus game-owned helper/state files under `samples/runner/src`, uses a 2-axis dead-zone camera and variable screen-position collision over a tall 24x48 Tiled map that expands to a 48x96 tile world, and forces both vertical and horizontal camera movement.

## Regenerating ROMs

Run this from the repository root to refresh the tracked sample ROMs:

```sh
tools/gameboy/generate_sample_roms.py
```

By default the script rebuilds manifest sample/target pairs that already have tracked sibling ROM outputs such as `.gb` or `.nes`. Use `--dry-run` to inspect the commands, `--all` to build every declared manifest target, or pass explicit sample paths such as `samples/runner/runner.retrosharp.json`. Manifest entries can declare `libraryPaths` for local source-only packages; the regeneration script forwards each path to the CLI as `--lib-path`.
