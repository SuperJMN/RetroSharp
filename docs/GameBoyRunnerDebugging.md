# Game Boy Runner Debugging Workflow

Status: operational debugging guide.
Last updated: 2026-07-13.

Use this workflow when debugging Game Boy runtime behavior with `samples/runner/runner.retrosharp.json` as the test application. The runner is the main acceptance app for playable Game Boy behavior: camera movement, Tiled map loading, collision, sprites, animation, input, and reset/fail state. The project manifest lists `src/main.rs` plus helper/state code under `samples/runner/src` and enables physical project namespaces, so direct CLI builds should use the project file. It is not automatically portable SDK evidence; check `samples/manifest.json` before treating a call as portable.

## Goal

Debug from observable behavior back to the responsible layer:

1. Reproduce with the full runner.
2. Isolate the first failing runner diagnostic.
3. Cross-check the emulator or debug bridge before blaming the ROM.
4. Fix the narrowest responsible layer.
5. Regenerate affected ROMs and run validation.

## Starting State

Begin every debugging pass from the repository root:

```bash
git status --short --branch
git submodule status --recursive
```

If runner source, Tiled maps, tilesets, sprite assets, or cartridge lowering changed, rebuild the tracked sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build only the runner when you need a quick local ROM:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner/bin/runner.gb \
  samples/runner/runner.retrosharp.json
```

## Reproduce

Use at least one automated screenshot path before making code changes.

Run the full diagnostic matrix:

```bash
python3 -m pip install --target /tmp/retrosharp-pyboy-site pyboy pillow
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 tools/gameboy/runner_diagnostics.py
```

Outputs go to `artifacts/gameboy-runner-diagnostics/`. Report the first failing step and scenario name, for example `02-flat-ground-camera-right` or `04-full-runner-left`.

Preview the full runner directly with PyBoy:

```bash
mkdir -p artifacts
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 - <<'PY'
from pyboy import PyBoy
pyboy = PyBoy("samples/runner/bin/runner.gb", window="null")
for _ in range(180):
    pyboy.tick()
pyboy.screen.image.save("artifacts/runner-pyboy.png")
pyboy.stop()
PY
```

Preview with RetroArch/Gambatte when visual output needs an independent emulator check:

```bash
mkdir -p artifacts
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=artifacts/runner-retroarch.png \
  samples/runner/bin/runner.gb
```

Use original DMG hardware reports as backend evidence, not as sample quirks. Input bugs that reproduce only on hardware can still be target-runtime bugs, especially around `JOYP` row settling.

### NES four-screen differential

When the shared runner fails only on NES, use the tracked NES ROM and the
single three-emulator acceptance instead of inferring correctness from RGB
occupancy or synthetic screenshots:

```bash
python3 tools/nes/verify_runner_visual_parity.py
```

It drives Right beyond camera X 300, exercises jump/collision, then returns
Left through X 256 in AprNes/NesMcp, isolated RetroArch/FCEUmm, and Nestopia.
The comparison includes framebuffer captures, lifecycle and PPU evidence, all
four physical nametables, exact visible tile IDs and attribute palette
selectors, and authored collision cells. See
[`NesRunnerVisualParityAcceptance.md`](NesRunnerVisualParityAcceptance.md) for
the accepted hashes, red reproduction, runtime invariants, and artifact layout.

## Diagnostic Ladder

The diagnostic samples under `samples/runner/diagnostics/` isolate the runner in layers:

| Step | Source | Use it to isolate |
| --- | --- | --- |
| 00 | `00-static-background.rs` | Generated tiles, palette, LCD startup, and static background setup. |
| 01 | `01-world-platforms.rs` | World rows, platforms, holes, hazards, and `World.Map(...)` data. |
| 02a | `02-flat-ground-camera.rs` | Player sprite, input, animation, jump, flat-ground collision, and cyclic camera movement. |
| 02b | `02-player-camera.rs` | Player collision with wrapped foot probes, platforms, holes, hazards, jump, animation, and camera. |
| 03 | `03-enemy-sprites.rs` | Enemy sprite drawing and animation in isolation. This is a wrap-loop diagnostic, not enemy AI. |
| 04 | `../runner.retrosharp.json` | Full runner scene. |

Use the first failing step to choose the investigation layer:

- `00` fails: inspect ROM startup, LCD state, tile generation, palette setup, and renderer/debug bridge.
- `01` fails: inspect world resource generation, map rows, collision flags, and initial tilemap setup.
- `02a` fails: inspect input polling, camera movement, sprite draw, animation tick, flat-ground collision, and frame order.
- `02b` fails: inspect platform/hazard/fall resolution, wrapped collision probes, and reset/input ordering.
- `03` fails: inspect logical sprite metadata, sprite tile emission, OAM attributes, palette slots, and animation frame selection.
- Only `04` fails: inspect interaction between systems in `src/main.rs`, Tiled map data, or full-scene resource budgets.

## Check The Debug Tool First

Do not rewrite runner logic because one debug backend shows a blank screen.

If SameBoy or a Game Boy debug MCP session renders white or blank output:

- Inspect LCD/PPU state first.
- A suspicious state is `LCDC=0x00` and `LY=0x00` after boot.
- A valid post-boot rendering path for this runner has reached `LCDC=0x97` in prior debugging.
- If the emulator bridge did not apply post-boot state correctly, fix the bridge before changing the ROM.

If PyBoy and RetroArch agree but the debug MCP disagrees, treat the debug bridge as suspect until register state proves otherwise. If hardware disagrees with emulators, inspect backend timing and I/O semantics before assuming the sample source is wrong.

## Classify The Bug

Use this table to avoid fixing the wrong layer:

| Symptom | Start with |
| --- | --- |
| Blank or white screen | LCD state, startup code, tilemap setup, emulator/debug bridge. |
| Background shifted or missing under terrain | `GameBoyTiledMapImporter`, `GameBoyRomCompiler` initial tilemap fill, `retrosharpWorldY`, `retrosharpStreamY`. |
| Background decorations above the band ghost/repeat every ~32 tiles while scrolling | Background-row streaming in `GameBoyRomBuilder` camera right/left steps and `PopulateBackgroundStreamRows`. |
| Background blocks tear/glitch only while crossing a tile boundary (especially on heavy frames such as jumping or landing on a platform) | Camera tile streaming writing VRAM during active display, starting too late in the current VBlank, or writing the wrong visible edge for the current `SCX`/`SCY`; confirm `Camera.Apply()` runs after a fresh VBlank edge and compare the 20x18 visible tile projection against the imported world map. Read `0xFF44` directly for `LY`; some debug bridges report a stale `LY` in compact PPU state. |
| Player sprite pieces flicker, appear at fixed screen positions, or remain as non-scrolling "stickers" | Treat these as OAM/sprite artifacts first, not background tiles. Logical `Sprite.Draw(...)` must update the `$C600` shadow and publish one `$FF46` DMA at the frame's accepted VBlank boundary; check the DMA source page, physical OAM slot order, and that CPU fetch remains in HRAM during transfer. Raw `Sprite.Set(...)` fixtures still write physical OAM directly. |
| Collision does not match visible tiles | World flags, tileset `objectgroup`, explicit collision layer, `Camera.AabbTiles(...)` vs `collision_aabb_tiles(...)`, actor camera/world coordinates. |
| Player cannot jump in one zone | Frame order, reset before input, collision state clearing, `Input.WasPressed(...)`. |
| Jump apex is not about 21/71/85/100 px for tap/standing/run/maximum-speed input | Inspect `PlayerState` 4.4 velocity plus subpixel remainder, the `-$38/-$3A/-$3C/-$40` takeoff tier selected from camera speed, and whether held A receives `+1` gravity only below `-$20`; release or velocity `>= -$20` must use `+5` without clamping. Use the exact CPU tests in `GameBoyRunnerLandingTests` / `NesRunnerLandingTests` rather than inferring height from OAM Y while the vertical camera is following. |
| Player walks into a pipe and snaps onto its top | Horizontal movement is missing or bypassing a lower-body wall probe; block camera motion before vertical landing resolution can reinterpret the side overlap as floor. |
| Player passes up through a solid block / lands on top from below | Missing ceiling response and/or a landing search window that reaches above the feet. Add a head probe (`Camera.AabbTiles(...)` above the head while rising) that bounces the actor down, and keep the landing search window feet-relative so descent cannot magnetise the actor up onto a block it just hit. |
| Player snaps to platform while rising | Landing/support should be gated to non-rising motion, for example `player.velocityY >= 0` (signed `i8`; positive means falling, zero checks grounded support). |
| Player reaches a floor below world Y 255 but falls through it and later resets | Check the storage type of every derived world-Y probe. Non-literal `let` currently falls back to `u8`; declare values such as `footWorldY`, ceiling probes, and wall-probe Y as `i16` before passing them to `Camera.AabbTiles(...)` or `Camera.AabbHitTop(...)`. |
| Player teleports from top to ground | Byte-backed Y wrap; clamp before collision/reset checks. |
| D-pad triggers A/B behavior on hardware | `JOYP` row settling in backend input lowering. |
| Mirrored sprite shifts sideways | Logical sprite width vs padded hardware width in flip math. |
| Palette looks darker/lighter than intended | Verify visible output, then adjust palette mapping rather than only register math. |
| NES or portable sample regresses | Check capability diagnostics and sample classification before copying runner behavior. |

## Fix In The Narrowest Layer

Prefer the smallest layer that explains the first failing diagnostic:

- Source gameplay policy: `samples/runner/src/main.rs` plus the helper/state files in `samples/runner/src`.
- Runtime map data: complete `samples/runner/assets/maps/stage1.tmj`; editable Tiled source and tileset: `stage1.tmx` and `stage1.tsx`. The older `stage1.playable.tmj` crop is no longer the runner input.
- Tiled import: `src/RetroSharp.GameBoy/GameBoyTiledMapImporter.cs`.
- Game Boy ROM lowering/runtime: `src/RetroSharp.GameBoy/GameBoyRomCompiler.cs` and nearby target code.
- CLI/sample tooling: `src/RetroSharp.Cli/Program.cs` and `tools/gameboy/`.
- Tests: `src/RetroSharp.GameBoy.Tests/GameBoyRomCompilerTests.cs` and `src/RetroSharp.Cli.Tests/CrossTargetCliAcceptanceTests.cs`.
- Architecture docs: `docs/GameBoyTarget.md`, `docs/Portable2DSdkV1.md`, and `docs/ArchitectureRoadmap.md`.

Do not move gameplay behavior into the language layer. Do not add portable SDK behavior without a target capability check. Keep transitional APIs working unless the roadmap explicitly removes them.

## Test The Fix

The exact regenerated GB/NES landing and jump evidence for issue #319 is
recorded in [`RunnerLandingAcceptance.md`](RunnerLandingAcceptance.md).

Use focused tests first, then broader validation:

```bash
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter FullyQualifiedName~ActorProjectileFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter FullyQualifiedName~ActorProjectileFunctionalAcceptanceTests
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1
dotnet test src/RetroSharp.Cli.Tests/RetroSharp.Cli.Tests.csproj -m:1
dotnet test RetroSharp.sln -m:1
```

Regenerate tracked sample ROMs when source or lowering changes:

```bash
tools/gameboy/generate_sample_roms.py
```

Re-run the diagnostic matrix for visual/gameplay regressions:

```bash
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 tools/gameboy/runner_diagnostics.py
```

Always finish with:

```bash
git diff --check
git status --short --branch
```

## Debug Report Format

When handing off or opening an issue, include:

- Symptom and expected behavior.
- Full runner result: emulator/tool, frame count, input scenario, screenshot path.
- First failing diagnostic step and scenario.
- Whether PyBoy, RetroArch/Gambatte, SameBoy/debug MCP, and hardware agree or disagree.
- Suspected layer and why.
- Files changed.
- Validation commands and results.
