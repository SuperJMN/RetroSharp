# Game Boy Target

Status: experimental, intentionally narrow.

The Game Boy target is the first playable target. It currently compiles a constrained RetroSharp subset directly to a DMG cartridge image. Programs that fit still emit a 32 KiB ROM-only cartridge; programs whose code, read-only data, or music outgrow that limit can emit an MBC1 banked ROM with bank selection hidden behind the generated runtime. This is not yet the shared IR backend path; it is a focused proving ground for the video/runtime API.

See `ArchitectureRoadmap.md` for the persistent architecture roadmap that separates the RetroSharp language, portable 2D SDK, and target intrinsics, and `Portable2DSdkV1.md` for the current SDK v1 reference. This file tracks the current Game Boy target subset and runner milestones.

## Target Capabilities

The Game Boy target exposes `GameBoyTarget.Capabilities` for portable 2D capability checks and `GameBoyTarget.AudioCapabilities` for audio capability checks.

| Capability | Value |
| --- | --- |
| Target name | `gb` |
| Screen pixels | 160x144 |
| Visible tile grid | 20x18 |
| Tile size | 8x8 |
| Background buffer | 32x32 tiles |
| Fine scroll | X and Y |
| Background tile write budget | 20 tile writes per frame |
| Attribute write budget | 0 per frame on the current DMG target |
| Hardware sprites | 40 total, 10 per scanline |
| Sprite size modes | 8x8 and 8x16 |
| Sprite palettes | 2 object palette slots |
| Background palettes | 1 background palette slot |
| Sprite transforms | Flip X and Flip Y |
| HUD modes | Window and sprite HUD; split-scroll HUD is not declared portable support |
| Collision queries | World tile flags, world AABB, camera-relative AABB, and camera-relative AABB hit-top |
| BGM formats | hUGETracker `.uge`; `.gbapu` / `.gbapu.json` APU traces; `.gbs` can be exported to `.gbapu` with the CLI helper |
| Cartridge size | 32 KiB ROM-only when possible; transparent MBC1 banked code, read-only data, and music up to 512 KiB ROMs |

## SDK Operation Boundary

`GameBoyRomCompiler.CollectSdkOperations(...)` exposes the first compiler boundary where portable 2D calls become semantic `Sdk2DOperation` records before Game Boy ROM lowering. The current boundary recognizes:

- `video.WaitVBlank()` as `Sdk2DOperation.WaitFrame`
- `input.Poll()` as `Sdk2DOperation.PollInput`
- `camera.SetPosition(x, y)` as `Sdk2DOperation.SetCameraPosition`
- `camera.Apply()` as `Sdk2DOperation.ApplyCamera`
- `sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` as `Sdk2DOperation.DrawLogicalSprite`
- `map_stream_column(targetColumn, sourceColumn, y, height)` as `Sdk2DOperation.StreamMapColumn`
- `world_tile_flags_at(worldX, worldY)` as `Sdk2DOperation.ReadWorldTileFlags`
- `camera.AabbTiles(screenX, worldY, width, height, flags)` as `Sdk2DOperation.CameraAabbTiles`
- `camera.AabbHitTop(screenX, worldY, width, height, flags)` as `Sdk2DOperation.CameraAabbHitTop`
- `hud.SetTile(window, x, y, tile)` as `Sdk2DOperation.SetHudTile`

`GameBoyRomCompiler.CollectSdkAudioOperations(...)` exposes the parallel audio boundary where portable audio calls become semantic `SdkAudioOperation` records. The current boundary recognizes:

- `audio.Init()` as `SdkAudioOperation.InitializeAudio`
- `music.Play(name)` as `SdkAudioOperation.PlayMusic`
- `audio.Update()` as `SdkAudioOperation.UpdateAudio`
- `music.Stop()` as `SdkAudioOperation.StopMusic`

`Sdk2DOperation.WaitFrame` now lowers through `GameBoySdkOperationLowerer` to the same VBlank edge wait routine previously emitted directly by `video.WaitVBlank()`. Logical sprite draw, explicit map-column streaming, and camera-relative AABB collision also lower through the SDK operation path while preserving the existing Game Boy byte emission. The runtime compiler consumes `program.SdkOperations` for migrated SDK calls instead of reconstructing those operations from the AST, and it fails if the collected operation stream and source call sites diverge. Game Boy compilation also runs the shared frame-budget pass, so multiple explicit map-column streams that exceed the 20-tile background write budget, more than 40 hardware sprites, or more than 10 constant-Y sprites on a scanline in one possible frame fail before lowering.

Target intrinsics and transitional helpers such as `sprite.Set(...)`, `scroll.Set(...)`, raw tilemap writes, and direction-specific camera movement still lower through the direct Game Boy path. Future roadmap tasks should move them only after adding the appropriate portable operation and capability checks.

## Sample Classification

Sample portability is tracked in `samples/manifest.json`. `samples/cross-target-camera/camera.rs` is the current `portable-sdk` sample. `samples/gameboy-drawing/drawing.rs` is a `target-intrinsic` sample, `samples/gameboy-hud/hud.rs` and `samples/gameboy-music/music-switch.rs` are `target-capability-spike` samples, `samples/gameboy-vscroll/vscroll.rs` is the Game Boy-only vertical camera `target-acceptance` sample, `samples/actor-framework/actors.rs` is the focused Game Boy/NES `target-acceptance` sample for the actor framework, and `samples/runner/runner.rs` remains a shared Game Boy/NES `target-acceptance` sample because it exercises richer runner behavior than the stable portable SDK sample. NES accepts runner audio calls as no-ops until real BGM lowering exists.

## Supported Runtime Subset

- `void main()`
- Top-level and block-local `const` declarations, with or without type annotations, folded into literal expressions
- Type aliases normalized to their underlying type before Game Boy lowering
- `sizeof(type)` folded into literal byte-size expressions for primitive, pointer, enum, and plain struct types
- `offsetof(type, field)` folded into literal byte-offset expressions for direct fields of plain struct types
- `countof(array)` folded into literal element-count expressions for fixed-size local arrays
- Decimal, hexadecimal (`0x2A`), binary (`0b1010_0000`), `_`-separated, and width-suffixed (`255u8`, `0x1234u16`) integer literals folded to the same immediates
- Top-level `enum` declarations with qualified members folded into literal expressions
- User helper calls inline with parameter substitution, including named arguments, default parameter values, helpers whose body is exactly one `return expr;`, or expression-bodied helpers written as `Ret name(args) => expr;` used as value expressions; there is no runtime call/return overhead for the current cartridge target path
- Local byte-backed variables declared as `i8`, `u8`, `i16`, `u16`, `bool`, or a declared enum type
- Plain local `struct` declarations whose fields are byte-backed types, with named and shorthand initializer lists
- Fixed-size local arrays of byte-backed types with initializer lists, optional initializer-inferred lengths, and constant or byte-backed runtime index reads/writes, for example `u8 values[4] = [1, 2]` or `u8 values[] = [1, 2]`
- Fixed-size local arrays of plain structs whose fields are byte-sized (`u8`, `i8`, `bool`, or enums), with constant or byte-backed runtime field reads/writes such as `actors[i].x`
- Actor framework pool/definition sugar: `actor.Pool(...)`, `actor.SpawnLayer(...)`, `actor.SpawnWindow(...)`, `enemy.Def(...)`, and called `enemy.*` metadata helpers expand before Game Boy lowering to fixed `Actor` struct arrays, constants, inline helper branches, generated ROM-table spawn helpers, a fixed `used[]` activation byte array, and runtime camera-window activation loops from Tiled object layers; unused `enemy.*` lookup helpers are not generated and have zero byte cost; actor world X uses byte split storage (`x` low byte plus `xHi` high byte), while `y` remains byte-sized; `pool.Update()`/`pool.Draw()` support the basic byte-field behavior set (`Walker`, `Flyer`, `Patrol`, `Shooter`, `Hazard`, and direction-driven `Chaser`); optional `animation` metadata draws through `animation.Frame(...)`; actor draw reads camera X once per generated draw loop and computes `screenX = worldX - cameraX`; one-slot pools write offscreen coordinates for inactive or off-window slots and then call `sprite.Draw` through stable call sites so direct Game Boy OAM entries do not retain stale actor frames, while larger pools keep the current visible-actor draw/cull shape until per-slot OAM allocation is added; `pool.TouchTiles(...)`/`pool.LandOnTiles(...)` use the same per-phase camera-X cache and per-actor projection before lowering through camera-relative AABB SDK calls, while `pool.TouchPlayer(...)` compares the projected actor AABB against a literal player AABB in screen coordinates; drawn pool sprite pressure is checked as pool capacity times the largest target-resolved enemy metasprite against the Game Boy 40-sprite frame cap and 10-sprite scanline cap, spawn windows are checked against maximum simultaneously activatable spawns, and actor draw usage also participates in the shared frame-budget validation
- Constant initializers
- Assignment and compound assignment (`+=`, `-=`, `&=`, `|=`, `^=`) to local variables, `struct.field` lvalues, constant or byte-backed runtime array indices, and fixed struct-array field lvalues
- Statement-only `++` and `--` on the same lvalues, including the increment slot of `for`
- Explicit casts such as `(u8)expr` to byte-backed local types as zero-cost expression markers
- `while`
- `do while` with post-body conditions and `continue` targeting the final condition check
- `loop` as explicit infinite-loop sugar over `while (true)`
- `for` loops with local or assignment initializers, byte-backed conditions supported by the current relational lowering, and assignment increments
- Half-open range `for` loops such as `for (u8 i in 0..countof(values))`
- `break` and `continue` inside `while`, `do while`, and `for`
- `if`/`else if`/`else`
- No-fallthrough `switch` with block-owned cases and multi-value or half-open range cases lowered to direct `if`/`else` compare chains
- Half-open range membership expressions such as `tile in 1..4`, lowered to the same direct bounds checks as `tile >= 1 && tile < 4`
- `+` between byte-backed runtime expressions
- `-` between constants, byte-backed runtime expressions, and nested byte-backed expressions
- `&`, `|`, and `^` between byte-backed expressions or constants for flag masks
- `==`, `!=`, `<`, `<=`, `>`, and `>=` conditions between constants, byte-backed runtime expressions, and nested byte-backed expressions
- Short-circuit `&&`/`||` and unary `!` in branch conditions built from the supported condition forms, and as byte-backed 0/1 value expressions
- Conditional value expressions (`condition ? whenTrue : whenFalse`) over byte-backed branch expressions
- `map_tile_at(...)`, `map_flags_at(...)`, `world_tile_flags_at(...)`, `collision_aabb_tiles(...)`, `camera_aabb_tiles(...)`, and `camera_aabb_hit_top(...)` as value expressions for runtime map queries
- `button_pressed(...)` as a value expression for joypad queries
- `button_down(...)`, `button_just_pressed(...)`, `button_just_released(...)`, and `button_hold_ticks(...)` as tick-based input value expressions
- `true` and `false`

Current numeric and enum locals are stored as one byte in WRAM. Types wider than one byte are accepted only as source-level convenience for this prototype. Type aliases are normalized to their underlying type before Game Boy lowering, so `type ActorIndex = u8;` has the same runtime shape as `u8`. Top-level constants, block-local constants, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, and enum members are substituted before ROM lowering and do not reserve WRAM. Integer literal spelling is source-only: decimal, `0x` hexadecimal, `0b` binary, `_`-separated, and `u8`/`i8`/`u16`/`i16` suffixed forms all fold to the same immediate value. Constant type annotations are optional; omitting one does not change the emitted code because the constant disappears before target lowering. Constant boolean literals normalize to `1` or `0`, and constant conditional expressions select one branch before ROM lowering. `sizeof(type)` returns the compile-time byte size used by the current layout model: 1 for byte-backed primitives, `bool`, and enums; 2 for 16-bit primitive and pointer types; and the sum of field sizes for plain structs. `offsetof(type, field)` returns the matching direct-field byte offset for plain structs. `countof(array)` returns the declared element count of a fixed-size local array visible at that point. Block-local constants can reference earlier constants visible at that point, including simple integer expressions such as `StartX + 1`, then disappear from the runtime block. Plain local structs are flattened to adjacent WRAM byte slots for their fields, so `position.x` and `position.y` have the same runtime cost as two separately declared locals. Struct initializer lists such as `Vec2 position = { y: seed + 1, x: 2 };` lower to the same zero-fill plus direct field stores as declaring the struct and assigning those members by hand; field expressions are emitted in declaration order, shorthand fields such as `{ x, y: seed + 1 }` are parsed as `x: x`, and omitted fields remain zero. Fixed-size local arrays are also flattened to adjacent WRAM byte slots; `values[0]` and `values[1]` compile to direct absolute loads/stores with no heap and no runtime indexing helper. Initializer lists such as `u8 values[4] = [1, seed, seed + 1];` lower to the same zero-fill plus direct element stores as declaring the array and assigning those constant indices by hand; `u8 values[] = [1, seed, seed + 1];` infers the fixed length `3` before lowering and emits the same bytes as the explicit length form. Omitted trailing elements remain zero when the explicit length is larger than the initializer list. Runtime element access such as `values[i]` computes `HL = base + i` and loads or stores through `HL`; it does not add an implicit bounds check or mask. Fixed-size local struct arrays reserve one flattened byte-sized field slot per element field, for example `actors[0].x`, `actors[0].y`, `actors[1].x`, and `actors[1].y`. Constant indexed field access uses the flattened field address directly. Runtime indexed field access such as `actors[i].x` computes `HL = actors[0].x + i * targetStructStride` and loads or stores through `HL`. Struct-array initializers such as `Actor actors[3] = [{ x: 1, active: 1 }, { y: seed + 1 }];` zero-fill the array and then emit direct field stores for listed fields. True `i16`/`u16` fields in struct arrays are rejected until mixed-width pool layout exists.

`++` and `--` are statement-only source sugar over `+= 1` and `-= 1`. They can be used as standalone statements or as the increment in a `for` loop, and lower to the same direct load/arithmetic/store sequences as the expanded compound assignment.

Byte-backed runtime subtraction and comparisons are general expression features in the Game Boy target, not actor-framework-only lowering. Constant operands use immediates, simple runtime operands use direct register sequences, and nested variable-vs-variable forms preserve the left operand with `PUSH AF` while the right operand is evaluated before `SUB`/`CP`. This avoids reusing a shared WRAM scratch byte across recursive expression emission.

Bitwise `&`, `|`, `^`, and their compound assignment forms lower to direct LR35902 AND/OR/XOR instructions. Constant masks, including forms such as `~Hazard`, use immediate opcodes such as `AND d8`, `OR d8`, and `XOR d8`; byte-backed runtime masks use direct register operations. There is no helper call, hidden local, or boolean materialization.

Explicit casts validate that the requested target is a byte-backed local type, then emit the operand directly. In this prototype `(u8)(wide | 2)` is an intent marker for the source and semantic model; it does not add a helper call, temporary, sign extension, or truncation sequence.

Current user helper calls are source-level inline expansion. Statement helpers expand their substituted block; value helpers are accepted when the body is exactly one `return expr;` and expand to that expression before Game Boy lowering. Expression-bodied helpers such as `u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;` normalize to that same single-return shape. Named arguments are matched to helper parameters before substitution, so `step(amount: 5, value: 4)` lowers like `step(4, 5)`. Default parameter values are substituted at the call site before lowering; a helper such as `u8 step(u8 value, u8 amount = value + 1) => value + amount;` emits the same bytes for `step(value: 4)` and `step(4)` as for `step(4, 5)`. This allows source-level helpers such as `set_flag(flags, mask)` without adding a call sequence, stack traffic, runtime local, or runtime ABI dependency.

`for` loops lower to direct branches: initializer code is emitted once, the condition uses the same compare-and-jump path as `if`/`while`, the body emits normally, and the increment is emitted before jumping back to the condition. A counted loop such as `for (u8 i = 0; i < 3; i += 1)` therefore adds no hidden iterator, stack frame, or helper call.

Half-open range `for` loops lower to the same counted-loop form. `for (u8 i in start..end)` becomes the equivalent of `for (u8 i = start; i < end; i++)`: `start` initializes the loop local once, `end` is the exclusive upper-bound condition, and no iterator object, hidden temporary, bounds check, or helper call is emitted.

Half-open range membership expressions lower to ordinary short-circuit bounds checks. `value in start..end` emits the same compare-and-jump sequence as `value >= start && value < end`; there is no range object, helper call, hidden local, hidden bounds check, or implicit subject cache.

`loop` lowers to the same direct-branch shape as `while (true)`. The body is emitted once, the tail jumps back to the body start, `continue` jumps directly to that start label, and `break` jumps to the loop end. There is no condition expression, hidden local, stack frame, or helper call.

`do while` loops lower to direct branches with the body first and the condition at the bottom. `continue` jumps to that bottom condition check, matching source semantics without duplicating the body or adding helper state.

`else if` lowers as a nested `if` in the `else` branch, using the same compare-and-jump path as hand-written nested conditionals. It does not allocate state, materialize a boolean, or call a helper.

`switch` lowers as a nested `if`/`else if` compare chain. Each `case` owns a block and there is no fallthrough, so no `break` is needed between cases. Multi-value cases such as `case 0, 1` lower to the same short-circuit branch condition as `state == 0 || state == 1`. Half-open range cases such as `case 1..4` lower to `state >= 1 && state < 4`, using the same direct compare-and-jump primitives as hand-written bounds checks. Case expressions are folded before Game Boy lowering when they are constants or enum members. There is no jump table, dispatcher, helper call, or hidden local.

`break` and `continue` lower to unconditional jumps to the active loop labels. In a `for` loop, `continue` jumps to the increment label so counted loops still execute their increment; in a `while` loop, it jumps back to the condition/start label.

Logical `&&` and `||` in conditions lower to short-circuit jumps. The right-hand condition is emitted only on the path that needs it, so expressions such as `if (x != 0 && y != 0)` and `if (x != 0 || y != 0)` add branch code only; they do not allocate a temporary boolean or call a helper. Unary `!` in a condition lowers by inverting the branch target. When used as byte-backed value expressions, logical and comparison forms materialize `1` or `0` through the same direct branch machinery and store that byte in the destination.

Conditional value expressions such as `moving != 0 ? fast : 0` lower to direct branches using the same condition lowering as `if`. Only the selected branch expression is emitted on each path, then the selected byte is stored by the surrounding assignment or call argument. There is no helper call, hidden local, or eager evaluation of both values.

## Supported Video API

The preferred source spelling is SDK dot-calls such as `video.Init()` and `camera.SetPosition(x, y)`. The older snake_case function names remain accepted as compatibility aliases and lower through the same target path.

Static setup calls:

- `video.Init()`
- `music.Asset(name, path)`
- `palette.Set(index, color)`
- `objectPalette.Set(index, color)`
- `palette.Background(slot, c0, c1, c2, c3)`
- `palette.Sprite(slot, c0, c1, c2, c3)`
- `sprite.Asset(name, path[, frameWidth, frameHeight])`
- `world.Column(index, tile0, tile1, ...)`
- `world.Flags(index, flags0, flags1, ...)`
- `map_column(index, tile0, tile1, ...)`
- `world.Map(width, streamY, height)`
- `world.Load(path)`
- `hud.SetTile(window, x, y, tile)`
- `tilemap.Set(x, y, tile)`
- `tilemap.Fill(x, y, width, height, tile)`
- `video.Present()`

Runtime calls:

- `video.WaitVBlank()`
- `audio.Init()`
- `audio.Update()`
- `music.Play(name)`
- `music.Stop()`
- `input.Poll()`
- `scroll.Set(x, y)`
- `camera.Init(mapWidth, streamY, streamHeight)`
- `camera.SetPosition(x, y)`
- `camera.Apply()`
- `camera_move_right()`
- `camera_move_left()`
- `camera_tile_column_at(screenColumn)`
- `camera_span_tile_at(screenX, widthPx, row)`
- `camera_span_has_tile(screenX, widthPx, row, tile)`
- `camera_span_has_flags(screenX, widthPx, row, flags)`
- `camera.AabbTiles(screenX, worldY, width, height, flags)`
- `camera.AabbHitTop(screenX, worldY, width, height, flags)`
- `sprite_width(name)`
- `sprite.Set(id, x, y, tile, flags)`
- `sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])`
- `tilemap_fill_column(column, y, height, tile)`
- `map_stream_column(targetColumn, sourceColumn, y, height)`
- `map_tile_at(sourceColumn, row)`
- `map_flags_at(sourceColumn, row)`
- `world_tile_flags_at(worldX, worldY)`
- `collision_aabb_tiles(x, y, width, height, flags)`
- `animation.Clip(name, firstFrame, duration...)`
- `animation.Frame(name, tick)`
- `button_pressed(button)`
- `button_down(button)`
- `button_just_pressed(button)`
- `button_just_released(button)`
- `button_hold_ticks(button)`

`scroll.Set(x, y)` writes `x` to `SCX` and `y` to `SCY`. On Game Boy this gives hardware background scroll over the 256x256 background map.

`music.Asset(name, path)` declares a BGM resource. `path` can point directly to a hUGETracker `.uge` file, directly to a `.gbapu` binary trace (or a legacy `retrosharp.gbapu.v1` `.gbapu.json`), or to a `retrosharp.music.v1` JSON envelope whose `platforms.gb` entry has `format: "uge"` or `format: "gbapu"` and a relative path. The current Game Boy BGM runtime accepts `.uge` v6 songs with duty, wave, and noise channel rows, fixed ticks-per-row timing, compact wavetable data, and compact per-row channel event data. Effect `Cxy` is lowered as a row-level volume override; effects `2xx`, `3xx`, `Bxx`, and `Exx` are currently accepted as best-effort no-ops so real tracker songs can compile, but they do not yet reproduce hUGEDriver pitch slides, jumps, or note cuts exactly. Timer-based tempo, routine jump command values, tempo changes, panning, arpeggio, and other hUGETracker effects fail explicitly or remain unsupported because this runtime is frame-update driven through `audio.Update()`. When compiled program/data/music output exceeds the ROM-only limit, the compiler emits an MBC1 ROM automatically; user source does not select banks manually. The current foundation supports bank-aware fixed trampolines for multi-bank subroutine bodies, compiler-inserted fixed-bank continuations for linear main-flow fall-through, fixed-bank audio helpers, banked music, and banked read-only tile/tilemap/map-row data. Direct non-linear control-flow between different switchable banks is rejected explicitly unless it goes through a fixed-bank trampoline/helper instead of a raw cross-bank jump.

`.gbapu` is the lossless-by-intent Game Boy audio asset path. It is not PCM and not a tracker module: it stores timed writes to the DMG APU registers `NR10`..`NR52` and wave RAM `$FF30`..`$FF3F`, with cycle deltas preserved in the source. The ROM compiler maps writes onto DMG VBlank frames (true 70224-cycle period), removes redundant non-trigger writes, deduplicates identical frame groups into a pool (v2 group-pool, stream marker `0x02`), and stores contiguous 16-byte Wave RAM uploads as block commands. The runtime replays all due commands from `audio.Update()`, so playback is frame-scheduled even though the source keeps finer timing for future runtimes. See `GameBoyApuTraceFormat.md` for the binary/JSON schema, generation flow, runtime packing, and limitations, and `GameBoyApuTraceFormatV2.md` for the design study.

GBS files are not loaded directly by `music.Asset(...)`. Use an explicit export helper before compilation. For faithful playback, export the post-driver APU register trace (binary by default; the loop is auto-detected and trimmed to one body):

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  gbs-to-gbapu \
  --in path/to/theme.gbs \
  --subsong 1 \
  --seconds 120 \
  --out path/to/theme.gbapu
```

The helper requires `gbsplay` on `PATH` unless `--gbsplay <path>` is supplied. It captures `gbsplay -o iodumper` APU register writes, auto-detects the musical loop, records the GBS-derived `replayHz`, and preserves supported writes as a binary `.gbapu` trace. `--no-auto-loop`, `--loop-cycle <n>`, and `--emit-json` override the defaults; `gbapu-dump <file>` prints the register writes. RetroSharp does not load `.gbs` directly.

`audio.Init()` enables the DMG APU through `NR52`, routes channels through `NR51`, sets master volume through `NR50`, and resets the BGM runtime state. `music.Play(name)` points the runtime at the compiled song data and starts from row 0 or the first APU trace order entry. `audio.Update()` advances BGM playback by one tick and writes compiled duty-channel rows to `NR11`..`NR14` and `NR21`..`NR24`, wave rows and wave RAM through `NR30`..`NR34`/`$FF30`..`$FF3F`, and noise rows through `NR42`..`NR44` when a UGE row is due. For `.gbapu`, `audio.Update()` resolves the next order entry's pooled group body, replays all its APU register commands for the current frame, and then waits the compiled frame delay before the next entry. In a banked ROM, the generated audio runtime tracks the current music bank alongside the data pointer and switches MBC1 banks when sequential playback crosses a 16 KiB bank boundary. Call it once per frame after `video.WaitVBlank()`; if a frame also writes direct Game Boy OAM through `sprite.Draw(...)`, present sprites first so dense audio bursts cannot push OAM writes out of VBlank on real hardware. `music.Stop()` disables BGM playback and silences the active audio channels.

### Multiple music themes

You can declare any number of themes with `music.Asset(name, path)` (each name must be unique) and switch between them at runtime by calling `music.Play(other)`. Source code never touches ROM banks: when the combined program fits, every theme is stored inline in a 32 KiB ROM-only cartridge; when it does not, the compiler emits an MBC1 banked ROM and places each theme in its own ROM bank range. `music.Play(name)` reconfigures both the data pointer and the base bank for the requested theme, and the audio runtime resolves all later bank crossings relative to that theme's base, so a theme that lives in the high banks streams exactly like one in bank 1.

```
void main() {
    video.Init();
    music.Asset(overworld, "overworld.gbapu");
    music.Asset(boss, "boss.uge");
    audio.Init();
    music.Play(overworld);

    u8 onBoss = 0;
    loop {
        video.WaitVBlank();
        input.Poll();
        audio.Update();
        if (button_just_pressed(start) != 0) {
            if (onBoss == 0) {
                music.Play(boss);
                onBoss = 1;
            } else {
                music.Play(overworld);
                onBoss = 0;
            }
        }
    }
}
```

Themes can mix formats (for example a `.gbapu` trace and a `.uge` tracker song), since each compiled theme carries its own runtime kind. Two limits still apply: the total amount of banked music is bounded by the current transparent MBC1 lowering (up to 32 banks / about 512 KiB ROMs, after which compilation fails with an explicit message), and each individual theme is bounded to 64 KiB by the `.gbapu` stream format. See `samples/gameboy-music/` for a runnable two-track example.


`camera.Init(mapWidth, streamY, streamHeight)` initializes the current world camera. It keeps 16-bit camera X/Y positions in WRAM, tracks sub-tile movement on both axes, tracks the circular Game Boy background map edges for horizontal streaming, tracks top/bottom background and source rows for vertical streaming, and seeds source-map columns from the generated world-map row data. `mapWidth`, `streamY`, and `streamHeight` are compile-time constants. Call it after declaring the source map and before `camera.Apply()`, `camera_move_right()`, `camera_move_left()`, or `camera_tile_column_at(...)`.

`camera.SetPosition(x, y)` is the current position-based camera API candidate. `x` and `y` can be byte-backed expressions such as constants, locals, struct fields, or constant-index array elements. The current Game Boy lowering compares the requested low byte with the current camera low byte as an unsigned modular delta, then moves at most one pixel per axis toward the shortest step direction on each call. This keeps continuous movement stable across the Game Boy `SCX`/`SCY` byte wrap at 255 -> 0 while the runtime still maintains the internal 16-bit camera counters used by streaming. A tile-boundary crossing does not write VRAM itself: it advances the camera cursors and queues the exposed column (horizontal) or row (vertical) for deferred streaming, which `camera.Apply()` commits during VBlank. At most one column or one row is queued per frame; a camera operation that can move both axes in the same frame is rejected for Game Boy until a scheduler or split-frame policy can keep the combined column-and-row writes within budget. `samples/gameboy-vscroll/vscroll.rs` exercises Y-only scrolling down and back up over a source map taller than the visible band.

`camera.Apply()` writes the camera X low byte to `SCX` and the camera Y low byte to `SCY`, and commits any queued column or row into the background tilemap. It runs at the top of the presentation phase after `video.WaitVBlank()`, so the streaming happens inside VBlank without a second VBlank wait: a scrolling frame now costs a single VBlank, which keeps `audio.Update()` locked to the real frame rate and stops background music from slowing down while the camera scrolls. If the same frame also draws Game Boy sprites, issue `sprite.Draw(...)` first so direct OAM writes get the earliest VBlank cycles, then call `camera.Apply()` before gameplay updates. `camera_move_right()` and `camera_move_left()` move the world camera horizontally by one pixel and queue streaming the same way `camera.SetPosition(...)` does; a program must call `camera.Apply()` every frame for the queued columns/rows to reach VRAM. When horizontal movement crosses an 8 px tile boundary, the next visible source map column is committed into the circular Game Boy background map. The same column commit also streams the visible background rows above the world band (GB rows `0..streamY-1`) from the imported `background` layer, so floating decorations such as Mario `?` blocks scroll with the world instead of freezing and repeating every 32 tiles. Horizontal column streaming is capped to the 18 visible screen rows; off-screen world rows are left alone until vertical row streaming brings them into view, and the visible-row cap keeps each committed batch inside the Game Boy VBlank/target budget. The large per-row streamer is only emitted when the program can actually scroll vertically (a `camera.SetPosition` with a non-zero Y); horizontal-only programs stay compact. `camera_tile_column_at(screenColumn)` returns the source-map column currently visible at a screen tile column, wrapped by the configured map width.

`camera_span_tile_at(screenX, widthPx, row)` checks every source-map tile column covered by a horizontal pixel span and returns the first non-zero tile id, or `0` when the span is empty. `camera_span_has_tile(screenX, widthPx, row, tile)` returns `1` when any covered source-map tile matches `tile`, or `0` otherwise. `camera_span_has_flags(screenX, widthPx, row, flags)` checks the generated collision flag table for any matching flag bit and returns `1` or `0`. `screenX`, `widthPx`, `row`, `tile`, and `flags` are compile-time values in this prototype; `widthPx` can use `sprite_width(name)` so collision follows the logical width declared by `sprite.Asset(...)`.

`camera.AabbTiles(screenX, worldY, width, height, flags)` returns `1` when an on-screen AABB overlaps generated world flags at the current camera position. The X coordinate is screen-relative and is combined with the camera's current source column and fine scroll, so it remains aligned with the visible Tiled map when the camera has scrolled beyond the byte range available to source locals. `screenX` and `worldY` can be byte-backed runtime expressions; `width`, `height`, and `flags` are compile-time values, and `width` can use `sprite_width(name)`. Zero width, zero height, or a zero flag mask returns `0`.

`camera.AabbHitTop(screenX, worldY, width, height, flags)` scans the supplied camera-relative AABB from top to bottom and returns the top world-pixel Y of the first overlapped tile whose flags match, or `255` when there is no hit. It is a collision fact, not a physics helper: source code still chooses when to query it, how tall the search window is, and whether to land, bounce, ignore, or reset the actor.

`palette.Background(slot, c0, c1, c2, c3)` declares a logical background palette. Game Boy currently supports background slot `0` and lowers the four colors to `BGP`. `palette.Sprite(slot, c0, c1, c2, c3)` declares a logical sprite palette. Game Boy supports sprite slots `0` and `1`, lowering them to `OBP0` and `OBP1`. Color values are Game Boy DMG palette indexes `0..3`. Raw `palette.Set(...)` and `objectPalette.Set(...)` remain available as target-intrinsic compatibility calls.

`tilemap_fill_column(column, y, height, tile)` writes a vertical run into the background tilemap at runtime. It is the current primitive for streaming new map columns as the camera advances. The `column` and `tile` arguments can be simple runtime expressions; `y` and `height` are compile-time constants in this prototype.

`world.Column(index, ...)` defines one source-level world tile-id column. `world.Flags(index, ...)` defines the matching collision flag column using `0` for `Empty`, `1` for `Solid`, `2` for `Hazard`, and `4` for `Platform`; flag values can be combined. `world.Map(width, streamY, height)` builds the current portable `WorldMap2D` resource from those columns, fills the initial visible Game Boy background rows from that resource, and generates the source-map ROM row tables used by camera streaming and collision flag reads.

`world.Load(path)` imports a finite orthogonal Tiled JSON map (`.tmj`) at compile time and builds the same `WorldMap2D`, source-map ROM rows, collision flags, generated Game Boy background tiles, and initial Game Boy background tilemap. The importer accepts unencoded JSON array tile-layer data, a required `world` tile layer, an optional `background` tile layer, and external `.tsj` or `.tsx` tilesets with PNG images. Tileset image paths can use the same target-variant convention as sprite PNGs: if a `.tsx` points at `tiles.png`, the Game Boy lowering first looks for `tiles.gb.png`, `tiles.GB.png`, `tiles.gameboy.png`, or `tiles.GameBoy.png` next to it, then falls back to `tiles.png`. Because the Game Boy target has one scrolling background tilemap, these Tiled authoring layers are flattened: `background` is the visual base, non-empty `world` cells overlay it, and empty `world` cells keep the background tile underneath at the same Tiled source coordinate. If `retrosharpWorldY` and `retrosharpStreamY` shift the playable world slice vertically, the background layer is shifted by the same amount before composition so the Tiled layers remain visually aligned. The background rows that sit above the streamed world band are also emitted as full-width source-map rows and streamed horizontally by the camera, so background decorations above the playable band scroll with the world across the whole map instead of staying frozen in the initial 32-column window. Collision flags remain independent from that visual composition. Tiled tile sizes must be positive multiples of 8. Source cells are expanded into Game Boy hardware cells instead of being downscaled: for example, a 16x16 Tiled tile becomes a 2x2 block of generated 8x8 Game Boy tiles. Each generated 8x8 tile is quantized to the four DMG color indexes, and repeated generated patterns are deduplicated. GID `0` remains empty. If there is no explicit `collision` tile layer, tile `objectgroup` rectangles in the tileset become `Solid` flags repeated across every generated sub-tile; optional `retrosharpCollision` or `retrosharpFlags` tile/object properties can name or number `Solid`, `Hazard`, and `Platform` flags. A `collision` layer is still accepted for compact direct flag values: `0` empty, `1` solid, `2` hazard, and `4` platform. The map must include integer custom property `retrosharpStreamY`, expressed in generated Game Boy 8x8 tile rows; optional `retrosharpWorldY` and `retrosharpWorldHeight` properties select source Tiled rows used by the streaming world before expansion.

`map_column(index, ...)` remains supported as a transitional compatibility call. New runner-level world data should use `world.Load(...)` for editable maps, or `world.Column(...)` plus `world.Flags(...)` for compact source-authored maps, so visual setup, streaming data, and collision flags can share the same world resource.

`map_tile_at(sourceColumn, row)` reads one tile id from the source-level map column data and returns it as a byte expression. `map_flags_at(sourceColumn, row)` reads the generated collision flag byte for the same source coordinate. The current prototype expects `row` to be a compile-time constant and leaves column wrapping to the source program. This is enough for simple terrain collision, for example `if (map_flags_at(column, 2) != 0) { ... }`.

`world_tile_flags_at(worldX, worldY)` reads collision flags by world pixel coordinates. The Game Boy lowering divides each byte-backed coordinate by the 8x8 tile size, reads the generated world flag row table, and returns `0` when the coordinate falls outside the active world map from `world.Map(...)` or `world.Load(...)`. The language syntax intentionally omits a `level` argument while this prototype has one active world map; the SDK operation still records `WorldId = "default"` as the future extension point for named maps.

`collision_aabb_tiles(x, y, width, height, flags)` returns `1` when any world tile overlapped by the pixel AABB has one of the requested flag bits, otherwise `0`. Width and height are compile-time values in this prototype, with `sprite_width(name)` accepted as a static width source. Zero width, zero height, or a zero flag mask returns `0`. This helper only reports overlap; actor movement and collision resolution remain source-level policy.

`input.Poll()` snapshots the joypad for the current game tick. Call it once after `video.WaitVBlank()` before using the tick-based input helpers. The Game Boy backend reads each selected `JOYP` row several times before latching it and deselects both rows afterward, which avoids stale row reads on original DMG hardware. `button_down(button)` returns `1` while the button is down in the current snapshot, `button_just_pressed(button)` returns `1` only on the up-to-down transition, `button_just_released(button)` returns `1` only on the down-to-up transition, and `button_hold_ticks(button)` returns the number of consecutive polls the button has been held, saturating at `255` and resetting to `0` when released. This supports variable-height jumps without introducing real-time clocks.

`button_pressed(button)` remains supported as a compatibility direct joypad read and returns `1` when the named button is currently pressed or `0` otherwise. New gameplay code should prefer `input.Poll()` with the tick-based helpers. Supported names are `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

`sprite.Asset(name, path, frameWidth, frameHeight)` loads an editable PNG sprite sheet relative to the `.rs` file. Frames are laid out horizontally, which maps directly to a simple Aseprite export. Transparent pixels become Game Boy sprite color `0`; up to three opaque colors become sprite colors `1`, `2`, and `3`. The sample palette maps `#E0F8D0` to `1`, `#88C070` to `2`, and `#346856` to `3`; grayscale exports also map white to `1`, gray to `2`, and black to `3`.

For example, a two-frame 16x16 runner can be referenced as:

```c
sprite.Asset(player_run, "assets/player-run.gb.png", 16, 16);
```

In Aseprite, edit at 1x and keep the transparent background. Export with a horizontal sheet:

```bash
aseprite -b assets/mario-run.aseprite --sheet assets/mario-run.gb.png --sheet-type horizontal
```

PNG frame dimensions do not need to be hardware-sized. The compiler pads each frame to Game Boy 8x16 hardware cells internally, so a 16x27 logical sprite is emitted as a 16x32 metasprite. Compiled assets now expose portable sprite metadata: logical width and height, a default origin at `(0, 0)`, a default full-size hitbox, palette slot count, and a default animation clip spanning the loaded frames. Animation clips carry frame indices, per-frame durations, frame-start ticks, and total duration; the default loaded-asset clip assigns one tick to each frame until authored animation data exists. Target lowering still uses the existing Game Boy metasprite pieces and tile data.

`sprite.Asset(name, path)` is still supported as a transitional legacy path for the experimental JSON asset format. That format uses a `platforms.gb.frames` array. Each frame is a list of rows, and each character is a Game Boy color index from `0` to `3`.

```json
{
  "platforms": {
    "gb": {
      "frames": [
        [
          "0022222022200000",
          "0222222222220000"
        ]
      ]
    }
  }
}
```

`sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite. The compiler splits the selected Game Boy variant into 8x16 hardware sprites, generates tile data, assigns OAM entries, and treats `frame` as a logical animation frame index. Logical sizes like 16x27 are valid; the emitted hardware footprint is rounded up to 8x16 cells. The optional `flipX` argument is a portable boolean: any non-zero local value mirrors the logical metasprite horizontally, and the Game Boy backend lowers that choice to the OAM X-flip bit internally. The optional `paletteSlot` argument is a portable sprite palette slot validated against the target descriptor; Game Boy supports slots `0` and `1` and lowers slot `1` to the OBP1 OAM attribute bit. Raw OAM attribute bytes remain available through the target-intrinsic `sprite.Set(...)` API, not through portable `sprite.Draw(...)`.

`animation.Clip(name, firstFrame, duration...)` declares a portable animation clip resource. The first argument is a clip identifier, the second is the first logical frame index, and the remaining arguments are per-frame durations in ticks. `animation.Frame(name, tick)` returns the logical frame for a tick value and loops by the clip's total duration. Game Boy lowering keeps runtime state explicit in source, reduces dynamic ticks modulo the clip duration, then checks frame boundaries in declaration order.

`hud.SetTile(window, x, y, tile)` writes one static HUD tile into the Game Boy Window tilemap. The compiler validates the requested HUD mode through `GameBoyTarget.Capabilities`, copies the HUD tilemap to `$9C00`, sets the Window position to `WY=0` and `WX=7`, and enables the LCD Window layer only when a Window HUD tile is declared. The HUD tilemap is separate from the scrolling background/camera tilemap. Runtime HUD writes and configurable Window positions are not implemented yet. `split_scroll` is rejected through the target capability check.

## Short-Term Checklist

- [x] Parse `while`.
- [x] Generate a real Game Boy runtime loop.
- [x] Move sprites by writing OAM during the loop.
- [x] Add `scroll.Set(x, y)` over Game Boy `SCX`/`SCY`.
- [x] Build a runner sample with a fixed actor and scrolling background.
- [x] Stream new background columns every 8 pixels.
- [x] Represent maps as source data instead of ad hoc `tilemap.Set` calls.
- [x] Load an editable logical sprite asset and lower it to Game Boy metasprites.
- [x] Add collision against a simple tile row.
- [x] Add input-driven jump from the Game Boy joypad.
- [x] Add tick-based input helpers for edge-triggered and variable-height jump behavior.
- [x] Make the Game Boy runner a playable loop: hitbox-based ground checks, holes, and reset/fail state.
- [x] Add a horizontal world-camera helper that owns scroll state and map-column streaming.
- [x] Add target capability descriptors for Game Boy and NES.
- [x] Add the first observable SDK operation boundary for frame wait and input poll.
- [x] Lower the first portable SDK operation through the shared operation path.
- [x] Define the portable world map resource shape for tile ids and collision flags.
- [x] Generate the runner's initial visible tilemap from world data.
- [x] Generate the runner's streaming map data from the same world resource.
- [x] Generate collision flag tables from the same world resource.
- [x] Add the first position-based camera API and SDK operation boundary.
- [x] Reuse the existing horizontal camera runtime from `camera.SetPosition(...)`.
- [x] Replace direction-specific camera helpers with a position-based camera API in the runner.
- [x] Unify visual map data, streaming data, and collision flags into one world resource.
- [x] Extend camera position state and `camera.Apply()` to vertical scroll.
- [x] Stream visible background rows when vertical camera movement crosses tile boundaries.
- [x] Preserve logical sprite metadata for loaded Game Boy sprite assets.
- [x] Consume the collected SDK operation stream during Game Boy runtime lowering.
- [x] Replace raw `sprite.Draw` flags with a portable `flipX` boolean.
- [x] Add logical sprite palette slot selection to `sprite.Draw`.
- [x] Add animation clip data and looping `animation.Frame(...)` lookup.
- [x] Migrate the runner's run animation to an explicit tick plus `animation.Frame(...)`.
- [x] Add world-coordinate tile flag queries through `world_tile_flags_at(...)`.
- [x] Add boolean AABB tile collision queries through `collision_aabb_tiles(...)`.
- [x] Add a NES parity spike for logical sprites, input, and horizontal camera scroll.
- [x] Add a cross-target camera sample that can compile for both Game Boy and NES.
- [x] Add a Game Boy Window HUD prototype behind capability checks.

## Progress Snapshot

Landed on 2026-06-01:

- The Game Boy runner can draw an editable 16x27 Aseprite/PNG logical sprite and lower it to 8x16 hardware sprites.
- The runner scrolls the background through the camera runtime and streams generated world-map data through `map_stream_column(...)`.
- `map_tile_at(...)`, `map_flags_at(...)`, and `camera_span_has_flags(...)` let RetroSharp source query generated world-map data for simple tile collision.
- `button_pressed(...)` lets RetroSharp source query the Game Boy joypad.
- The sample now has a small gameplay loop: gravity, simple ground collision, running animation, and A-button jump.
- The compiler subset grew just enough for that loop: runtime-local addition, relational conditions against constants, value-returning runtime intrinsics, and byte-backed state.
- Generated runner screenshots are not tracked as source artifacts; regenerate them with RetroArch when needed.

Landed after the initial runner loop:

- `input.Poll()`, `button_down(...)`, `button_just_pressed(...)`, `button_just_released(...)`, and `button_hold_ticks(...)` provide a tick-based input surface.
- The Game Boy runner uses the new input helpers for edge-triggered, variable-height jumping: holding A extends upward impulse for a bounded number of ticks, and releasing A cuts the extension.
- The runner's horizontal scroll, column streaming, and run animation now advance from a horizontal speed value rather than raw D-pad state: holding a direction moves at a base walk speed and faces that way immediately, holding B builds speed up to a higher run limit only while grounded (Mario has traction), airborne input preserves horizontal momentum without building or bleeding speed, releasing the D-pad coasts to a stop through ground friction, and pressing the opposite direction turns instantly instead of drifting backward.
- `camera.SetPosition(x, y)` advances the runtime camera by at most one pixel per axis per call toward the requested low byte, using a signed 8-bit delta so source-level byte positions can wrap. A frame that moves two pixels on one axis therefore calls `camera.SetPosition` once per single-pixel step instead of once at the end of the frame; otherwise the camera falls a pixel behind each frame until the delta crosses 128 and scrolls backward.
- The runner now draws idle, run, and jump states through a single player sprite sheet so the same OAM slots are updated every frame; the jump frame is used whenever the actor is airborne.
- `sprite.Draw` accepts optional portable `flipX` and `paletteSlot` values; the runner uses them to make the same idle, run, and jump frames face left while preserving the last facing direction and selecting a logical sprite palette slot.
- `palette.Background(...)` and `palette.Sprite(...)` declare logical palette slots for SDK-shaped samples; the runner uses them instead of raw `palette.Set(...)` and `objectPalette.Set(...)`.
- `animation.Clip(...)` and `animation.Frame(...)` now express the runner's run cycle while keeping `animTick`, idle, and jump state explicit in source.
- `world_tile_flags_at(...)` lets collision code query generated world flags by pixel coordinates without depending on camera-span helpers.
- `collision_aabb_tiles(...)` reports whether an actor-sized world-space rectangle overlaps requested tile flags while keeping movement resolution explicit in source.
- `camera.AabbTiles(...)` reports collision for camera-relative AABBs against the current camera view, including fine-scroll X alignment.
- `camera.AabbHitTop(...)` reports the contacted tile's top world Y for a caller-defined camera-relative search AABB, using `255` as the no-hit sentinel.

Landed after the playable-loop pass:

- The runner checks the logical player width against each covered foot column instead of using a single source-map tile.
- The initial visible background matches the same source-map pattern used for streaming, including a visible multi-column hole and failure tile.
- Separate left/right streaming cursors keep the background stable when changing direction.
- The reset path restores actor position, velocity, animation, facing, jump, and movement state without rebasing the scrolled background.

Landed after the camera-runtime pass:

- `camera.Init(...)`, `camera.Apply()`, `camera_move_right()`, `camera_move_left()`, and `camera_tile_column_at(...)` lift horizontal scrolling one layer above raw `SCX` writes and hand-managed streaming cursors.
- The camera runtime owns 16-bit world X, sub-tile scroll state, circular background-map edge columns, source-map edge columns, and 8 px column streaming.
- Camera span helpers remain available for source-map checks, including logical sprite widths through `sprite_width(...)`, but the runner no longer depends on them for player feet.

Landed after the Collision V1 pass:

- The runner keeps the actor at a fixed screen X, derives collision X from the camera runtime through `camera.AabbTiles(...)`, derives collision Y from the actor's current screen Y, and probes generated world flags with the logical width from `sprite_width(mario_player)`.
- Camera span collision and world-space `collision_aabb_tiles(...)` remain available, but the runner uses the camera-relative AABB helper so long maps stay aligned after the camera scrolls beyond the source-local byte range.

Landed after the landing-query pass:

- The runner uses `camera.AabbHitTop(...)` to query the top edge of the first solid tile in a caller-defined landing search window, so descending actors can snap to stacked or multi-tile solids without copying a ladder of one-pixel probes.
- Landing policy remains source-owned: the runner still gates the query on downward velocity and calls `player.Land(...)` only when the query returns something other than `CollisionProbe.NoTileHit`.

Landed after the NES portable spike:

- NES now supports the first shared tick-input, logical sprite, unified world-map, and horizontal camera-scroll subset.
- `samples/cross-target-camera/camera.rs` builds for both Game Boy and NES without raw sprite, scroll, tilemap, or target-palette calls.
- Later NES runner work added horizontal runtime map streaming, camera-relative collision queries, runtime animation, and no-op audio acceptance for the runner-shaped path. The cross-target sample still deliberately excludes vertical camera movement, real audio playback, generic world-space collision, and HUD until those features have explicit capability-gated support on both targets.

Landed after the first HUD pass:

- `hud.SetTile(window, x, y, tile)` compiles to the Game Boy Window tilemap at `$9C00` and enables the Window layer without sharing camera scroll state.
- `samples/gameboy-hud/hud.rs` builds as the first HUD sample and keeps Window restrictions explicit.

Landed after the richer runner scene pass:

- The runner imports `samples/runner/maps/runner.tmj` with `world.Load(...)`, combining editable Tiled background/world layers, external `.tsx` tileset data, generated Game Boy background tiles, and collision flags while keeping the runtime inside the current 32 KiB ROM-only target.
- Tileset `objectgroup` rectangles now provide the runner's solid platform and ground collision flags without a separate hand-authored collision layer.
- The runner scene focuses on the player, camera, Tiled map streaming, tileset-authored solid collision, fall reset, and variable-height jump so the generated Tiled graphics still fit the ROM-only target.

Landed after the ceiling-collision pass:

- Solid blocks now block the player from below: `FrameState.ResolveCeilingHit(...)` probes a short AABB over the head with `camera.AabbTiles(...)` while the actor is rising (`velocityY >= World.SignedVelocityWrap`) and calls `player.BounceDown()` on contact, cancelling the jump and applying a small downward velocity so the actor rebounds with a physical feel instead of passing through the block.
- The head probe is offset to the sprite's visible content, not its full cell. The player sheet is 32 px tall but the figure is bottom-aligned with ~4 px of transparent padding at the top, so the probe references the real head at `footWorldY - CeilingProbeTopOffset` with `CeilingProbeTopOffset = 28` (probe band `[footWorldY - 28, footWorldY - 24]`). This makes the impact register when the visible head reaches the block instead of a few pixels early.
- The landing search window is now feet-relative (`LandingSearchTopOffset = 4`, `LandingSearchHeight = 12`) instead of spanning the whole sprite body, so `camera.AabbHitTop(...)` only snaps the actor onto a surface at or just below the feet. This stops a descending actor from being magnetised up onto a block whose underside it just hit, while preserving normal landing on ground and platforms approached from above. Both responses remain source-level policy in `samples/runner/runner.rs`.

## Current Framework Backlog

The SDK v1 reference already exists in `docs/Portable2DSdkV1.md`. The #106 stabilization backlog items for runner-shaped collision, cross-target diagnostics, and logical palette declarations have landed. New work in this area should be filed as narrower follow-up issues rather than reusing the closed stabilization backlog.
