# Static and Source-Camera Functional Acceptance

Status: CSL-3 / #338 implemented on top of the CSL-2 / #337 shared runner.

This rung executes exact ROM bytes compiled from the production sample sources.
It does not copy, simplify, or reimplement a sample for validation. Every test
compiles the source twice and requires byte-for-byte determinism; samples with a
tracked ROM also require the fresh output to equal that artifact.

## Scenario matrix

| Sample | Target | Warm-up + observed frames | What is retained and checked |
| --- | --- | ---: | --- |
| `static-drawing` | GB | 4 + 60 | Authored face/platform tiles, palette, display state, no post-startup unsafe VRAM/OAM writes |
| `static-drawing` | NES | 4 + 60 | Authored face/platform nametable, attribute-selected four-color palette identity, rendering state, no post-startup unsafe PPUDATA/OAM writes |
| `cross-target-camera` | GB | 20 + 161 | Right 80 px, left to origin, every transient visible tile/palette, request/resident/commit/visible state |
| `cross-target-camera` | NES | 20 + 161 | The same input/source contract over the authored 48-column mapper-0 world and both horizontal nametables |
| `source-vscroll` | GB | 20 + 260 | Down/up traversal through the 24-row source, row-wrap transients, SCY publication, legal row streaming |
| `source-free-scroll` | GB | 20 + 600 | Diagonal down-right/up-left, both 248/0 boundaries, 21x19 fine-scroll exposure, bounded two-frame raw diagonal publication |
| `source-free-scroll` | NES | 20 + 600 | The same 248/0 source boundaries across all four physical nametables, including fine-scroll exposure |
| `window-hud` | GB | 8 + 60 | Independent background and `$9C00` Window rows, palette, LCD Window enable, `WX=7`, `WY=0` |

The static samples intentionally have no gameplay loop. Every animated sample
asserts exactly one completed source/gameplay tick per observed physical frame,
a measured ratio of `1.0`, and a maximum missed-tick streak of `0`. Reviewed
scenario limits remain slightly looser (`ratio >= 0.98`, missed streak `<= 1`)
so a budget change stays a visible contract review rather than a learned value.
Request-to-resident and request-to-visible limits are one frame except for the
Game Boy diagonal raw path, whose reviewed bound is two frames. The runner may
drain only that declared deadline for a request made on the final observed
frame; drain frames cannot affect cadence or retained-integrity results.

## Independent integrity evidence

The test adapters read physical state after every frame:

- Game Boy: circular `$9800` background cells, `$9C00` Window cells where
  applicable, `BGP`, `SCX`, `SCY`, camera lifecycle WRAM, LY/LCD state for each
  VRAM/OAM write, resets, and completed `Video.WaitVBlank()` calls.
- NES: all applicable `$2000/$2400/$2800/$2C00` nametable cells, attribute
  quadrant selection, all four `$3F00..$3F0F` colors reachable by the retained
  background, coarse/fine scroll plus nametable selection, camera lifecycle
  RAM, reset-vector re-entry and completed wait counts, and the exact integer
  PPU-dot/odd-frame scanline, dot, and rendering phase of PPUDATA/OAM writes.

Authored oracles compute the expected visible tile and palette independently
from the sample's declared rows. They deliberately cover the extra right/bottom
tile exposed by fine scroll. A correct final screen cannot erase an earlier
wrong edge: all retained frames must have zero background/palette mismatches,
zero resets, and zero unsafe writes.

## Exact-ROM MCP checkpoints

The following 2026-07-13 checkpoints were captured through GameboyMcp/SameBoy
and NesMcp from the same production outputs used by the tests. MCP frame zero
and the deterministic test CPU have slightly different boot cut points, so the
behavioral boundaries, RAM, and hardware scroll state are the cross-check—not
an assumed shared startup offset.

| ROM SHA-256 | MCP evidence |
| --- | --- |
| GB `cross-target-camera`: `ae006da5ca8e63935bd71e9ee5a760ff91fe3eead134eda7fa7333868e0c6bd4` | After 80 held-right frames, source X is 80 and `SCX=79`; after 81 held-left frames, both are 0. LCD/background/sprites stay enabled. |
| NES `cross-target-camera`: `944b912359a0773e12842f012ef7d10fda06fb3c28b10a6871b5ee0cdabef2eb` | NesMcp observes source X 80 after the right span, then source/fine X 0 after the left span, with both background and sprite rendering enabled. |
| GB `source-vscroll`: `a387ca90e44822dd4511e1b4724ba3a0149be90de976836a5b68a9ddfd456804` | SameBoy observes source Y/`SCY` progress through 119/118 and then the wrap back through source 0, `SCY=1`, and `SCY=0` one frame later. |
| GB `source-free-scroll`: `de916a85396374d404adfc564263e6bb84b5703b424b915da5018b437ddd0913` | Source X/Y reach 248 together; hardware `SCX/SCY` reach 247 then 248 within the two-frame bound. The return reaches source 0 and visible 0 together within the same bound. |
| NES `source-free-scroll`: `45fae408c33bc28dd487ad8eeb1f463fdf9ae488e3af3245d9dfb02acc56044f` | Source X/Y reach 248 with nametable select 2 and fine X 7, then return through source 0 to nametable 0/fine X 0. Rendering stays enabled over all four-screen transitions. |

Tracked static artifacts remain deterministic as well: GB
`f8cbb5e6ff33c96c923277e0054d480f3444000187d65a64e262e4d0d0bd4afe`
and NES
`3115f01722a5b8ec08ffb44c9d0be340de3e8a7cebd045f2f13b30156a452aac`.

## Validation

```bash
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 \
  --filter FullyQualifiedName~SimpleSampleFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 \
  --filter FullyQualifiedName~SimpleSampleFunctionalAcceptanceTests
dotnet test RetroSharp.sln -m:1
tools/gameboy/generate_sample_roms.py --dry-run
git diff --check
```

This matrix is deliberately limited to #338. Packed/Tiled runtimes, music,
sprite-animation, collision/state samples, and the pipeline gate remain the
later CSL rungs. The two production corrections in this slice are confined to
the raw Game Boy camera path exposed by these exact samples: a bounded row-copy
loop that finishes its 21 writes in VBlank, and atomic diagonal hardware-scroll
publication after both raw edges are resident. `EmitApplyPackedCamera(...)` and
the packed/Tiled preparation, residency, commit, and publication paths are
unchanged.
