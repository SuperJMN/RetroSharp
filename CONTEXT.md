# Domain Context

This glossary names the execution and frame-planning concepts used by
RetroSharp. It describes stable domain meaning, not a particular implementation.

## Certification gate

An acceptance issue that evaluates a declared ladder of evidence. A
certification gate does not own corrective implementation: it stops at the
first failing rung and delegates that failure to one implementation slice.

## Implementation slice

One target, one owner seam, and one observable behavior that can normally be
completed and reviewed in one pull request and one bounded agent session.

## Physical frame plan

A target-owned, compile-time schedule of mandatory cartridge work and
explicitly staged work across hardware frame windows.

## Physical frame window

A hardware interval in which a declared class of target work or writes is
legal and has a target-native capacity.

## Explicitly staged work

Bounded work intentionally split across a finite number of physical frames,
with a fixed target-owned phase state and completion deadline.

## Functional observation engine

A target adapter that executes an exact cartridge and projects target-native
events into normalized semantic frame observations.

## Active engineering time

Time spent diagnosing, editing, or running focused local verification. Waiting
for an external agent, CI service, or queued infrastructure is not active
engineering time.
