# NES Target

Status: experimental, intentionally narrow.

The NES target compiles the constrained subset to mapper 0 when that exact image fits, then automatically retries an internal mapper 4 / TVROM-style four-screen profile when the final PRG/DPCM link requires banked world data. It supports tick-based input, logical sprites, staged packed-world row/column streaming beyond the preloaded 64x60 surface, runtime animation helpers, camera-relative collision queries, VGM/VGZ-sourced 2A03 BGM including DMC/DPCM, and the actor-framework acceptance slice. The shared runner loads complete `stage1.tmj`; its final image selects MMC3 automatically while smaller packed worlds may still fit mapper 0. NES still does not support HUD, generic world-space collision queries, expansion audio, runtime CHR banking, or public mapper APIs. See `docs/LargeWorldsRoadmap.md` for the completed cross-target packed-world and mapper execution plan.

See `ArchitectureRoadmap.md` for the persistent architecture roadmap that separates the RetroSharp language, portable 2D SDK, and target intrinsics, and `Portable2DSdkV1.md` for the current SDK v1 reference.

## Target Capabilities

The NES target exposes `NesTarget.Capabilities` for portable 2D capability checks and `NesTarget.AudioCapabilities` for audio capability checks.

| Capability | Value |
| --- | --- |
| Target name | `nes` |
| Screen pixels | 256x240 |
| Visible tile grid | 32x30 |
| Tile size | 8x8 |
| Background buffer | 64x60 tiles across four-screen nametables when vertical camera movement is used; 64x30 across two horizontal nametables for horizontal-only programs |
| Fine scroll | X and Y |
| Background tile write budget | 32 explicit runtime tile writes per frame; automatic four-screen row streaming uses 8-tile phases |
| Runtime background streaming axes | Horizontal and vertical |
| Attribute write budget | At most 9 runtime attribute writes for a committed column or row attribute phase |
| Hardware sprites | 64 total, 8 per scanline |
| Sprite size modes | 8x8 and 8x16 hardware modes |
| Sprite palettes | 4 object palette slots |
| Background palettes | 4 background palette slots |
| Sprite transforms | Flip X and Flip Y hardware flags |
| HUD modes | None declared portable support yet |
| Collision queries | Camera-relative AABB, camera-relative AABB hit-top, and screen-space camera AABB forms |
| BGM formats | VGM/VGZ 2A03 register logs for pulse, triangle, noise, DMC/DPCM, `$4015`, and `$4017`; expansion audio is ignored/deferred |
| SFX formats | VGM/VGZ 2A03 register logs as pulse 1 one-shot multi-frame traces with a ring-out linger |

The descriptor records NES sprite, palette, X/Y fine-scroll, horizontal background-column streaming, four-screen camera movement, vertical row streaming, and camera-relative collision-query support. Projects load `RetroSharp.Portable2D` from manifest `libraries`; standalone files can use `import RetroSharp.Portable2D;` as the explicit source-level form. Unknown imports fail compilation, and SDK dot-calls require a loaded source package. `Video.WaitVBlank()` and `Input.Poll()` are provided by that SDK source library as inline wrappers over NES target intrinsics (`wait_frame`/`wait_vblank` and `poll_input`), while the collector still records the matching `Sdk2DOperation` values for validation and frame-budget boundaries.

Runtime sprite lowering is implemented for logical PNG sprite sheets and transitional JSON assets through `Sdk2DOperation.DrawLogicalSprite`; `Sprite.Draw(...)` is provided by the SDK library over a role-bearing `sprite_draw` target intrinsic. `Audio.Init()`, `Audio.Update()`, `Music.Play(name)`, `Music.Stop()`, and `Sfx.Play(name)` are also provided by the SDK library over the `audio_init`/`audio_update`/`music_play`/`music_stop`/`sfx_play` target intrinsics (`music_play` and `sfx_play` carry the audio asset as a compile-time `AssetRef` operand), and `Music.Asset(...)` / `Sfx.Asset(...)` are package resource declarations. Camera-relative `Camera.AabbTiles(...)` and `Camera.AabbHitTop(...)`, plus screen-space `Camera.ScreenAabbTiles(...)` and `Camera.ScreenAabbHitTop(...)`, are likewise provided by the SDK library over role-bearing `camera_aabb_tiles`/`camera_aabb_hit_top`/`camera_screen_aabb_tiles`/`camera_screen_aabb_hit_top` target intrinsics matching the Game Boy target.

The horizontal-only camera path updates horizontal scroll, selects the horizontal nametable from its absolute source tile, and requests the next world-map column when the camera crosses an 8-pixel tile boundary. In mapper-backed packed builds, request preparation completes outside VBlank and publishes an immutable resident edge before visible camera state may advance. VBlank validates the complete 16-bit tag, commits at most 32 column tiles plus 9 prepared LW-1.4 palette/provenance attributes, and performs no R6 selection, directory access, or raw/RLE decode. Four-screen rows use four 8-tile phases followed by one attribute-only phase; the row slot remains committing until the fifth phase completes. Independent pending column/row descriptors stagger diagonal work without starvation, while unavailable, malformed, reversed, or wrongly tagged work leaves visible camera state unchanged.

A world that exactly fills the visible height (map height at most the 30-row screen and its streamed background reaching the bottom visible row) would otherwise draw its bottom tile row against the very bottom scanlines that hardware and most emulators crop as bottom overscan. For that case the camera scroll restore shifts the whole scene up one tile row (adds 8 px of vertical scroll) and `Sprite.Draw(...)` offsets sprite Y by the same amount, so the full bottom row lands inside the safe area while staying aligned with sprites; the exposed bottom strip wraps to the empty four-screen sky row. Vertically scrolling worlds keep their framing and receive no inset.

Camera-relative `Camera.AabbTiles(...)`/`Camera.AabbHitTop(...)` and screen-space `Camera.ScreenAabbTiles(...)`/`Camera.ScreenAabbHitTop(...)` lower against the active world flag rows using the same absolute camera tile plus fine X/Y state, so collision stays aligned after either scroll byte wraps; their screen operands may be literals or byte-backed runtime expressions. Portable SDK operations that still need generic world tile/world AABB collision queries, HUD support, or richer Tiled runtime palette provenance must fail capability checks before reaching NES backend code. NES compilation runs the shared `Sdk2DOperationCollector`, validates each operation through `Sdk2DOperationValidator`, and applies the shared frame-budget pass against `NesTarget.Capabilities` before lowering, so unsupported operations (for example HUD tiles, too much explicit background streaming, too many hardware sprites, or too many constant-Y sprites on one scanline in one possible frame) are rejected by the same capability checks the Game Boy target uses. `Camera.AabbTiles(...)`/`Camera.AabbHitTop(...)` and screen-space `Camera.ScreenAabbTiles(...)`/`Camera.ScreenAabbHitTop(...)` lower against the active world flag rows using the same absolute camera tile plus fine X/Y state, so collision stays aligned after either scroll byte wraps; their screen operands may be literals or byte-backed runtime expressions. Portable SDK operations that still need generic world tile/world AABB collision queries, HUD support, or richer Tiled runtime palette provenance must fail capability checks before reaching NES backend code. NES compilation runs the shared `Sdk2DOperationCollector`, validates each operation through `Sdk2DOperationValidator`, and applies the shared frame-budget pass against `NesTarget.Capabilities` before lowering, so unsupported operations (for example HUD tiles, too much explicit background streaming, too many hardware sprites, or too many constant-Y sprites on one scanline in one possible frame) are rejected by the same capability checks the Game Boy target uses.

Fitting programs retain the NROM-256 shape: two 16 KiB PRG banks plus one 8 KiB CHR bank. When the honest final link exceeds that profile, the production selector can emit the internal MMC3/TVROM shape used by complete `stage1`: 64 KiB PRG, 16 KiB CHR, and four-screen VRAM. Horizontal-only mapper-0 programs keep the existing two-nametable vertical-mirroring header; vertically moving or packed MMC3 camera programs use the four-screen bit so all four logical nametables remain distinct.

NES also runs the shared `SdkAudioOperationCollector` and validates audio operations through `SdkAudioOperationValidator` against `NesTarget.AudioCapabilities`. `Music.Asset(name, path)` accepts VGM/VGZ files directly or a `retrosharp.music.v1` envelope whose `platforms.nes` entry uses `format: "vgm"`. `Sfx.Asset(name, path)` accepts VGM/VGZ 2A03 register logs as pulse 1 one-shot traces. Generic paths use target variants just like PNG assets: `Music.Asset(theme, "assets/music/runner.vgz")` resolves `assets/music/runner.nes.vgz`, and `Sfx.Asset(jump, "assets/sfx/jump.vgz")` resolves `assets/sfx/jump.nes.vgz` when present. The importer keeps 2A03 pulse, triangle, noise, DMC/DPCM, `$4015`, and `$4017` writes for BGM, imports NES DPCM data blocks, and rejects unsupported chip commands explicitly. The compiler repacks BGM frame-quantized register writes into a compact group-pool stream embedded in PRG ROM, places DPCM sample bytes in the `$C000-$FFF9` PRG address range expected by `$4012/$4013`, and `Audio.Update()` replays one due BGM order entry through writes to `$4000-$4017`. For SFX, pulse 1 is the dedicated sound-effect channel: the compiler keeps only the pulse 1 registers (`$4000-$4003`) and drops captured global/other-channel state such as `$4010`, `$4015`, and `$4017`, then packs a flat per-frame trace (one `[count, (registerOffset, value)*count]` body per frame, empty `[0]` bodies for gaps, a `0xFF` end marker). This position-independent SFX data is emitted after the DPCM samples so it does not shrink the DPCM window. `Sfx.Play(...)` only arms the effect (a zero-page cursor, a ring-out linger counter, and an `SfxActive` flag); each `Audio.Update()` then plays one frame body via a shared APU body writer that both engines call. While an effect owns pulse 1 the BGM engine suppresses its own pulse 1 writes but still *shadows* them to RAM (so RAM tracks the BGM's intended pulse 1 state). After the last register frame the effect keeps owning pulse 1 for its linger frames so the note rings out for its authored length, then the channel's full state (`$4000-$4003`) is restored from that shadow so the BGM reclaims a clean channel with no effect residue. Restoring the whole channel (not just the `$4001` sweep) matters because the BGM can go dozens of frames without rewriting `$4000` (duty + volume/envelope), so the effect's duty/volume would otherwise stick on the melody. This keeps action feedback independent from the BGM tick stream (it never resets the BGM tick/order state) and fits the tight NROM runner by sharing the body writer with the BGM engine. See `NesApuTraceFormat.md`.

## Supported Video API

The preferred project spelling is a manifest `libraries` entry for `RetroSharp.Portable2D`, followed by SDK dot-calls such as `Video.Init()` and `World.Map(...)` in source. Snake_case names such as `video_wait_vblank`, `input_poll`, `sprite_draw`, and `camera_set_position` are target intrinsic IDs behind package declarations, not public source calls. Hosts can supply additional source-level SDK libraries through `SdkLibraryRegistry`, and the CLI accepts local source-only packages through `--lib-path` or project `libraryPaths`. A package manifest can restrict support to `targets` that include `nes`; otherwise loading it for NES fails before target lowering.

Static setup calls:

- `Video.Init()`
- `Palette.Set(index, color)`
- `Palette.Background(slot, c0, c1, c2, c3)`
- `Palette.Sprite(slot, c0, c1, c2, c3)`
- `Tilemap.Set(x, y, tile)`
- `Tilemap.Fill(x, y, width, height, tile)`
- `Video.Present()`

Helper functions can group those calls; parameters are substituted inline before lowering. Helpers whose body is exactly one `return expr;` can also be used as value expressions and are expanded before NES code generation. Expression-bodied helpers written as `Ret name(args) => expr;` normalize to that same single-return shape. Named arguments are matched to helper parameters, and default parameter values are filled at the call site before lowering.

The current target uploads two horizontal nametables during startup for horizontal-only SDK world maps, or the initial four nametables for four-screen free-scroll maps. Raw `Tilemap.Fill(...)` remains a visible-screen setup primitive and rejects rectangles outside the 32x30 visible area.

## Supported Runtime API

The NES runtime spike supports type aliases normalized to their underlying type before lowering, top-level and block-local `const` declarations with or without type annotations folded into literal expressions, decimal, hexadecimal (`0x2A`), binary (`0b1010_0000`), `_`-separated, and width-suffixed (`255u8`, `0x1234u16`) integer literals folded to the same immediates, `sizeof(type)` folded into literal byte-size expressions for primitive, reserved internal pointer-size marker, enum, and plain struct types, `offsetof(type, field)` folded into literal byte-offset expressions for direct fields of plain struct types, `countof(array)` folded into literal element-count expressions for fixed-size local arrays, top-level `enum` declarations with qualified members folded into literal expressions, user helper calls inlined with parameter substitution including named arguments and default parameter values, single-return value helpers, and `=>` expression-bodied helpers, scalar local variables including declared enum types, immutable `let` locals whose statically resolved initializer selects the same fixed byte or word storage, fixed-size local arrays of scalar types with initializer lists, initializer-inferred lengths, and constant or runtime index reads/writes, fixed-size local arrays of plain structs whose fields are scalar (`u8`, `i8`, `u16`, `i16`, `bool`, or enums) with constant or runtime field reads/writes such as `actors[i].x`, actor framework pool/definition sugar that expands to fixed `Actor` struct arrays, constants, called inline metadata helpers, generated ROM-table spawn helpers plus fixed `used[]` bytes and runtime camera-window activation loops from Tiled object layers via `Actors.SpawnLayer(...)`/`Actors.SpawnWindow(...)`, split actor world X/Y storage (`x`/`y` low bytes plus `xHi`/`yHi` high bytes), `pool.Update()`/`pool.Draw()` grouped loops before NES lowering with optional animation metadata drawing through `Animation.Frame(...)`, camera-relative actor drawing that reads camera X/Y once per generated draw loop and projects actors; one-slot pools write offscreen coordinates for inactive or off-window slots before calling `Sprite.Draw`, while larger pools keep the current visible-actor draw/cull shape until per-slot OAM allocation is added, `pool.TouchTiles(...)`/`pool.LandOnTiles(...)` per-phase camera caching with per-actor 2-axis camera-relative AABB helpers, `pool.TouchPlayer(...)` screen-space player contact using each actor's projected X/Y, drawn pool sprite pressure checked as pool capacity times the largest target-resolved enemy metasprite against the NES 64-sprite frame cap and 8-sprite scanline cap, spawn windows checked against the maximum simultaneously activatable spawns in each declared camera window, actor draws charged through the shared frame-budget pass, projectile/effect framework MVP sugar (`Projectiles.Pool`, `Projectiles.Def`, request processing, update, draw, tile hit hooks, actor hit hooks, hero hit hooks, `Effects.Pool`, `Effects.Def`, and effect lifecycle calls) that expands to fixed hero/enemy projectile arrays, fixed visual effect arrays, fixed request queues, signed vertical projectile movement, projected camera-bound projectile culling, tile bounce/expiration responses through `Camera.ScreenAabbTiles(...)`, page-aware projectile-vs-actor collision, accumulated hero damage, and optional projectile spawn/impact/expiration effect requests before NES lowering, plain local structs whose fields are scalar types with named and shorthand initializer lists, explicit casts to supported scalar local types, assignment and compound assignment (`+=`, `-=`, `&=`, `|=`, `^=`), statement-only `++`/`--`, constant array indices, runtime array indices, `struct.field` lvalues, and fixed struct-array field lvalues, byte-backed and direct 16-bit add/sub, bitwise `&`, `|`, and `^`, byte-backed and direct 16-bit comparisons between constants, runtime expressions, and nested expressions, `if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, half-open range membership expressions, `while` loops with constant or byte-backed runtime branch conditions, `do while`, counted `for` loops with byte-backed relational conditions, half-open range `for` loops, short-circuit `&&`/`||` and unary `!` branch conditions or byte-backed 0/1 value expressions, conditional value expressions (`condition ? whenTrue : whenFalse`) over byte-backed branch expressions, `break`/`continue` inside loops, `Video.WaitVBlank()`, `Input.Poll()`, `World.Column(...)`, `World.Flags(...)`, `World.Map(...)`, `World.Load(...)`, `Camera.Init(...)`, `Camera.SetPosition(...)`, `Camera.Apply()`, `Camera.AabbTiles(...)`, `Camera.AabbHitTop(...)`, `Camera.ScreenAabbTiles(...)`, `Camera.ScreenAabbHitTop(...)`, `Sprite.Asset(...)`, `Sprite.Width(...)`, `Sprite.Draw(...)`, `Animation.Clip(...)`, `Animation.Frame(...)`, and these tick-based button helpers:

- `Input.IsDown(button)`
- `Input.WasPressed(button)`
- `Input.WasReleased(button)`
- `Input.HoldTicks(button)`

`Input.Poll()` snapshots the previous controller state, strobes controller port `$4016`, reads the current serial button state, and updates per-button hold counters. The `button` argument is a member of the built-in `Button` enum (`Button.A`, `Button.B`, `Button.Select`, `Button.Start`, `Button.Right`, `Button.Left`, `Button.Up`, `Button.Down`), shared with the Game Boy input surface. `Input.IsDown`, `Input.WasPressed`, and `Input.WasReleased` return `bool`; `Input.HoldTicks` returns the hold count. Direct snake_case button calls and bare lowercase identifiers are rejected at the public source layer.

Scalar locals use fixed-width zero-page storage: `u8`, `i8`, `bool`, and enums reserve one byte; `u16` and `i16` reserve two adjacent little-endian bytes. Type aliases are normalized to their underlying type before NES lowering, so `type Pixel = i16;` has the same two-byte runtime shape as `i16`. Top-level constants, block-local constants, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, and enum members are substituted before ROM lowering and do not reserve zero-page storage.

`let` width inference runs before zero-page allocation. A suffixed literal keeps
its declared suffix and an unsuffixed literal keeps the historical `u8`
default. For non-literal initializers, `i16`/`u16` locals, struct or
restricted-class fields, helper results, and arithmetic propagate the same
unambiguous word type; expressions with no word-valued operand retain one-byte
storage. An expression that mixes `i16` and `u16`, or whose scalar type cannot
be proven, fails deterministically and requires an explicit cast. The inferred
source and the equivalent explicit declaration emit identical NES bytes.

`sizeof(type)` returns the compile-time byte size used by the current layout model: 1 for byte-backed primitives, `bool`, and enums; 2 for 16-bit primitive types and the reserved internal `sizeof(ptr<T>)` pointer-size marker; and the sum of field sizes for plain structs. `ptr<T>` is not a public storage, signature, field, or cast type in gameplay source. `offsetof(type, field)` returns the matching direct-field byte offset for plain structs. Plain local structs and struct arrays are flattened to adjacent zero-page byte slots using mixed-width field offsets. For example, `struct Actor { u16 worldX; u8 y; }` has stride 3, `actors[0].worldX` occupies low/high bytes at offsets 0/1, and `actors[0].y` is offset 2.

Struct initializer lists such as `Vec2 position = { y: seed + 1, x: 2 };` lower to zero-fill plus direct field stores in declaration order; shorthand fields such as `{ x, y: seed + 1 }` are parsed as `x: x`, and omitted fields remain zero. Fixed-size local arrays are flattened the same way. Runtime element access transfers `i * sizeof(element)` to `X`, and runtime struct-field access transfers `i * targetStructStride` to `X`; there is no implicit bounds check, heap object, or helper call. Direct 16-bit assignment, add/sub, and comparisons preserve both bytes; APIs that still consume byte expressions read the low byte.

`+=` and `-=` lower to direct zero-page arithmetic and store for simple operands; `x += y` uses the same zero-page load/add/store shape as `x = x + y`. Byte-backed runtime subtraction and comparisons are general expression features in the NES target, not actor-framework-only lowering. Constant and direct zero-page operands use immediate or zero-page opcodes. Nested variable-vs-variable forms preserve the left operand with `PHA` while the right operand is evaluated, then store the completed right operand in the expression scratch byte immediately before `PLA` and `CMP`/`SBC`, so the scratch byte is not live across recursive expression emission.

Bitwise `&`, `|`, `^`, and their compound assignment forms lower to direct 6502 AND/ORA/EOR instructions. Constant masks, including forms such as `~Hazard`, use immediate opcodes; byte-backed runtime masks use zero-page operations where supported. There is no helper call, hidden zero-page local, or boolean materialization.

Explicit casts validate that the requested target is a supported scalar local type, then emit the operand directly in the requested storage context. In byte contexts a cast such as `(u8)(wide | 2)` remains an intent marker and reads the low byte; in 16-bit storage contexts byte casts zero-extend or sign-extend according to the cast type.

`++` and `--` are statement-only source sugar over `+= 1` and `-= 1`. They can be used as standalone statements or as the increment in a `for` loop, and lower to the same direct zero-page load/arithmetic/store sequences as the expanded compound assignment.

Current user helper calls are source-level inline expansion. Parameters are substituted before target lowering, and helpers whose body is exactly one `return expr;` can be used where a byte expression is expected. Expression-bodied helpers such as `u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;` normalize to that same single-return shape. Named arguments are matched to helper parameters before substitution, so `step(amount: 5, value: 4)` lowers like `step(4, 5)`. Default parameter values are substituted at the call site before lowering; a helper such as `u8 step(u8 value, u8 amount = value + 1) => value + amount;` emits the same bytes for `step(value: 4)` and `step(4)` as for `step(4, 5)`. They improve reuse without adding call/return code, stack traffic, hidden zero-page storage, or a runtime ABI requirement.

`for` loops are also source-level structure over direct branches. The initializer emits once, a supported relational condition such as `i < 3` lowers to `LDA`/`CMP` plus a relative branch out of the loop when the loop end is within the 6502 branch range, the body emits normally, and the increment emits before an absolute jump back to the condition. If the false branch would exceed the +/-127 byte relative-branch range, only that loop gets a local long-branch trampoline. `while` loops use the same condition-lowering path as `if`/`do while`/short `for` loops: `while (false)` emits no body, `while (true)` emits a direct infinite loop, and runtime byte-backed conditions such as `while (x < 3)` branch out of the loop before the body when the condition becomes false.

Half-open range `for` loops lower to the same counted-loop form. `for (u8 i in start..end)` becomes the equivalent of `for (u8 i = start; i < end; i++)`: `start` initializes the loop local once, `end` is the exclusive upper-bound condition, and no iterator object, hidden temporary, bounds check, or helper call is emitted.

Half-open range membership expressions lower to ordinary short-circuit bounds checks. `value in start..end` emits the same compare-and-branch sequence as `value >= start && value < end`; there is no range object, helper call, hidden local, hidden bounds check, or implicit subject cache.

`do while` loops lower to direct branches with the body first and the condition at the bottom. `continue` jumps to that bottom condition check, matching source semantics without duplicating the body or adding helper state.

`else if` lowers as a nested `if` in the `else` branch, using the same compare-and-branch path as hand-written nested conditionals. It does not allocate state, materialize a boolean, or call a helper.

`switch` lowers as a nested `if`/`else if` compare chain. Each `case` owns a block and there is no fallthrough, so no `break` is needed between cases. Multi-value cases such as `case 0, 1` lower to the same short-circuit branch condition as `state == 0 || state == 1`. Half-open range cases such as `case 1..4` lower to `state >= 1 && state < 4`, using the same direct compare-and-branch primitives as hand-written bounds checks. Case expressions are folded before NES lowering when they are constants or enum members. There is no jump table, dispatcher, helper call, or hidden zero-page local.

`break` and `continue` lower to absolute jumps to the active loop labels. In a `for` loop, `continue` targets the increment label before the loop jumps back to the condition; in `while (true)`, it targets the loop start. There is no runtime state or helper call.

Logical `&&` and `||` in conditions lower to short-circuit branches using the same compare-and-branch primitives as `if` and `for`. Unary `!` lowers by inverting the branch target. When used as byte-backed value expressions, logical and comparison forms materialize `1` or `0` through the same direct branch machinery and store that byte in the destination.

Conditional value expressions such as `moving != 0 ? fast : 0` lower to direct 6502 branches using the same condition lowering as `if`. Only the selected branch expression is emitted on each path, then the selected byte is stored by the surrounding assignment or call argument. There is no helper call, hidden zero-page local, or eager evaluation of both values.

`Sprite.Asset(name, path[, frameWidth, frameHeight])` loads a logical sprite asset. PNG sheets require explicit frame dimensions and are quantized into NES sprite colors where transparent pixels become color `0` and opaque pixels become colors `1..3`. The PNG compiler picks up to three representative opaque source colors for the base metasprite layer, derives nearest NES hardware colors, and applies that derived sprite palette to each `Sprite.Draw(...)` logical palette slot that uses the asset unless that slot has raw `Palette.Set(...)` overrides. If additional opaque source colors remain, NES emits transparent optional overlay pieces for those pixels, derives a second sprite palette, and draws those overlay pieces with the next physical sprite palette slot; drawing such an asset from base slot `3` fails because sprite palette slot `4` does not exist. If two PNG assets drawn with the same logical slot derive incompatible palettes, the backend can place one asset in a free physical sprite palette range and emit the matching OAM attributes, so shared Game Boy/NES source does not need a NES-only palette slot literal. Overlay pieces are emitted after base pieces, count as real OAM usage, and are treated as optional detail for scanline-pressure diagnostics. The compiler preserves the universal background color in sprite palette entry `0`, because NES sprite palette color `0` mirrors the background universal color. JSON assets remain supported through `platforms.nes.frames`, with rows using NES color indexes `0`, `1`, `2`, and `3`; JSON assets do not derive a hardware palette. For PNG paths, the compiler first looks for a platform variant next to the requested file: `Mario.png` resolves to `Mario.nes.png`/`Mario.NES.png` when present, then falls back to `Mario.png`. If the source still names another platform variant such as `Mario.gb.png`, NES strips that suffix while looking for the matching NES variant. The compiler pads frames to 8x8 hardware cells, writes their tiles into CHR ROM starting at tile `6`, and rejects assets that need more than 64 hardware sprites or exceed the one-byte pattern-table tile index range.

`Sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite. Mapper-0 and non-packed profiles write the NES OAM shadow page and perform page-$02 OAM DMA. MMC3 packed-camera profiles instead initialize `$2003` and write the same sequential OAM bytes through `$2004` during VBlank; this preserves the portable draw contract while avoiding AprNes' corrupt page-$02 DMA path under mapper 4. `x`, `y`, `frame`, and `flipX` can be byte-backed constants or storage locations in the shared SDK operation model. `flipX` is portable boolean data, not raw OAM flags. `paletteSlot` remains a compile-time logical sprite palette slot and must fit the NES sprite palette slots `0..3`.

The preferred `Sprite.Draw(...)` spelling is declared by the `RetroSharp.Portable2D` source package as an inline helper over the NES `sprite_draw` target intrinsic. That intrinsic descriptor marks the sprite name as a compile-time asset reference and the palette slot as a compile-time constant while keeping X, Y, frame, and flipX as runtime operands. Collection still produces `Sdk2DOperation.DrawLogicalSprite`, so sprite asset lookup, palette-slot validation, frame-budget validation, and ROM emission are shared with the common sprite draw operation.

`Palette.Background(slot, c0, c1, c2, c3)` and `Palette.Sprite(slot, c0, c1, c2, c3)` declare logical palette slots. NES supports background slots `0..3` and sprite slots `0..3`; the lowering writes background slots to palette indexes `0..15` and sprite slots to indexes `16..31`. Color values are logical luminance tones `0..3` from lightest to darkest and map to fixed NES grays when no sprite PNG derives a more specific palette for a draw slot. Raw `Palette.Set(index, color)` remains available for target-intrinsic samples that need direct NES palette indexes `0..63`; raw writes also opt that sprite slot out of automatic PNG palette derivation.

`World.Column(...)`, `World.Flags(...)`, and `World.Map(width, streamY, height)` build the active `WorldMap2D` from unified world resources. Logical widths up to 4,096 hardware cells are accepted when the monolithic mapper-0 cartridge still fits. Horizontal-only maps seed the initial two-nametable 64-column buffer, and runtime camera movement streams the next logical source column into the off-screen nametable. When a vertical camera axis is used, `World.Map(...)` seeds the initial four-screen 64x60 surface, keeps all declared rows in ROM row tables, and streams visible rows/columns while preserving the complete source-column word. Target nametable columns remain modulo 64. A `Camera.Init(...)` stream height taller than the four-screen buffer is accepted for tall worlds: the initial buffer clips to the 60-row VRAM surface while the full source height remains available for vertical streaming.

`World.Load(path)` imports a Tiled map through the shared target-neutral model. NES resolves target PNG variants, derives the universal background color and four palette slots, allocates/deduplicates resident CHR tiles, and emits the initial nametable surface plus canonical `WorldPack` v1 data. Startup copies that derived universal background color to sprite-side palette aliases `$3F10`, `$3F14`, `$3F18`, and `$3F1C`, whose hardware mirroring would otherwise overwrite `$3F00` with a stale default. Attribute quadrants retain the LW-1.4 provenance rules: `world` wins a palette tie against `background`, and upper `world` cells win vertical ties. Packed camera preparation reads the two-byte target expansion and a pinned attribute plan, so newly streamed columns and rows preserve the same tile/palette result instead of falling back to palette slot 0. Wide X/Y requests, source coordinates, and pending edge tags remain words across `255 <-> 256`. `LW-3.5` migrated the shared runner to complete `stage1.tmj` through this path.

LW-1.4 also exposes an internal inspection build that serializes the same Tiled
result as `WorldPack` v1. It retains the weighted palette plan and historical
first-encounter CHR deduplication, then writes two-byte target expansion cells:
tile ID followed by palette slot plus the world-layer provenance bit. Full
normalized `stage1` uses 2,762 bytes and retains all 90 generated CHR patterns.
At LW-1.4 this payload was inspection-only and mapper-0 behavior, runner input,
and tracked ROMs stayed unchanged. Today fitting mapper-0 images still preserve
their byte-identical path, while packed mapper-0/MMC3 runtimes read the same
canonical bytes and the shared runner uses the complete banked path.

LW-1.5 exposes that exact inspection payload through the opt-in CLI form
`--target nes --world-budget-report <map.tmj>`. The deterministic JSON sums the
actual stored visual/collision bytes from each chunk directory entry and
reports the accepted 338-byte one-byte-ID staging shape (128 visual chunk
bytes, 128 collision chunk bytes, and two 41-byte tile/attribute edge slots)
against the format's 594-byte two-byte-ID maximum. The machine's 2 KiB RAM
capacity remains a separate reported fact, not the staging allowance.
It evaluates current PRG use against `nes-mapper-0-current` and separately
names `nes-mmc3-tvrom-v1-accepted-future` as an unimplemented requirement with
64 KiB PRG, 8 KiB world-data banks, and 16 KiB physical CHR capacity. The
current 8 KiB physical CHR capacity, 8 KiB resident CHR limit, and 256-entry
tile-index limit remain separately named values. The report's
`selectedProfile` is null: it does not change mapper-0 output, select mapper 4,
or wire a pack reader into production.

`Camera.Init(mapWidth, streamY, streamHeight)` enables the camera path for the current world map. `Camera.SetPosition(x, y)` consumes complete little-endian word operands and walks toward them by at most one tile crossing (≤8 px) per axis per call. Packed builds keep requested/logical coordinates separate from visible coordinates: fine movement publishes immediately only when it needs no new edge, while a tile crossing publishes after its correctly tagged resident edge completes. The fixed, bank-neutral NMI increments only a 16-bit hardware-frame counter and sets a saturated pending-frame byte. Both mapper-0 and MMC3 packed-camera links vector NMI to that fixed handler; mapper-0 reset and IRQ retain their simple `$8000` targets, and mapper-0 programs without the packed camera keep all three vectors at `$8000`. `Video.WaitVBlank()` discards any stale coalesced signal, waits for a new NMI edge, and then rechecks the hardware `$2002` VBlank bit before committing at most one bounded phase in mainline code and restoring scroll from visible state; a late main loop cannot begin a packed commit partway through VBlank. Request, resident, and commit stamps plus tile/attribute and forbidden-work counters make the two-frame preparation and zero-bank/directory/decode-in-commit contracts directly testable. The mapper-0 runtime evidence is recorded in [`NesMapper0PackedCameraAcceptance.md`](NesMapper0PackedCameraAcceptance.md), the measured complete-stage cadence and boundary evidence is recorded in [`NesRunnerCadenceAcceptance.md`](NesRunnerCadenceAcceptance.md), and the exact AprNes/FCEUmm/Nestopia four-screen differential is recorded in [`NesRunnerVisualParityAcceptance.md`](NesRunnerVisualParityAcceptance.md).

`Camera.AabbTiles(screenX, worldY, width, height, flags)` and `Camera.AabbHitTop(screenX, worldY, width, height, flags)` query collision flags from the active world map using the current absolute camera tile, camera fine X, and a screen-space AABB whose Y is supplied as a complete world-pixel word. Byte-backed Y values zero-extend. World hit-top returns the aligned world top through `A:X` (`A` low, `X` high), or `FF FF` for no hit. A byte world-hit destination is accepted only when the active world is at most 32 hardware rows; taller worlds require an `i16` destination and `-1` sentinel. `Camera.ScreenAabbTiles(screenX, screenY, width, height, flags)` and `Camera.ScreenAabbHitTop(screenX, screenY, width, height, flags)` use fully projected screen-space X/Y bytes and add the camera X/Y state inside the backend. These forms support both the runner's projected player X and actor-framework world X/Y projections on the four-screen camera path. All four are declared by the `RetroSharp.Portable2D` source package as inline helpers over role-bearing target intrinsics. Generic world-space `World.TileFlagsAt(...)` and `collision_aabb_tiles(...)` remain unsupported on NES.

Although screen hit-top is semantically byte-range, its source signature and
descriptor are `I16`. Under the accepted word ABI, a word destination receives
`A:X = 0x0000..0x00F8` for a hit or `0x00FF` for no hit (`A` low, `X = 0`); an
actor-framework byte destination consumes `A`. The target returns the complete
pair explicitly rather than relying on an accumulator-only result and a
caller-synthesized high byte.

Large Worlds LW-0.4 is accepted in
[`WorldCoordinateCollisionContract.md`](WorldCoordinateCollisionContract.md).
It specifies complete little-endian `i16` logical camera/world operands, an
internal NES `A:X` word-return ABI, and world hit-top no-hit bits `0xFFFF`, while
screen-relative hit-top keeps byte `255`. This is the contract for
LW-1.1/LW-1.2. LW-1.1 implements the camera-position and logical
source-column portion while preserving mapper-0 output for programs that fit;
LW-1.2 implements word world-Y collision operands, complete `A:X` results,
runner migration, and safe-narrowing diagnostics.

Large Worlds LW-0.3 selects the future banked NES profile in
[`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md):
mapper 4 with a TVROM-style 64 KiB PRG, 16 KiB CHR-ROM, and four-screen board
shape. The measured full-`stage1` sketch keeps `WorldPack`, pinned audio data,
boot data, fixed execution/DPCM, and vectors in explicit banks. `LW-3.1` now
implements the internal forced MMC3/TVROM linker and fixed-runtime acceptance
profile: code, helpers, DPCM, handlers, and vectors live at `$C000-$FFFF`, reset
initializes PRG mode 0, R6/R7 shadows, linear resident CHR, and disabled IRQs,
and AprNes proves the combined mapper-4/four-screen behavior. `LW-3.2` adds the
production mapper-0-first selector and target-private physical data linker:
ordered R6 world banks are `0, 3, 4, 5`, pinned R7 is bank `1`, boot-only R7 is
bank `2`, and fixed execution is banks `6, 7`. The normalized full-`stage1`
placement probe measures 2,762 pack, 5,012 pinned, 4,128 boot, 4,327 fixed
payload including the LW-3.3 reader, and 3,056 resident CHR bytes. The later
packed-camera profile uses a 2,052-byte runtime index and measures 6,204
pinned bytes, still inside the same 8 KiB R7 window. The 8 KiB R6 window is not a whole-pack
limit: continuation segments preserve canonical bytes and all v1 relative
offsets across non-contiguous physical banks. Mapper 0 remains byte-identical
when its exact historical final link fits; the internal MMC3 profile is chosen
only after a real mapper-0 PRG/DPCM layout failure. `LW-3.3` reads resident or
continued R6 packs through fixed code with bounded slots and LIFO restoration.
Runtime CHR banking and the IRQ HUD remain out of scope. Large Worlds v1 banks data only; callable
code, handlers, DPCM, helpers, and vectors remain in the fixed 16 KiB region,
and automatic executable-code banking is not implemented by this epic.

The final packed-camera runtime probe currently measures 8,999 fixed bytes and
6,204 pinned R7 bytes after the bounded column-commit and hardware-VBlank
corrections. It remains within the same `nes-mmc3-tvrom-v1` layout; the mapper,
bank ownership, PRG/CHR capacity, and automatic profile-selection rules are
unchanged.

`Animation.Clip(name, firstFrame, duration...)` stores a looping frame-duration table whose frame indexes and total duration must fit one byte. `Animation.Frame(name, tick)` is declared by the source package over the `animation_frame` target intrinsic and returns the current frame for that clip. `Sprite.Width(name)` is likewise a source-package helper over the compile-time `sprite_width` target intrinsic and returns the logical sprite width for a declared sprite asset.

## HUD Decision

NES HUD support is intentionally undeclared in the current descriptor. `Hud.SetTile(window, x, y, tile)` is parsed far enough to fail through `TargetCapabilityChecks.RequireHudMode(...)` with a NES capability error, while `Hud.SetTile(none, ...)` is treated as disabled HUD and compiles as a no-op.

Split-scroll HUD needs a timed scroll-change path that the current NES spike does not have. A reserved nametable band would still share the current horizontal scroll and would not behave as a stable HUD. Sprite HUD needs a separate tile-as-sprite contract and sprite-budget policy. Until one of those paths is implemented deliberately, `NesTarget.Capabilities.HudModes` remains `HudMode.None`.

## Cross-Target Sample

`samples/cross-target-camera/camera.rs` is the first shared source sample that builds for both Game Boy and NES. It uses a 48-column repeating authored world, tick input, bounded bidirectional horizontal camera positioning, and JSON logical sprite variants under `platforms.gb` and `platforms.nes`. Its exact mapper-0 ROM is part of the deterministic functional acceptance matrix in [`SimpleSampleFunctionalAcceptance.md`](SimpleSampleFunctionalAcceptance.md).

`samples/runner/runner.retrosharp.json` is the shared Game Boy/NES acceptance project for the runner path. It lists `src/main.rs` plus local helper/state code from `samples/runner/src`, and uses the complete 156x20-cell `stage1.tmj` map (312x40 hardware tiles), 2-axis camera movement, camera-relative collision helpers, runtime animation, jump/reset logic, `assets/music/runner.vgz`, and `assets/mario-player.png` for both targets. NES resolves its target variants, preserves both DPCM blocks, stages packed rows/columns outside NMI, commits through all four nametables, and automatically selects `nes-mmc3-tvrom-v1` for the final runner image.

NES packed-camera correctness is independent of unspecified CPU RAM power-on
contents. Reset initializes the exact WorldPack/camera-owned `$0326..$03FF`
control block and the exact 594-byte `$0400..$0651` staging layout, assigns the
`NoSlot` sentinel explicitly, and leaves mapper shadows, OAM, audio, game state,
and the first byte after staging outside those clears. FCEUmm `$00`/`$FF` plus
a deterministic nonzero pattern and AprNes lifecycle evidence are recorded in
[`NesRunnerPowerOnRamAcceptance.md`](NesRunnerPowerOnRamAcceptance.md).

NES packed-camera visual correctness is also differential rather than
screenshot-only. The tracked runner is driven right beyond camera X 300, jumped
against authored collision, and driven left through X 256 in AprNes, FCEUmm,
and Nestopia. Acceptance compares all four physical nametables plus exact
visible tile IDs and attribute palette selectors, lifecycle and forbidden-work
counters, PPU state, framebuffers, and collision cells. See
[`NesRunnerVisualParityAcceptance.md`](NesRunnerVisualParityAcceptance.md).

`samples/actor-framework/actors.rs` is the focused Game Boy/NES acceptance sample for the actor framework. It uses `Actors.Pool`, `Enemies.Def`, Tiled object-layer spawn data, runtime camera-window activation, `enemies.Update()`, `enemies.TouchTiles(...)`, `enemies.LandOnTiles(...)`, and `enemies.Draw()` over the same source. The framework lowers before NES target emission to fixed `Actor` arrays with split X/Y world coordinates, generated spawn helpers plus `used[]`, direct kind branches, camera-relative 2-axis collision, and ordinary `Sprite.Draw(...)` calls.

The cross-target sample intentionally avoids raw target calls such as `Sprite.Set(...)`, `Scroll.Set(...)`, `Tilemap.Set(...)`, `Tilemap.Fill(...)`, `tilemap_fill_column(...)`, `map_stream_column(...)`, `Palette.Set(...)`, and `ObjectPalette.Set(...)`. It does not exercise generic world-space collision queries or HUD APIs. Runner-specific coverage now exercises the complete packed `stage1` path with real target audio; focused free-scroll samples remain useful isolation fixtures for diagonal and four-screen behavior.

Runner-shaped NES parity is covered by `NesRunnerAcceptanceTests`: the shared runner source must compile for NES with VGM audio, and runtime animation must lower. `CrossTargetScrollAcceptanceTests` also verifies that runner-shaped `Camera.AabbTiles(...)` and `Camera.AabbHitTop(...)` lower on both Game Boy and NES, that a bounded four-screen camera source lowers on NES, that `samples/tiled-vscroll/vscroll.rs` lowers to four-screen vertical NES scroll from a tall Tiled `World.Load(...)` map, and that `samples/tiled-free-scroll/free-scroll.rs` lowers to diagonal four-screen NES scroll from a wide/tall Tiled map with populated nametable attributes. The exact tracked-ROM production matrix for those samples plus `deadzone-follow` is recorded in [`PackedTiledFunctionalAcceptance.md`](PackedTiledFunctionalAcceptance.md), including per-frame authored palette checks and external NesMcp/AprNes checkpoints.

Sample portability is tracked in `samples/manifest.json`. `samples/static-drawing/drawing.rs` is the single neutral drawing identity declared for Game Boy and NES, but it remains `target-intrinsic`, not a portable SDK sample. Private compile-time target variants retain each original raw tilemap/palette fixture and visible projection; the manifest remains the only declaration of target availability. Raw setup and transitional camera-streaming helpers belong in `target-intrinsic`, `target-capability-spike`, or `target-acceptance` samples until a documented SDK replacement exists.
