# tiled-gb-irregular-diagonal

Sample Layer: `target-acceptance`

Game Boy canary for diagonal streaming under an **irregular** horizontal cadence.
The vertical axis advances a steady pixel per frame, while the horizontal axis is
driven by a subpixel accumulator (speed 20 over 16) that yields 0, 1 or 2 pixels
on any given frame.

## Why this sample exists

The neighbouring `tiled-gb-fast-diagonal` canary moves at a uniform two pixels
per frame. Uniform motion is a blind spot: the runtime anchors a prefetched row
on a predicted camera column, and a perfectly regular camera always lands where
the prediction says it will, so the canary stays green even when the row payload
carries no horizontal slack at all.

Real gameplay does not move uniformly. `samples/runner` advances its camera
through a subpixel accumulator, and that is the motion that surfaced background
corruption on real hardware. When the camera drifts off the predicted column
between a row's preparation and its crossing, the tile at the diagonal corner —
the intersection of the newly prepared row and the newly prepared column — is
covered by neither edge and is left never written.

The failure needs slack on **both** sides: a camera drifting right runs off the
right end of the row payload, and a camera drifting left runs off the left end.
Anchoring the row one column to the left and widening it by two tiles covers
both.

This sample reproduces that: `validation/scenarios/tiled-gb-irregular-diagonal.gb.json`
observes 520 frames and reports 0 background mismatches. Against the pre-fix
runtime at commit `a4b2ba4` the same window reports 127, with the offending tiles
reading as never-written rather than stale.

## Build

```bash
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/tiled-gb-irregular-diagonal/irregular-diagonal.gb \
  samples/tiled-gb-irregular-diagonal/irregular-diagonal.rs
```
