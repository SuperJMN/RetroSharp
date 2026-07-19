# NES Frame Scheduling Roadmap

Status: accepted execution roadmap for issue #410.
Last updated: 2026-07-19.

This roadmap turns the NES retained-OAM and packed-camera VBlank failure into
independently verifiable slices. GitHub issue #410 is the integrator, not an
implementation target. Every child owns one observable and normally one pull
request.

The language and portable SDK are unchanged. This work stays in the NES target
lowering/runtime and its validation adapters. It must not reduce the runner map,
metasprite, gameplay/audio throughput, or acceptance strength.

GitHub tracking:

- integrator: [NFS-0 / #410](https://github.com/SuperJMN/RetroSharp/issues/410);
- timing adapter: [NFS-1.1 / #413](https://github.com/SuperJMN/RetroSharp/issues/413);
- OAM publication module: [NFS-1.2 / #414](https://github.com/SuperJMN/RetroSharp/issues/414);
- physical gate: [NFS-1.3 / #415](https://github.com/SuperJMN/RetroSharp/issues/415);
- lifecycle: [NFS-2.1 / #416](https://github.com/SuperJMN/RetroSharp/issues/416);
- bounded scheduler: [NFS-3.1 / #417](https://github.com/SuperJMN/RetroSharp/issues/417);
- certification: [NFS-4.1 / #418](https://github.com/SuperJMN/RetroSharp/issues/418); and
- transient parity investigation: [NFS-V1 / #419](https://github.com/SuperJMN/RetroSharp/issues/419).

External timing calibration is retained under
[`validation/nes-test-cpu`](../validation/nes-test-cpu/README.md).

## Why the previous WIP was discarded

The earlier monolithic attempt mixed timing-model repair, runtime lifecycle,
emission strategy, scheduling policy, ABI probes, and cross-emulator raster
alignment. Its measurements were not trustworthy enough to select a physical
schedule:

- `NesTestCpu` omitted page-crossing penalties for indexed loads and taken
  branches.
- PPU writes were timestamped at instruction start rather than at the real bus
  write.
- stale-to-fresh camera publication could stay suppressed across ticks.
- phase calculations did not consistently own the physical 30-row nametable
  boundaries.
- the visual harness could report false positives or conflate a transient RGB
  sampling endpoint with physical PPU corruption.

All implementation children start from the merged `origin/master`. No child
may reuse code from the discarded branches.

## Execution graph

```text
NFS-1.1 Timing 6502 ──> NFS-1.2 OAM publication ──┐
          └───────────> NFS-2.1 Lifecycle ─────────┤
NFS-1.3 Physical gate ─────────────────────────────┤
                                                   v
                                      NFS-3.1 Bounded scheduler
                                                   |
                                                   v
                                      NFS-4.1 Certification
                                                   |
                                                   v
                                      #409 -> #408 -> #322

NFS-V1 Transient palette-index parity  (independent, non-blocking)
```

The scheduler implementation does not start until timing, OAM publication
ownership, physical-gate semantics, and stale-to-fresh lifecycle are merged.
Certification never implements a fix: it stops at the first red rung and routes
that evidence to the owning child.

## NFS-1.1 — Restore a trustworthy 6502 timing source

Kind: implementation. Owner seam: the in-process NES validation CPU adapter.

Observable: instruction cycles and PPU bus-write timestamps match the 6502 and
AprNes for the instructions that bound retained-OAM publication.

Work:

- add page-crossing penalties to `LDA abs,X`, `LDA abs,Y`, and `LDA (zp),Y`;
- add the page-crossing penalty to taken relative branches;
- timestamp PPU writes on their real bus-write cycle;
- add focused no-cross/cross microtests; and
- calibrate a 76-byte retained-OAM publisher against AprNes.

This child changes validation adapters only. Its green result is useful even
while #410 remains open.

Stop if AprNes disagrees after the instruction microtests are green. Preserve
the ROM, instruction address, CPU cycle, PPU position, and next falsifiable
hypothesis instead of compensating in production code.

## NFS-1.2 — Make OAM bytes and cost one module

Kind: implementation. Owner seam: internal NES retained-OAM publication.

Introduce the deep module `NesOamPublicationSchedule` with the small interface:

```text
Create(shadowAddress, retainedByteCount)
CpuCycles
Emit(PrgBuilder)
```

The module owns the emitted bytes and their matching CPU cost. The physical
frame scheduler is its only production caller; `NesFramePlan` must not recreate
a parallel cost formula.

The first implementation preserves the current loop exactly. With the corrected
timing adapter, the expected costs are 1,071 cycles for 76 bytes and 2,135
cycles for 152 bytes. Representative and tracked ROMs must remain byte-identical
in this child.

Stop if byte identity changes or AprNes contradicts the corrected cost. Do not
change the publisher shape in this slice.

## NFS-1.3 — Harden the physical acceptance gate

Kind: implementation. Owner seam: NES runtime ABI projection plus the visual
parity validation adapter.

Add `--gate physical|full` to
`tools/nes/verify_runner_visual_parity.py`; `full` remains the default.

The physical gate requires:

- `$2003` followed by every `$2004` byte contiguously;
- no `$2000`, `$2001`, or `$2005-$2007` write interleaved inside OAM;
- exact `$2006` pairs, targets, latch state, and ordered `$2007` data;
- `$2001` as a sensitive register, with rendering enabled throughout the proof;
- coherent selected slot, lifecycle state, phase, cursor, pending axis, and
  critical-section state; and
- exactly one commit and release in the final phase.

Generated runtime ABI sidecars expose semantic phase/cursor aliases per slot.
Python must resolve those names through `runtime_abi.py`; it must not calculate
CPU RAM addresses.

This child does not relax or reinterpret the existing full raster gate.

## NFS-2.1 — Make stale-to-fresh publication atomic

Kind: implementation. Owner seam: packed-camera lifecycle state in the NES
target runtime.

Keep the existing RAM byte, but name its internal states `Ready`, `Applied`, and
`SuppressedForCurrentTick`.

Required behavior:

- a stale edge suppresses `Camera.Apply()` only for that tick;
- the next fresh edge restores `Ready` before publication;
- `PendingStreamFlags` clears only in the branch that actually publishes the
  camera; and
- an edge cannot reach `Released` without becoming visible.

The decisive regression performs stale then fresh without another
`Camera.SetPosition()` and observes exactly one publication, one commit, and one
release.

Status: implemented by NFS-2.1. The existing `$0309` byte now has named target
states, the fresh hardware-VBlank branch restores `Ready` before packed work,
and each completed axis clears its pending flag only after copying the logical
camera into visible state. The executable regression drives a real packed ROM
through stale then fresh with one `Camera.SetPosition(8, 0)` and observes visible
X `8`, one commit, one release, a released slot, and no pending axis/flag.

## NFS-3.1 — Implement the bounded physical scheduler

Kind: implementation. Owner seam: `NesPhysicalFrameScheduler`. Its external
interface does not grow.

Fresh-frame order:

1. observe a fresh NMI;
2. read `$2002` and confirm physical VBlank;
3. publish retained OAM;
4. emit at most one packed phase;
5. restore `PPUCTRL` and `PPUSCROLL`;
6. finalize lifecycle with RAM-only writes; and
7. reset the OAM shadow.

Standard profile, at most 76 retained bytes:

- unroll four bytes, then publish 72 bytes in groups of nine;
- corrected cost: at most 855 cycles;
- column phase: at most 20 tiles, up to two combined attributes, and at most
  seven attribute-only writes;
- row phase: at most eight tiles or nine attributes; and
- maximum deadlines: 9 column frames, 10 row frames.

Large profile, 80-152 retained bytes:

- fully unroll publication; corrected maximum cost: 1,222 cycles;
- column phases: eight tiles or one attribute;
- row phases: one tile or one attribute; and
- maximum deadlines: 29 column frames, 56 row frames.

The physical row-30 and row-60 nametable boundaries participate in phase and
deadline calculation. They are not emitter exceptions. The selected slot stays
`Committing`; visible, commit, and release advance together only after the final
phase.

Pinned phase cases:

- runner column `payload=30/start=10`: `20/0`, `10/2`, `0/6`;
- worst standard column `payload=32/start=29`: `1/0`, `20/0`, `10/0`,
  `1/2`, `0/7`; and
- worst large profile: six tile phases and nine attribute phases.

Stop this child if corrected timing violates these bounds or the exact runner
overflows PRG. Open one focused investigation with the exact ROM, linker size or
first failing cycle, and next falsifiable hypothesis. Do not trim content or
weaken throughput/gates.

## NFS-4.1 — Certify and integrate

Kind: certification gate. Owner seam: end-to-end exact-ROM acceptance.

Run in order and stop at the first red:

1. focused timing, OAM schedule, physical gate, lifecycle, and scheduler tests;
2. the complete NES test project;
3. `dotnet test RetroSharp.sln -m:1`;
4. deterministic ROM/sidecar regeneration and a clean subsequent dry-run;
5. AprNes/NesMcp at five focal commits, with every physical `$2000-$2007`
   write inside VBlank;
6. FCEUmm and Nestopia state, input, collision, four nametables, and visible
   tile/palette checks;
7. preserve #409 with an explicit local WIP commit, rebase it onto the new
   `master`, and run its exact respawn gate without changing behavior; and
8. CI, independent review, PR, merge, remote alignment, and #410 closeout.

A behavior-changing #409 rebase conflict returns to #409. Certification does not
implement that correction. After #410 closes, resume the integration chain in
the order #409, #408, then #322 and its parent #318.

## NFS-V1 — Investigate transient palette-index parity

Kind: investigation. This child is independent and does not block #410.

Compare the three transient cross-emulator endpoints through palette indices,
camera/lifecycle continuity, and the incoming strip on both sides of each
sample. It may not change the runtime or add allowlists unless it demonstrates
real corruption. Its result is a bounded evidence report, not a scheduler patch.

## Execution rules

- One worktree belongs to one child issue. A second worktree requires another
  independently dispatched issue.
- Each child ends in a merged PR or a handoff containing the exact red command,
  ROM hash, first failing frame/cycle, owner seam, and next falsifiable
  hypothesis.
- Checkpoint at 90 active minutes. Stop editing at 120 active minutes unless the
  focused gate is already green and only predetermined validation remains.
- Run full validation only after the focused gate is green.
- Retry an external emulator/backend once. A second timeout becomes a focused
  infrastructure issue.
- No public language or SDK change, capability reduction, content trimming,
  rendering-disable workaround, or weakened acceptance is permitted.
