# Runner Sample

Sample Layer: `target-acceptance`

The entry point is `runner.retrosharp.json`. It lists the game-owned source
files in build order: constants, player state, camera state, and frame helpers
live in `src/*.rs`, while assets, map loading, enemy declarations, and the main
loop stay in `src/main.rs` so their paths remain relative to this sample
directory. Game data lives under `assets/`: sprite sheets at the root, Tiled
map data in `assets/maps/`, and BGM inputs in `assets/music/`. No local library
package is required for the runner's own code; the manifest loads the built-in
`RetroSharp.Portable2D` SDK through `libraries`. The manifest enables
`namespaceMode: "physical"` with `rootNamespace: "Runner"` and
`sourceRoot: "src"`; helper files live under folders such as `src/player/`, `src/camera/`,
`src/frame/`, and `src/level/`, and those folders become compile-time namespace
segments with no runtime representation.

Build both runner ROMs into `bin/` from the shared project:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- runner.retrosharp.json
```

Build only the Game Boy acceptance runner when you need a focused local ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out bin/runner.gb runner.retrosharp.json
```

Build the NES acceptance runner from the same source. The shared music path resolves to `assets/music/runner.nes.vgz` for NES:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out bin/runner.nes runner.retrosharp.json
```

Preview with RetroArch Flatpak:

```bash
flatpak run --command=retroarch org.libretro.RetroArch \
  -L ~/.var/app/org.libretro.RetroArch/config/retroarch/cores/gambatte_libretro.so \
  --max-frames=180 --max-frames-ss \
  --max-frames-ss-path=runner-retroarch.png \
  bin/runner.gb
```

Headless preview with PyBoy:

```bash
python3 -m pip install --target /tmp/retrosharp-pyboy-site pyboy pillow
PYTHONPATH=/tmp/retrosharp-pyboy-site python3 - <<'PY'
from pyboy import PyBoy
pyboy = PyBoy("bin/runner.gb", window="null")
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

This sample uses a project source set, C#-style physical namespace usings such as `using Runner.Player;` and `using Runner.Frame;`, parameterless helper functions, declarations, assignment, static constant groups such as `Level.Width` and `Player.StartX`, a `type` alias for 16-bit pixel values, an enum for collision flags, restricted static class state with methods such as `player.Reset(view)`, immutable `let` locals, a pipeline for camera-relative collision probes, a `switch` expression for the display frame, `while (true)`, `if`, `&&`, half-open range membership, compound assignments, statement-only `++`/`--`, relational conditions, `Video.WaitVBlank()`, `Input.Poll()`, `Palette.Background(...)`, `Palette.Sprite(...)`, `Camera.Init(...)`, `Camera.Apply()`, `Camera.SetPosition(...)`, `Camera.AabbTiles(...)`, `Camera.AabbHitTop(...)`, `Sprite.Asset(...)`, `Sprite.Draw(...)`, `Animation.Clip(...)`, `Animation.Frame(...)`, `World.Load(...)`, and tick-based button helpers. The runner stores Mario's world X/Y separately from camera X/Y, derives real screen X/Y values into frame-local `let` bindings where needed, lets Mario move inside a central 2-axis dead-zone, and advances the camera state when he leaves that band. Collision passes the projected `screenX` into `Camera.AabbTiles(...)` / `Camera.AabbHitTop(...)` so walls, ceiling, and landing stay aligned after the camera moves; the Y collision value remains the runner's world-row coordinate.

The horizontal speed model moves at a base walk speed while a direction is held, builds extra run speed while B is held only on the ground, preserves horizontal momentum while airborne, coasts to a stop through ground friction when the D-pad is released, and turns instantly when the opposite direction is pressed. The runner updates camera state per single-pixel movement step, then calls `view.ApplyPosition()` near the end of the frame; that helper issues a single `Camera.SetPosition(x, y)`, and the backend walks the camera the rest of the way to the requested position, bounded to at most one tile crossing per axis per frame. On NES that walk queues any newly exposed row/column for the next VBlank. NES `Video.WaitVBlank()` applies pending camera scroll at VBlank entry before sprite DMA, and the following `Camera.Apply()` is skipped unless source code changes the camera after the wait. A queued NES row spans four 8-tile phases plus a fifth attribute phase so runtime row streaming does not overrun VBlank.

The playable route now runs over a tall 24x48 Tiled map, expanded to a 48x96 tile world, with platforms that force vertical and horizontal camera movement on Game Boy and NES; NES wraps that 48-column source across the startup 64-column four-screen buffer so the right-hand nametables never expose blank padding while the camera moves. It flips the same player sheet horizontally through portable `flipX` when facing left, selects sprite palette slot `0` explicitly, declares that sprite palette through the logical palette API, lets the NES PNG sprite sheet derive hardware colors for that draw slot, advances the run frames through an explicit `animTick` plus a portable animation clip, lands on any solid Tiled collision tile while descending by querying the top edge of the first matching tile in a caller-defined search AABB near the feet, bounces back down when the actor's head hits a solid tile from below while rising, blocks horizontal movement when the actor's lower body would overlap a solid Tiled collision tile, resets the actor on falls while clearing horizontal motion without rebasing the scrolled background, and jumps with variable height plus lower-gravity airtime when the A button is pressed.

The constant groups are source-level static declarations used for readability and folded like numeric constants; they do not allocate state. The class methods are source-level inline helpers over fixed-layout value storage, so the runner keeps a lightweight object-oriented shape without heap allocation, vtables, dynamic dispatch, or hidden calls.

The frame loop is intentionally narrow: `PresentFrame(...)` owns VBlank and writes the player OAM entries, then the loop calls `Camera.Apply()`, draws the enemy pool with `goombas.Draw()`, and ticks `Audio.Update()` once per frame. On NES, the pending camera scroll has already been restored by `Video.WaitVBlank()` at VBlank entry, so the explicit `Camera.Apply()` normally acts as a same-frame guard instead of writing `$2000/$2005` after sprite work. `frame.Begin()` clears transient collision state, `player.ApplyGravity()` advances physics, a local `screenX` feeds the landing and ceiling probes, `view.FollowPlayer(player)` updates vertical dead-zone camera state, `frame.ResolveSolidLanding(...)`, `frame.ResolveCeilingHit(...)`, `frame.ResolveFall(...)`, and `frame.ResolveReset(player, view)` handle collision outcomes, `player.HandleJumpInput()` and `view.HandleHorizontalInput(player, movementFootWorldY)` consume input and horizontal inertia, `view.ApplyPosition()` synchronizes the camera backend with a single `Camera.SetPosition`, and `player.UpdateRunAnimation(view)` derives the displayed frame. The helper boundaries are chosen around gameplay responsibilities, not around individual opcodes, so the source remains readable while still lowering to inline target code.

The runner also hosts a single animated enemy through the scalable actor framework (see `docs/ActorFrameworkRoadmap.md`). `Actors.Pool(goombas, 1)` declares a fixed one-slot pool, `Enemies.Def(Goomba, sprite: goomba, behavior: Patrol, animation: goomba_walk, ...)` declares the enemy as data, and `Actors.SpawnLayer(goombas, "assets/maps/runner.tmj", "actors")` activates the Goomba placed in the `actors` Tiled object layer once the camera window reaches it. `goombas.Update()` runs the `Patrol` behavior (pace left/right and advance the walk animation) and `goombas.Draw()` draws the enemy camera-relative, moving its sprite offscreen when the slot is inactive or outside the camera window so Game Boy OAM does not retain a stale frame. The two-frame `assets/goomba.png` walk sheet resolves to `assets/goomba.gb.png` on Game Boy and `assets/goomba.nes.png` on NES; the NES variant currently reuses the player's sprite-slot colors so both share sprite palette slot `0` and can be recolored later.

The background music is declared as `assets/music/runner.vgz`. Game Boy resolves that to `assets/music/runner.gb.vgz`; NES resolves it to `assets/music/runner.nes.vgz`. Both are VGM/VGZ register-write logs that feed the target audio repack through `Music.Asset(...)`, `Audio.Init()`, `Music.Play(...)`, and the per-frame `Audio.Update()` call. The older `assets/music/delight.gbapu` trace remains in the folder as a licensed Tronimal reference asset derived from `assets/music/free_06_delight.uge`, and `assets/music/sml2_track1.gbapu` remains as the previous Game Boy-only runner trace for comparison/provenance checks.

The editable level lives at `assets/maps/runner.tmj` and can be opened in Tiled together with `assets/maps/Super Mario Land 2.tsx`. The map is 24x48 source cells with `retrosharpWorldY = 0`, `retrosharpWorldHeight = 48`, and `retrosharpStreamY = 0`, so the full height is imported and each 16x16 cell expands to 2x2 target tiles. The `background` layer draws decorative source tiles, and the `world` layer draws streamable terrain. Game Boy still has one scrolling background tilemap, so the importer flattens those authoring layers: `background` is the visual base, non-empty `world` cells draw over it, and empty `world` cells keep the background tile underneath. When `retrosharpWorldY` and `retrosharpStreamY` move the playable world slice vertically on screen, the background layer uses that same offset so the Tiled layers stay aligned. Collision remains separate from that visual composition and comes from the tileset's object rectangles; tiles with an `objectgroup` become solid in the generated world flags. The importer resolves external `.tsj`/`.tsx` tilesets and can substitute target PNG variants for their image source: Tiled edits the shared baseline `assets/maps/tilesheets.png`, while Game Boy uses `assets/maps/tilesheets.gb.png` and NES uses `assets/maps/tilesheets.nes.png` when those files are present. NES derives a universal background color, up to four background palette slots, and the initial attribute table from the placed tiles in `assets/maps/tilesheets.nes.png`, so the runner can keep the cyan sky, white clouds, yellow terrain, and green grass/tubes in the initial two-nametable buffer. The importer then expands each 16x16 source cell into target 8x8 tiles, maps source colors to target palette indexes, deduplicates repeated generated tiles, and stores the result in the ROM. The map custom property `retrosharpStreamY` is expressed in generated Game Boy 8x8 tile rows; `retrosharpWorldY` and `retrosharpWorldHeight` select the source Tiled rows imported into the active streaming world before expansion.

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
