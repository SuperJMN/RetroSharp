# Falling Blocks

Sample Layer: `target-acceptance`

`falling-blocks` is a compact Tetris-style game built from RetroSharp's fixed
storage, control flow, input facade, static tile resources, and runtime
`Tilemap.Set(...)` writes. The same source builds for Game Boy and NES.

Build both cartridges from the repository root:

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  samples/falling-blocks/falling-blocks.retrosharp.json
```

Controls:

- `Left` / `Right`: move, with delayed repeat.
- `A` / `B`: rotate clockwise / counter-clockwise.
- `Down`: soft drop.
- `Up`: hard drop.
- `Start`: restart after game over.

The game includes all seven tetrominoes, four orientations, board collision,
automatic fall, soft and hard drop, locking, multi-line compaction, increasing
fall speed, game over, and restart. A four-block preview to the right of the
board always shows the next piece in its spawn orientation. The compact 10x16
board and bottom border fit the complete Game Boy viewport. The meter at the
far right grows with cleared lines. Every orientation is normalized to its
visible left edge, so even the vertical I piece can occupy the board's leftmost
column without wall kicks.

The board is a fixed `u8[160]`; the shape table uses an initializer-inferred
fixed length, and restart clears the board through `countof(board)`. Piece,
cell-position, and game state use restricted class values plus receiver methods,
which lower to the same flat fixed storage and direct operations: there is no
heap, object identity, virtual dispatch, or runtime allocation. Line compaction
updates logical storage first, then redraws each 10-cell row across two VBlanks
so the visible update remains inside the Game Boy write budget.
The four active blocks and four preview blocks keep eight explicit
`Sprite.Draw(...)` call sites because each call site owns a fixed OAM slot on
the cartridge targets; receiver helpers prepare each slot without duplicating
the coordinate and tile-to-pixel logic.
As the sample has no generated background tiles and `block` is its first sprite
asset, settled cells reuse cartridge tile `6`; this keeps their 2bpp pattern and
logical palette identical to the falling blocks.
The piece order is a deterministic seven-piece cycle rather than random input.
Wall kicks, hold, ghost pieces, sound, and scoring text are intentionally out of
scope for this first playable sample.

When the next piece cannot enter the board, the active piece and preview
disappear while the settled board remains visible. Press `Start` to clear the
board and begin again with the initial active piece, next-piece preview, speed,
and cleared-line meter restored.

Runtime `Tilemap.Set(...)` remains a target-acceptance escape hatch rather than
a portable SDK v1 guarantee. Use it only for a fixed screen immediately after
`Video.WaitVBlank()`; camera/world streaming remains owned by the portable
camera and world APIs.
