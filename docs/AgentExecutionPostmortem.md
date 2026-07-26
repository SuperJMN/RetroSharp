# Agent Execution Post-mortem

Status: active process guidance.
Last updated: 2026-07-26.

This document records where AI agents lose time in RetroSharp and which
operating rules prevent the same failure mode from recurring. It is not part
of normal task startup. Read it when agent work is taking too long, when
changing the issue-execution process, or when a diagnostic lane keeps refining
an unchanged conclusion.

The repository rules remain in `AGENTS.md`; the current workflow is
`AgentExecution.md`. Live issue steering overrides historical requirements in
either document.

## Executive conclusion

The main cost was not an inability to understand a large codebase. It was an
execution loop:

1. an ambiguous observation contract produced conflicting evidence;
2. each review hardened another edge of the diagnostic artifact;
3. evidence was regenerated after each hardening;
4. later hardening no longer changed reproducibility, owner, or the #408
   decision;
5. the live issue relaxed confirmation requirements, but the long-running lane
   did not refresh the issue before more costly runs.

The first independent reviews found material false-green paths and were worth
their cost. Later full review waves increasingly improved provenance and
forensic precision without changing the product conclusion:
`NOT_REPRODUCED`, no production owner, and no valid historical bisect.

Guarantee should come from one authoritative observer, fail-closed contracts,
and explicit stop conditions. It should not come from unbounded confirmation.

## RPH-6 case study

The Game Boy runner cadence lane is the clearest example.

- `GameBoyTestCpu` originally looked like a physical-frame observer, but its
  fixed 70,224-cycle buckets can cross instructions. ROM-visible gameplay and
  audio counters therefore appeared on adjacent host buckets.
- SameBoy `GB_run_frame` became the physical authority. `GameBoyTestCpu`
  remains useful behavioral simulation, but it cannot establish or veto a
  phase-specific physical-frame RED.
- Current `master` remained green across the complete runner timeline and the
  bounded A/jump/SFX phase sweep. The historical `f612a7e` result used an
  incompatible pre-reorientation contract, so there was no valid good/bad pair
  to bisect.
- Seven complete 21-phase, three-pass matrices were executed across the main
  and independent audit lanes. Each matrix invoked three SameBoy replays per
  phase run: `7 * 21 * 3 * 3 = 1,323` emulator replays.
- The final live-issue policy reduced confirmation to two matching passes:
  `21 * 2 * 3 = 126` more replays. The lane therefore consumed at least 1,449
  SameBoy replays, excluding base comparisons and isolated phase checks.

The physical result was already stable before most of those repeats. The
additional runs found artifact-contract defects, not a production cadence
defect.

## Where agents suffered

| Friction | Observable symptom | Root cause | Correction |
| --- | --- | --- | --- |
| Stale context | Agents followed an older issue body after an integrator steering comment had narrowed the work. | The issue was read at claim time but not refreshed before later expensive phases. | Re-read `updatedAt`, body, and comments before a costly matrix, bisect, broad validation, checkpoint, and publication. |
| Observer ambiguity | Agents alternated between `GameBoyTestCpu` host counters and SameBoy physical frames. | “Both observers” did not distinguish behavioral simulation from physical authority. | Name one authority per observable. Record secondary observers as diagnostics with an explicit non-authoritative role. |
| Audit amplification | Each fresh audit found another possible forged-artifact or provenance edge, causing another full regeneration. | Review had no information-gain budget and no distinction between product blockers and forensic hardening. | Run one parallel audit wave. After fixes, verify only its concrete findings. Start another broad wave only if the owner or acceptance branch can still change. |
| Confirmation inflation | Three-pass matrices were repeated even after two matching results. | “More certainty” was treated as always useful. | Two matching deterministic runs are sufficient. A third is only a disagreement resolver or a live-issue requirement. |
| Premature bisect design | Agents refined a `git bisect run` wrapper without a current RED or a compatible ancestral pair. | The existence of a requested artifact was confused with applicability. | Record `NOT_APPLICABLE` plus the missing precondition. Build or run the bisect only after both endpoints exist. |
| Unstable diagnostic hashes | A SameBoy camera power-on offset changed one absolute diagnostic value and therefore the whole comparison digest. | Provenance and product acceptance were mixed; an unspecified absolute offset entered a deterministic artifact. | Normalize only the documented power-on offset. Keep hashes as provenance, never product gates. |
| Evidence generated before freeze | A generator changed after a matrix was captured, invalidating the artifact's relationship to final code. | Implementation, audit, and evidence capture were interleaved. | Freeze code and focused tests first, then perform one final evidence capture. Store generator identity when the artifact matters. |
| Repeated broad gates | Full validation risked being rerun after diagnostic-only refinements. | Validation was used as reassurance rather than as a decision gate. | Run focused tests while iterating and the broad gate once on the selected final candidate. |
| Fresh-worktree restore assumptions | `--no-restore` failed with `NETSDK1004` in a new worktree. | A clean worktree had no assets file yet. | Let the first focused .NET command restore; use `--no-restore` only after that succeeds. |
| Publication-role mismatch | Agent issue authority forbade PR/merge while repository policy and user intent preferred PR+merge. | Implementation and integrator authority were intentionally separated but the handoff was not cheap. | Commit and checkpoint under the issue lease, release it, then publish from a distinct integrator branch without reopening diagnosis. |
| Context overload | Completed roadmaps and historical acceptance rules could look like current constraints. | Too much documentation was loaded before selecting the task route. | Read `AGENTS.md`, `AgentContext.md`, the live issue, and only the routed owner document. |

## What produced useful information

These steps materially changed the decision and should remain:

- reproducing the exact current runner ROM and input timeline before editing;
- separating ROM-visible counters from host instrumentation;
- introducing SameBoy `GB_run_frame` as the physical-frame authority;
- controlled canaries proving that gameplay, audio, camera, and OAM failures
  are detectable;
- binding comparison artifacts to the exact ROM, timeline, SameBoy library,
  and generator;
- the first parallel review wave, which found real false-green and
  misclassification paths;
- preserving the complete load while moving only the six-frame A input span;
- proving that historical evidence lacked a compatible ancestral endpoint.

## What had diminishing value

These steps should have stopped earlier once they could no longer change #408:

- repeating the complete phase matrix after every artifact-shape adjustment;
- adding another broad audit wave after the concrete findings from the prior
  wave had been verified;
- hardening a deferred bisect command before a current RED and ancestral pair
  existed;
- treating every possible malformed hand-authored JSON as equal to a
  production or tooling owner defect;
- preserving increasingly verbose per-replay evidence when per-phase digests,
  canaries, coverage, and errors were sufficient;
- using a stronger reasoning model for deterministic test, JSON, Git, or
  documentation work.

## Required operating rules

Before any experiment, write one sentence:

> This result can change: reproducibility / owner / implementation / acceptance
> / split / handoff.

If none applies, do not run it.

Use this sequence:

1. Read the live issue and record its `updatedAt`.
2. Select one observable and one authority.
3. Reproduce once and name the decision branches.
4. Run the cheapest discriminator.
5. After two consecutive no-gain steps, checkpoint and hand off.
6. Use two matching deterministic confirmations.
7. Freeze implementation and focused tests.
8. Run one final evidence capture and one broad closeout gate.
9. Publish without reopening diagnosis.

For parallel agents:

- one writer owns the worktree;
- evidence agents receive a small committed evidence pack and no inherited chat
  unless the task requires it;
- use at most one initial broad audit wave with distinct roles;
- follow-up review verifies named findings only;
- a new broad wave requires a concrete unresolved decision branch.

## Model and effort policy

Model choice should follow uncertainty, not issue importance.

| Work | Default |
| --- | --- |
| GitHub orchestration, docs, deterministic scripts, ordinary tests, focused review | Terra high or lower |
| Bounded refactor with clear failing test and owner | Terra high |
| Ambiguous runtime causality with competing owners and physical traces | Terra xhigh |
| Narrow, unresolved causal fork after cheaper evidence is exhausted | Sol max |

Do not use Sol max for emulator execution, confirmation runs, CI waiting,
artifact formatting, issue updates, or PR publication. Those costs are
workflow costs, not reasoning deficits.

No-context agents help independent review, but they do not make an underspecified
task cheaper. Give them the live issue, exact artifact paths, one question, and
a no-edit boundary. Do not make each agent rediscover the entire repository.

## Tests, docs, and diagnostic-code budget

Additional code is justified when it prevents a concrete false green, false
RED, or wrong owner. It is not justified merely because a malformed artifact
can be imagined.

- Prefer behavior and contract-shape tests over exact ROM, byte, cycle, or
  digest assertions.
- Keep generated evidence out of read-first context.
- Compact repeated evidence before committing it.
- Do not turn a one-off diagnostic into a permanent framework unless a
  downstream gate reuses it.
- After #322 completes, review the RPH-6 diagnostic tools. Keep them if they
  remain the supported runner-cadence path; otherwise archive or remove them
  rather than making every future agent maintain them.

## Remaining product uncertainty

RPH-6.3 did not reproduce a sustained gameplay/audio cadence defect under its
reviewed complete-runner timeline and phase matrix. That does not prove that a
subjective FPS drop can never occur under another duration, emulator, host, or
gameplay path.

The next justified step is #322's exact tracked GB/NES playability gate. Reopen
cadence diagnosis only with a new observable RED: exact ROM, input timeline,
first failing physical frame, and the acceptance decision it changes. Do not
reopen RPH-6 merely because more profiling precision is possible.
