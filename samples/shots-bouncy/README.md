# Shots Bouncy Sample

Sample Layer: `target-acceptance`

`samples/shots-bouncy/shots-bouncy.retrosharp.json` is a Game Boy/NES
`target-acceptance` sample. It is the bouncing companion to `shots-simple`: same
`hero: 2` projectile pool cap, but each shot is a `GravityArc` fireball that
bounces off a solid floor while it travels right.

As in `shots-simple`, a fixed-cadence timer (`Fire.Interval`) stands in for a
player mashing the **B** (fire) button much faster than a shot can leave the
screen. Because the pool is declared with `hero: 2`, only two bouncing shots are
ever live at once: extra `shots.Request(...)` calls are dropped while both slots
are busy, and a new shot only appears after an earlier one leaves the screen.

The floor is a solid row along the bottom of the world: `World.Column(...)` draws
the visible floor tiles and the matching `World.Flags(...)` mark those cells
`Solid` (1). `shots.TouchTiles(0, 1)` queries those flags each frame, and the
`Bounce` tile-collision response flips the shot's vertical velocity so gravity
pulls it back into an arc.

Build it with:

```sh
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/shots-bouncy/bin/shots-bouncy.gb \
  samples/shots-bouncy/shots-bouncy.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/shots-bouncy/bin/shots-bouncy.nes \
  samples/shots-bouncy/shots-bouncy.retrosharp.json
```
