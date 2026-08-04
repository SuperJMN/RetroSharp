# NES Code Banking v1

Status: implemented as a target-private NES final-link profile.

This document owns executable PRG banking for the NES target. The accepted
[`NesLargeWorldsCartridgeProfile.md`](NesLargeWorldsCartridgeProfile.md) still
owns the 64 KiB MMC3/TVROM board, `WorldPack`, R7, CHR, reset, DPCM, and
interrupt layout. This profile extends that layout only when flattened gameplay
code cannot remain fixed. It does not add a source, SDK, CLI, or manifest bank
selector, and it does not affect the Game Boy target.

## Final-link selection contract

Normal NES compilation attempts these layouts in order:

1. Preserve the exact mapper-0 link when it fits.
2. Retry the existing fixed-execution MMC3/TVROM profile,
   `nes-mmc3-tvrom-v1`, when mapper-0 reports a program-PRG constraint, or when
   the existing packed-world path reports its PRG/DPCM capacity constraint. A
   DPCM-only failure without a packed world keeps the historical mapper-0 error.
3. Retry as `nes-mmc3-tvrom-codebank-v1` only when the second attempt proves
   that removing the movable gameplay stream makes the fixed region fit.

A CHR, pinned-R7, DPCM, `WorldPack`, or fixed-resident-layout failure is not
permission to select code banking. A later combined R6-capacity failure also
keeps its owning diagnostic. A successful earlier attempt returns directly, so
code banking does not rewrite fitting mapper-0 or data-only MMC3 images.

## Physical ownership

Code banking retains the accepted 64 KiB PRG / 16 KiB CHR TVROM shape and MMC3
PRG mode 0:

| Physical 8 KiB banks | Runtime window | Owner |
| --- | --- | --- |
| `0, 3, 4, 5` | R6 at `$8000-$9FFF` | `WorldPack` first, then banked program |
| `1` | R7 at `$A000-$BFFF` | Pinned runtime/audio data |
| `2` | R7 during boot | Palette and four-screen nametable upload |
| `6, 7` | Fixed at `$C000-$FFFF` | Runtime, helpers, DPCM, handlers, reset, veneers, vectors |

`WorldPack` placement runs first in its canonical R6 order. Each physical R6
bank is then owned wholly by either the pack or the program; v1 never mixes
both in one bank. The linker gives the program the remaining banks in physical
order. A build with no pack may use all four R6 banks; every pack segment
reduces that program pool by one whole bank.

The movable program is the flattened `Main` stream, including inline-expanded
user, receiver, and value helpers, followed by its terminal loop. A repeated
multi-piece `DrawLogicalSprite` shape stores its runtime operands in
`NesRuntimeMemoryLayout` scratch and calls one fixed-resident target helper;
single-use and one-piece shapes remain inline when a call would not save code.
This is target-owned SDK lowering, not a user-function ABI. Startup, runtime
initialization, target subroutines, `WorldPack` and MMC3 helpers, generated ROM
tables, DPCM, NMI/IRQ/reset code, and vectors remain fixed.

## Linker module

`NesPrgLinker.Link(...)` is the deep module for the banked link. Its internal
interface accepts sectioned `PrgBuilder` emission plus the fixed and available
R6 layout, and returns fixed bytes, physical program segments, resolved symbols,
and capacity totals. Callers do not place branches or synthesize bank switches.

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
far destination costs one 12-byte fixed veneer. V1 does not grow the ROM beyond
the four R6 banks already present in the TVROM profile.

The internal `NesRomBuildReport` identifies the selected profile and exposes
`ProgramR6Bytes`, `FixedVeneerBytes`, `program:r6:*` segments, and bank-aware
symbols. R6 exhaustion reports the `WorldPack` banks and bytes, program banks
and linked bytes, and the physical pool `[0, 3, 4, 5]`. Fixed veneer exhaustion
and unsupported relocation shapes fail explicitly. These linker details do not
change the public `retrosharp.nes.runtime-abi` v1 sidecar or its schema.

## Stable evidence

[`validation/fixtures/nes-code-banking-v1`](../validation/fixtures/nes-code-banking-v1)
is the versioned canary. It combines a small Tiled `WorldPack`, pinned NES
music, and inline receiver code that performs 3,456 increments. The normal
selector must reserve the world bank first, place gameplay across at least two
remaining R6 banks without source-authored banking, keep R7 pinned during
runtime, and let a fixed `WorldPack` read restore the active code bank.

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
- Existing mapper-0 and MMC3 `WorldPack` suites own compatibility of the two
  earlier selection stages.

Run the focused NES tests before the full target suite. For a final candidate,
follow `AGENTS.md`: two matching deterministic executions where required, one
broad solution run, and `git diff --check`. An external AprNes run is useful
physical diagnostic evidence when available, but it is not a separate product
gate.
