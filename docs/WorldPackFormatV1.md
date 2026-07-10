# ADR: `WorldPack` v1 logical, packed, and staging format

Status: **accepted for LW-0.2 on 2026-07-10.**

This decision is the implementation contract for LW-1.3 and LW-1.4. It fixes
the shared logical seam and the target-owned packed payload shape; it does not
implement a packer, runtime reader, bank placement, mapper, wider coordinate
ABI, or runner migration.

## Context and measured choice

The frozen [full `stage1` baseline](LargeWorldsStage1Baseline.md) is 156x20
authored 16x16 cells, or 312x40 portable 8x8 cells. The current targets emit
one visual byte and one collision byte for every 8x8 cell:

| Representation | Visual bytes | Collision bytes | Tables/directory | Full `stage1` total |
| --- | ---: | ---: | ---: | ---: |
| Current expanded raw rows | 12,480 | 12,480 | 0 | 24,960 |
| Source-cell ID planes before tables | 3,120 | 3,120 | target-dependent | 6,240 |

`stage1` contains 3,120 source cells, 53 distinct authored visual metatiles,
and two distinct 2x2 collision profiles (`Empty` and `Solid`). Its generated
8x8 art remains target-owned: 82 Game Boy patterns and 90 NES patterns.

V1 therefore stores source-cell metatile IDs and target-owned expansion tables.
It uses 8x8 source-metatile chunks. The measured alternatives below show why:

| Source chunk | Chunks | Directory | Four decoded slots, 1-byte IDs | Worst chunk visits per 32-cell edge |
| --- | ---: | ---: | ---: | ---: |
| 4x8 | 117 | 2,340 bytes | 128 bytes | 5 |
| 8x4 | 100 | 2,000 bytes | 128 bytes | 5 |
| **8x8** | **60** | **1,200 bytes** | **256 bytes** | **3** |
| 16x8 | 30 | 600 bytes | 512 bytes | 3 |
| 16x16 | 20 | 400 bytes | 1,024 bytes | 2 |

For a candidate `cw` x `ch`, the table uses
`ceil(156/cw) * ceil(20/ch) * 20` directory bytes and
`4 * cw * ch` decoded-slot bytes. Because `stage1` expands each source cell to
2x2 hardware cells, a worst-aligned 32-cell edge intersects
`max(ceil((32 + 2*cw - 1)/(2*cw)), ceil((32 + 2*ch - 1)/(2*ch)))`
chunks. The smaller asymmetric choices save only 128 RAM bytes while adding
800–1,140 directory bytes and raising the worst edge from three to five chunk
visits. A 16x8 chunk does not reduce the three-visit bound but doubles decoded
RAM; 16x16 consumes half of the NES's 2 KiB RAM in decoded slots alone. The
8x8 choice is the measured balance: 256 decoded bytes, symmetric access, and at
most three chunk visits without the larger alternatives' NES RAM pressure.

## Layer boundary

The target-neutral logical model owns:

- dimensions in portable 8x8 cells;
- source-metatile expansion width and height;
- the 8x8-metatile chunk grid and deterministic traversal order;
- one visual-metatile reference and one collision-profile reference per source
  cell;
- collision profile bytes using `WorldTileFlags` meanings;
- format version, relative offsets, lengths, codecs, and malformed-data rules.

Each target owns:

- the bytes in a visual expansion record, including generated tile IDs and any
  palette/provenance metadata required by that target;
- generated pattern data and its placement;
- cartridge placement and the mechanism used to reach a relative pack offset;
- the reader/decompressor implementation and target residency bookkeeping;
- conversion of a staged logical edge into its bounded VRAM/PPU commit.

`World.Load(...)` remains the public authoring boundary. No chunk, bank, mapper,
PPU address, MBC register, cartridge window, or target expansion record becomes
a portable SDK argument. The packed bytes are an internal compiler artifact,
not a new gameplay-visible file API.

## Logical shape

### Units and metatile identity

`hardwareWidth` and `hardwareHeight` count portable 8x8 cells. Every pack has a
uniform `metatileWidth` and `metatileHeight`, also in 8x8 cells, and both
hardware dimensions must divide exactly by the corresponding metatile size.
The resulting source grid is:

```text
sourceWidth  = hardwareWidth  / metatileWidth
sourceHeight = hardwareHeight / metatileHeight
```

For Tiled input, one authored source cell is one logical metatile. Its visual
identity is the complete ordered authoring tuple needed for composition (for
the current importer, the optional `background` reference and the `world`
reference after GID validation). This preserves target-owned per-subcell
composition: a target expands the same logical identity to its own record.
Visual IDs are assigned by lexicographic authoring-key order. Empty references
sort before non-empty references, tilesets use importer order, and local tile
IDs use ascending numeric order. Duplicate target expansion bytes are allowed;
v1 does not make target quantization part of portable identity.

The compatibility adapter for an already-expanded `WorldTileGrid` uses a 1x1
metatile and orders distinct one-cell target records lexicographically. It is a
target lowering path, not a portable visual-ID promise.

### Collision profiles and overrides

A collision profile is exactly
`metatileWidth * metatileHeight` bytes in subcell row-major order. Profile 0 is
the all-`Empty` profile. Remaining unique profiles are sorted lexicographically
by their flag bytes. Only the defined `WorldTileFlags` bits (`Solid`, `Hazard`,
and `Platform`) are valid.

Every source-cell record carries a collision-profile ID independently from its
visual ID. There is no implicit visual-to-collision coupling in v1. Collision
resolution happens before profile interning:

1. Without an explicit collision layer, tileset/object metadata supplies the
   profile.
2. An explicit collision layer overrides that metadata for the source cell.
3. An existing `WorldMap2D` supplies its exact per-8x8-cell flags; grouping must
   reproduce those bytes without loss.

This rule preserves the current Tiled behavior (the current metadata or direct
collision value repeats over the source cell) while allowing a later importer
to describe non-uniform subcell profiles without changing the format. The
separate collision plane also lets collision lookup avoid decoding visual data.

## Deterministic chunk ordering

Chunks are fixed at 8x8 source metatiles. Edge chunks are clipped; padding
cells are neither serialized nor addressable.

```text
chunkColumns = ceil(sourceWidth  / 8)
chunkRows    = ceil(sourceHeight / 8)
chunkIndex   = chunkY * chunkColumns + chunkX
```

Horizontal worlds (`chunkRows == 1`) are stored left to right. Vertical worlds
(`chunkColumns == 1`) are stored top to bottom. Two-axis worlds are stored by
chunk row, then chunk column. Cells inside a chunk are stored by local row,
then local column. This is the only v1 ordering; camera direction does not
alter serialization.

## Binary envelope

All integers wider than one byte are unsigned little-endian. All offsets are
32-bit byte offsets relative to the first byte of the pack, never CPU or
cartridge addresses. Sections have byte alignment and no implicit padding.

### Header (48 bytes)

| Offset | Width | Field | V1 rule |
| ---: | ---: | --- | --- |
| 0 | 4 | `magic` | ASCII `RWPK` |
| 4 | 1 | `major` | `1` |
| 5 | 1 | `minor` | `0` |
| 6 | 2 | `headerBytes` | `48` |
| 8 | 2 | `hardwareWidth` | Positive 8x8-cell width |
| 10 | 2 | `hardwareHeight` | Positive 8x8-cell height |
| 12 | 1 | `metatileWidth` | Positive expansion width |
| 13 | 1 | `metatileHeight` | Positive expansion height |
| 14 | 1 | `chunkWidth` | `8` source metatiles |
| 15 | 1 | `chunkHeight` | `8` source metatiles |
| 16 | 2 | `chunkColumns` | Derived ceiling division |
| 18 | 2 | `chunkRows` | Derived ceiling division |
| 20 | 2 | `visualMetatileCount` | Positive; maximum 65,535 |
| 22 | 2 | `collisionProfileCount` | Positive; maximum 65,535 |
| 24 | 1 | `visualIdBytes` | Canonical width: 1 through 256 entries, otherwise 2 |
| 25 | 1 | `collisionIdBytes` | Canonical width: 1 through 256 entries, otherwise 2 |
| 26 | 1 | `targetCellStride` | Target-owned bytes per expanded 8x8 visual cell |
| 27 | 1 | `flags` | Must be zero in v1 |
| 28 | 4 | `collisionProfilesOffset` | Start of collision profiles |
| 32 | 4 | `targetExpansionsOffset` | Start of target visual records |
| 36 | 4 | `directoryOffset` | Start of the chunk directory |
| 40 | 4 | `chunkDataOffset` | Start of the first chunk plane |
| 44 | 4 | `packLength` | Exact total byte length |

The canonical section order is header, collision profiles, target expansions,
directory, and chunk data. Each section begins exactly where the previous one
ends. This makes output byte-for-byte deterministic and turns overlapping,
gapped, reordered, or trailing data into malformed input.

Collision profiles contain
`collisionProfileCount * metatileWidth * metatileHeight` bytes. Target visual
records contain
`visualMetatileCount * metatileWidth * metatileHeight * targetCellStride`
bytes. Subcells are row-major in both tables: subcell
`y * metatileWidth + x` is serialized before the next subcell.

The Game Boy target stride is exactly one byte per subcell: generated tile ID
`0..255`. Every byte value is a valid ID; there are no reserved GB expansion
bits or bytes in v1.

The NES target stride is exactly two bytes per subcell, in this order:

1. generated tile ID `0..255`;
2. render metadata: bits 0–1 are palette slot `0..3`, bit 2 is `1` when the
   non-empty value came from the `world` layer and `0` for background/empty,
   and bits 3–7 are zero.

The NES provenance bit preserves the existing attribute-quadrant tie-break in
which world-layer cells beat background cells before the upper/lower-row
rules. A NES reader rejects an expansion record whose metadata reserved bits
are non-zero. These exact records are deterministic target-owned bytes; their
layout is internal to NES lowering and never appears in a portable SDK type or
argument.

### Directory entry (20 bytes)

There are exactly `chunkColumns * chunkRows` entries in canonical chunk order.

| Offset | Width | Field | Rule |
| ---: | ---: | --- | --- |
| 0 | 4 | `visualOffset` | Relative offset of this chunk's visual plane |
| 4 | 2 | `visualStoredBytes` | Encoded visual-plane length |
| 6 | 2 | `visualDecodedBytes` | `cellCount * visualIdBytes` |
| 8 | 4 | `collisionOffset` | Relative offset of this chunk's collision plane |
| 12 | 2 | `collisionStoredBytes` | Encoded collision-plane length |
| 14 | 2 | `collisionDecodedBytes` | `cellCount * collisionIdBytes` |
| 16 | 1 | `validWidth` | Source cells in this chunk, 1 through 8 |
| 17 | 1 | `validHeight` | Source cells in this chunk, 1 through 8 |
| 18 | 1 | `visualCodec` | `0` raw, `1` element RLE |
| 19 | 1 | `collisionCodec` | `0` raw, `1` element RLE |

Chunk data is contiguous in directory order: visual plane, then collision
plane, for each chunk. A raw plane is the row-major ID sequence using its
declared one- or two-byte ID width.

### Element RLE

Compression is selected independently for each chunk plane. Codec 1 operates
on complete one- or two-byte IDs, not individual bytes:

- control `0..127`: `control + 1` literal IDs follow;
- control `128..255`: `(control & 0x7f) + 3` copies of the one following ID;
- runs of three or more are encoded as runs; shorter sequences join literals;
- packets are emitted left to right, with a maximum literal length of 128 and
  maximum run length of 130.

The encoder scans left to right. It emits maximal run packets (splitting runs
longer than 130) and otherwise accumulates the maximal literal packet up to 128
IDs or the next run of at least three. A one- or two-ID tail left after a split
run is encoded as a literal. This removes encoder-choice ambiguity.

The packer uses RLE only when it is strictly shorter than raw; ties use raw.
Consequently compression can never increase the budgeted payload. No v1 codec
may span chunks or planes, and a whole-level sequential stream is invalid.

## Random access

For hardware-cell coordinate `(x, y)`:

```text
metatileX = x / metatileWidth       subcellX = x % metatileWidth
metatileY = y / metatileHeight      subcellY = y % metatileHeight
chunkX    = metatileX / 8           localX   = metatileX % 8
chunkY    = metatileY / 8           localY   = metatileY % 8
entry     = chunkY * chunkColumns + chunkX
cell      = localY * validWidth + localX
subcell   = subcellY * metatileWidth + subcellX
```

A collision lookup reads one directory entry, decodes only that chunk's
collision plane into a fixed slot, reads its profile ID, and then reads one
profile byte. A camera edge reads the intersecting visual chunks, expands IDs
through the target table, and writes the target edge slot. Neither path reads
or decompresses the whole level; visual lookup never requires the collision
plane and collision lookup never requires the visual plane.

## Fixed staging and residency

The v1 staging contract has no dynamic allocation and no compressed-input
buffer. The decoder reads the target-owned ROM source directly into a slot.

- Two visual chunk slots, each `64 * visualIdBytes` bytes.
- Two collision chunk slots, each `64 * collisionIdBytes` bytes.
- Two target edge slots. A Game Boy slot is 21 tile bytes, matching its current
  maximum tile-write edge. An NES slot is 32 tile bytes plus 9 attribute bytes,
  matching its current bounded phase.

The two edge slots are peers, not permanently `current` and `next`. Each slot is
tagged with state (`empty`, `preparing`, `resident`, or `committing`), axis
(`column` or `row`), direction (negative or positive), and the logical
world-edge coordinate it represents. The coordinate's eventual scalar ABI is
owned by LW-0.4; the tag equality and residency rules are fixed here.

Outside VBlank/NMI, the scheduler fills the slots required by the requested
movement before exposing a crossing:

- For one same-axis crossing, one slot holds that edge.
- For Game Boy movement that crosses twice on one axis in a frame, the slots
  hold the near and far edges. `Camera.Apply()` consumes both in crossing order
  during that VBlank, preserving the already-merged two-edge behavior.
- For diagonal Game Boy or NES movement, one slot holds the column and the
  other holds the row. The axes drain on staggered VBlanks. Game Boy gives the
  column the first tie priority and then alternates the next axis; the other
  resident edge is retained. NES keeps its existing staggered next-axis
  schedule, and a phased NES row remains in its slot until all four tile phases
  and its attribute phase complete.

A resident slot is immutable. It becomes reusable only after its whole edge or
final target phase commits. A slot already tagged for the same
axis/direction/world-edge is reused as-is; a direction reversal may invalidate
and rebuild a `preparing` or uncommitted `resident` slot, but never a
`committing` one. A third same-axis edge, a replacement diagonal edge, or any
camera crossing for which the correctly tagged slot is not resident is
deferred. No camera state exposes tiles from an unstaged edge.

Intersecting chunks may be decoded sequentially into the chunk slots while both
expanded edge slots stay resident. Thus the same two payload slots cover both
near/far same-axis and column/row diagonal scheduling without allocating four
target edges.

This fixes payload staging without selecting a bank register, mapper, runtime
coordinate ABI, or sentinel; those remain target placement and LW-0.4 work.

| Fixed staging | Full `stage1` (1-byte IDs) | V1 maximum (2-byte IDs) |
| --- | ---: | ---: |
| Two visual slots + two collision slots | 256 bytes | 512 bytes |
| Two Game Boy edge slots | 42 bytes | 42 bytes |
| **Game Boy total** | **298 bytes** | **554 bytes** |
| Two NES edge slots | 82 bytes | 82 bytes |
| **NES total** | **338 bytes** | **594 bytes** |

The totals are the complete pack payload/edge buffers. Small target-owned
control fields (slot tags, progress, and the coordinate representation selected
by LW-0.4) are not pack buffers and are intentionally not specified here.

## Full `stage1` worst-case costs

The calculations are asserted by
`WorldPackFormatAnalysisTests.Full_stage1_v1_costs_are_reproducible`. The
pack-only raw-fallback budget is:

```text
chunks = ceil(156 / 8) * ceil(20 / 8) = 20 * 3 = 60
directory = 60 * 20 = 1,200 bytes
visual plane raw fallback = 3,120 * 1 = 3,120 bytes
collision plane raw fallback = 3,120 * 1 = 3,120 bytes
collision profiles = 2 * 2 * 2 = 8 bytes

GB  = 48 + 8 + (53 * 2 * 2 * 1) + 1,200 + 3,120 + 3,120 = 7,708 bytes
NES = 48 + 8 + (53 * 2 * 2 * 2) + 1,200 + 3,120 + 3,120 = 7,920 bytes
```

| Pack-only component | Game Boy | NES PRG side |
| --- | ---: | ---: |
| Header | 48 | 48 |
| Collision profiles | 8 | 8 |
| Target expansion table | 212 | 424 |
| Chunk directory | 1,200 | 1,200 |
| Raw visual plane fallback | 3,120 | 3,120 |
| Raw collision plane fallback | 3,120 | 3,120 |
| **WorldPack total** | **7,708** | **7,920** |

These are worst-case pack sizes for the measured level because every plane is
budgeted as raw; RLE may only reduce them.

The complete known non-code payload frozen by LW-0.1 is:

| Known payload | Game Boy ROM | NES PRG | NES CHR |
| --- | ---: | ---: | ---: |
| Raw-fallback `WorldPack` | 7,708 | 7,920 | 0 |
| Reserved + background + sprite patterns | 2,368 (`(6 + 82 + 60) * 16`) | 0 | 3,056 (`(6 + 95 + 90) * 16`) |
| BGM | 11,614 | 4,126 | 0 |
| DPCM blocks | 0 | 1,282 | 0 |
| SFX | 28 | 26 | 0 |
| **Known target subtotal** | **21,718** | **13,354** | **3,056** |

The NES combined known payload is 16,410 bytes, but PRG and CHR are physically
separate budgets; the combined figure is comparison only. These subtotals
exclude generated code, linker/runtime helpers, ROM/iNES headers, vectors,
alignment/padding, and cartridge placement. The 131,072-byte Game Boy capacity
probe is an output allocation size, not bytes used. The NES 41,907-byte
no-audio failure is not used to infer any packed layout or subtotal.

Against the current 24,960-byte expanded visual/collision rows, the uncompressed
v1 pack removes 17,252 bytes on Game Boy and 17,040 bytes on NES before any RLE.

## Malformed data and version compatibility

A v1 reader must reject the pack before staging any payload when any of these
conditions is true:

- magic, version, header size, fixed chunk dimensions, flags, or a codec value
  is not the v1 value;
- dimensions, metatile sizes, counts, ID widths, or target stride are zero;
- hardware dimensions are not exact multiples of metatile dimensions;
- any checked product, sum, ceiling division, range end, or relative-offset
  calculation overflows, or its result does not fit the field's declared
  8-/16-/32-bit width before allocation or reading;
- derived chunk counts, canonical ID widths, directory length, section starts,
  or `packLength` do not match the header;
- a section or plane is out of bounds, overlaps another range, introduces a
  gap, is out of canonical order, or leaves trailing bytes;
- an edge chunk's valid dimensions differ from the exact remaining source
  rectangle, or a decoded length differs from `validWidth * validHeight *
  idBytes`;
- raw stored and decoded lengths differ;
- RLE ends early, overruns the declared decoded length, splits a two-byte ID,
  contains trailing encoded bytes, or produces an out-of-range ID;
- collision profile 0 is not all `Empty`, remaining profiles are not strictly
  lexicographic and unique, a collision byte contains bits outside `0x07`, or
  any visual/collision ID is outside its table;
- a target stride differs from the linked reader (`1` for Game Boy, `2` for
  NES), or a NES expansion metadata byte has any reserved bit 3–7 set.

All derived byte products and offset/range arithmetic are evaluated with
checked arithmetic at least 64 bits wide. A writer rejects a result that cannot
be serialized; a reader rejects it before converting to an allocation length
or target address. Wraparound is never a compatibility behavior.

Major versions are incompatible. The v1 implementation emits exactly 1.0 and
accepts exactly 1.0. A future minor version may add behavior only through an
explicitly specified reader update; v1 readers do not silently accept it.
Portable `WorldMap2D` and target-owned `WorldTileGrid` constructors and lookup
semantics remain unchanged. Tooling may materialize either legacy resource by
walking chunks, but large-world runtimes use bounded lookup. Existing small
outputs stay on the current raw path and remain byte-identical unless a test or
later task explicitly selects packing.

## Consequences, rejected alternatives, and non-goals

- Expanded 8x8 raw rows retain the measured 24,960-byte duplication and do not
  define random-access compression or staging boundaries.
- Whole-level RLE/LZ streams make a late collision or camera-edge lookup depend
  on all preceding data and are not v1 packs.
- Portable target tile IDs or palette fields would reverse the existing
  `WorldMap2D`/`WorldTileGrid` separation.
- A collision flag implied only by visual identity cannot preserve explicit
  collision-layer overrides or arbitrary existing `WorldMap2D` flags.
- Wider chunks trade too much fixed RAM for modest directory savings on the
  first NES target; v1 remains 8x8 source metatiles.

The deliberate costs are a 1,200-byte `stage1` directory, two independent ID
planes, and target expansion records that may duplicate bytes when two authored
identities quantize alike. In return, RAM is fixed, collision and visuals can be
fetched independently, output order is deterministic, and future cartridge
placement does not alter the logical or binary pack.

This ADR does not implement the production model, packer, codec, reader,
banking, mapper, cartridge placement, coordinate/collision ABI, or runner
migration. Later work may optimize target expansion records or add codecs only
behind a new compatible version; it must not reopen the v1 topology or leak
target hardware into portable source.
