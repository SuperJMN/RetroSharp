# Actor and Projectile Functional Acceptance

Status: implemented and accepted by CSL-6 / #341.

This is the sustained sprite-integrity rung for the portable actor,
projectile, and effect frameworks. It executes the exact tracked Game Boy and
NES cartridges rather than a rewritten probe and evaluates every retained
frame through the shared functional-acceptance contract.

## Production matrix

| Sample | GB scenario / ROM SHA-256 | NES scenario / ROM SHA-256 | Window |
| --- | --- | --- | --- |
| `actor-framework` | `actor-framework.gb.json` / `b539deec2c01c3af4b35c484bb168abdbde9a557c9801a194c534cbdd32b9536` | `actor-framework.nes.json` / `b48ee3e53243f7c75a8292ed9bef97547489368ea5cf5d1e9e874ff1680c53e3` | 20 warm-up + 321 observed frames |
| `shots-simple` | `shots-simple.gb.json` / `4089433342749ff66dfcd7c5f1c723d48612edbfc5c2a5869a6e7317d76715ac` | `shots-simple.nes.json` / `f777a9e235a8abee7aa32dab30090b595c5af6b018452c26e3f7d53037e6d423` | 20 warm-up + 240 observed frames |
| `shots-bouncy` | `shots-bouncy.gb.json` / `590afa67a6070d9925e50211d9a1001830a32cff59ae4f97e45adf6c20e6469a` | `shots-bouncy.nes.json` / `641395a4c7f980bf72ee934bd7bbfd7e54769d028d222595757c3ab330f68e43` | 20 warm-up + 240 observed frames |
| `runner-projectile` | `runner-projectile.gb.json` / `f64fc12dc9234bae02a538d2d047e6696a4d53aee47bc0ce543244104e31d93f` | `runner-projectile.nes.json` / `f0ae159e7702ef0769cd469e885333968159cd2ea6a0b3b5cceb23f9bdf37e69` | 20 warm-up + 100 observed frames |

The actor input crosses the complete 160-pixel horizontal span and reverses to
camera X=0. The runner-projectile input accepts right and left shots, attempts
two requests while both fixed slots are occupied, then fires again after a slot
expires. The autonomous simple and bouncy samples run long enough to saturate,
drop, recycle, expire, and—where authored—bounce their two-slot pools.

## Per-frame invariants

The independent oracle derives authored backgrounds from the imported target
video program and sprite intent from the generated fixed-pool variables plus
compiled sprite assets. The emulator adapter contributes only observed
hardware state. Every retained frame checks:

- authored tile and palette identity over the complete visible background;
- logical visibility, fixed OAM slot, tile, attributes, coordinates, and every
  metasprite byte;
- canonical unused OAM after the last allocated call site;
- no visible-time VRAM/PPU or OAM writes, with cycle/scanline/dot evidence;
- camera request/resident/commit/visible state where the sample scrolls;
- pool capacity, saturation, dropped requests, reuse, bounce, and effect expiry;
- one logical tick per physical frame within the reviewed cadence budget.

`maximumSpawnToVisibleFrames` is 1 for all eight scenarios. The shared runner
requires monotonically ordered activation and visibility sequence IDs, rejects
gaps and unresolved final activations, and reports the observed maximum. All
scenarios require at least a 0.95 gameplay-tick ratio and at most one
consecutive missed tick. Actor and input-driven runner scenarios additionally
require input-to-state response within one frame.

## Retained OAM ownership

Game Boy logical draws write the 160-byte `$C600` shadow. At the accepted
VBlank boundary a ten-byte routine at `$FF80-$FF89` starts one `$FF46` DMA and
waits in HRAM for the full 640-cycle transfer; `$FF8A-$FFFF` remains the stack
range. Physical OAM must equal the transfer's source snapshot. Raw
`Sprite.Set(...)` compatibility calls remain direct physical-OAM writes.

NES logical draws write page `$0200`. `Video.WaitVBlank()` publishes one
`$4014` DMA during VBlank and then resets only the statically allocated logical
call-site bytes to `$FF`. Consequently an unexecuted actor-definition,
projectile, or effect call site cannot retain the prior frame. Raw/direct NES
OAM programs keep the existing `$2003/$2004` behavior and startup profile.

Actor draw generation uses stable definition call sites for every pool size and
projects inactive or off-window actors to hidden coordinates. This is portable
frontend policy; retained-page placement and DMA timing remain target lowering
details.

## Regression proof

The contract suite includes a controlled adapter that leaves an old sprite in
OAM after its logical draw path disappears. The pre-integrity behavior accepted
that trace; the strict slot/byte oracle now reports `sprite-oam` on the first
stale frame. Additional probes reject missing `maximumSpawnToVisibleFrames`,
out-of-order visible sequences, activation gaps, and activations still pending
after the declared drain.

The production regression reproduced two target bugs before the GREEN path:

- Game Boy logical sprites wrote physical OAM directly during visible time;
  retained `$C600` plus the HRAM DMA boundary removes those unsafe writes.
- NES emitted one OAM DMA per logical draw call, producing tens of thousands of
  visible-time OAM writes over the full scenarios; retained `$0200` now emits
  one DMA per accepted frame boundary and clears future call-site state.

## External emulator checkpoints

External sessions are a parity check over the same tracked bytes; the
deterministic .NET oracle remains the exhaustive per-frame report producer.
Both sessions used `load_rom` followed by one atomic `run_input_timeline`.

| Target | ROM | Checkpoint evidence |
| --- | --- | --- |
| GameboyMcp | `actors.gb`, `runner-projectile.gb` | Actors: 20 idle, 160 Right, 161 Left returned `SCX=0`; slots 0-1 remained definition-ordered and both ended hidden at Y=160. Runner: after B/A, slots 6-7 carried tile `$42`; the slot-8 `$44` effect expired to hidden Y=160; frame 83 had all three hidden and frame 103 reused slot 6. LCD, sprites, and background stayed enabled. |
| NesMcp | `actors.nes`, `runner-projectile.nes` | Actors: 20 idle, 160 Right, 161 Left kept tile `$06` in stable definition slots, with inactive slots canonical `$FF` or hidden at Y=239; rendering stayed enabled. Runner: slots 19-20 carried tile `$65`, slot 21 carried effect tile `$66`, expired entries returned to Y=239, and slot 19 was reused after saturation. Rendering, sprites, and background stayed enabled. |

## Validation

Focused deterministic acceptance:

```bash
dotnet test src/RetroSharp.FunctionalAcceptance.Tests/RetroSharp.FunctionalAcceptance.Tests.csproj -m:1
dotnet test src/RetroSharp.GameBoy.Tests/RetroSharp.GameBoy.Tests.csproj -m:1 --filter FullyQualifiedName~ActorProjectileFunctionalAcceptanceTests
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 --filter FullyQualifiedName~ActorProjectileFunctionalAcceptanceTests
```

Repository acceptance:

```bash
tools/gameboy/generate_sample_roms.py --dry-run
tools/gameboy/generate_sample_roms.py
dotnet test RetroSharp.sln -m:1
git diff --check
```
