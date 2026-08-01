# tiled-gb-fast-diagonal

Sample Layer: `target-acceptance`

Game Boy canary for the fastest diagonal camera cadence the packed streaming
runtime supports: **two pixels per frame on both axes**, so a new column and a
new row enter the visible window every four frames.

The camera bounces vertically over the full `Camera.VerticalScrollMax()` range
and horizontally over the first 320 pixels of `samples/shared/platformer-assets/maps/stage1.tmx`,
so the window exercises all four diagonal quadrants (down-right, up-right,
down-left, up-left) against a world that is 40 rows tall — taller than the
32-row Game Boy background buffer, so rows really do stream and wrap.

## Why this sample exists

At two pixels per frame a column edge is prepared several frames before it
becomes visible, and the camera can cross a tile row in between. If the column
payload is anchored to the camera position at *preparation* time with no
vertical slack, the row that scrolls into view is left holding the tile of the
world column 32 positions earlier — visible as stale background tiles marching
in from the leading edge.

`validation/scenarios/tiled-gb-fast-diagonal.gb.json` observes 520 frames and
reports 0 background mismatches. Against the pre-fix runtime the same window
reports 108, starting at frame 152.

The slower `tiled-gb-diagonal-streaming` canary (one pixel per frame) does not
reproduce this: the margin is only exhausted at the faster cadence.

## Build

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/tiled-gb-fast-diagonal/fast-diagonal.gb \
  samples/tiled-gb-fast-diagonal/fast-diagonal.rs
```
