# Platformer Landing Functional Acceptance

Status: implemented and accepted by CSL-7 / #342.

This is the focused cross-target platformer rung between the packed camera and
actor/projectile matrices and the complete runner. It uses one shared source,
one small authored Tiled map, and only existing Portable2D calls. Landing,
jump, wall response, fall reset, and the post-reset input latch remain source
policy; no language feature, SDK API, target intrinsic, allocator, or runtime
dispatch was added.

## Exact production artifacts

| Target | Tracked ROM | Profile | Bytes | SHA-256 |
| --- | --- | --- | ---: | --- |
| Game Boy | `samples/platformer-landing/bin/platformer-landing.gb` | `gb-rom-only-current` | 32,768 | `8955273d1ee3596389f6c5219995d360097a5d9e9b40b7612e91050e4f373e54` |
| NES | `samples/platformer-landing/bin/platformer-landing.nes` | `nes-mapper-0-current` | 40,976 | `7dcd7b5f943183390397d03ab2de182eced50b7b3a3576d8bac45c7c22dbd26f` |

The CLI acceptance test compiles
`samples/platformer-landing/platformer-landing.retrosharp.json` for both targets
and requires byte-for-byte equality with the tracked ROMs. Target tests also
compile `src/main.rs` from the sample project directory so they can read typed
variable addresses from the build report. The 32x20 source map
expands to 64x40 hardware cells and serializes as 4x3 eight-cell chunks (12
chunks). The floor top is word-wide world Y=304. Its left opening is the one
authored fall; the solid wall begins at world X=384 and stops the 18-pixel-wide
player at X=366.

## Shared timeline and mechanics evidence

Both scenarios warm up the production preload for 160 physical frames, then
run the same 1,080-frame measurement window:

1. hold Right for 400 frames, crossing camera X=255/256 and packed column/chunk
   boundaries before contacting the wall;
2. hold Left for 266 frames and return across those boundaries without reset;
3. hold A for 40 frames, observe the complete apex, and land back on Y=273
   with foot Y=304;
4. hold Left for 159 frames, enter the authored opening, exceed grounded Y,
   and perform exactly one gameplay reset.

The Game Boy return checkpoint is player X=127 with source camera X=63; the
NES checkpoint is player X=100 with camera X=36. Both end at player X=72,
Y=273, grounded, with `jumpCount=1`, `landingCount=1`, and
`gameplayResetCount=1`. Support and wall-contact counters prove that collision
queries, rather than coordinate clamps, produced the stable ground and wall.
Hardware reset-vector counts remain at their normal boot value.

Those intermediate world coordinates intentionally differ because the shared
physical-frame timeline is evaluated against each target's checked-in authored
projection and timing budget. The parent contract explicitly excludes
pixel-perfect cross-target equality. Within each target, player, camera,
collision counters, background, and OAM must all agree at every checkpoint;
the exact apex is retained at frame 876 on Game Boy and frame 875 on NES.

The reset preserves the completed jump/landing evidence and latches input until
the still-held fall direction is released. That source-owned policy prevents a
faster target from walking the respawn into the same opening repeatedly while
preserving one identical input timeline on both targets.

## Timing and integrity budgets

The checked-in scenarios retain the pre-Large-Worlds baseline
`95f166886713ff3b88bc1e17c03ef0ffe93d649a`. Limits are reviewed constants,
not learned from the current build.

| Metric | Limit | Game Boy observed | NES observed |
| --- | ---: | ---: | ---: |
| Gameplay tick ratio | >= 0.95 | 1045/1080 = 0.968 | 1080/1080 = 1.000 |
| Consecutive missed gameplay ticks | <= 1 | 1 | 0 |
| Input to state | <= 1 frame | 1 | 0 |
| Request to resident | <= 2 GB / <= 1 NES | 1 | 0 |
| Request to visible | <= 2 frames | 2 | 0 |

Every retained frame independently compares the authored background and
palette, the complete target-compiled player metasprite (six Game Boy pieces,
19 NES pieces), and all unused OAM slots. A separate reference timeline replays
the checked-in input spans, source mechanics, reviewed input latency, and
reviewed Game Boy zero/two-tick catch-up frames. The machine adapters expose
only retained OAM; neither emulator variables nor observed OAM can redefine
the expected bytes. Both targets report zero background or sprite
mismatches, zero unsafe VRAM/PPU/OAM writes, and zero bank/mapper restoration
failures. Game Boy keeps unused OAM zero; NES keeps it `$FF`.

## MCP parity checkpoints

The deterministic .NET runner remains the exhaustive per-frame oracle. The
external sessions used GameBoy.Mcp 0.0.14 in DMG mode and Nes.Mcp 0.0.7 with
the AprNes mapper-0 backend. Both loaded the absolute tracked-ROM path and ran
one atomic `run_input_timeline` call. Its reproducible frame/button segments
are `(160,-), (400,RIGHT), (266,LEFT), (20,-), (40,A), (110,-),
(159,LEFT), (85,-)`; splitting a segment only adds capture points and does not
release its held button.

| Target | Observed checkpoint |
| --- | --- |
| GameBoy.Mcp / DMG | LCD, background, and sprites stayed enabled. `SCX` moved `0 -> $0E` (world camera X=270) -> `$40/$3F` on return/settle -> `0`. At apex frame 876, player OAM used airborne tiles `$3A-$44` at Y=43/59; during the real fall at frame 1100 it descended to Y=121/137. Slots 0-5 stayed coherent and slots 6-39 remained zero. The final projection returned to OAM X=80/88/96 and Y=113/129. |
| Nes.Mcp / AprNes | Mapper 0 loaded with rendering, background, and sprites enabled. The right-wall checkpoint selected nametable 1 with fine X=6 (world camera X=270), return selected nametable 0 with fine X=4 (camera X=36), and final state returned to nametable 0/fine X=0. At apex frame 875, player OAM used airborne tiles `$52-$64` beginning at Y=122; during the real fall at frame 1070 it descended to Y=216-240. Slots 0-18 stayed coherent and slots 19-63 remained canonical `$FF`. The final projection returned to OAM X=72/80/88 and Y=192/200/208/216. |

## Validation

```bash
dotnet test src/RetroSharp.Core.Tests/RetroSharp.Core.Tests.csproj -m:1 --filter FullyQualifiedName~Platformer_landing
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter FullyQualifiedName~PlatformerLandingFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter FullyQualifiedName~PlatformerLandingFunctionalAcceptanceTests
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter "FullyQualifiedName~PackedTiledFunctionalAcceptanceTests&DisplayName~platformer-landing"
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter "FullyQualifiedName~PackedTiledFunctionalAcceptanceTests&DisplayName~platformer-landing"
tools/gameboy/generate_sample_roms.py --dry-run
dotnet test RetroSharp.sln -m:1
git diff --check
```
