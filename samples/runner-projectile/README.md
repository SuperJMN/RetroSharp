# Runner Projectile Sample

Sample Layer: `target-acceptance`

`samples/runner-projectile/runner-projectile.retrosharp.json` is a small Game Boy/NES target-acceptance variant of the runner setup. It reuses the runner Mario spritesheet and keeps the scene intentionally minimal so `Projectiles.Pool`, `Projectiles.Def`, `Effects.Pool`, `Effects.Def`, `Input.WasPressed(Button.B/A)`, and multiple `shots.Request(...)` call sites are easy to inspect.

Build it with:

```sh
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target gb \
  --out samples/runner-projectile/bin/runner-projectile.gb \
  samples/runner-projectile/runner-projectile.retrosharp.json

dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes \
  --out samples/runner-projectile/bin/runner-projectile.nes \
  samples/runner-projectile/runner-projectile.retrosharp.json
```
