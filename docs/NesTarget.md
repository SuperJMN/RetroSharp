# NES Target

Status: experimental, intentionally narrow.

The NES target currently compiles a constrained static drawing subset directly to an iNES ROM and supports the first tick-based input, logical sprite, and horizontal camera runtime helpers. It does not yet support vertical camera movement, full Game Boy runner parity, or frame-by-frame background streaming.

See `ArchitectureRoadmap.md` for the persistent architecture roadmap that separates the RetroSharp language, portable 2D SDK, and target intrinsics, and `Portable2DSdkV1.md` for the current SDK v1 reference.

## Target Capabilities

The NES target exposes `NesTarget.Capabilities` for portable 2D capability checks.

| Capability | Value |
| --- | --- |
| Target name | `nes` |
| Screen pixels | 256x240 |
| Visible tile grid | 32x30 |
| Tile size | 8x8 |
| Background buffer | 32x30 tiles for the current static drawing path |
| Fine scroll | Horizontal only |
| Background tile write budget | 0 runtime tile writes per frame |
| Attribute write budget | 0 runtime attribute writes per frame |
| Hardware sprites | 64 total, 8 per scanline |
| Sprite size modes | 8x8 and 8x16 hardware modes |
| Sprite palettes | 4 object palette slots |
| Background palettes | 4 background palette slots |
| Sprite transforms | Flip X and Flip Y hardware flags |
| HUD modes | None declared portable support yet |

The descriptor records NES sprite, palette, and horizontal fine-scroll support. Runtime sprite lowering is implemented for the current JSON logical sprite spike through `Sdk2DOperation.DrawLogicalSprite`. The current camera path can update horizontal scroll but cannot stream new nametable columns yet, so portable SDK operations that need vertical scroll, per-frame nametable writes, attribute writes, or HUD support must fail capability checks before reaching NES backend code. NES compilation now runs the shared `Sdk2DOperationCollector` and validates each operation through `Sdk2DOperationValidator` against `NesTarget.Capabilities` before lowering, so unsupported operations (for example vertical camera movement or HUD tiles) are rejected by the same capability checks the Game Boy target uses. Horizontal `camera.SetPosition(x, 0)` validates because NES fine-scrolls within its pre-loaded nametable without streaming background tiles.

## Supported Video API

The preferred source spelling is SDK dot-calls such as `video.Init()` and `world.Map(...)`. The older snake_case function names remain accepted as compatibility aliases and lower through the same target path.

Static setup calls:

- `video.Init()`
- `palette.Set(index, color)`
- `tilemap.Set(x, y, tile)`
- `tilemap.Fill(x, y, width, height, tile)`
- `video.Present()`

Helper functions can group those calls; parameters are substituted inline before lowering. Helpers whose body is exactly one `return expr;` can also be used as value expressions and are expanded before NES code generation. Expression-bodied helpers written as `Ret name(args) => expr;` normalize to that same single-return shape. Named arguments are matched to helper parameters, and default parameter values are filled at the call site before lowering.

The current target stores one visible 32x30 nametable shape and uploads it during startup. `tilemap.Fill(...)` rejects rectangles outside that visible area.

## Supported Runtime API

The NES runtime spike supports type aliases normalized to their underlying type before lowering, top-level and block-local `const` declarations with or without type annotations folded into literal expressions, decimal, hexadecimal (`0x2A`), binary (`0b1010_0000`), `_`-separated, and width-suffixed (`255u8`, `0x1234u16`) integer literals folded to the same immediates, `sizeof(type)` folded into literal byte-size expressions for primitive, pointer, enum, and plain struct types, `offsetof(type, field)` folded into literal byte-offset expressions for direct fields of plain struct types, `countof(array)` folded into literal element-count expressions for fixed-size local arrays, top-level `enum` declarations with qualified members folded into literal expressions, user helper calls inlined with parameter substitution including named arguments and default parameter values, single-return value helpers, and `=>` expression-bodied helpers, byte-backed local variables including declared enum types, fixed-size local arrays of byte-backed types with initializer lists, initializer-inferred lengths, and constant or byte-backed runtime index reads/writes, plain local structs whose fields are byte-backed types with named and shorthand initializer lists, explicit casts to byte-backed local types, assignment and compound assignment (`+=`, `-=`, `&=`, `|=`, `^=`), statement-only `++`/`--`, constant array indices, runtime array indices, and `struct.field` lvalues, byte-backed bitwise `&`, `|`, and `^`, `if`/`else if`/`else`, no-fallthrough `switch` with multi-value and half-open range cases, half-open range membership expressions, `while (true)`, `do while`, `loop`, counted `for` loops with byte-backed relational conditions against constants, half-open range `for` loops, short-circuit `&&`/`||` and unary `!` branch conditions or byte-backed 0/1 value expressions, conditional value expressions (`condition ? whenTrue : whenFalse`) over byte-backed branch expressions, `break`/`continue` inside loops, `video.WaitVBlank()`, `input.Poll()`, `world.Column(...)`, `world.Flags(...)`, `world.Map(...)`, `camera.Init(...)`, `camera.SetPosition(...)`, `camera.Apply()`, `sprite.Asset(...)`, `sprite.Draw(...)`, and these tick-based button helpers:

- `button_down(button)`
- `button_just_pressed(button)`
- `button_just_released(button)`
- `button_hold_ticks(button)`

`input.Poll()` snapshots the previous controller state, strobes controller port `$4016`, reads the current serial button state, and updates per-button hold counters. The logical button names match the portable Game Boy input surface: `a`, `b`, `select`, `start`, `right`, `left`, `up`, and `down`.

Type aliases are normalized to their underlying type before NES lowering, so `type ActorIndex = u8;` has the same runtime shape as `u8`. Top-level constants, block-local constants, `sizeof(type)`, `offsetof(type, field)`, `countof(array)`, and enum members are substituted before ROM lowering and do not reserve zero-page storage. Integer literal spelling is source-only: decimal, `0x` hexadecimal, `0b` binary, `_`-separated, and `u8`/`i8`/`u16`/`i16` suffixed forms all fold to the same immediate value. Constant type annotations are optional; omitting one does not change the emitted code because the constant disappears before target lowering. Constant boolean literals normalize to `1` or `0`, and constant conditional expressions select one branch before ROM lowering. `sizeof(type)` returns the compile-time byte size used by the current layout model: 1 for byte-backed primitives, `bool`, and enums; 2 for 16-bit primitive and pointer types; and the sum of field sizes for plain structs. `offsetof(type, field)` returns the matching direct-field byte offset for plain structs. `countof(array)` returns the declared element count of a fixed-size local array visible at that point. Block-local constants can reference earlier constants visible at that point, including simple integer expressions such as `StartX + 1`, then disappear from the runtime block. Enum locals are stored as one zero-page byte in this spike. Plain local structs are flattened to adjacent zero-page byte slots for their fields, so `position.x` and `position.y` lower to the same direct zero-page loads/stores as two separate byte-backed locals. Struct initializer lists such as `Vec2 position = { y: seed + 1, x: 2 };` lower to the same zero-fill plus direct field stores as declaring the struct and assigning those members by hand; field expressions are emitted in declaration order, shorthand fields such as `{ x, y: seed + 1 }` are parsed as `x: x`, and omitted fields remain zero. Fixed-size local arrays are flattened the same way; `values[0]` and `values[1]` lower to direct zero-page loads/stores with no heap and no runtime indexing helper. Initializer lists such as `u8 values[4] = [1, seed, seed + 1];` lower to the same zero-fill plus direct element stores as declaring the array and assigning those constant indices by hand; `u8 values[] = [1, seed, seed + 1];` infers the fixed length `3` before lowering and emits the same bytes as the explicit length form. Omitted trailing elements remain zero when the explicit length is larger than the initializer list. Runtime element access such as `values[i]` transfers the byte index to `X` and uses zero-page indexed addressing from the array base; it does not add an implicit bounds check or mask.

`+=` and `-=` lower to direct zero-page arithmetic and store. There is no helper call or stack traffic; `x += y` uses the same zero-page load/add/store shape as `x = x + y`.

Bitwise `&`, `|`, `^`, and their compound assignment forms lower to direct 6502 AND/ORA/EOR instructions. Constant masks, including forms such as `~Hazard`, use immediate opcodes; byte-backed runtime masks use zero-page operations where supported. There is no helper call, hidden zero-page local, or boolean materialization.

Explicit casts validate that the requested target is a byte-backed local type, then emit the operand directly. In this prototype `(u8)(wide | 2)` is an intent marker for the source and semantic model; it does not add a helper call, temporary, sign extension, or truncation sequence.

`++` and `--` are statement-only source sugar over `+= 1` and `-= 1`. They can be used as standalone statements or as the increment in a `for` loop, and lower to the same direct zero-page load/arithmetic/store sequences as the expanded compound assignment.

Current user helper calls are source-level inline expansion. Parameters are substituted before target lowering, and helpers whose body is exactly one `return expr;` can be used where a byte expression is expected. Expression-bodied helpers such as `u8 choose_speed(u8 moving, u8 fast) => moving != 0 ? fast : 0;` normalize to that same single-return shape. Named arguments are matched to helper parameters before substitution, so `step(amount: 5, value: 4)` lowers like `step(4, 5)`. Default parameter values are substituted at the call site before lowering; a helper such as `u8 step(u8 value, u8 amount = value + 1) => value + amount;` emits the same bytes for `step(value: 4)` and `step(4)` as for `step(4, 5)`. They improve reuse without adding call/return code, stack traffic, hidden zero-page storage, or a runtime ABI requirement.

`for` loops are also source-level structure over direct branches. The initializer emits once, a supported relational condition such as `i < 3` lowers to `LDA`/`CMP` plus a branch out of the loop, the body emits normally, and the increment emits before an absolute jump back to the condition. NES `while` support remains narrower in this spike: `while (true)` and `loop` are the stable runtime infinite-loop shapes, while counted loops should use `for` when they need a byte-backed relational condition.

Half-open range `for` loops lower to the same counted-loop form. `for (u8 i in start..end)` becomes the equivalent of `for (u8 i = start; i < end; i++)`: `start` initializes the loop local once, `end` is the exclusive upper-bound condition, and no iterator object, hidden temporary, bounds check, or helper call is emitted.

Half-open range membership expressions lower to ordinary short-circuit bounds checks. `value in start..end` emits the same compare-and-branch sequence as `value >= start && value < end`; there is no range object, helper call, hidden local, hidden bounds check, or implicit subject cache.

`loop` lowers to the same direct-branch shape as `while (true)`. The body is emitted once, the tail jumps back to the body start, `continue` jumps directly to that start label, and `break` jumps to the loop end. There is no condition expression, hidden zero-page local, stack frame, or helper call.

`do while` loops lower to direct branches with the body first and the condition at the bottom. `continue` jumps to that bottom condition check, matching source semantics without duplicating the body or adding helper state.

`else if` lowers as a nested `if` in the `else` branch, using the same compare-and-branch path as hand-written nested conditionals. It does not allocate state, materialize a boolean, or call a helper.

`switch` lowers as a nested `if`/`else if` compare chain. Each `case` owns a block and there is no fallthrough, so no `break` is needed between cases. Multi-value cases such as `case 0, 1` lower to the same short-circuit branch condition as `state == 0 || state == 1`. Half-open range cases such as `case 1..4` lower to `state >= 1 && state < 4`, using the same direct compare-and-branch primitives as hand-written bounds checks. Case expressions are folded before NES lowering when they are constants or enum members. There is no jump table, dispatcher, helper call, or hidden zero-page local.

`break` and `continue` lower to absolute jumps to the active loop labels. In a `for` loop, `continue` targets the increment label before the loop jumps back to the condition; in `while (true)`, it targets the loop start. There is no runtime state or helper call.

Logical `&&` and `||` in conditions lower to short-circuit branches using the same compare-and-branch primitives as `if` and `for`. Unary `!` lowers by inverting the branch target. When used as byte-backed value expressions, logical and comparison forms materialize `1` or `0` through the same direct branch machinery and store that byte in the destination.

Conditional value expressions such as `moving != 0 ? fast : 0` lower to direct 6502 branches using the same condition lowering as `if`. Only the selected branch expression is emitted on each path, then the selected byte is stored by the surrounding assignment or call argument. There is no helper call, hidden zero-page local, or eager evaluation of both values.

`sprite.Asset(name, path)` currently loads a JSON asset with a `platforms.nes.frames` variant. Each frame is an array of rows using NES color indexes `0`, `1`, `2`, and `3`. The compiler pads frames to 8x8 hardware cells, writes their tiles into CHR ROM starting at tile `6`, and rejects assets that need more than 64 hardware sprites or exceed the one-byte pattern-table tile index range.

`sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])` draws a logical sprite through the NES OAM shadow page and performs OAM DMA. `x`, `y`, `frame`, and `flipX` can be byte-backed constants or storage locations in the shared SDK operation model. `flipX` is portable boolean data, not raw OAM flags. `paletteSlot` remains a compile-time logical sprite palette slot and must fit the NES sprite palette slots `0..3`.

`world.Column(...)`, `world.Flags(...)`, and `world.Map(width, streamY, height)` build the initial visible nametable from unified world resources. In this spike, `width` must fit the visible 32-column nametable because runtime column streaming is not implemented.

`camera.Init(mapWidth, streamY, streamHeight)` enables the horizontal camera path for the current world map. `camera.SetPosition(x, 0)` stores the horizontal scroll byte, and `camera.Apply()` writes horizontal scroll followed by zero vertical scroll to `$2005`. Any non-zero or runtime Y position is rejected with a NES capability error until vertical movement and streaming have a budgeted lowering.

## HUD Decision

NES HUD support is intentionally undeclared in the current descriptor. `hud.SetTile(window, x, y, tile)` is parsed far enough to fail through `TargetCapabilityChecks.RequireHudMode(...)` with a NES capability error, while `hud.SetTile(none, ...)` is treated as disabled HUD and compiles as a no-op.

Split-scroll HUD needs a timed scroll-change path that the current NES spike does not have. A reserved nametable band would still share the current horizontal scroll and would not behave as a stable HUD. Sprite HUD needs a separate tile-as-sprite contract and sprite-budget policy. Until one of those paths is implemented deliberately, `NesTarget.Capabilities.HudModes` remains `HudMode.None`.

## Cross-Target Sample

`samples/cross-target-camera/camera.rs` is the first shared source sample that builds for both Game Boy and NES. It uses unified world data, tick input, horizontal camera positioning, and JSON logical sprite variants under `platforms.gb` and `platforms.nes`.

The sample intentionally avoids raw target calls such as `sprite.Set(...)`, `scroll.Set(...)`, `tilemap.Set(...)`, `tilemap.Fill(...)`, `map_stream_column(...)`, and `objectPalette.Set(...)`. It does not imply support for NES vertical camera movement, runtime nametable streaming, collision queries, runtime animation, or HUD APIs yet.

Sample portability is tracked in `samples/manifest.json`. The NES drawing sample is classified as `target-intrinsic`, not as a portable SDK sample, because it demonstrates raw static `tilemap_*` setup calls for this target.
