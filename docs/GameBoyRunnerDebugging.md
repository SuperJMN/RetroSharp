# Game Boy Runner Debugging Workflow

Status: operational debugging guide.
Last updated: 2026-06-13.

Use this workflow when debugging Game Boy runtime behavior with `samples/gameboy-runner/runner.rs` as the test application. The runner is the main acceptance app for playable Game Boy behavior: camera movement, Tiled map loading, collision, sprites, animation, input, and reset/fail state. It is not automatically portable SDK evidence; check `samples/manifest.json` before treating a call as portable.

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

If runner source, Tiled maps, tilesets, sprite assets, or Game Boy lowering changed, rebuild the tracked Game Boy sample ROMs:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
```

Build only the runner when you need a quick local ROM:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/gameboy-runner/runner.gb \
  samples/gameboy-runner/runner.rs
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
pyboy = PyBoy("samples/gameboy-runner/runner.gb", window="null")
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
  samples/gameboy-runner/runner.gb
```

Use original DMG hardware reports as backend evidence, not as sample quirks. Input bugs that reproduce only on hardware can still be target-runtime bugs, especially around `JOYP` row settling.

## Diagnostic Ladder

The diagnostic samples under `samples/gameboy-runner/diagnostics/` isolate the runner in layers:

| Step | Source | Use it to isolate |
| --- | --- | --- |
| 00 | `00-static-background.rs` | Generated tiles, palette, LCD startup, and static background setup. |
| 01 | `01-world-platforms.rs` | World rows, platforms, holes, hazards, and `world.Map(...)` data. |
| 02a | `02-flat-ground-camera.rs` | Player sprite, input, animation, jump, flat-ground collision, and cyclic camera movement. |
| 02b | `02-player-camera.rs` | Player collision with wrapped foot probes, platforms, holes, hazards, jump, animation, and camera. |
| 03 | `03-enemy-sprites.rs` | Enemy sprite drawing and animation in isolation. This is a wrap-loop diagnostic, not enemy AI. |
| 04 | `../runner.rs` | Full runner scene. |

Use the first failing step to choose the investigation layer:

- `00` fails: inspect ROM startup, LCD state, tile generation, palette setup, and renderer/debug bridge.
- `01` fails: inspect world resource generation, map rows, collision flags, and initial tilemap setup.
- `02a` fails: inspect input polling, camera movement, sprite draw, animation tick, flat-ground collision, and frame order.
- `02b` fails: inspect platform/hazard/fall resolution, wrapped collision probes, and reset/input ordering.
- `03` fails: inspect logical sprite metadata, sprite tile emission, OAM attributes, palette slots, and animation frame selection.
- Only `04` fails: inspect interaction between systems in `runner.rs`, Tiled map data, or full-scene resource budgets.

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
| Background blocks tear/glitch only while crossing a tile boundary (especially on heavy frames such as jumping or landing on a platform) | Camera tile streaming writing VRAM during active display, starting too late in the current VBlank, or trying to stream too many rows in one VBlank; confirm `EmitWaitForVBlankBeforeStream` waits for a fresh VBlank edge before the column/row writes and that horizontal streaming is capped to visible rows. Read `0xFF44` directly for `LY`; some debug bridges report a stale `LY` in compact PPU state. |
| Collision does not match visible tiles | World flags, tileset `objectgroup`, explicit collision layer, `camera.AabbTiles(...)` vs `collision_aabb_tiles(...)`, actor camera/world coordinates. |
| Player cannot jump in one zone | Frame order, reset before input, collision state clearing, `button_just_pressed(...)`. |
| Player walks into a pipe and snaps onto its top | Horizontal movement is missing or bypassing a lower-body wall probe; block camera motion before vertical landing resolution can reinterpret the side overlap as floor. |
| Player snaps to platform while rising | Landing should be gated by descent, for example `velocityY < World.SignedVelocityWrap` and non-zero velocity. |
| Player teleports from top to ground | Byte-backed Y wrap; clamp before collision/reset checks. |
| D-pad triggers A/B behavior on hardware | `JOYP` row settling in backend input lowering. |
| Mirrored sprite shifts sideways | Logical sprite width vs padded hardware width in flip math. |
| Palette looks darker/lighter than intended | Verify visible output, then adjust palette mapping rather than only register math. |
| NES or portable sample regresses | Check capability diagnostics and sample classification before copying runner behavior. |

## Fix In The Narrowest Layer

Prefer the smallest layer that explains the first failing diagnostic:

- Source gameplay policy: `samples/gameboy-runner/runner.rs`.
- Editable map data: `samples/gameboy-runner/maps/runner.tmj` and `samples/gameboy-runner/maps/Super Mario Land 2.tsx`.
- Tiled import: `src/RetroSharp.GameBoy/GameBoyTiledMapImporter.cs`.
- Game Boy ROM lowering/runtime: `src/RetroSharp.GameBoy/GameBoyRomCompiler.cs` and nearby target code.
- CLI/sample tooling: `src/RetroSharp.Cli/Program.cs` and `tools/gameboy/`.
- Tests: `src/RetroSharp.GameBoy.Tests/GameBoyRomCompilerTests.cs` and `src/RetroSharp.Cli.Tests/CrossTargetCliAcceptanceTests.cs`.
- Architecture docs: `docs/GameBoyTarget.md`, `docs/Portable2DSdkV1.md`, and `docs/ArchitectureRoadmap.md`.

Do not move gameplay behavior into the language layer. Do not add portable SDK behavior without a target capability check. Keep transitional APIs working unless the roadmap explicitly removes them.

## Test The Fix

Use focused tests first, then broader validation:

```bash
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1
dotnet test src/RetroSharp.Cli.Tests/RetroSharp.Cli.Tests.csproj -m:1
dotnet test RetroSharp.sln -m:1
```

Regenerate tracked Game Boy ROMs when source or lowering changes:

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
