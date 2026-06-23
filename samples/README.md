# RetroSharp Samples

Samples are classified by architectural layer in `samples/manifest.json`.

Current samples use the active language surface where it helps readability without hiding target cost: symbolic `const` values, enums for tile/flag names and zero-cost constant groups, aliases for byte-backed values, immutable `let` locals, `inline`/`pure` helper contracts, SDK dot-calls, restricted static classes, receiver methods, `switch` expressions, pipelines, `loop` for intentional infinite loops, and compound mutation syntax for direct state updates.

Until RetroSharp grows a dedicated `module` or `const group` syntax, samples may group static configuration with enums such as `World.Width` or `Player.ScreenX`. Treat that as a compile-time naming pattern, not runtime object state. Use restricted `class` syntax or the equivalent `struct` plus receiver methods only for mutable state that behaves like a real value, such as `PlayerState.Reset()` or `EnemyState.Step()`. The flat style remains valid when a sample needs to show every local and helper call directly.

| Layer | Meaning |
| --- | --- |
| `portable-sdk` | A source sample that is allowed to prove cross-target SDK portability. It must not call target intrinsics or transitional helpers. |
| `target-intrinsic` | A target-specific sample that demonstrates raw setup or hardware-shaped calls. It is not evidence that the API is portable. |
| `target-capability-spike` | A target-specific spike for a capability-gated SDK feature whose cross-target contract is not complete yet. |
| `target-acceptance` | A target-specific acceptance sample for a runnable scenario. It can use transitional calls while they are explicitly documented. |

The portable quarantine check in `RetroSharp.Core.Tests` reads the manifest and rejects transitional or target-intrinsic calls inside `portable-sdk` samples.

## Regenerating Game Boy ROMs

Run this from the repository root to refresh the tracked Game Boy sample ROMs:

```sh
tools/gameboy/generate_sample_roms.py
```

By default the script rebuilds only manifest samples that target Game Boy and already have a sibling `.gb` output tracked by Git. Use `--dry-run` to inspect the commands, `--all` to build every manifest sample that declares the `gb` target, or pass explicit sample paths such as `samples/runner/runner.rs`.
