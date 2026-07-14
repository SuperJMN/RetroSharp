# RetroSharp Samples

Samples are classified by architectural layer in `samples/manifest.json`.
Every manifest entry has a stable, feature-named `id`; automation and historical
results should key on that identity rather than on a path that may move. Target
availability remains explicit in `targets`, while target-specific assets and
generated ROMs use their normal target extensions.

Current samples use the active language surface where it helps readability without hiding target cost: symbolic `const` values, enums for tile/flag names and zero-cost constant groups, aliases for byte-backed values, immutable `let` locals, `inline`/`pure` helper contracts, SDK dot-calls, actor framework pool/definition sugar, restricted static classes, receiver methods, `switch` expressions, pipelines, `while (true)` for intentional infinite loops, and compound mutation syntax for direct state updates.

Until RetroSharp grows a dedicated `module` or `const group` syntax, samples may group static configuration with enums such as `Level.Width` or `Player.StartX`. Treat that as a compile-time naming pattern, not runtime object state. Use restricted `class` syntax or the equivalent `struct` plus receiver methods only for mutable state that behaves like a real value, such as `PlayerState.Reset(...)` or `EnemyState.Step()`. The flat style remains valid when a sample needs to show every local and helper call directly.

| Layer | Meaning |
| --- | --- |
| `portable-sdk` | A source sample that is allowed to prove cross-target SDK portability. It must not call target intrinsics or transitional helpers. |
| `target-intrinsic` | A sample that demonstrates raw setup or hardware-shaped calls. It may build on more than one declared target, but it is not evidence that those calls form a portable API. |
| `target-capability-spike` | A target-specific spike for a capability-gated SDK feature whose cross-target contract is not complete yet. |
| `target-acceptance` | A target-specific acceptance sample for a runnable scenario. It can use transitional calls while they are explicitly documented. |

The portable quarantine check in `RetroSharp.Core.Tests` reads the manifest and rejects transitional or target-intrinsic calls inside `portable-sdk` samples. Raw calls such as `Scroll.Set(...)`, `Sprite.Set(...)`, `Tilemap.Set(...)`, `Tilemap.Fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `Palette.Set(...)`, and `ObjectPalette.Set(...)` belong only in `target-intrinsic`, `target-capability-spike`, or documented `target-acceptance` samples.

For game-owned source organization, samples can use a `retrosharp.json` or
`*.retrosharp.json` project manifest that lists multiple source files and local
`libraryPaths`/`libraries`. That manifest is not a new architectural layer: it
just defines the compilation unit and the source-only library imports that are
loaded for it. `libraryPaths` discovers local packages; `libraries` names the
import paths to inject. Source-only library packages remain the separate
mechanism for code that should be reused across projects.

`samples/source-library-package/source-library.retrosharp.json` is the smallest
portable source-library sample: the project loads `Acme.Timing` from
`samples/source-library-package/lib` and proves that local source-only packages
compile for Game Boy and NES without adding compiler plugins.

`samples/static-drawing/drawing.rs` is the canonical static-rendering identity
for both cartridge targets. Private compile-time target variants in that one
source retain each original raw palette/tilemap fixture and its exact visible
projection without adding runtime dispatch or a target-specific public API.
The manifest alone declares target availability for the neutral identity.

`samples/source-vscroll/vscroll.rs` is a Game Boy-only `target-acceptance` sample for vertical camera movement over source-authored columns. `samples/tiled-tall/tall.rs` is the Game Boy-only `target-acceptance` sample for vertical camera movement over a tall Tiled map loaded with `World.Load(...)`, proving row streaming into its wrapped 32-row background buffer. `samples/tiled-hscroll/` contains three Game Boy/NES horizontal-only Tiled canaries for #335: `tiled-hscroll-short` scrolls a collision-free 64x20-cell crop, `tiled-hscroll-full` scrolls the complete collision-free 156x20-cell `stage1` at zero NES Y, and `tiled-hscroll-offset` repeats the full fixture at a non-zero vertical camera offset; all advance exactly one pixel per source tick and omit players, input, sprites, audio, and collision queries. `samples/tiled-vscroll/vscroll.rs` is the Game Boy/NES `target-acceptance` sample for vertical camera movement over a 40x60 Tiled map loaded with `World.Load(...)`: Game Boy builds the same source, and NES proves the Tiled source enters the four-screen vertical path across all four nametables. `samples/tiled-diagonal/diag.rs` is the Game Boy-only `target-acceptance` sample for diagonal camera movement over a 40x40 Tiled map loaded with `World.Load(...)`, proving both wrapped columns and rows are streamed from the imported map. `samples/tiled-free-scroll/free-scroll.rs` is the Game Boy/NES `target-acceptance` sample for diagonal camera movement over a 50x60 Tiled `World.Load(...)` map: NES proves the imported map populates the four-screen surface across both axes, and Game Boy keeps diagonal Tiled streaming coverage. `samples/deadzone-follow/deadzone.rs` is the Game Boy/NES `target-acceptance` sample for 2-axis dead-zone camera following over a 64x60 Tiled `World.Load(...)` map: the scripted player point moves inside a central band before the camera advances by at most one pixel per frame on both axes. `samples/source-free-scroll/freescroll.rs` is a Game Boy/NES `target-acceptance` sample for diagonal camera movement over source-authored columns: NES proves preloaded four-screen free scroll over a bounded 64x60 surface, and Game Boy proves staggered one-edge-per-VBlank streaming over source-authored columns. The shared `samples/runner/runner.retrosharp.json` project is the richer playable runner acceptance path: it lists `src/main.rs` plus game-owned helper/state files and loads the complete 156x20-cell `stage1.tmj` map, expanded to 312x40 hardware tiles and packed independently for Game Boy and NES without trimming. `samples/runner-projectile/runner-projectile.retrosharp.json` is the minimal runner-derived projectile acceptance path: Mario stays on screen, pressing `B` or `A` queues hero fireballs from separate request call sites, and the sample isolates `Projectiles.Pool`/`Def`/`Request`/`TouchTiles` plus `Effects.Pool`/`Def` against a small source-authored floor instead of the full runner world. `samples/shots-simple/shots-simple.retrosharp.json` is a Game Boy/NES `target-acceptance` sample focused purely on the projectile pool cap: a scripted fire cadence stands in for a player mashing `B`, and the `hero: 2` pool proves that at most two plain `Linear` shots are ever on screen at once (extra fire requests are dropped until a shot leaves the screen). It carries no effects, gravity, or tile collision; a companion sample will add bouncing shots. `samples/shots-bouncy/shots-bouncy.retrosharp.json` is that bouncing companion: same `hero: 2` cap, but each shot is a `GravityArc` fireball that bounces off a solid floor (`World.Flags` `Solid` cells queried by `shots.TouchTiles(0, 1)` with a `Bounce` response) while it travels right.

## Regenerating ROMs

Run this from the repository root to refresh the tracked sample ROMs:

```sh
tools/gameboy/generate_sample_roms.py
```

By default the script rebuilds manifest sample/target pairs that already have tracked sibling ROM outputs such as `.gb` or `.nes`. Use `--dry-run` to inspect the commands, `--all` to build every declared manifest target, or pass explicit sample paths such as `samples/runner/runner.retrosharp.json`. Manifest entries can declare `libraryPaths` for local source-only packages and `libraries` for the import paths to load.
