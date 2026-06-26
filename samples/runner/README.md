# Runner Sample

Sample Layer: `target-acceptance`

Build the Game Boy acceptance runner from the shared source:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out runner.gb runner.rs
```

Build the NES acceptance runner from the same source. NES accepts the audio calls as no-ops until NES BGM lowering exists:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out runner.nes runner.rs
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

For the full debugging process used by agents, including how to classify symptoms, cross-check emulator/debug-tool output, and choose the correct layer to fix, see `../../docs/GameBoyRunnerDebugging.md`.

This sample uses parameterless helper functions, declarations, assignment, enum-backed constant groups such as `World.Width` and `Player.ScreenX`, a `type` alias for pixel-like byte-backed values, an enum for collision flags, restricted static class state with methods such as `player.Reset()`, immutable `let` locals, a pipeline for camera-relative collision probes, a `switch` expression for the display frame, `loop`, `if`, `&&`, half-open range membership, compound assignments, statement-only `++`/`--`, relational conditions, `video.WaitVBlank()`, `input.Poll()`, `palette.Background(...)`, `palette.Sprite(...)`, `camera.Init(...)`, `camera.Apply()`, `camera.SetPosition(...)`, `camera.AabbTiles(...)`, `camera.AabbHitTop(...)`, `sprite.Asset(...)`, `sprite.Draw(...)`, `animation.Clip(...)`, `animation.Frame(...)`, `world.Load(...)`, and tick-based button helpers. The runner actor keeps a fixed screen X while the camera moves through the target scroll path (`SCX` on Game Boy and PPU scroll on NES), derives collision X from the camera runtime so it stays aligned beyond the source-local byte range, streams the compact playable map from ROM through a small horizontal speed model that moves at a base walk speed while a direction is held, builds extra run speed while B is held only on the ground (Mario has traction) and preserves horizontal momentum while airborne, coasts to a stop through ground friction when the D-pad is released, repositions the camera once per single-pixel step so a multi-pixel run frame keeps the 1px-per-call camera follower in sync across the source byte wrap, and turns instantly when the opposite direction is pressed so the actor never drifts backward. It flips the same player sheet horizontally through portable `flipX` when facing left, selects sprite palette slot `0` explicitly, declares that sprite palette through the logical palette API, lets the NES PNG sprite sheet derive hardware colors for that draw slot, advances the run frames through an explicit `animTick` plus a portable animation clip, clamps upward movement at the top of the scene before byte-backed Y can wrap, lands on any solid Tiled collision tile while descending by querying the top edge of the first matching tile in a caller-defined search AABB near the feet, bounces back down when the actor's head hits a solid tile from below while rising, blocks horizontal camera motion when the actor's lower body would overlap a solid Tiled collision tile, resets the actor on falls while clearing horizontal motion without rebasing the scrolled background, and jumps with variable height plus lower-gravity airtime when the A button is pressed.

The constant groups are compile-time enum members in the current language. They are used for readability and fold like numeric constants; they do not allocate state. The class methods are source-level inline helpers over fixed-layout value storage, so the runner keeps a lightweight object-oriented shape without heap allocation, vtables, dynamic dispatch, or hidden calls.

The frame loop is intentionally narrow: `PresentFrame(...)` owns VBlank, ticks `audio.Update()`, applies the camera, and draws sprites; `frame.Begin()` clears transient collision state, `player.ApplyGravity()` advances physics, `frame.ResolveSolidLanding(...)`, `frame.ResolveCeilingHit(...)`, `frame.ResolveFall(...)`, and `frame.ResolveReset(player, view)` handle collision outcomes, `player.HandleJumpInput()` and `view.HandleHorizontalInput(player, movementFootWorldY)` consume input and horizontal inertia, and `player.UpdateRunAnimation(view)` derives the displayed frame. The helper boundaries are chosen around gameplay responsibilities, not around individual opcodes, so the source remains readable while still lowering to inline target code.

The background music is `music/sml2_track1.gbapu`, a Game Boy APU register trace used directly as a Game Boy BGM resource through `music.Asset(...)`, `audio.Init()`, `music.Play(...)`, and the per-frame `audio.Update()` call. NES validates those calls and lowers them as no-ops until NES BGM has its own implementation. The older `music/delight.gbapu` trace remains in the folder as a licensed Tronimal reference asset derived from `music/free_06_delight.uge`.

The editable level lives at `maps/runner.tmj` and can be opened in Tiled together with `maps/Super Mario Land 2.tsx`. The `background` layer draws decorative source tiles, and the `world` layer draws streamable terrain. Game Boy still has one scrolling background tilemap, so the importer flattens those authoring layers: `background` is the visual base, non-empty `world` cells draw over it, and empty `world` cells keep the background tile underneath. When `retrosharpWorldY` and `retrosharpStreamY` move the playable world slice vertically on screen, the background layer uses that same offset so the Tiled layers stay aligned. Collision remains separate from that visual composition and comes from the tileset's object rectangles; tiles with an `objectgroup` become solid in the generated world flags. The importer resolves external `.tsj`/`.tsx` tilesets and can substitute target PNG variants for their image source: Tiled edits the shared baseline `maps/tilesheets.png`, while Game Boy uses `maps/tilesheets.gb.png` and NES uses `maps/tilesheets.nes.png` when those files are present. NES derives a universal background color, up to four background palette slots, and the initial attribute table from the placed tiles in `maps/tilesheets.nes.png`, so the runner can keep the cyan sky, white clouds, yellow terrain, and green grass/tubes in the initial two-nametable buffer. The importer then expands each 16x16 source cell into target 8x8 tiles, maps source colors to target palette indexes, deduplicates repeated generated tiles, and stores the result in the ROM. The map custom property `retrosharpStreamY` is expressed in generated Game Boy 8x8 tile rows; `retrosharpWorldY` and `retrosharpWorldHeight` select the source Tiled rows imported into the active streaming world before expansion.

The runner sprite sources are `assets/mario-idle.aseprite`, `assets/mario-run.aseprite`, and `assets/mario-jump.aseprite`. Export them to the PNGs used by RetroSharp with:

```bash
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-idle.aseprite --sheet assets/mario-idle.gb.png --sheet-type horizontal
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
/home/jmn/Repos/Aseprite/build/bin/aseprite -b assets/mario-jump.aseprite --sheet assets/mario-jump.gb.png --sheet-type horizontal
```

Combine the state exports into `assets/mario-player.gb.png` as five 18x32 frames: idle, three run frames, and jump. The runner source asks for `assets/mario-player.png`; Game Boy resolves that to `assets/mario-player.gb.png`, and NES resolves it to `assets/mario-player.nes.png` when present. The runner uses that single sheet so the same OAM slots are updated every frame:

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
