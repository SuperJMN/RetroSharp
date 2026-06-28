# RetroSharp Samples

Samples are classified by architectural layer in `samples/manifest.json`.

Current samples use the active language surface where it helps readability without hiding target cost: symbolic `const` values, enums for tile/flag names and zero-cost constant groups, aliases for byte-backed values, immutable `let` locals, `inline`/`pure` helper contracts, SDK dot-calls, actor framework pool/definition sugar, restricted static classes, receiver methods, `switch` expressions, pipelines, `loop` for intentional infinite loops, and compound mutation syntax for direct state updates.

Until RetroSharp grows a dedicated `module` or `const group` syntax, samples may group static configuration with enums such as `World.Width` or `Player.ScreenX`. Treat that as a compile-time naming pattern, not runtime object state. Use restricted `class` syntax or the equivalent `struct` plus receiver methods only for mutable state that behaves like a real value, such as `PlayerState.Reset()` or `EnemyState.Step()`. The flat style remains valid when a sample needs to show every local and helper call directly.

| Layer | Meaning |
| --- | --- |
| `portable-sdk` | A source sample that is allowed to prove cross-target SDK portability. It must not call target intrinsics or transitional helpers. |
| `target-intrinsic` | A target-specific sample that demonstrates raw setup or hardware-shaped calls. It is not evidence that the API is portable. |
| `target-capability-spike` | A target-specific spike for a capability-gated SDK feature whose cross-target contract is not complete yet. |
| `target-acceptance` | A target-specific acceptance sample for a runnable scenario. It can use transitional calls while they are explicitly documented. |

The portable quarantine check in `RetroSharp.Core.Tests` reads the manifest and rejects transitional or target-intrinsic calls inside `portable-sdk` samples.

`samples/gameboy-vscroll/vscroll.rs` is a Game Boy-only `target-acceptance` sample for vertical camera movement. `samples/nes-free-scroll/freescroll.rs` is a NES-only `target-capability-spike` for preloaded four-screen free scroll over a bounded 64x60 surface. The shared `samples/runner/runner.rs` is the current NES acceptance path for four-screen two-axis camera movement while Game Boy remains on the byte-identical horizontal path.

## Regenerating ROMs

Run this from the repository root to refresh the tracked sample ROMs:

```sh
tools/gameboy/generate_sample_roms.py
```

By default the script rebuilds manifest sample/target pairs that already have tracked sibling ROM outputs such as `.gb` or `.nes`. Use `--dry-run` to inspect the commands, `--all` to build every declared manifest target, or pass explicit sample paths such as `samples/runner/runner.rs`.
