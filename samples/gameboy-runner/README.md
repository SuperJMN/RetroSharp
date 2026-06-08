# Game Boy Runner Sample

Sample Layer: `target-acceptance`

Build a `.gb` ROM from RetroSharp source that executes a real loop on the Game Boy CPU:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out runner.gb runner.rs
```

Preview with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=runner-retroarch.png \
  runner.gb
```

Headless preview with PyBoy:

```bash
python3 -m pip install --target /tmp/retrosharp-pyboy-site pyboy pillow
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 - <<'PY'
from pyboy import PyBoy
pyboy = PyBoy("runner.gb", window="null")
for _ in range(180):
    pyboy.tick()
pyboy.screen.image.save("runner-pyboy.png")
pyboy.stop()
PY
```

For bug isolation, use the incremental diagnostics under `diagnostics/` or run the full matrix:

```bash
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 ../../tools/gameboy/runner_diagnostics.py
```

This sample uses parameterless helper functions, declarations, assignment, `while`, `if`, relational conditions, `video_wait_vblank()`, `input_poll()`, `camera_init(...)`, `camera_apply()`, `camera_set_position(...)`, `collision_aabb_tiles(...)`, `sprite_asset(...)`, `sprite_draw(...)`, `animation_clip(...)`, `animation_frame(...)`, `world_map(...)`, `world_column(...)`, `world_flags(...)`, `tilemap_set(...)`, and tick-based button helpers. The actor keeps a fixed screen X while the camera moves through the Game Boy `SCX` register, derives world-space collision X from the camera position, streams the compact playable map from ROM while D-pad right or left is held, flips the same player sheet horizontally through portable `flipX` when facing left, selects sprite palette slot `0` explicitly, advances the run frames through an explicit `animTick` plus a portable animation clip, clamps upward movement at the top of the scene before byte-backed Y can wrap, wraps left/center/right foot probes to the active 16-column world width, lands on an elevated one-way platform only while descending or on the ground row, shows holes and wider failure tiles before the actor reaches them, bounces visibly on hazard contact, resets the actor on falls/enemy contact without rebasing the scrolled background, and jumps with variable height when the Game Boy A button is pressed.

The background uses five built-in Game Boy tiles: empty sky, cloud, distant hill, hazard spikes, and brick/ground. Decorative clouds and hills are placed with static `tilemap_set(...)` calls so the visual background does not inflate the runtime streaming path. The playable platforms, holes, ground, and hazard flags stay in the `world_map(...)` data so visual tiles and collision flags remain synchronized for gameplay.

Enemies are original two-frame 16x16 sprites in `assets/enemy-slug.gb.png`. One slug loops from right to left on the ground and resets the actor on simple screen-space contact; another sits on the raised platform to exercise multiple logical sprite draws in the same frame.

The runner sprite sources are `assets/mario-idle.aseprite`, `assets/mario-run.aseprite`, and `assets/mario-jump.aseprite`. Export them to the PNGs used by RetroSharp with:

```bash
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-idle.aseprite --sheet assets/mario-idle.gb.png --sheet-type horizontal
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-jump.aseprite --sheet assets/mario-jump.gb.png --sheet-type horizontal
```

Combine the state exports into `assets/mario-player.gb.png` as five 18x32 frames: idle, three run frames, and jump. The runner uses that single sheet so the same OAM slots are updated every frame:

```bash
tmpdir=$(mktemp -d)
convert assets/mario-idle.gb.png -background none -gravity South -extent 18x32 PNG32:"$tmpdir/idle.png"
convert assets/mario-run.gb.png -crop 16x27+0+0 -background none -gravity South -extent 18x32 PNG32:"$tmpdir/run0.png"
convert assets/mario-run.gb.png -crop 16x27+16+0 -background none -gravity South -extent 18x32 PNG32:"$tmpdir/run1.png"
convert assets/mario-run.gb.png -crop 16x27+32+0 -background none -gravity South -extent 18x32 PNG32:"$tmpdir/run2.png"
convert assets/mario-jump.gb.png -background none -gravity South -extent 18x32 PNG32:"$tmpdir/jump.png"
convert "$tmpdir/idle.png" "$tmpdir/run0.png" "$tmpdir/run1.png" "$tmpdir/run2.png" "$tmpdir/jump.png" +append PNG32:assets/mario-player.gb.png
rm -rf "$tmpdir"
```

The compiler pads logical sprite sizes internally so they fit Game Boy 8x16 hardware sprites. Keep the background transparent and use at most three opaque colors.
