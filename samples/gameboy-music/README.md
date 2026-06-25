# Game Boy Multiple Music Themes Sample

Sample Layer: `target-capability-spike`

Build a Game Boy ROM that carries two BGM themes in **different formats** and switches between
them at runtime:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out music-switch.gb music-switch.rs
```

## What it shows

- Declaring more than one theme with `music.Asset(name, path)` (each name unique).
- Mixing formats: `music/terminate.gbapu` is an APU-register trace and `music/blue_ocean_remix.uge`
  is a hUGETracker song. Each compiled theme carries its own runtime kind.
- Switching themes at runtime with `music.Play(other)` -- here on the **START** button.
- No manual bank handling in source: the compiler stores each theme on its own and, when the
  combined program outgrows a 32 KiB ROM-only cartridge, emits an MBC1 banked ROM and resolves
  every bank switch inside the generated audio runtime. These two themes happen to pack into a
  ROM-only cartridge (the `.gbapu`/`.uge` compilers deduplicate aggressively); adding more or
  larger themes flips it to a banked ROM with the exact same source code. The banked multi-theme
  path is covered by `RetroSharp.GameBoy.Tests`.

## Controls

- **START**: toggle between *Terminate* (`.gbapu` trace) and *Blue Ocean Remix* (`.uge` tracker).

## Notes

- `audio.Update()` runs once per frame after `video.WaitVBlank()`; `input.Poll()` provides the
  `button_just_pressed(start)` edge used to trigger the switch.
- The sample has no graphics: it boots to a blank screen and plays BGM.

## Music credits

`music/blue_ocean_remix.uge` and `music/terminate.gbapu` are from Tronimal's *Free Game Boy Music
Pack* and are licensed `CC-BY 2025` (Tronimal). See `music/README.md`.
