# NES Free Scroll Roadmap (free 2-axis camera scrolling)

Status: **implemented through NF-9 on branch `feature/nes-free-scroll`; NF-10
remains a separate epic.** This is the hard NES milestone: pixel-level,
artifact-free scrolling in **both** axes simultaneously (8-direction / diagonal),
the kind Super Mario Land 2 does on Game Boy and games like Gauntlet do on NES.

It is written so an autonomous agent (Codex) can pick up a task and know exactly
what to change, where, and how to verify it. Read `AGENTS.md`,
`docs/AgentContext.md`, `docs/NesTarget.md`, and
`docs/CameraVerticalScrollRoadmap.md` first.

## TL;DR for the agent

- **Free 2-axis scroll fundamentally needs 4 distinct nametables.** The NES PPU
  has only 2 KiB internal VRAM = 2 nametables. When the camera scrolls diagonally
  the visible 256x240 window straddles a 2x2 grid of nametable quadrants; with
  only 2 physical nametables, two of those quadrants alias to the same memory and
  you get duplicated/garbage corners. The clean fix is **four-screen VRAM** (the
  cart supplies the extra 2 KiB so all four nametables are distinct → a 512x480
  virtual surface). The mapper choice is *orthogonal*: it only matters for level
  size, CHR/PRG banking, and an IRQ-split HUD — not for the scroll itself.
- **The test substrate must be built first.** The current NES behavioral test
  path is the `nes_debug` MCP (`Nes.Mcp`, ADNES core). ADNES today supports
  NROM/CNROM/UxROM/MMC1 and only 2-nametable mirroring — **no four-screen**.
  Decision (made): **extend ADNES with four-screen** in the `NesMcp` repo so the
  MCP and CI can validate free scroll. This is **NF-0** and gates everything else.
- **Ship the easy win before the hard one.** With four-screen, a level that fits
  in 512x480 scrolls freely with *zero* runtime streaming (upload 4 nametables at
  startup, then just write scroll registers). NF-3 delivered that first; NF-4..NF-6
  then added staggered column/row/attribute streaming for larger source-authored
  worlds without starting the mapper-backed NF-10 work.
- Keep the golden rule: the language and classic IR never learn about cameras;
  free scroll lives in `Sdk2DOperation` + NES lowering + `Target2DCapabilities`.

## Why this is hard on NES (the core constraint)

- 2 physical nametables (2 KiB). iNES mirroring picks how the 4 logical
  nametables map onto them:
  - Vertical mirroring (current: `NesRomBuilder` `rom[6] = 0x01`) → two
    side-by-side tables → seamless **horizontal** scroll only.
  - Horizontal mirroring → two stacked tables → seamless **vertical** scroll only.
  - Single-screen → one table.
- A diagonal camera needs both axes seamless at once → it reads from all four
  logical nametables → needs four *distinct* physical nametables → **four-screen
  VRAM**. This is a cart feature (iNES flags6 bit 3), honored by accurate
  emulators; on real hardware it needs the extra 2 KiB SRAM on the board.
- A 2-nametable mapper with runtime mirroring control (MMC1) does **not** solve
  this — it only lets you switch which single axis is seamless (good for
  axis-locked rooms like Metroid, not free scroll).

## Decisions (locked for this branch)

1. **Substrate = four-screen VRAM.** Start on **NROM + four-screen** to isolate the
   scrolling problem from banking. The 512x480 surface is enough for a real
   free-scroll demo. Larger levels (streaming beyond 2x2, or banking/HUD-IRQ) move
   to a mapper in the follow-on epic (NF-10).
2. **Test emulator = extend ADNES** (in `~/Repos/NesMcp`) with four-screen, rather
   than adopting an external emulator. Optional cross-check with Mesen2 later.

## What exists today (verified in source)

- NF-0..NF-3 are implemented in the active branches:
  - `~/Repos/NesMcp` branch `feature/four-screen-nametables` adds ADNES
    four-screen nametable VRAM and validates four distinct nametables.
  - RetroSharp branch `feature/nes-free-scroll` emits the iNES four-screen bit
    when a program uses vertical camera movement, uploads four nametables at
    startup, and keeps horizontal-only programs on the existing two-nametable
    vertical-mirroring path.
- NES still emits NROM-256: 32 KiB PRG + 8 KiB CHR. Four-screen is selected with
  iNES flags6 bit 3 only for vertical-camera programs; horizontal-only programs
  keep the old flags6 vertical-mirroring bit.
- The bounded free-scroll path preloads the first 64x60 four-screen surface and
  writes `$2000`/`$2005` for X/Y camera movement. Source maps narrower than that
  surface wrap into the unused buffer columns during startup instead of exposing
  tile-0 padding when the camera reaches the right-hand nametables. It tracks
  absolute source X/Y tile positions and maps Y through the 60-row buffer before
  deriving the NES 240-pixel coarse-Y scroll, so `$2005` never receives
  240..255.
- Horizontal-only maps retain the previous runtime column-streaming path for
  worlds wider than 32 columns. Four-screen maps wider than 64 columns stream
  the next off-screen visible-edge column while keeping the four nametables
  distinct.
- Source-authored worlds taller than 60 rows queue the exposed vertical row at
  tile-boundary time. `Video.WaitVBlank()` drains one pending camera stream phase
  at VBlank entry, before sprite DMA can consume the window; a later
  `Camera.Apply()` in the same frame is skipped unless the source changes camera
  state after the wait. Rows stream through a row-pointer table as four 8-tile
  phases plus a separate attribute phase. The attribute phase refreshes the
  worst-case 9 touched bytes with palette slot 0. Row tile writes are emitted as
  contiguous `$2007` segments, resetting `$2006` only at a 32-column nametable
  boundary. Diagonal column/row crossings are intentionally staggered as one
  edge or row phase per VBlank.
- Capabilities now describe the free-scroll substrate: `ScrollAxes: Horizontal |
  Vertical`, `SupportsFineScrollY: true`, `BackgroundBufferTiles: 64x60`,
  `MaxBackgroundTileWritesPerFrame: 32`, `MaxAttributeWritesPerFrame: 9`,
  `RuntimeBackgroundStreamingAxes: Horizontal | Vertical`, and
  `CameraMovementStreamsBackground = false` for preloaded camera movement.
- `Sdk2DOperation.StreamMapRow` lowers on NES for explicit source-authored row
  spans and is also used by the shared capability/budget model.
- `samples/nes-free-scroll/freescroll.rs` remains the source-authored NES four-screen
  behavioral proof and now also builds for Game Boy, where the same diagonal source
  exercises the GB staggered one-axis-queue-per-VBlank streaming policy. `samples/tiled-free-scroll/free-scroll.rs`
  is the matching Tiled `World.Load(...)` diagonal proof inside the 64x60 four-screen surface.
- **No in-process NES emulator** in the repo (GB has `GameBoyTestCpu`; NES does
  not). Behavioral NES testing is via the `nes_debug` MCP (`Nes.Mcp`, ADNES).

## Test strategy (built before the feature)

NES behavioral validation uses the `nes_debug` MCP. Relevant tools: `load_rom`,
`run_frame`, `run_input_timeline`/`press_buttons`, `read_ppu_state` (scroll, fine
X, nametable byte), `dump_tilemap` (nametable contents), `capture_screen` /
`read_screen_region` (rendered output), `read_memory`, watchpoints, savestates.

Golden-byte tests in `src/RetroSharp.NES.Tests/` remain the fast CI layer (assert
emitted PPU writes, header bytes). Behavioral acceptance (does it actually scroll
in both axes without corner artifacts) runs through the MCP, and optionally
through an in-process `NesTestCpu` once ADNES is vendored into the test project
(mirroring `GameBoyTestCpu`).

## Guardrails (do not violate)

- Language and classic IR never gain camera/scroll concepts. Free scroll stays in
  `Sdk2DOperation` + NES lowering + `Target2DCapabilities`.
- Capability-check before lowering; keep clear diagnostics. Do not silently lower
  an unsupported scroll mode.
- Keep horizontal scroll and the existing single-axis path working with no
  regression and byte-identical tracked ROMs unless a task deliberately changes a
  tracked sample.
- Respect the **240 coarse-Y wrap** (nametable is 30 rows; `$2005` Y of 240..255
  is invalid and corrupts attribute fetches).
- Do not add a genre-specific operation if `SetCameraPosition` / `StreamMapColumn`
  / `StreamMapRow` suffice.

## Reliable commands

```bash
dotnet test RetroSharp.sln -m:1
git diff --check
# Build the NES sample after changing it:
dotnet run --project src/RetroSharp.Cli/RetroSharp.Cli.csproj -- \
  --target nes --out samples/<sample>/<name>.nes samples/<sample>/<name>.rs
# Behavioral check via the nes_debug MCP: load_rom -> run_frame/run_input_timeline
# -> read_ppu_state / dump_tilemap / capture_screen.
```

---

## Phase NF-0 — Extend ADNES with four-screen (TEST PREREQUISITE, do first)

Without a four-screen-capable emulator, free scroll cannot be validated. This work
lives in the `~/Repos/NesMcp` repo, not RetroSharp.

- Files (`~/Repos/NesMcp`):
  `src/Nes.Debug.Emulator/Adnes/Cartridge/Mappers/Enums/NametableMirroring.cs`,
  `src/Nes.Debug.Emulator/Adnes/Cartridge/NESCartridge.cs:82`, the PPU nametable
  read/write resolution, `src/Nes.Debug.Mcp/NesDebugTools.cs` (expose 4 NTs).
- Steps:
  - [x] Add `FourScreen` to `NametableMirroring`.
  - [x] Parse iNES flags6 bit 3 in `NESCartridge` (four-screen overrides bit 0);
    allocate 4 KiB nametable VRAM (4 distinct nametables) when set.
  - [x] Resolve PPU `$2000-$2FFF` to 4 distinct nametables under `FourScreen`.
  - [x] Make `read_ppu_state` / `dump_tilemap` able to address all four
    nametables.
- Verify:
  - [x] A hand-crafted four-screen ROM that writes a distinct tile to each of the
    four nametables reads back four distinct tilemaps via `dump_tilemap`.
  - [x] NesMcp unit tests cover four-screen address resolution; existing
    NROM/MMC1 tests stay green.
- Output: a published/locally-runnable `nes_debug` MCP that emulates four-screen.

---

## Phase NF-1 — Emit four-screen ROM substrate (RetroSharp NES)

- Layer: NES target.
- Files: `src/RetroSharp.NES/NesRomBuilder.cs` (iNES header + startup nametable
  upload), `src/RetroSharp.NES/NesTarget.cs`.
- Steps:
  - [x] Set iNES flags6 four-screen bit (`rom[6] |= 0x08`) when the program
    requests free scroll. (Note: a current test pins `rom[6] & 0x01 == 1`; update
    it.)
  - [x] Upload the initial 2x2 nametable grid at startup (extend
    `EmitNameTableUpload` from 2 to up to 4 nametables; each is 960 tiles + 64
    attribute bytes).
  - [x] Keep horizontal-only programs on the existing two-nametable vertical
    mirroring path (no regression).
- Verify: golden-byte test asserts the four-screen header bit and the four startup
  nametable uploads; the ROM boots in the four-screen `nes_debug` MCP.

---

## Phase NF-2 — 2D NES camera model

- Layer: NES target.
- Files: `src/RetroSharp.NES/NesRomBuilder.cs` (`NesCameraConfig`, `CameraNewX*`
  state, `EmitSetCameraPosition`, `EmitApplyCamera`).
- Steps:
  - [x] Add absolute camera Y state next to X (track coarse Y 0..29 + the vertical
    nametable select bit + fine Y 0..7), mirroring the X state.
  - [x] Generalize `NesCameraConfig` to a world that is wider **and** taller than
    one screen.
  - [x] In `EmitApplyCamera`, write **both** nametable select bits to `$2000`
    (bit 0 = X table, bit 1 = Y table) and **both** scroll bytes to `$2005`
    (X then Y), handling the 240 coarse-Y wrap.
- Verify: `read_ppu_state` reports the expected scroll X/Y, fine X, and nametable
  byte for a set of camera positions across the 512x480 surface.

---

## Phase NF-3 — First free-scroll win: level fits in 512x480 (no streaming)

This is the fast, low-risk milestone: prove genuine diagonal scroll with no
runtime VRAM streaming.

- Layer: NES target + sample + validation.
- Files: `samples/nes-free-scroll/freescroll.rs`, `samples/manifest.json`,
  `samples/README.md`, NES acceptance tests.
- Steps:
  - [x] Author a level that fits entirely in the 512x480 four-screen surface.
  - [x] Upload all four nametables (+ attributes) at startup; the per-frame loop
    only updates `$2000`/`$2005` from the 2D camera. No `$2007` streaming.
  - [x] Drive the camera diagonally (X and Y both change) and confirm no corner
    artifacts.
- Verify:
  - [x] Golden-byte: per-frame writes are only scroll-register writes, no runtime
    `$2007`.
  - [x] Behavioral (MCP): `dump_tilemap` shows all four nametables distinct, and
    `run_input_timeline` / PPU-state reads show X/Y camera state and the vertical
    nametable bit changing as the camera moves diagonally.

---

## Phase NF-4 — Vertical row streaming (levels taller than the surface)

Status: implemented with a deferred, staggered policy. A vertical boundary
crossing queues the 32-tile row that is about to enter the visible window, not
the full 64-tile four-screen row. `Camera.Apply()` drains one pending row phase
during the caller's VBlank: four phases write 8 contiguous `$2007` tile bytes
each, and a fifth phase refreshes the 9-byte worst-case attribute span. Every
phase restores PPUCTRL/PPUSCROLL before rendering resumes. If a frame also
crosses a horizontal boundary, the queued column and row are drained across
separate `Camera.Apply()` calls so no single VBlank combines both edges.

- Layer: NES target.
- Files: `src/RetroSharp.NES/NesRomBuilder.cs` (stream helpers),
  `src/RetroSharp.NES/NesSdkOperationLowerer.cs` (add `StreamMapRow` case — it
  currently throws), `src/RetroSharp.Core/Sdk/Sdk2DOperationValidator.cs`.
- Steps:
  - [x] On a vertical tile-boundary crossing, stream the next world row into the
    off-screen (stacked) nametable via `$2006`/`$2007`, handling the 240 wrap and
    selecting the correct nametable quadrant.
  - [x] Lower `Sdk2DOperation.StreamMapRow` on NES (mirror the existing
    `EmitStreamColumn*` horizontal path).
- Verify: golden-byte row-write tests; MCP shows fresh rows entering as the camera
  scrolls vertically past the initial surface; horizontal regression green.

---

## Phase NF-5 — Diagonal streaming (both column and row in one crossing)

Status: implemented with the same deferred staggered policy as NF-4. Preloaded
64x60 movement still only restores scroll registers. Larger source-authored
worlds stream at most one column or one row phase per `Camera.Apply()` VBlank; a
diagonal tile crossing queues both edges and drains them over successive frames
before each frame's scroll restore.

- Layer: NES target + validator.
- Files: `NesRomBuilder.cs` stream scheduler, `Sdk2DOperationValidator.cs`.
- Steps:
  - [x] When a frame crosses both a column and a row boundary, stream the new
    column **and** the new row (the exposed L-shape + corner) into the correct
    quadrants.
  - [x] Apply a per-frame VBlank budget. NES VBlank (~2273 CPU cycles) must also
    cover OAM DMA (~513 cycles); a full column (30) + row (32) + attributes may
    not fit. Stagger across frames if needed (commit one edge per VBlank), like
    the Game Boy strategy A, and document the policy.
- Verify: a diagonal-crossing test stays within budget or is explicitly staggered;
  MCP behavioral diagonal scroll shows both fresh column and fresh row.

---

## Phase NF-6 — Runtime attribute streaming

Status: implemented for runtime row streaming. Startup attributes are still
derived from Tiled palette provenance, while runtime streamed rows refresh the
touched attribute bytes as palette slot 0. Rich runtime palette provenance for
newly streamed Tiled rows remains future refinement, not an NF-10 mapper
requirement.

- Layer: NES target + capability.
- Files: `NesRomBuilder.cs`, `NesRomCompiler.cs` (`ApplyWorldAttributes` →
  runtime equivalent), `NesTarget.cs` (`MaxAttributeWritesPerFrame`).
- Steps:
  - [x] Raise `MaxAttributeWritesPerFrame` above 0.
  - [x] Stream attribute bytes for newly exposed rows. Attribute
    granularity is 16x16 px (one byte per 32x32 px / 4x4 tiles); the 30-row
    nametable means the bottom attribute row covers only 2 tile rows — handle the
    partial row.
- Verify: golden-byte attribute-write tests; MCP screen capture shows correct
  palettes on freshly streamed edges (no attribute "smear").

---

## Phase NF-7 — Capability + validator: allow NES free scroll

- Layer: shared SDK.
- Files: `src/RetroSharp.NES/NesTarget.cs`,
  `src/RetroSharp.Core/Sdk/Sdk2DOperationValidator.cs`,
  `src/RetroSharp.Core/Targeting/Target2DCapabilities.cs` (if a `FreeScroll` /
  four-screen capability is needed).
- Steps:
  - [x] Flip NES `ScrollAxes` to `Horizontal | Vertical` and
    `SupportsFineScrollY: true` behind the bounded four-screen implementation.
    Attribute budget remains `0` until NF-6.
  - [x] Stop rejecting bounded `Horizontal | Vertical` for NES; keep a clear
    diagnostic for maps that do not fit the preloaded four-screen surface.
  - [x] Update `RequireVerticalCameraAxis` / `RequireCameraMovementBudget`
    accordingly.
- Verify: validator tests for the new accepted/rejected cases; the
  vertical-scroll roadmap's NES gate message is replaced, not silently dropped.

---

## Phase NF-8 — World / Tiled data for 2-axis levels

- Layer: portable SDK asset pipeline.
- Files: `src/RetroSharp.Core/Sdk/Tiled/LogicalTiledMapImporter.*`,
  `src/RetroSharp.NES/NesTiledWorldImporter.*`, sample maps.
- Steps:
  - [x] Provide a world wider **and** taller than the screen, with source column
    **and** row tables available to NES streaming.
  - [x] Respect the current one-byte source-column runtime limit (or lift it
    deliberately); document the maximum free-scroll level size per substrate.
- Verify: `samples/tiled-free-scroll/free-scroll.rs` loads a Tiled map and scrolls
  in both axes; Game Boy parity unaffected.

---

## Phase NF-9 — Acceptance: sample + tests + cross-check

- Layer: samples + validation + docs.
- Files: `samples/nes-free-scroll/`, `samples/tiled-free-scroll/`, NES acceptance tests, `docs/NesTarget.md`,
  `samples/manifest.json`, `docs/ArchitectureRoadmap.md`.
- Steps:
  - [x] Golden-byte tests for the four-screen header, scroll-register writes,
    explicit row streaming, runtime row streaming, attribute refresh, and the
    shared runner using its current horizontal `stage1.playable` path.
  - [x] MCP behavioral acceptance: diagonal `run_input_timeline`, `dump_tilemap`,
    and `read_ppu_state` assert both axes move and the four nametables stay
    distinct.
  - [ ] Optional Mesen2 cross-check of the same ROM.
  - [x] Update `docs/NesTarget.md` and `ArchitectureRoadmap.md` for four-screen
    free scroll plus staggered runtime streaming.
- Verify: `dotnet test` green; the sample is a tracked NES ROM artifact.

---

## Phase NF-10 — Follow-on: mapper-backed large levels + HUD (separate epic)

Not in this branch. For levels larger than 512x480, or a stable HUD split:

- Large Worlds LW-0.3 has selected the mapper 4 / TVROM-style four-screen
  profile in
  [`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md).
  Production header/linker/runtime work remains a later issue.
- NesMcp `auto` now routes mapper 4 to AprNes. Existing tests prove mapper-4
  loading, instruction stepping, auto routing, and bank changes; the production
  slice still owes one combined mapper-4 + four-screen behavioral probe.
- Keep mapper-backed level reads separate from a scanline IRQ HUD. MMC3 IRQs
  remain disabled until a HUD issue supplies its own timing and acceptance.
- Stream content beyond the 2x2 grid as the camera moves across a large level.

---

## Acceptance criteria (free-scroll milestone)

- `samples/nes-free-scroll/freescroll.rs` and `samples/tiled-free-scroll/free-scroll.rs`
  scroll **diagonally** (both axes at once) with no corner artifacts, proven behaviorally
  through the four-screen `nes_debug` MCP, not just compile level.
- Levels up to 512x480 work with no runtime streaming (NF-3). Larger
  source-authored worlds stream columns/rows with the staggered one-edge-per-
  VBlank policy; NF-10 remains only for mapper-backed scale, banking, and HUD IRQs.
- Horizontal-only NES programs stay on the horizontal camera path. The shared
  runner currently uses that path over `stage1.playable`; the focused vertical
  and diagonal samples continue to own four-screen acceptance coverage.
- The validator accepts NES free scroll only behind the working four-screen
  implementation and still rejects over-budget or non-four-screen requests with a
  clear diagnostic.
- Docs (`NesTarget.md`, `ArchitectureRoadmap.md`, this file) match shipped
  behavior in the same patch.

## Risk register

| Risk | Mitigation |
| --- | --- |
| Free scroll attempted on 2 nametables → corner artifacts. | Require four-screen VRAM; reject otherwise. |
| ADNES cannot emulate four-screen → cannot validate. | NF-0 extends ADNES first; optional Mesen2 cross-check. |
| 240 coarse-Y wrap corrupts attribute fetch. | Map world Y → (nametable Y bit, 0..239); never write 240..255 to `$2005` Y. |
| VBlank budget overrun (column + row + attributes + OAM DMA). | Camera movement queues streams and `Camera.Apply()` drains at most one edge or 8-tile row phase per VBlank; row attributes are a separate phase, and runtime rows only reset `$2006` at nametable boundaries. Larger mapper-backed worlds remain NF-10. |
| Real-hardware four-screen needs cart SRAM. | Emulator-first demo; document the hardware caveat; mapper path in NF-10. |
| Tracked NES ROMs change unexpectedly. | Gate four-screen behind explicit free-scroll request; keep horizontal path byte-identical. |

## Suggested order

1. **NF-0** (extend ADNES four-screen) — prerequisite; nothing is testable without it.
2. **NF-1 → NF-2 → NF-3** — emit four-screen, 2D camera, first artifact-free
   diagonal demo with no streaming. Highest value, lowest risk.
3. **NF-4 → NF-5 → NF-6** — streaming for large levels (rows, diagonal, attributes).
   This is the painful part; budget-driven.
4. **NF-7 → NF-8 → NF-9** — flip capabilities, world data, acceptance.
5. **NF-10** — mapper-backed large levels / HUD, only as a separate epic.
