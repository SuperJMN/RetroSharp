# NES Code Banking v1

Status: implemented as a target-private NES final-link profile.

This document owns executable PRG banking and MMC3 board selection for the NES
target. The accepted
[`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md) still
owns the MMC3/TVROM board shape, `WorldPack`, R7, CHR, reset, DPCM, and
interrupt layout. This profile extends that layout only when flattened gameplay
code cannot remain fixed, or when the R6 pool of the current board is exhausted.
It does not add a source, SDK, CLI, or manifest bank or board selector, and it
does not affect the Game Boy target.

## Final-link selection contract

Normal NES compilation attempts these layouts in order:

1. Preserve the exact mapper-0 link when it fits.
2. Retry the existing fixed-execution MMC3/TVROM profile,
   `nes-mmc3-tvrom-v1`, when mapper-0 reports a program-PRG constraint, or when
   the existing packed-world path reports its PRG/DPCM capacity constraint. A
   DPCM-only failure without a packed world keeps the historical mapper-0 error.
3. Retry as `nes-mmc3-tvrom-codebank-v1` only when the second attempt proves
   that removing the movable gameplay stream makes the fixed region fit.
4. Retry steps 2 and 3 on the next larger MMC3 board only when the R6 pool is
   what ran out.

A CHR, pinned-R7, DPCM, or fixed-resident-layout failure is not permission to
select code banking or a larger board. A later combined R6-capacity failure also
keeps its owning diagnostic. A successful earlier attempt returns directly, so
code banking does not rewrite fitting mapper-0 or data-only MMC3 images.

A `WorldPack` failure is split by cause, because only one of the two causes more
banks can fix. A pack the current board's R6 pool cannot hold reports that pool
and escalates under step 4. A pack past the banked reader's eight-segment
(64 KiB) addressing ceiling fails identically on every board, so it reports the
reader limit and is never retried on a larger one.

## PRG boards

The MMC3 layout is generated from a bank count rather than listed as eight fixed
sections. Supported boards are 64, 128, 256 and 512 KiB — 8, 16, 32 and 64
physical 8 KiB banks. 512 KiB is the hardware ceiling because MMC3 R6/R7
bank-select values are 6-bit; a board needing a bank number above 63 fails with
its own explicit diagnostic instead of truncating, and that ceiling is never a
promotion signal because no larger board exists on this mapper.

Board choice is a target-private final-link decision. There is no source, SDK,
CLI, or manifest bank or board selector, and step 4 above never skips a smaller
layout that fits. 64 KiB stays the first choice, so an image that already links
there is unaffected.

Bank roles are identical at every size, which is why growing the board is a
capacity change and not a relocation: bank 1 is pinned R7 data, bank 2 is the
boot-only R7 upload, the top two banks are the fixed `$C000-$FFFF` region, and
every remaining bank belongs to the R6 pool. So every bank a larger board adds
joins that pool. The pool is `0, 3, 4, 5` on 64 KiB, `0, 3-13` on 128 KiB,
`0, 3-29` on 256 KiB, and `0, 3-61` on 512 KiB: four, twelve, twenty-eight and
sixty banks. The iNES PRG-ROM size field follows the emitted image; mapper and
mirroring flags are the same on every board.

## Physical ownership

Code banking retains the 16 KiB CHR TVROM shape and MMC3 PRG mode 0. On the
default 64 KiB board the concrete map is:

| Physical 8 KiB banks | Runtime window | Owner |
| --- | --- | --- |
| `0, 3, 4, 5` | R6 at `$8000-$9FFF` | `WorldPack` first, then banked program |
| `1` | R7 at `$A000-$BFFF` | Pinned runtime/audio data |
| `2` | R7 during boot | Palette and four-screen nametable upload |
| `6, 7` | Fixed at `$C000-$FFFF` | Runtime, helpers, DPCM, handlers, reset, veneers, vectors |

`WorldPack` placement runs first in its canonical R6 order. Each physical R6
bank is then owned wholly by either the pack or the program; v1 never mixes
both in one bank. The linker gives the program the remaining banks in physical
order. A build with no pack may use the whole R6 pool; every pack segment
reduces that program pool by one whole bank.

The movable program is emitted as named placement units with a residence and an
inferred phase on each unit. `Main` is split in source order into
`program:main:init` for top-level setup before the first perpetual frame loop,
`program:main:frame` for the first top-level `while (true)` that reaches the
wait-frame intrinsic, and `program:main:tail` for source statements after that
loop plus the compiler-generated terminal idle loop. Programs with no such
frame loop emit a single `program:main:init` unit reported as `OneShot` rather
than manufacturing a hot phase. All units remain `ProgramR6` in the code-banked
profile and `Fixed` in the earlier flat profiles. The banked linker keeps
`ProgramR6` units distinct through placement and assigns them to R6 banks by
phase (see "Phase placement policy" below).
Named `Fixed` units are rejected by the sectioned builder/linker until a fixed
placement policy defines their real offsets. A repeated
multi-piece `DrawLogicalSprite` shape stores its runtime operands in
`NesRuntimeMemoryLayout` scratch and calls one fixed-resident target helper;
single-use and one-piece shapes remain inline when a call would not save code.
This is target-owned SDK lowering, not a user-function ABI. Startup, runtime
initialization, target subroutines, `WorldPack` and MMC3 helpers, generated ROM
tables, DPCM, NMI/IRQ/reset code, and vectors remain fixed.

### Interrupt residence and bank neutrality

An NMI can arrive at any instruction boundary, including inside
`mmc3_select_r6` between its `STA $8000` latch write and its `STA $8001` bank
write. Two properties keep that harmless, and both are load-bearing:

- **Fixed-resident.** The vector slots at `$FFFA-$FFFF` sit in the last bank,
  which PRG mode 0 always maps. Their targets resolve through the fixed
  `PrgBuilder`, whose base is `FixedRuntimeCpuBaseAddress` (`$C000`) and whose
  payload is capped below the trailer, so a handler address in the switchable
  window is unrepresentable. Emitting a handler inside a placement unit is a
  hard link error, not a silent relocation.
- **Bank-neutral.** The handler must leave R6, R7, the bank-select latch and
  the CPU registers exactly as it found them, and must not execute from or
  touch `$8000-$BFFF`. MMC3 bank registers are write-only, so a handler that
  switches banks cannot restore what it displaced; only the RAM shadows record
  the intended selection. The emitted NMI handler therefore does frame-counter
  and frame-flag RAM work only and makes no call at all.

The IRQ handler is currently unreachable: reset raises `I`, disables mapper
IRQs through `$E000`, and the emitters never emit `CLI` or write `$E001`. It
stays a fixed-resident `RTI` so the vector is never dangling.

Adding work to the NMI - a music tick, a streaming commit, an OAM DMA - is the
realistic way to break this, because the code such work would reach already
calls `mmc3_select_r6`. `NesInterruptBankNeutralityTests` measures both
properties from linked ROMs under held input and carries its own negative
control.

## Phase placement policy

`NesProgramBankPlanner.Plan(...)` is the deep module that turns the analyzer's
phase-classified units into an R6 bank assignment. It takes the units in
emission order with their indivisible atoms and returns one plan: a bank and
offset per atom, used bytes per bank, the required bank count, a per-phase
bank/byte summary, and the hot phase's bank. Bin packing, atom indivisibility,
the bank-edge reserve, and hot-phase wholeness live inside it; `NesPrgLinker`
consumes the plan and does not re-derive placement, and neither `PrgBuilder` nor
`NesRomBuilder` participates in placement decisions.

The policy is:

- units are placed in emission order, so the linear bank-to-bank fallthrough
  chain the linker writes stays valid and no unit is reordered;
- the `Hot` unit is indivisible at unit granularity: it is never split across a
  bank cut. If it does not fit in the remaining space of the current bank, that
  bank is closed early (its bank-edge jump still lands immediately after its
  last atom) and the hot unit starts at offset 0 of a fresh bank;
- `Cold` and `OneShot` units keep the ordinary atom-granular fill, so cold code
  still shares and straddles banks freely and the early close costs at most the
  tail of one cold bank;
- a `Hot` unit larger than a whole bank is a hard diagnostic, not an escalation:
  the fixed PRG region is a constant 16 KiB at every board size and larger
  boards only add R6 banks, so no board makes a bank wider. `NesRomBuilder`
  reports it as the `Mmc3HotPhaseSize` link constraint, which is deliberately
  outside the board-escalation set.

V1 duplicates no shared bytes into phase banks. Shared SDK helper bodies are
fixed-resident and are reached by ordinary bank-neutral `JSR`, so duplicating
them into an R6 bank could only add bytes; `DuplicatedSharedBytes` is therefore
structurally `0` in v1 rather than a tuning knob.

## Linker module

`NesPrgLinker.Link(...)` is the deep module for the banked link. Its internal
interface accepts fixed `PrgBuilder` emission, named placement units with
per-unit residence, and the fixed and available R6 layout. It returns fixed
bytes, physical program segments, resolved symbols, unit descriptions, and
capacity totals. Callers do not place branches or synthesize bank switches.

The sectioned builder records indivisible emitted atoms, labels, typed
relocations, and `Fixed` versus `ProgramR6` residence. The linker owns these
rules:

- an atom is never split across banks;
- a local relative branch stays short when its final displacement fits;
- a long branch expands monotonically to the inverse condition plus an
  absolute jump;
- each non-final program segment ends in an absolute fallthrough jump;
- cross-bank jumps, branches, and fallthroughs target deduplicated 12-byte
  veneers in fixed PRG;
- a veneer saves status and A, selects R6 through the fixed helper, restores A
  and status, and jumps to the destination; X and Y are untouched;
- the selector updates the R6 software shadow together with the mapper;
- fixed `WorldPack` helpers restore the caller's R6 bank and shadow before
  returning to banked gameplay.

The code-banked raw `WorldPack` fast path also reuses target-private staging
storage as direct-mapped visual, collision-cell, and collision-metatile caches.
Repeated raw lookups therefore avoid an R6 round trip without changing the
pack, SDK, or runtime ABI. Packed-camera preparation keeps the selected world
data bank across one bounded preparation call (an eight- or sixteen-cell
column slice, or one row) and restores the entry program bank before every
return to gameplay. Fixed MMC3,
RLE-backed planes, interrupt handlers, and the VBlank commit path retain their
previous banking behavior; VBlank still performs no bank selection.

V1 rejects cross-bank `JSR`, address-only references to program labels, and
program-label relocation addends. User helpers remain inline, while calls from
banked gameplay to fixed target subroutines remain ordinary `JSR`/`RTS` calls.
NMI and IRQ code stay fixed and bank-neutral.

Shared SDK helpers use the existing fixed residence and `AbsoluteCall`
relocation. Their shape key retains compile-time asset, palette, transform,
frame, and flip specialisation; per-call coordinates and runtime frame/flip
values use the named scratch aliases. No new relocation, veneer, stack, or
public ABI is introduced.

## Capacity and diagnostics

Each program bank is 8 KiB. Every non-final bank reserves a three-byte bank-edge
jump, so an indivisible atom placed before the final position may be at most
8,189 bytes. The final bank needs no fallthrough and can use all 8,192 bytes.
Branch expansion and fixed veneers count against their final regions; a distinct
far destination costs one 12-byte fixed veneer. The link never grows the ROM
beyond the R6 banks of the selected board, and it only selects a larger board
after the current one proves its pool is exhausted.

The internal `NesRomBuildReport` identifies the selected profile and exposes
placement-unit names, residences, inferred phases, and linked sizes (including
branch expansion and any bank-edge fallthrough owned by the unit) together with
`PrgRomSize`, `ProgramR6Bytes`, `FixedVeneerBytes`, `FixedHeadroomBytes`,
`program:r6:*` segments, and bank-aware symbols. A banked build also reports
`BankPlacement`: the physical bank(s) and byte count of every phase, the hot
phase's name, size and physical bank, the linked occupancy of every R6 program
bank, remaining R6 headroom, and duplicated shared bytes. R6 exhaustion reports
the `WorldPack` banks and bytes,
program banks and linked bytes, and the selected board's physical pool — for
example `[0, 3, 4, 5]` on 64 KiB. A hot phase larger than one 8 KiB bank fails
as `Mmc3HotPhaseSize` without escalating the board. A `WorldPack` that outgrows
that pool reports its own capacity diagnostic; the banked reader indexes
segments from bits 13-15 of a 16-bit offset, so one physical pack stays within
eight R6 segments even on a larger board. Fixed veneer exhaustion and
unsupported relocation shapes fail explicitly. These linker details do not
change the public `retrosharp.nes.runtime-abi` v1 sidecar or its schema.

`NesCapacityReportProjection` turns a finished build into the author-facing
`retrosharp.nes-capacity/v1` report, which the CLI writes to stdout for
`--capacity-report` (see `README.md`). It names the selected profile, PRG/CHR
size, both headroom figures against the region that owns them, the phase-to-bank
map with per-bank occupancy, the bytes attributed to fixed veneers, pinned R7,
boot R7 and resident CHR, the top user-function duplication holders, and
per-frame CPU work per named window. Shared SDK subroutines and outlined user
functions are counted with their call sites rather than sized, because no build
report measures their bytes. Region capacity is reported as used
plus headroom exactly as the build measured it, never as a re-derived board
constant. The report is diagnostic: when fixed or R6 headroom falls below five
per cent of its region it emits a `near-cliff` warning and the build still
succeeds, because spilling into R6 and escalating the board are the designed
behaviours. The only build-time failures in this area stay
`NesProgramBankCapacityException` and `NesFramePlan.RequireVideoSafeBudget`.

## Stable evidence

[`samples/phase-banked-frame`](../samples/phase-banked-frame) is the tracked
canary pair for phase placement. The candidate and the control share one
`src/scene.rs` frame loop and differ only in one-shot cold init, so the control
is an honest steady-state baseline for the same gameplay work. The candidate's
cold init is bulky enough that a raw emission-order fill would cut the hot
frame phase across a bank boundary. `NesPhaseBankPlacementCanaryTests` owns the
evidence: automatic selection of `nes-mmc3-tvrom-codebank-v1`, the hot phase
whole in a single R6 bank distinct from the cold init bank, the discriminating
straddle margin, positive R6 and fixed headroom, zero duplicated shared bytes,
steady-state execution that never leaves the hot bank, one logical tick per
physical frame, zero unsafe PPU/OAM writes, and an upper-bound active-tick
budget derived from the control. It pins no ROM bytes and no exact cycle count.

[`samples/executable-banking`](../samples/executable-banking) is the tracked
build canary for automatic executable banking. Its compact nested inline source
expands beyond fixed PRG capacity, so the normal selector must choose
`nes-mmc3-tvrom-codebank-v1` and emit the tracked NES ROM through the real
sample-generation path.

[`validation/fixtures/nes-code-banking-v1`](../validation/fixtures/nes-code-banking-v1)
is the versioned canary. It combines a small Tiled `WorldPack`, pinned NES
music, and inline receiver code that performs 3,456 increments. The normal
selector must reserve the world bank first, place gameplay across at least two
remaining R6 banks without source-authored banking, keep R7 pinned during
runtime, and let a fixed `WorldPack` read restore the active code bank.

[`validation/fixtures/nes-prg-board-escalation-v1`](../validation/fixtures/nes-prg-board-escalation-v1)
is the versioned board-selection canary. It scales the same shape to 4,992
increments alongside a `WorldPack` bank and pinned music, so the 64 KiB R6 pool
cannot hold it. The selector must climb to the 128 KiB board, place gameplay
across the enlarged pool, boot, keep one logical tick per physical frame, and
make zero unsafe PPU/OAM writes.

[`validation/fixtures/nes-banked-frame-load-v1`](../validation/fixtures/nes-banked-frame-load-v1)
is the focused behavioral canary for representative banked frame load. It
combines packed camera movement, raw `WorldPack` visual and collision reads,
retained sprites, actor-style work, input, and audio. Its automated observer
owns build, liveness, progress, banking restoration, and safe PPU/OAM evidence;
the editable runner and user playback remain the authority for perceived
smoothness.

[`samples/platformer-landing`](../samples/platformer-landing) is the stable
shared-operation canary. Its repeated collision probes must compile to one
fixed helper body per shape with one call per site, use less fixed PRG than the
same unrolled control, remain at one logical tick per physical frame in steady
state with no worse peak active tick, and retain its existing cross-target
camera, background, OAM, input, reset, and video-write acceptance. No sample
source is edited to manufacture the repetition.

The shared *sprite* helper is declared unit-test-only. Sharing requires two
draw sites with the same compile-time shape, and no current sample has that
shape twice, so `NesSharedSdkOperationSubroutineTests` covers the mechanism
with synthetic programs only. That status is deliberate: the machinery is
generic and correct, and widening the shape key so a constant operand can share
a body with a runtime one is what activates it on real samples. It is recorded
here rather than left silently dead.

Focused evidence is split by owner:

- `NesPrgLinkerTests` owns atom placement, local and relaxed branches,
  cross-bank fallthrough/veneers, register preservation, unsupported calls,
  and deterministic capacity diagnostics.
- `NesRomCompilerTests.RomBanking.cs` owns automatic selection, whole-bank
  `WorldPack`/program ownership, loops with `break`/`continue`, NMI
  interruption, audio/R7 coexistence, mapper-shadow restoration, determinism,
  and safe observed PPU/OAM writes.
- `NesRuntimeAbiProjectionTests` owns the unchanged public ABI v1 projection.
- `NesInterruptBankNeutralityTests` owns interrupt residence and bank
  neutrality: every linked ROM with a switchable PRG window is observed under
  held input, every NMI it takes must stay inside `$C000-$FFFF` and leave R6,
  R7, the bank-select latch and A/X/Y unchanged, the banked canary must
  actually be interrupted while running from a switched bank, and a patched
  bank-hostile handler must be reported.
- `NesMmc3PrgBoardTests` owns board generation, bank roles at every size, the
  6-bit bank-number ceiling, iNES header agreement, board escalation, and the
  escalation canary's boot, tick keep-up, and write safety.
- Existing mapper-0 and MMC3 `WorldPack` suites own compatibility of the two
  earlier selection stages.

Run the focused NES tests before the full target suite. For a final candidate,
follow `AGENTS.md`: two matching deterministic executions where required, one
broad solution run, and `git diff --check`. An external AprNes run is useful
physical diagnostic evidence when available, but it is not a separate product
gate.
