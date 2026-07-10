# ADR: 16-bit world coordinates and collision hits

Status: **accepted for LW-0.4 on 2026-07-10.**

This decision is the implementation contract for LW-1.1 and LW-1.2. It fixes
the portable scalar meanings, byte layout, collision sentinel, and target
lowering boundary. It does not widen the current operations or target
runtimes, migrate callers, change `WorldPack` topology, or implement banking,
mapping, packing, or physics.

## Decision summary

- Absolute world-pixel positions, camera positions, hardware-cell coordinates,
  and map dimensions use non-negative `i16` values in the portable SDK.
- Every such word has a fixed little-endian layout: low byte at offset 0, high
  byte at offset 1. Values wrap nowhere in the portable contract.
- `Camera.AabbHitTop(...)` continues to return `i16`, but its full word becomes
  significant. A hit returns a non-negative, 8-pixel-aligned world top in
  `0..32760`; no hit returns `-1`, whose bits are `0xFFFF`.
- `Camera.ScreenAabbHitTop(...)` remains a deliberately different,
  screen-relative byte-range fact, but its source signature and descriptor are
  still `I16`. A word destination receives zero-extended `0x0000..0x00F8`, or
  `0x00FF` for no hit; a byte destination consumes the low byte.
- Screen coordinates, AABB extents, flags, target background-buffer slots, and
  hardware scroll writes remain byte-sized after range checks and culling.
- `WorldPack` edge-slot tags store the logical hardware-cell coordinate as the
  same two-byte word. Chunk-local and subcell coordinates remain bytes.
- `TargetIntrinsicReturnKind.I16` means a complete two-byte result. The target
  ABI returns it in `HL` on Game Boy (`L` low, `H` high) and `A:X` on NES (`A`
  low, `X` high). A direct-to-destination optimization is allowed only when it
  is byte-for-byte equivalent. Even when the semantic high byte is zero, as for
  screen hit-top, the future intrinsic return must set the complete pair. The
  current accumulator-only result followed by a caller-synthesized zero high
  byte is an implementation artifact, not the accepted word-return ABI.

No public type names a register, scroll register, mapper, bank, pointer, heap
object, or target runtime structure.

## Context and current break

The frozen [full `stage1` baseline](LargeWorldsStage1Baseline.md) has these
logical extents:

| Quantity | Decimal | Little-endian word |
| --- | ---: | --- |
| Hardware width | 312 cells | `38 01` |
| Hardware height | 40 cells | `28 00` |
| Pixel width | 2496 px | `C0 09` |
| Pixel height | 320 px | `40 01` |
| Acceptance floor top | 304 px | `30 01` |
| No hit | -1 | `FF FF` |

The source package already declares `Camera.SetPosition(...)`, the world-Y AABB
operands, `Camera.VerticalScrollMax()`, and `Camera.AabbHitTop(...)` as `i16`.
The intrinsic descriptor also declares hit-top as
`TargetIntrinsicReturnKind.I16`. The shared collector nevertheless converts the
camera and world-Y operands to `SdkByteExpression`. Both target word-assignment
paths call the value emitter, store the accumulator as the low byte, and write
zero as the high byte. The hit emitters themselves return only the low byte and
load `255` for no hit.

That accidental byte implementation creates two independent failures:

1. camera requests and logical source-cell cursors cannot distinguish the two
   sides of a low-byte wrap; and
2. the floor top `304` becomes low byte `48`, while a one-byte result has no
   representation that distinguishes every world top from the legacy `255`
   no-hit value.

Game Boy already stores logical camera X/Y as low/high pairs, but its requested
position and source-column cursors are byte-backed. NES stores its camera pixel
and hardware-cell cursors as bytes. Both targets correctly write only the low,
target-derived scroll values to their hardware interfaces. The public contract
therefore needs wider logical state, not wider hardware registers.

## Alternatives considered

| Alternative | Result | Reason |
| --- | --- | --- |
| **Two-byte world top with `0xFFFF` no hit** | **Selected** | The current public and descriptor return type is already `i16`; the runner needs a world top for `Land(top - FootOffset)`; the value is independent of a caller's camera snapshot; and LW-1.1 and LW-1.2 can widen operands and results separately. |
| Byte screen top with `255` no hit everywhere | Rejected as the world-query replacement | It is valid for visible screen tops and remains the actor-framework seam, but every world-space caller must retain exactly the camera snapshot used by the query and add it back. That changes more callers and can resolve against a later camera state. |
| Unsigned `u16` absolute coordinates and hit top | Rejected for v1 | The language and two-byte storage can carry `u16`, but the public camera facade, `Pixel` callers, intrinsic resolver, and descriptor catalog currently use `i16`. Changing all of them adds a signature and migration break without increasing the full-`stage1` acceptance envelope. `WorldPack` counts remain independently unsigned. |
| `{ hit, topLow, topHigh }` fixed record | Rejected for v1 | It is unambiguous but costs three bytes, needs a new aggregate intrinsic-return convention, and adds a boolean that `0xFFFF` supplies without object semantics. |
| Keep `255` in an `i16` result | Rejected | It still aliases a legitimate non-aligned world pixel and preserves the current misleading high-byte-zero lowering. |
| New public method such as `AabbHitTop16` | Rejected | The existing method and intrinsic already promise `i16`. Correcting that promise keeps one collision concept instead of permanently exposing a width suffix. |

The screen-relative alternative is not removed. It is the correct zero-cost
contract when a framework already has a projected screen AABB and a stable
camera snapshot, which is why `Camera.ScreenAabbHitTop(...)` retains it.

## Coordinate domains

| Domain | Unit and valid range | Stored form | Portable rule |
| --- | --- | --- | --- |
| Map width/height | Count of 8x8 hardware cells, `1..4096` | `i16` little-endian | `cellCount * 8` must fit an extent of at most 32768 pixels. The last pixel coordinate therefore fits `i16`. |
| Absolute world pixel | Pixel coordinate `0..32767` | `i16` little-endian | Inputs outside the active map are out of bounds; they do not wrap. |
| Logical camera position | World pixel `0..max(0, extent - viewport)` | `i16` little-endian | Movement compares the complete word. The target may advance toward a request under its existing bounded edge policy. |
| Hardware-cell coordinate | 8x8-cell index `0..4095` | `i16` little-endian | `worldPixel >> 3`; it is logical lookup state, not a hardware tile register. |
| Screen projection | Signed source expression; byte after visible-range validation | `i16` before culling, then byte | Off-screen negative or large values are culled before byte-only draw/collision operands. |
| AABB width/height and flags | `0..255` pixels / defined flag bits | Byte | These describe a bounded local query and do not become world addresses. |
| Source metatile coordinate | Hardware cell divided by pack metatile size | `i16` little-endian | It can cross 255 independently of chunk-local storage. |
| Chunk X/Y coordinate | Derived non-negative word | `i16` little-endian | Each axis is derived from the hardware-cell coordinate. The directory entry multiplication/offset remains checked `WorldPack` reader arithmetic, not an SDK coordinate scalar. |
| Chunk-local metatile | `0..7` on each axis in `WorldPack` v1 | Byte | Fixed by the 8x8 source-metatile chunk. |
| Metatile subcell | `0..metatileSize-1` | Byte | For `stage1`, each axis is `0..1`. |
| Edge-slot logical tag | Hardware-cell coordinate `0..4095` | `i16` little-endian | Two bytes per resident/preparing edge slot; target buffer coordinates remain bytes. |

`WorldPack` v1 can serialize larger unsigned 16-bit cell counts than this first
runtime coordinate envelope. A target selecting the v1 runtime must diagnose a
pack whose pixel extent exceeds 32768 rather than truncate it. A later SDK
version may adopt an unsigned or wider public coordinate domain without
changing `WorldPack` v1 bytes.

The use of `i16` is deliberate. The language and layout also support `u16`, but
existing gameplay source uses signed word arithmetic for movement and
projection, current public declarations and `TargetIntrinsicReturnKind` use
`i16`, and full `stage1` is far inside the positive range. Changing to `u16`
would require a facade/catalog/caller migration without helping this payload.
Absolute values remain non-negative; `-1` is reserved only as the collision
no-hit result.

## Collision result contract

### World-relative hit

`Camera.AabbHitTop(screenX, worldY, width, height, flags)` scans in its existing
top-to-bottom, left-to-right sample order. Its result is:

```text
hit:    top = (matchingWorldPixelY & 0xFFF8), 0 <= top <= 32760
no hit: top = -1                         , bits = 0xFFFF
```

The low byte is at destination offset 0 and the high byte at offset 1. Both
bytes are live on both paths. `0xFFFF` cannot be a tile top because valid tops
are non-negative multiples of eight.

At the SDK-operation boundary, `WorldY` is a word expression. `ScreenX` remains
a validated byte expression, width/height remain bounded local extents, and
flags remain compile-time bits. `Camera.AabbTiles(...)` uses the same word
world-Y input but continues to produce the byte fact `0` or `1`, zero-extended
only when stored in a word destination.

### Screen-relative hit

`Camera.ScreenAabbHitTop(screenX, screenY, width, height, flags)` returns an
8-pixel-aligned screen top `0..248`, or `255` for no hit. Its operands are
byte-backed only after visibility checks. This remains unambiguous because
`255` is not an aligned screen tile top, including the NES fine-scroll exposure
row.

The semantic range is byte-sized, but the public extern and intrinsic
descriptor return `I16`. A word destination therefore receives:

```text
hit:    0x0000..0x00F8 (8-pixel aligned)
no hit: 0x00FF
```

Game Boy returns the low fact in `L` and sets `H = 0`; NES returns it in `A`
and sets `X = 0`. A byte destination consumes `L` or `A`. This is a complete
word return with a known-zero high byte, not permission for an accumulator-only
intrinsic ABI.

Framework code may add the camera word to a screen hit while using the same
cached camera snapshot. The actor framework already follows that pattern with
split `y`/`yHi` state. It does not need to migrate to the world-hit result.

### Word-return ABI

The compiler-facing value layout is:

```text
destination + 0 = result bits 0..7
destination + 1 = result bits 8..15
```

For a target intrinsic whose descriptor return kind is `I16`, word assignment,
`let` inference, comparison, arithmetic, and explicit word casts consume both
bytes. The Game Boy cartridge ABI returns the word in `HL`, with `L` carrying
offset 0 and `H` offset 1. The NES cartridge ABI returns it in `A:X`, with `A`
carrying offset 0 and `X` offset 1. These register choices remain target
lowering details and never enter an SDK signature. A target may optimize a
known destination by storing the same two bytes directly, but the generic
word-assignment path must not append `high = 0` after an intrinsic. Byte
destinations consume the low return register only under the compatibility rule
below.

Byte-range `I16` facts follow the same rule. In particular,
`Camera.ScreenAabbHitTop(...)` returns `HL = 0x0000..0x00F8` or `0x00FF` on GB
and `A:X` with `X = 0` on NES. Its high byte is predictably zero, but is still
part of the target return postcondition.

## Full `stage1` trace

### Hardware columns 255 and 256

Full `stage1` uses 2x2 hardware-cell metatiles and 8x8-metatile chunks. Applying
the accepted [`WorldPack` v1 lookup](WorldPackFormatV1.md) gives:

| Hardware X | Word | Metatile X | Subcell X | Chunk X | Local X |
| ---: | --- | ---: | ---: | ---: | ---: |
| 255 | `FF 00` | 127 | 1 | 15 | 7 |
| 256 | `00 01` | 128 | 0 | 16 | 0 |

Moving right performs `255 -> 256`; moving left performs the exact reverse.
No byte is reused for both `255` and `256`, and the chunk transition is
`(15,7,1) <-> (16,0,0)`.

The target streaming traces for logical edge 256 are:

| Target/direction | Camera pixel crossing | Edge calculation | Logical edge tag | Hardware-facing value |
| --- | --- | --- | --- | --- |
| GB right | `1887 -> 1888` | old left tile `235` + 21-cell exposed edge = `256` | `00 01` | `SCX` receives camera low byte `60`; the background slot remains modulo 32. |
| GB left | `2056 -> 2055` | old left tile `257` - 1 = `256` | `00 01` | `SCX` receives camera low byte `07`; the background slot remains modulo 32. |
| NES right | `1791 -> 1792` | new left tile `224` + 32-cell lookahead = `256` | `00 01` | `$2005` receives the derived byte; nametable/buffer selection remains target-owned. |
| NES left | `2056 -> 2055` | new left tile `256` = exposed source `256` | `00 01` | `$2005` receives the derived byte; the target buffer column remains modulo 64. |

These are logical source-edge coordinates. They do not increase the existing
GB 21-tile edge commit or NES 32-tile/9-attribute phased commit.

After streaming, a collision lookup can address the same column without an
edge special case. With camera X `1920` (`80 07`) and screen X `128`, both
targets derive `(1920 + 128) >> 3 = 256`. The pack lookup selects metatile 128,
chunk 16, local metatile 0, subcell 0. The collision reader then obtains the
profile byte for that hardware cell. Only the camera/lookup words participate;
the final flag is still one byte.

### Floor top Y 304

The runner's feet-relative landing search supplies foot Y 304, subtracts the
4-pixel top offset, and scans a 12-pixel window. The existing sample offsets
are `0`, `8`, and `11`:

```text
query base       = 304 - 4 = 300
first floor hit  = 300 + 8 = 308
hardware row     = 308 >> 3 = 38
returned top     = 308 & ~7 = 304
result bytes     = 30 01
```

For the 2x2 `stage1` metatile shape, hardware row 38 selects metatile Y 19,
subcell Y 0, chunk Y 2, and local Y 3. A miss returns `FF FF`, so the hit and
miss compare differently as complete words.

At the bottom of the level, the target view differs but the world result does
not:

| Target | Viewport height | Maximum camera Y | Floor screen top | Returned world top |
| --- | ---: | ---: | ---: | ---: |
| GB | 144 | 176 (`B0 00`) | 128 | 304 (`30 01`) |
| NES | 240 | 80 (`50 00`) | 224 | 304 (`30 01`) |

The caller compares the word against `-1`, then resolves
`Land(304 - Player.FootOffset) = Land(273)`. The stored player Y is `11 01`.
No camera byte needs to be added back and no later camera update can change the
meaning of the returned world top.

## Fixed storage and lowering cost

### Storage

The contract requires only fixed scalar storage:

| Live value | Bytes | Notes |
| --- | ---: | --- |
| Logical camera X/Y | 4 | Two little-endian words. GB already has these four bytes; NES adds one high byte per axis. |
| Reused requested-axis word | 2 | X and Y may reuse one word because the current lowerers process axes sequentially. |
| Two `WorldPack` edge coordinates | 4 | Two bytes per edge slot; state/axis/direction remain separate byte tags from the pack ADR. |
| Cached hardware-cell X/Y | 0 or 4 | A target may derive them from camera words or cache two words; this is target-private and bounded. |
| One world hit local | 2 | Existing `i16` source locals already reserve two bytes. A screen hit or proven legacy byte destination uses one. |
| Hardware scroll shadows | 2 | One byte per axis; unchanged and target-owned. |

Thus the mandatory camera/request/edge-tag budget is 10 bytes per active camera
before target-private queue state. GB already supplies the four camera bytes,
so at most six of those bytes are new. NES supplies the two low camera bytes,
so at most eight are new. A target that caches both hardware-cell axes has a
fixed four-byte addition. Map dimensions are compile-time/ROM metadata and do
not require per-frame RAM.

There is no allocation, pointer, object header, identity, virtual dispatch,
mapper state, or unbounded collection.

### Representative target lowering

The focused analysis test fixes the costs of the word-return ABI plus a caller
store. These counts cover result materialization after the flag scan; the scan
itself is unchanged.

| Path | Game Boy LR35902 | NES 6502 |
| --- | --- | --- |
| Aligned word hit, zero extra offset | 9 instructions, 88 clock cycles: load/mask `L`, load `H`, then store `HL` to the caller word | 5 instructions, 14 CPU cycles: load/mask `A`, load `X`, then store `A:X` to zero-page caller storage |
| `0xFFFF` no hit | 5 instructions, 52 clock cycles: load `HL = FFFF`, then store both bytes | 4 instructions, 10 CPU cycles: load `A = FF`, transfer to `X`, then store both bytes |

The named instruction sequences and per-instruction cycle counts are asserted
by the analysis test. A signed constant offset still requires a bounded
carry-propagating word add before low-byte alignment. Its exact instruction and
cycle delta depends on whether the future emitter has a constant, direct local,
or indexed operand; LW-1.2 must measure the emitted path rather than treating an
unimplemented nominal sum as an ABI promise. No variant introduces a loop or
dynamic storage.

The register pairs are internal cartridge ABI details, not public SDK types.
The caller store avoids a persistent result scratch when the caller already
owns an `i16` local; an equivalent direct-store optimization has the same fixed
two-byte result layout.

For camera movement, GB already performs carry/borrow propagation into its
camera high bytes; LW-1.1 must compare the requested high byte as well as the
low byte. NES can implement a representative word increment as
`INC low; BNE done; INC high`, costing 8 cycles without carry and 12 with carry
in zero page. A representative equal-high word comparison costs 14 cycles
before its branch. These are constant-time word operations performed outside
the bounded background commit.

`Camera.Apply()` still writes target-owned bytes. GB continues to commit at
most 21 tile writes per exposed edge and writes the low camera bytes to
`SCX`/`SCY`. NES continues to commit at most 32 tile bytes and 9 attribute bytes
per phase and derives its `$2000`/`$2005` bytes from logical state. Widening does
not add VBlank writes or change those budgets.

## Compatibility and migration

### LW-1.1: coordinates and camera

LW-1.1 owns these changes:

- introduce a shared word-expression/storage shape for logical SDK operands;
- collect `Camera.Init` dimensions, `Camera.SetPosition` X/Y,
  `Camera.VerticalScrollMax`, world-pixel query operands, and logical
  hardware-cell/edge coordinates without truncating them to
  `SdkByteExpression`;
- compare and step complete camera words in both directions;
- keep screen operands, extents, flags, background slots, and hardware writes
  byte-backed;
- add `255 -> 256` and `256 -> 255` target tests using the logical edge tag,
  not only the hardware scroll byte.

Existing byte-backed camera variables and constants zero-extend into the word
operation. Small maps need no source change. A target may retain an optimized
byte lowering when compile-time bounds prove that the high byte is always zero;
existing ROM identity remains a target-test decision, not a public semantic
requirement.

### LW-1.2: collision result

LW-1.2 owns these changes:

- make the world-Y operand of `Camera.AabbTiles`/`AabbHitTop` a word;
- make `TargetIntrinsicReturnKind.I16` materialize both result bytes;
- emit `30 01` for the full-`stage1` floor and `FF FF` for no hit on both
  targets;
- migrate word callers to compare with `-1`;
- keep `Camera.ScreenAabbHitTop` semantics and its actor-framework `255`
  sentinel, while returning its `I16` value as zero-extended `0x00xx` through
  the complete target word ABI;
- add a diagnostic for unsafe narrowing described below.

The runner's `let footTile` inherits the `i16` return. Its
`CollisionProbe.NoTileHit` constant must change from `255` to `-1` in LW-1.2;
the landing expression remains `footTile - Player.FootOffset`. This ADR does
not edit the sample or its tracked ROMs.

An explicit byte destination for world `AabbHitTop` consumes the low result
byte. That preserves legacy behavior only when the active map has at most 32
hardware rows (pixel extent at most 256): every possible tile top is then
`0..248`, while `0xFFFF` narrows to `255`. The compiler must reject a byte
destination for a taller active world because a hit at 256 or above would
silently truncate. The diagnostic must direct the caller to an `i16` local and
the `-1` sentinel. Inferred/declared word destinations never use the legacy
narrowing rule.

Existing actor-framework landing remains byte-backed because it calls
`Camera.ScreenAabbHitTop`, checks `255`, and combines the result with cached
camera low/high bytes. It consumes the low byte of the zero-extended `I16`
result and is not an unsafe narrowing of the world-hit query.

## Consequences and non-goals

- LW-1.1 and LW-1.2 share the fixed word layout but can implement camera
  operands/state and collision results independently.
- World collision callers receive a stable absolute fact; screen-relative
  callers retain the cheaper byte fact when that is their natural seam.
- The first runtime envelope is intentionally smaller than every dimension
  representable in a `WorldPack` v1 header. Oversized packs fail explicitly.
- Carry/borrow and word comparisons add a bounded target cost, while hardware
  commit counts and register widths do not change.
- The `255` world no-hit contract is not retained for word callers. Only the
  unambiguous screen result and proven byte-destination compatibility keep it.

This ADR does not implement production operations, target code, a pack reader,
chunk decoding, edge scheduling, banking, a mapper, MBC state, a physics
engine, heap storage, object identity, dynamic dispatch, or a full-`stage1`
runner migration.

## Reproducible proof

`WorldCoordinateCollisionContractAnalysisTests` proves the byte encodings,
bidirectional 255/256 transition, `WorldPack` chunk-local coordinates, target
edge calculations, Y 304 hit/no-hit distinction, caller landing result,
fixed-storage totals, and representative GB/NES lowering costs:

```bash
dotnet test src/RetroSharp.Core.Tests/RetroSharp.Core.Tests.csproj -m:1 \
  --filter "FullyQualifiedName~WorldCoordinateCollisionContractAnalysisTests"
```

The production target paths remain unchanged until LW-1.1 and LW-1.2.
