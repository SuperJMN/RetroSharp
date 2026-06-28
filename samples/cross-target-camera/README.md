# Cross-Target Camera Sample

Sample Layer: `portable-sdk`

This sample is the first small portability acceptance case for the 2D SDK surface. The same `camera.rs` source builds for Game Boy and NES by using shared world data, tick input, horizontal camera positioning, and logical sprite drawing.

The source also exercises the current language surface shared by both cartridge targets: enum-backed constant groups such as `World.Width` and `Marker.ScreenX`, immutable `let` values inside the frame loop, SDK dot-calls, `loop`, and byte-backed locals for portable sprite draw frame and flip operands.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out cross-camera.gb camera.rs
```

Build the NES ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target nes --out cross-camera.nes camera.rs
```

The sample intentionally avoids raw target calls such as `sprite.Set(...)`, `scroll.Set(...)`, `tilemap.Set(...)`, `tilemap.Fill(...)`, `map_stream_column(...)`, and `objectPalette.Set(...)`.

Current intentionally excluded optional features:

- This portable sample uses only horizontal `camera.SetPosition(x, 0)`. NES vertical and diagonal free-scroll capability is covered by `../nes-free-scroll/freescroll.rs`, not by this cross-target sample.
- NES seeds a two-nametable horizontal buffer from `world.Map(...)` for this sample and streams new columns at runtime.
- NES `sprite.Draw(...)` accepts byte-backed frame and flip operands; palette slot remains a compile-time logical slot.
- This sample does not use collision queries, runtime animation, audio, or HUD APIs. Runner-shaped camera-relative collision and runtime animation are covered by the NES build of `samples/runner/runner.rs`; HUD, real NES audio playback, and generic world-space collision remain outside this sample.
