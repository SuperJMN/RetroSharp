# ADR 0001: Target-private physical frame plans

- Status: Accepted
- Date: 2026-07-18

## Context

Game Boy and NES cartridge work is currently discovered across emitters,
runtime helpers, CPU-work reporting, and exact-ROM tests. A change can satisfy
one of those views while violating another physical constraint, especially
when retained sprite publication, camera streaming, audio, and video-safe
writes share a frame.

The two targets do not share the same legal write windows, native cycle units,
runtime phases, or staging strategies. The portable SDK must remain independent
of those hardware details.

## Decision

Each target owns one private, static physical frame plan selected at compile
time from compiler-known program and cartridge facts. The plan declares its
physical windows, mandatory work, and any explicitly bounded staging phases.

The selected policy must have one executable target-owned authority consumed
atomically by target emission, target CPU-work ledger projection, and target
diagnostics. Game Boy currently consumes its plan directly. NES encapsulates
`NesFramePlan` behind `NesPhysicalFrameScheduler`; production builders,
runtime compilers, and lowerers receive the scheduler and cannot consume the
plan directly. The scheduler owns runtime NMI/VBlank admission, retained OAM
publication, video-safe transfer ordering, and bounded camera staging. NES
lowerer partials retain only the byte-emission mechanics selected through
closed scheduler commands. Raw four-screen and packed camera rows share one
declared staging policy, and the scheduler validates that its deadline matches
the tile/attribute phase schedule it emits.

ROM builders may supply compiler-known facts and orchestrate output, but they
do not own or reconstruct scheduling policy.

Only vocabulary and checked range arithmetic may be shared across targets.
No public language or portable SDK API is added by this decision.

## Consequences

- A target schedule change must update emitted behavior, static accounting,
  diagnostics, and focused exact-ROM evidence in the same slice.
- Work may cross frame boundaries only when the target plan declares a finite
  staging state and deadline.
- Whole-frame CPU reports remain compatible while gaining per-window evidence.
- Architecture guards can reject duplicated plan policy in ROM builders or
  distributed lowerer conditionals, and on NES can reject any direct plan
  consumption outside the scheduler.
- Game Boy and NES may use different profiles and implementation structures.

## Considered alternatives

### Distributed target conditionals

Keeping scheduling decisions in individual lowerer and runtime methods makes
local edits easy but leaves no single authority for composition. Rejected
because emission and accounting can drift independently.

### Shared generic frame solver

A cross-target solver would centralize composition, but its abstraction would
need to encode target-specific write legality, interrupt behavior, and staging.
Rejected because it would move hardware policy into a broad, shallow shared
module.

### Dynamic runtime scheduler

A runtime scheduler could react to measured work, but it adds cartridge state,
branches, and hidden runtime cost. Rejected for the default compiler path;
RetroSharp prefers compile-time, zero-cost ergonomics and bounded target state.
