# Shots Simple Sample

Sample Layer: `target-acceptance`

`samples/shots-simple/shots-simple.retrosharp.json` is a Game Boy/NES
`target-acceptance` sample focused on a single thing: proving that a hero
projectile pool caps the number of on-screen shots at **2**, with no glitches.

The scene is deliberately bare - a small player marker on a blank world - so the
only moving pieces are the shots. A fixed-cadence timer (`Fire.Interval`) stands
in for a player mashing the **B** (fire) button much faster than a shot can
leave the screen. Because the pool is declared with `hero: 2`, only two shots
are ever live at once: extra `shots.Request(...)` calls are dropped while both
slots are busy, and a new shot only appears after an earlier one leaves the
screen and frees its slot.

The shots are plain `Linear` projectiles (constant `speedX`, no gravity, no tile
collision, no muzzle/impact effects). A separate sample covers bouncing shots.

Build it with:

```sh
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/shots-simple/bin/shots-simple.gb \
  samples/shots-simple/shots-simple.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/shots-simple/bin/shots-simple.nes \
  samples/shots-simple/shots-simple.retrosharp.json
```
