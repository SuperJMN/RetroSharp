# Source Vertical Scroll Sample

Sample Layer: `target-acceptance`

This sample exercises the Game Boy camera's vertical SDK path. It uses `Camera.SetPosition(0, y)` with a byte-backed Y value that moves by one pixel per frame, scrolls down, reverses, and scrolls back up over a world taller than the visible 18-tile band.

`Camera.Init(...)` receives the complete 24-row source height; the backend clips
the initial VRAM window and retains the remaining rows for runtime streaming.

The requested position is prepared before `Video.WaitVBlank()` and `Camera.Apply()` runs immediately after it, keeping row commits inside the legal VRAM window.

Build the Game Boy ROM:

```bash
dotnet run --project ../../src/RetroSharp.Cli/RetroSharp.Cli.csproj -- --target gb --out vscroll.gb vscroll.rs
```

The sample is Game Boy-only until NES vertical camera movement has a decided mirroring/mapper plan and a budgeted row/attribute streaming implementation.
