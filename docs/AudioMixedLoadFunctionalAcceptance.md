# Audio Mixed-Load Functional Acceptance

Status: accepted evidence for CSL-5 / #340.
Captured: 2026-07-17.

This rung keeps the existing Game Boy-only `music-switch` capability spike and
adds `samples/audio-mixed-load/` as a stable neutral Game Boy/NES
`target-acceptance` identity. The source uses only `RetroSharp.Portable2D` and
the production order `WaitVBlank -> Camera.Apply -> Sprite.Draw ->
Audio.Update -> Input.Poll -> simulation -> Camera.SetPosition`. Target asset
variants and target lowering own the DMG APU, 2A03/DPCM, banking, mapper, OAM,
and packed-camera details.

## Exact artifacts

| Artifact | SHA-256 |
| --- | --- |
| `audio-mixed-load.gb` | `adcef49366405e81912c1de028809662de497e97c2fd630ab11ecf5595dbe1ef` |
| `audio-mixed-load.nes` | `2c70f5e910916f2541ece692528064c653801ec90c31c1f24d33cae5a1725d17` |
| NES runtime ABI sidecar | `99e9d062b99b3bfd7a5299af8ed6a22ee1988567e94dbdc91fcf734bcdc77668` |
| exact tracked `runner.nes` | `3e61d5566bfdd9acd19c9c16007c265c8ccd374186b92dfb960361d978dd0d49` |
| runner NES runtime ABI sidecar | `39d8603c73304963ba7636fb631c7cf6b5105f9c85ba2ab398c9539f342dc5e6` |

The canary carries BGM, two independently completed SFX starts, two airborne
arcs, periodic packed collision probes, two moving metasprites, a packed
background, and a bounded camera burst. The Game Boy scenario uses 31 RIGHT
frames and retains a seven-pixel camera path without a packed edge. The NES
scenario uses 32 RIGHT frames and additionally exercises one packed edge. Both
observe 360 physical frames after 100 warm-up frames.

## Acceptance budgets

| Measurement | Game Boy | NES |
| --- | ---: | ---: |
| Gameplay ticks | 360/360 | 360/360 |
| Longest missed gameplay streak | 0 | 0 |
| Audio-service gap / drift | 0 / 0 | 0 / 0 |
| Ordered APU events | 616 | 407 |
| Maximum non-authored event gap | 4 | 15 |
| SFX starts / completions / restarts | 2 / 2 / 0 | 2 / 2 / 0 |
| DPCM starts / completions / restarts | 0 / 0 / 0 | 14 / 14 / 0 |
| Camera request / resident / committed / visible | 7 / 7 / 7 / 7 | 8 / 8 / 8 / 8 |

The ordered event digests are
`b2dfc95e7a8248ca1bf747c0b744b0845c561de7aa80bfa3f6ea4387e58b9248`
for Game Boy and
`b1f78ea09c83f7d1879bce64609d1aedfc06a7f66b0802ecee033cfe995c96d7`
for NES. Digest integers are serialized explicitly as little-endian, so the
contract is host-architecture independent.

Every observation also retains the authored background, both complete
metasprites, unused OAM, bank/mapper restoration, and cycle-positioned video
and OAM writes. The accepted runs contain no background, sprite, unsafe-write,
bank, reset, stuck-active, restart, or truncated-DPCM failure. NES DPCM
lifecycle is reconstructed from `$4010/$4013/$4015` writes and NTSC timing; the
test CPU does not synthesize audible PCM or model DMC DMA cycle stealing.

`authoredSilence` now describes frames where ordered APU events are not
expected. It never excuses the `Audio.Update` heartbeat. Shared contract tests
reject a frozen register stream, restart storm, stuck SFX, reordered registers,
truncated DPCM, and a missing heartbeat even inside authored silence. The Game
Boy production adapter also injects one frozen physical service frame and
proves the exact canary rejects it.

## Exact runner correction

The old tracked runner SHA
`7464aea0f068869d6786ef4591445c75d85800a394f830b9dc607af62c9ec5c7`
lost one gameplay and audio tick at input-start delays `0, 2, 10, 12, 16, 20`
through AprNes after a 500-frame settle. Delays `4, 6, 8, 14, 18` remained
green. Per-frame observation, rather than a helper that waits for the next
logical tick, exposed the loss at observation frame 48 or 95.

Packed `Video.WaitVBlank()` had discarded a saturated `FramePending` signal
that arrived while mainline work was still executing, forcing an extra whole
physical frame. It now consumes that signal and returns to gameplay/audio
without publishing. It marks the following explicit `Camera.Apply()` as
already handled and defers `Camera.SetPosition()`'s packed visible-camera
publication, then reserves camera/OAM publication for a newly awaited NMI and
a fresh `$2002` VBlank check. Logical camera preparation can continue, but an
edge never becomes visible before its packed column or row commit.

MMC3 links reserve hardware-aligned DPCM blocks at the start of the fixed
`$C000-$FF7F` region and place the symbol-addressed runtime behind them. This
keeps both production blocks and the fixed runtime below the vector trailer
without weakening DMC alignment or size checks.

The canary also exposed the historical MMC3 direct `$2003/$2004` logical-sprite
path writing after VBlank. Mapper-4 page-$02 `$4014` DMA is not a viable AprNes
fallback because that emulator does not complete the instruction. MMC3 packed
draws now update the retained `$0200` shadow and publish its statically used
bytes sequentially through `$2004` only at the fresh VBlank boundary. The
current budget permits at most 38 hardware sprites/152 bytes; the accepted
publisher costs 2,135 NTSC CPU cycles, including the indexed-load page-crossing
penalties. A compile-time regression rejects a 39-sprite MMC3 packed program.

The final exact tracked runner passes all start delays
`[0,2,4,6,8,10,12,14,16,18,20]` in both the deterministic `NesTestCpu` gate
and NesMcp/AprNes: after `500 + delay` physical frames, each of the following
120 physical frames advances `$03FA` gameplay and `$03FB` audio by exactly one
while A is held for the first 40 frames. The AprNes start/end pairs for both
counters were `CC->44`, `CE->46`, `D0->48`, `D2->4A`, `D4->4C`, `D6->4E`,
`D8->50`, `DA->52`, `DC->54`, `DE->56`, and `E0->58`, with no skipped,
repeated, or divergent counter value. AprNes also executes the final canary
for 500/500 frames with both metasprites visible and without the prior DMA
hang.

## Validation

```bash
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
dotnet test src/RetroSharp.Core.Tests/RetroSharp.Core.Tests.csproj -m:1 --filter FullyQualifiedName~SampleApiQuarantineTests
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter FullyQualifiedName~PackedTiledFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter "FullyQualifiedName~PackedTiledFunctionalAcceptanceTests|FullyQualifiedName~AudioMixedLoadFunctionalAcceptanceTests"
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
dotnet test RetroSharp.sln -m:1
git diff --check
```

The installed GameboyMcp and NesMcp surfaces provide production-emulator RAM,
screen, tilemap/nametable, OAM, and targeted last-writer evidence. They do not
provide an exhaustive continuous APU event stream, so deterministic .NET
ordered-event oracles remain authoritative for the complete APU/DPCM sequence;
MCP supplies independent runtime and visual corroboration.

On the exact tracked artifacts, the eleven-phase runner sweep above was
replayed through NesMcp's atomic `run_input_timeline` against AprNes, retaining
one memory observation per physical frame. SameBoy reached `SCX=$5E` after the
220-frame mixed input path, retained both six-piece metasprites, and returned a
non-empty DMG framebuffer histogram (`22769/80/0/191` shades); WRAM
`$C00E/$C00F` advanced together. AprNes reached gameplay/audio `$C4/$C4` at the same point,
then `$CE/$CE` ten physical frames later, with distinct hashes for all four
nametables, a seven-color non-empty framebuffer, and both retained metasprites
in hardware OAM. Its bounded `$4014` trace exhausted 200,000 instructions
without a DMA write, while the `$2003/$2004` trace showed sequential OAM
publication only while rendering was inactive.
