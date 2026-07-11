# ADR: NES Large Worlds v1 cartridge profile

Status: **accepted for LW-0.3 on 2026-07-10; forced linker/runtime foundation
implemented by LW-3.1 and production placement/selection implemented by
LW-3.2 on 2026-07-12.**

This ADR selects the cartridge architecture for the NES Large Worlds
linker/runtime. LW-3.1 implements its target-private forced linker and fixed
runtime foundation; LW-3.2 implements mapper-0-first selection plus physical
`WorldPack`, pinned-R7, boot-R7, DPCM/vector, and CHR placement. Production
pack reading remains `LW-3.3`. This ADR does not migrate the runner or
implement a HUD.

## Decision

NES Large Worlds v1 selects an **MMC3-family mapper 4, TVROM-style four-screen
profile** with:

- an iNES 1.0 header;
- 64 KiB PRG ROM, arranged as eight 8 KiB banks;
- 16 KiB CHR ROM, with one static 8 KiB resident set for v1;
- cartridge four-screen nametable RAM selected by header flag 6 bit 3;
- MMC3 PRG mode 0 kept constant;
- one switchable `WorldPack` window at `$8000-$9FFF` through R6;
- one independently selected, normally pinned data/audio window at
  `$A000-$BFFF` through R7;
- fixed executable code, DPCM, interrupt handlers, and vectors in
  `$C000-$FFFF`;
- mapper IRQs disabled. A later IRQ HUD is a separate decision and acceptance
  slice.

The selected 64 KiB PRG / 16 KiB CHR shape matches the documented Nintendo
TVROM board envelope: 64 KiB PRG, CHR ROM, and 4 KiB cartridge VRAM for four
screens. See the [NESdev TxROM board table](https://www.nesdev.org/wiki/TxROM).
It is the first banked v1 profile, not a claim that every packed program must
use every bank or that the currently reconstructed payload already overflows
mapper 0.

## Why this is the selected profile

All three candidates can provide a fixed 16 KiB upper PRG region, so a mapper
name alone does not decide the result. MMC3/TVROM is selected because it is the
only compared canonical profile that satisfies all of these v1 properties at
once:

1. `WorldPack` reads can change the `$8000` bank without evicting BGM data from
   `$A000`.
2. DPCM and every instruction that can run during a banked read remain fixed
   in `$C000-$FFFF`.
3. The current runner calls `Camera.SetPosition(x, y)` with variable `y`, so
   the real baseline selects the 4,096-byte four-screen startup path. TVROM
   supplies a canonical mapper-4 + CHR-ROM + four-screen board contract; it is
   not an invented mapper/header combination.
4. The measured 3,056-byte CHR payload remains ordinary CHR ROM and needs no
   boot upload or runtime art banking.
5. Mapper 4 already has instruction-step and bank-switch coverage through the
   selected NesMcp `AprNes` backend.

MMC3's scanline IRQ is not part of this decision. The profile would still be
selected if its IRQ counter did not exist. The decisive hardware distinction
is TVROM's canonical CHR-ROM plus four-screen shape; independent PRG windows
then make world/audio ownership simpler. A 64 KiB UxROM counter-layout is
byte-viable and is rejected below for board-contract reasons, not hidden
capacity arithmetic.

## Reproducible full `stage1` measurement

The inputs come from the merged
[`LargeWorldsStage1Baseline.md`](LargeWorldsStage1Baseline.md), the accepted
[`WorldPack` v1 contract](WorldPackFormatV1.md), and current `NesRomBuilder`
emission order. The focused analysis test reproduces each subtraction rather
than copying a nominal mapper capacity.

### Current fixed runtime/code proxy

The full no-audio baseline emits 41,907 bytes. Its current target data contains:

| Current emitted data | Bytes |
| --- | ---: |
| Raw visual and collision planes | 24,960 |
| Initial four-screen nametable image | 4,096 |
| Palette image | 32 |
| 40 collision-row pointers, low and high tables | 80 |
| 10 attribute rows x 78 column blocks | 780 |
| **Total subtracted target data** | **29,948** |

The remainder is **11,959 bytes** of current runner program plus NES runtime
code. This is a conservative proxy for the future fixed executable region: it
still includes the current raw-world access routines that the `WorldPack`
reader will replace.

The full-audio failure records music data ending at `$134FC`. Subtracting the
`$8000` builder base and the 4,126-byte music stream gives 42,206 bytes before
music data, which is 299 bytes more than the no-audio emission. The measured
fixed code proxy with the audio runtime is therefore:

```text
11,959 current runner/runtime code
+  299 audio runtime
= 12,258 fixed executable bytes
```

This is a layout measurement, not permission for Wave 3 to ignore a fixed-bank
overflow. The production linker must report the real fixed-code total after
the banked reader and wider-coordinate work land.

### DPCM and fixed-region cost

The two current DPCM blocks are 1,153 and 129 bytes. DMC start addresses are
64-byte aligned. After 12,258 fixed-code bytes, the next free address is
`$EFE2`, so the first sample needs 30 bytes of initial padding and occupies
`$F000-$F480`. The second needs another 63 bytes and occupies `$F4C0-$F540`.
The measured fixed DPCM reservation is therefore 1,375 bytes, not only the
1,282 payload bytes.

| Fixed `$C000-$FFFF` owner | Bytes |
| --- | ---: |
| Runner/runtime/audio code proxy | 12,258 |
| Aligned DPCM reservation | 1,375 |
| NMI, reset, and IRQ vectors | 6 |
| **Measured fixed-region total** | **13,639** |
| **Free in 16 KiB** | **2,745** |

DPCM bytes must stay between `$C000` and `$FFF9`, outside the six vector bytes.
They are never placed in R6 or R7 because the APU fetches them independently of
CPU bank-switch code. Every DMC address-register relocation is calculated from
the final fixed address, using the current 64-byte address granularity.

### Switchable and CHR payload

The accepted NES `WorldPack` is 7,920 bytes. The current 9,140-byte non-world
data set is split between an R7 bank pinned after startup and a boot-only R7
bank:

| Data owner | Bytes |
| --- | ---: |
| Pinned: collision-row pointer proxy | 80 |
| Pinned: streamed-column attributes | 780 |
| Pinned: BGM stream | 4,126 |
| Pinned: jump SFX | 26 |
| **Pinned R7 total** | **5,012** |
| Boot-only: four-screen nametable | 4,096 |
| Boot-only: palette | 32 |
| **Boot-only R7 total** | **4,128** |
| **All non-world data** | **9,140** |

The pointer proxy is deliberately retained in the estimate even though the
future pack directory may replace it. That keeps this Wave 0 sketch from
claiming unmeasured savings.

| Window/resource | Used | Capacity | Free |
| --- | ---: | ---: | ---: |
| R6 world bank at `$8000-$9FFF` | 7,920 | 8,192 | 272 |
| R7 pinned bank at `$A000-$BFFF` | 5,012 | 8,192 | 3,180 |
| R7 boot-only bank at `$A000-$BFFF` | 4,128 | 8,192 | 4,064 |
| Fixed `$C000-$FFFF` across banks 6 and 7 | 13,639 | 16,384 | 2,745 |
| CHR ROM | 3,056 | 16,384 | 13,328 |

The measured PRG placement is **30,699 bytes** (about 30.0 KiB), including
aligned DPCM and the vectors. It does not include the unimplemented mapper
helper/reader or widened-coordinate changes. A hypothetical packed mapper-0
image would have 2,069 bytes (about 2.0 KiB) left in 32 KiB. That is a viable
lower bound, not proof of final NROM fit or overflow. The final selector must
measure the production reader and coordinate lowering rather than force either
conclusion from this ADR.

The real runner baseline uses a 4,096-byte startup nametable image because its
variable Y camera selects four-screen mode. Physical bank 2 holds that image
and the palette. Reset selects it through R7 while rendering/NMI/audio are
disabled, uploads it, then pins R7 to physical bank 1 before normal runtime.

## iNES and bank shape

The v1 header is:

| Byte | Value | Meaning |
| ---: | ---: | --- |
| 0-3 | `4E 45 53 1A` | iNES magic |
| 4 | `04` | Four 16 KiB units = 64 KiB PRG |
| 5 | `02` | Two 8 KiB units = 16 KiB CHR ROM |
| 6 | `0x48` | Mapper low nibble 4 plus four-screen flag |
| 7 | `0x00` | Mapper high nibble 0; iNES 1.0 |
| 8-15 | `00` | No trainer/battery contract or NES 2.0 extension |

The CPU view after initialization is:

| Physical 8 KiB bank | CPU address | Owner |
| ---: | --- | --- |
| 0 initially | `$8000-$9FFF` through R6 | Current `WorldPack` bank; switchable later |
| 1 normally | `$A000-$BFFF` through R7 | 5,012-byte pinned BGM/SFX/runtime data |
| 2 during boot | `$A000-$BFFF` through R7 | 4,128-byte nametable/palette upload data |
| 3-5 | `$8000-$9FFF` through R6 | Ordered `WorldPack` continuation banks after physical bank 0 |
| 6 | `$C000-$DFFF` fixed in PRG mode 0 | First physical half of fixed execution/DPCM region |
| 7 | `$E000-$FFFF` fixed | Second physical half; vectors at `$FFFA-$FFFF` |

Reset enters through bank 7. Before enabling rendering, audio, or NMI, reset
sets MMC3 PRG mode 0, maps R6 to the initial world bank, maps R7 to boot bank 2,
maps the eight CHR-ROM 1 KiB pages linearly, disables/acknowledges mapper IRQs
through `$E000`, and initializes both software bank shadows. Startup uploads
palette/nametables and then pins R7 to bank 1 before normal audio/gameplay.

### World-data continuation policy

The 8 KiB R6 CPU window is not a `WorldPack` length limit. Within the fixed
64 KiB TVROM v1 capacity, the final linker places one canonical pack across
an ordered list of R6-owned 8 KiB continuation segments. R7-pinned or boot data
may make the physical bank ids non-contiguous, so placement records the exact
logical-segment-to-physical-bank mapping rather than assuming
`physicalBank = baseBank + segmentIndex`.

Every serialized offset remains the original 32-bit pack-relative byte offset.
The target translates `segmentIndex = relativeOffset / 8192` and
`windowOffset = relativeOffset % 8192` through the linker-owned segment list;
it does not add padding or rewrite the canonical v1 envelope. A header section,
directory, encoded plane, or chunk payload may cross a segment boundary. The
raw 7,920-byte fallback used by the current `stage1` analysis may remain in one
segment, but it does not establish a general one-window restriction.

The four-screen flag is a board requirement, not merely an emulator preference.
TVROM supplies 4 KiB of cartridge nametable VRAM; the console's CIRAM is not the
four-screen store. V1 never writes the MMC3 `$A000` mirroring register. The
[MMC3 register contract](https://www.nesdev.org/wiki/MMC3) documents that this
write has no effect on a hardwired four-screen board. Two-screen H/V mirroring
and TxSROM selection among two CIRAM pages are not substitutes for four
distinct nametables.

## Fixed execution and restoration invariant

No callable code, return address target, jump table, NMI handler, IRQ handler,
reset path, vector, DPCM byte, or bank-switch helper may live in R6 or R7. A
banked window contains data only.

Large Worlds v1 therefore implements **banked world/data placement with a fixed
executable runtime**. It does not automatically partition or bank executable
program code. Overflow of the fixed 16 KiB execution/DPCM/vector region remains
a profile failure and requires a separate future code-banking architecture
decision rather than silently placing callable code in R6 or R7.

The only runtime-changing window in v1 is R6. Its callable protocol is:

1. Enter a fixed-bank helper.
2. Push the current R6 bank shadow on the CPU stack.
3. Write bank-select value 6 with PRG mode 0 to `$8000`, write the requested
   bank to `$8001`, and update the R6 shadow.
4. Read/copy/decode only through fixed-bank code. No indirect call or return
   target may point into `$8000-$9FFF`.
5. Route every success, miss, and bounds-error result through one fixed-bank
   epilogue.
6. Pop the entry bank, restore it through the same select helper, update the
   shadow, and only then return.

The postcondition is exact: **on every return, the visible R6 bank and its
software shadow equal their values at helper entry**. LIFO storage on the CPU
stack makes bounded nested helper calls restore correctly. RetroSharp does not
support recursion; a future implementation still has to include the saved bank
byte in its normal stack-depth analysis.

NMI and IRQ vectors and handlers are fixed. The v1 NMI may use fixed code and
the pinned R7 audio data, but it must not read the R6 world window, write
`$8000/$8001`, or change either bank shadow. It is therefore safe for NMI to
interrupt between the R6 select and restore operations. Mapper IRQ remains
disabled and its fixed handler is bank-neutral. If a later HUD or CHR-residency
feature needs mapper writes from an interrupt, that issue must define
serialization and extend the restoration tests before enabling the writes.

R7 remains pinned while normal gameplay/audio runs. A boot-only R7 selection
is allowed only with rendering, NMI, and audio updates disabled, and it must
restore the pinned bank before any of them start.

## CHR policy

V1 emits a 16 KiB CHR-ROM image matching the TVROM profile. The measured
resident payload is 191 tiles / 3,056 bytes: 6 reserved tiles, 95 sprite tiles,
and 90 background tiles. Reset maps the first eight physical 1 KiB pages
linearly and leaves them unchanged; the second 8 KiB is reserved/filled for the
first profile, not dynamically selected by gameplay.

This avoids a CHR-RAM upload and keeps the current sprite/background allocator
contract. Region art beyond 8 KiB is a later content-residency decision; it may
use MMC3 CHR banking, but it must not be inferred from the existence of MMC3 or
added to the first banked-level reader.

## Candidate comparison

| Concern | MMC1 (mapper 1) | UxROM (mapper 2) | MMC3/TVROM (mapper 4) |
| --- | --- | --- | --- |
| Fixed execution/DPCM | 16 KiB upper fixed bank; measured fixed payload fits | 16 KiB upper fixed bank; measured fixed payload fits | Two fixed 8 KiB banks form `$C000-$FFFF`; measured fixed payload fits |
| Switchable PRG data | One 16 KiB `$8000` window | One 16 KiB `$8000` window | Independent 8 KiB R6 and R7 windows |
| Full `stage1` data fit | A startup bank plus a 16 KiB runtime bank is byte-viable | A startup bank plus a 12,964-byte pinned runtime bank is byte-viable | `WorldPack`, pinned runtime data, and boot data occupy separate 8 KiB banks |
| CHR policy | CHR ROM or RAM; 8/4 KiB banking available | Canonical Nintendo UxROM uses CHR RAM; the data can still fit a separate startup bank | Canonical TVROM supplies 16/32/64 KiB CHR ROM; v1 selects 16 KiB |
| Mirroring/four-screen | H/V/single-screen control over two CIRAM pages; canonical boards do not supply four-screen RAM | Fixed mirroring on canonical boards; no four-screen RAM | TVROM-style extra nametable RAM and iNES four-screen flag |
| Audio/window interaction | One 16 KiB window can be pinned for this level, but later world/audio growth shares it | Same; the current counter-layout fits without runtime switching | R7 audio stays readable while R6 world data changes |
| IRQ HUD | No scanline IRQ | No scanline IRQ | Hardware exists but is disabled and explicitly out of scope |
| Current debug backend | ADNES through NesMcp auto routing | ADNES through NesMcp auto routing | AprNes through NesMcp auto routing |
| Decision | Rejected | Rejected | **Selected** |

### Rejected: MMC1

MMC1 passes the measured byte fit, so it is not rejected for capacity. Its
[documented nametable modes](https://www.nesdev.org/wiki/INES_Mapper_001) are
horizontal, vertical, and either single screen over the console's two CIRAM
pages. An MMC1 cartridge with separate four-screen RAM is electrically
possible, and an emulator may accept the generic header flag, but it is a
custom board contract rather than a canonical SxROM profile. Its one
switchable PRG window also couples later world and audio growth.

### Rejected: UxROM

UxROM is the simplest PRG switch and is already supported by ADNES. The strong
counter-layout is accepted: on a 64 KiB mapper-2 image, a startup bank can hold
the 4,096-byte nametable and the 3,056-byte CHR-RAM upload, then a 16 KiB runtime
bank can remain pinned with `WorldPack` 7,920 + BGM 4,126 + SFX 26 + attributes
780 + palette 32 + pointer proxy 80 = **12,964 bytes**. Code and DPCM still fit
the fixed upper bank. Capacity and runtime switching therefore do not reject
UxROM for this payload.

It is rejected because the canonical
[UxROM board contract](https://www.nesdev.org/wiki/INES_Mapper_002) provides a
single 16 KiB PRG window with fixed H/V mirroring, and Nintendo UxROM boards use
CHR RAM rather than the selected CHR-ROM + four-screen combination. ADNES does
accept a synthetic mapper-2 header with CHR ROM and flag-6 four-screen RAM, but
that proves a permissive emulator combination, not a standard reproducible
cartridge board. Choosing it would require RetroSharp to define custom UxROM
wiring. TVROM already supplies the required hardware combination under mapper
4.

### Selected: MMC3/TVROM

MMC3 has more initialization state, but TVROM resolves the material
hardware-versus-simplicity tradeoff with a documented 64 KiB PRG, CHR-ROM, and
4 KiB four-screen VRAM board. The independent world/audio PRG windows are a
secondary operational advantage. Its IRQ counter is neither a selection reason
nor part of v1 behavior.

## Mapper-0 default and selection rule

This ADR does not change existing output. `NesRomBuilder` continues to emit the
current mapper 0 header and byte layout for existing programs.

The LW-3.2 final-link selector preserves these rules:

1. Attempt mapper 0 first whenever the final linked program needs no banked
   address and fits its emitted-code limit, DPCM window, 8 KiB CHR, addressing,
   and nametable diagnostics. This applies to a packed program too; existing
   small ROMs stay byte-identical.
2. Select the MMC3 profile when the final linked program requires a
   switchable `WorldPack` address or exceeds mapper 0 while satisfying every
   accepted MMC3 window. Full `stage1` is the acceptance payload used to prove
   this selected banked profile, but the selector does not force MMC3 if
   the completed production image genuinely needs no banking and fits mapper 0.
3. Do not silently promote a program to MMC3 to hide an unrelated coordinate,
   collision, CHR, palette, or fixed-bank overflow. Emit the responsible
   diagnostic.
4. An explicit analysis/test mode may force the MMC3 layout for header and bank
   boundary acceptance, but there is no public mapper argument in gameplay
   source. `World.Load(...)` remains the authoring boundary.

The 30,699-byte reconstruction leaves 2,069 bytes in a hypothetical 32 KiB
image, so the accepted payload is not currently proven to require a mapper.
That headroom excluded the pack reader and later runtime changes. LW-3.2 now
measures the real link: fit keeps the exact historical mapper-0 bytes; an
actual PRG/DPCM layout failure retries MMC3 and every MMC3 constraint is then
reported independently. Its normalized full-`stage1` placement probe measures
2,762 pack, 5,012 pinned, 4,128 boot, 2,151 fixed payload, and 3,056 resident
CHR bytes. This is a profile and capacity policy, not a false NROM-overflow
claim.

## Behavioral emulator evidence

The selected backend is NesMcp `auto` at commit
`c67ee435fc3b9e0126ab4e4ce908bab10d49ac5c`. Its
`AutoNesDebugSession.IsAdnesSupportedMapper` routes mappers 0, 1, 2, and 3 to
ADNES and routes mapper 4 to AprNes.

Existing focused NesMcp tests provide the behavioral gate:

- `AprNesDebugSessionTests.Load_rom_accepts_mapper4_and_steps_instruction`
  loads an iNES mapper-4 ROM, reads `$8000`, and executes an instruction;
- `AprNesDebugSessionTests.Auto_backend_uses_aprnes_for_mapper4` proves the
  normal auto backend takes that working path;
- `Save_state_and_load_state_restore_mapper_bank_registers` additionally
  switches the R6 bank and observes different bytes through `$8000` before
  restoring emulator state.

The same backend's `Mapper004` implements R6/R7 8 KiB PRG selection and the two
fixed last banks in PRG mode 0. Separately, its iNES loader maps header flag 6
bit 3 to four distinct nametables. The named mapper-4 tests use ordinary
mapper-4 headers: they prove mapper loading, instruction execution, auto
routing, and bank selection, **not the combined mapper-4 + four-screen visual
behavior**. The loader source is authoritative existing capability evidence
that the combination is representable. LW-3.1 added the focused
RetroSharp-generated `0x48` header/bank/four-nametable ROM and ran it through
NesMcp `auto`/AprNes: reset entered the always-fixed bank 7 at `$FF80`, fixed
PRG mode 0, and jumped to the runtime at `$C000`; R6 exposed `A0` then `A2`
after selecting physical bank 2, R7 exposed `A1` then `A3` after selecting bank
3, and the four nametable probes read `01`, `02`, `03`, and `04` at `$2000`,
`$2400`, `$2800`, and `$2C00` after startup completed.

ADNES remains the normal mapper-0/MMC1/UxROM backend. This decision does not
remove it or claim AprNes solves every emulator-accuracy concern.

## HUD, issue #247, and follow-up ownership

The broader [issue #247](https://github.com/SuperJMN/RetroSharp/issues/247)
tracks NES target gaps including mapper-backed scale, HUD, collision, and
palette work. This ADR resolves only the cartridge-profile decision and links
that issue; it does not close or solve its unrelated gaps.

An IRQ HUD is not required for mapper-backed levels. MMC3 mapper IRQs stay
disabled in the Large Worlds v1 reader. A HUD follow-up may reuse this profile,
select another board policy, or remain sprite-based, but it must provide its
own timing, PPU-state, interrupt, and emulator acceptance evidence. It may not
retroactively make IRQ behavior part of this ADR.

Production ownership remains split:

- Wave 1 owns `WorldPack`, wider coordinates, collision results, packing, and
  deterministic budget diagnostics.
- Wave 3 owns the mapper-4 header/linker, fixed-bank runtime, R6 reader,
  restoration tests, DPCM placement, and full `stage1` behavioral acceptance.
- LW-3.2 owns the static v1 boot/pinned relocation; later content-residency
  work owns CHR banking or larger replacement policies.
- The HUD backlog owns IRQ or sprite-HUD behavior independently.

## Non-goals

The implemented LW-3.1/LW-3.2 linker still does not add or change:

- production `WorldPack` reading or banked camera streaming;
- current mapper-0 sample ROM bytes or any public gameplay mapper argument;
- `WorldPack` bytes, compression, chunking, or staging buffers;
- 16-bit camera/collision lowering;
- target palette provenance or generic world collision;
- an IRQ HUD, mapper IRQ handler, split scroll, or CHR residency system;
- runner source, maps, assets, or tracked ROMs.

## Reproducible proof

The focused RetroSharp analysis test fixes the arithmetic, selected header,
candidate constraints, bank ownership, and nested/interrupt restoration model:

```bash
dotnet test src/RetroSharp.NES.Tests/RetroSharp.NES.Tests.csproj -m:1 \
  --filter "FullyQualifiedName~NesLargeWorldsCartridgeProfileAnalysisTests"
```

The public production target preserves mapper 0 whenever its exact historical
link fits. LW-3.2 selects MMC3 only from a real banked address/capacity need;
the forced internal profile remains available for focused linker acceptance.
