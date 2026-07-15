# Runner Sample

Sample Layer: `target-acceptance`

The entry point is `runner.retrosharp.json`. It lists the game-owned source
files in build order: constants, player state, camera state, and frame helpers
live in `src/*.rs`, while assets, map loading, enemy declarations, and the main
loop stay in `src/main.rs` so their paths remain relative to this sample
directory. Game data lives under `assets/`: sprite sheets at the root, Tiled
map data in `assets/maps/`, BGM inputs in `assets/music/`, and one-shot action
FX in `assets/sfx/`. No local library
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
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --runtime-abi-out bin/runner.nes.runtime-abi.json --out bin/runner.nes runner.retrosharp.json
```

The NES sidecar is the versioned, ROM-bound address contract consumed by the
power-on, cadence, and visual-parity probes. Regenerate it with the ROM; a hash
mismatch is rejected before an emulator starts.

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

This sample uses a project source set, C#-style physical namespace usings such as `using Runner.Player;` and `using Runner.Frame;`, parameterless helper functions, declarations, assignment, static constant groups such as `Level.Width` and `Player.StartX`, a `type` alias for 16-bit pixel values, an enum for collision flags, restricted static class state with methods such as `player.Reset(view)`, immutable `let` locals, a pipeline for camera-relative collision probes, a `switch` expression for the display frame, `while (true)`, `if`, `&&`, half-open range membership, compound assignments, statement-only `++`/`--`, relational conditions, `Video.WaitVBlank()`, `Input.Poll()`, `Palette.Background(...)`, `Palette.Sprite(...)`, `Camera.Init(...)`, `Camera.Apply()`, `Camera.SetPosition(...)`, `Camera.AabbTiles(...)`, `Camera.AabbHitTop(...)`, `Camera.VerticalScrollMax()`, `Sprite.Asset(...)`, `Sprite.Draw(...)`, `Animation.Clip(...)`, `Animation.Frame(...)`, `World.Load(...)`, and tick-based button helpers. The runner stores Mario's world X/Y separately from camera X/Y, derives real screen X/Y values into frame-local `let` bindings where needed, lets Mario move inside a central 2-axis dead-zone, and advances the camera state when he leaves that band. Collision passes the projected `screenX` into `Camera.AabbTiles(...)` / `Camera.AabbHitTop(...)` so walls, ceiling, and landing stay aligned after the camera moves; the Y collision value remains the runner's world-row coordinate.

The horizontal speed model moves at a brisk base walk speed while a direction is held, builds a faster run speed while B is held only on the ground, preserves horizontal momentum while airborne, coasts to a stop through ground friction when the D-pad is released, and turns instantly when the opposite direction is pressed. A movement tick can contain two single-pixel substeps. Both collision probes project against a snapshot of camera X from the start of that tick, while source camera state still advances after each accepted pixel; this prevents the second run-speed probe from repeating the first world column and entering a solid by one pixel. The runner then calls `view.ApplyPosition()` near the end of the frame; that helper issues a single `Camera.SetPosition(x, y)`, and the Game Boy backend walks the camera the rest of the way to the requested position, bounded to two same-axis tile crossings (16 px) per frame with both newly exposed edges committed during `Camera.Apply()`. Diagonal Game Boy movement still staggers column/row edge commits by axis. On NES that walk queues any newly exposed row/column for the next VBlank. NES `Video.WaitVBlank()` applies pending camera scroll at VBlank entry before sprite DMA, and the following `Camera.Apply()` is skipped unless source code changes the camera after the wait. A queued NES row spans four 8-tile phases plus a fifth attribute phase so runtime row streaming does not overrun VBlank.

The runner loads the complete `stage1.tmj` design: 156x20 source cells expand to 312x40 hardware tiles (2496x320 px) without trimming. Both targets consume their exact production `WorldPack`; Game Boy emits a 128 KiB MBC1 cartridge and NES automatically selects its 64 KiB PRG / 16 KiB CHR MMC3 four-screen profile while retaining BGM, jump SFX, and NES DPCM. World-Y collision is word-backed: the landing caller stores `Camera.AabbHitTop(...)` in an `i16` and reaches the solid floor at Y 304 or the green one-way ledges at Y 272. Landing/support queries use the combined `Landable = Solid | Platform` mask, while wall and ceiling queries still request only `Solid`. A non-rising actor lands only when its previous/current feet straddle the returned tile top, so it crosses a `Platform` from below, accepts a downward step that crosses a top still overlapping the landing window, and rests on it from above; a grounded actor with no landable support clears `grounded` and falls after walking off an edge. `Camera.VerticalScrollMax()` folds to each target's real `worldHeight - screenHeight` bound, so the same source can follow both axes despite the different view heights. The runner also flips the player through portable `flipX`, derives animation frames from `animTick`, resolves walls/ceilings/landings from tileset-authored collision, and resets falls without rebasing the world.

Vertical motion mirrors the Super Mario Bros. 3 jump model in signed 4.4 fixed point. A jump starts at `-$38`; the runner's walking, running, and maximum B-speed tiers select `-$3A`, `-$3C`, and `-$40`. Gravity is applied before motion: `+1` only while A remains held and velocity is below `-$20`; release or velocity `>= -$20` applies `+5` without clamping, with a terminal fall velocity of `$45`. The player keeps an integer world Y plus a `0..15` subpixel remainder, so this remains fixed storage and direct arithmetic on both 8-bit targets. Real-ROM tests pin the resulting rises at `330/16 = 20.625 px` for a tap, `1131/16 = 70.6875 px` from rest, `1361/16 = 85.0625 px` while running, and `1607/16 = 100.4375 px` at the maximum-speed tier. The standard visible apex is therefore 71 pixels, or about 4.4 authored 16x16 blocks.

The constant groups are source-level static declarations used for readability and folded like numeric constants; they do not allocate state. The class methods are source-level inline helpers over fixed-layout value storage, so the runner keeps a lightweight object-oriented shape without heap allocation, vtables, dynamic dispatch, or hidden calls.

The frame loop is intentionally narrow: it enters VBlank, applies at most one prepared camera edge, presents the player, then ticks `Audio.Update()` and `Input.Poll()` once per frame. Game Boy keeps bank selection, directory lookup, and decode outside VBlank; the commit is only a resident 19-tile column or 21-tile row copy. Because the 18x32 player metasprite already consumes the remaining OAM budget, a packed-edge commit frame retains its previous OAM entries and refreshes them on the next frame, keeping audio/input/simulation at 60 Hz and all VRAM/OAM writes legal. On NES, `Video.WaitVBlank()` restores the pending four-screen camera state at NMI entry and the explicit `Camera.Apply()` remains the source-level guard. The gameplay helpers then handle gravity, collision, reset, input, camera request, and animation without hidden allocation or dispatch.

The runner keeps the scalable actor framework out of this sample while exercising the complete `stage1` course with its 2-axis camera. Actor pools, data-defined enemy behavior, and Tiled object-layer spawning remain covered by `samples/actor-framework/actors.rs` and the roadmap in `docs/ActorFrameworkRoadmap.md`.

The background music is declared as `assets/music/runner.vgz`. Game Boy resolves that to `assets/music/runner.gb.vgz`; NES resolves it to `assets/music/runner.nes.vgz`. Both are VGM/VGZ register-write logs that feed the target audio repack through `Music.Asset(...)`, `Audio.Init()`, `Music.Play(...)`, and the per-frame `Audio.Update()` call. The jump effect is declared as `assets/sfx/smb-jump.vgm`; Game Boy resolves that to `assets/sfx/smb-jump.gb.vgm`, while NES uses the supplied `assets/sfx/smb-jump.vgm` 2A03 capture. NES filters the capture down to the first supported channel-register burst so captured global APU writes cannot leak into the BGM runtime. It is played through `Sfx.Play(jump_sfx)` when the runner accepts a grounded jump. The older `assets/music/delight.gbapu` trace remains in the folder as a licensed Tronimal reference asset derived from `assets/music/free_06_delight.uge`, and `assets/music/sml2_track1.gbapu` remains as the previous Game Boy-only runner trace for comparison/provenance checks.

The runtime and editable level is `assets/maps/stage1.tmj` (with `stage1.tmx` as the Tiled source), 156x20 source cells over `assets/maps/stage1.tsx` and `stage1.png`. The JSON carries the `world` layer name plus the whole-world stream properties required by the importer, so no derived crop is involved. `stage1.playable.tmj` and `tools/runner/build_stage1_playable_map.py` remain only as historical/smaller-fixture assets. Collision stays separate from visual composition and comes from `stage1.tsx`: tiles with an `objectgroup` become solid, while green ledge tile `30` declares `retrosharpCollision=platform`. This produces 788 solid and 56 platform hardware cells; the current serialized pack is 2,568 bytes on Game Boy and 2,780 bytes on NES. Each target importer expands 16x16 source cells into 8x8 hardware tiles, maps colors and palette provenance, deduplicates patterns, and serializes the complete target-owned packed world.

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
