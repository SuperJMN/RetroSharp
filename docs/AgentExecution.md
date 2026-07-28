# Autonomous Agent Execution

Status: operational guide.
Last updated: 2026-07-28.

This document explains how to turn `docs/ArchitectureRoadmap.md` into GitHub milestones, labels, and issues that agents can execute with minimal coordination overhead.

For generic repo orientation, read `../AGENTS.md` first. Use
`AgentContext.md` for the current authority map, task router, code anchors, and
known traps.

## Source Of Truth

- Architecture and broad iteration backlog: `docs/ArchitectureRoadmap.md`
- Dedicated epic execution plans: linked `docs/*Roadmap.md` files such as
  `docs/history/LargeWorldsRoadmap.md` and `docs/history/GeneratedCodePerformanceRoadmap.md`
- Agent entrypoint: `AGENTS.md`
- Current agent routing/context: `docs/AgentContext.md`
- Issue seeding script: `tools/roadmap/seed_github_issues.py`
- Issue template: `.github/ISSUE_TEMPLATE/agent-roadmap-task.yml`
- Pull request template: `.github/PULL_REQUEST_TEMPLATE.md`

Do not duplicate a detailed task body across two local roadmaps. The broad
architecture roadmap owns layer boundaries and links to a dedicated roadmap
when an epic needs a larger decision log or dependency graph. The dedicated
roadmap then owns its detailed task ids and issue-ready contracts. The existing
seeding script parses `AR-x.y` cards from `docs/ArchitectureRoadmap.md`; epics
with another task prefix may be seeded manually until the script supports that
prefix.

## Prerequisites

- Push the roadmap and automation files before creating remote GitHub issues.
- Authenticate GitHub CLI:

```bash
gh auth status
```

- Dry-run the issue plan first:

```bash
python3 tools/roadmap/seed_github_issues.py --dry-run
```

## Creating Issues

Create labels, milestones, and issues for the first implementation slice:

```bash
python3 tools/roadmap/seed_github_issues.py --iterations 1,2,3 --apply
```

Create the whole backlog:

```bash
python3 tools/roadmap/seed_github_issues.py --apply
```

The script is idempotent by title prefix. If an issue titled `AR-1.1: ...` already exists, it is skipped.

### Dedicated epics and native subissues

For a broad, dependency-heavy epic:

1. Land the dedicated roadmap before creating remote issues.
2. Create one milestone and one parent tracking issue.
3. Create only the first decision/foundation waves initially.
4. Attach executable tasks as native GitHub subissues of the parent.
5. Keep the parent issue for integrator state; never dispatch it as an
   implementation task.
6. Seed later target waves only after their shared contracts and ADRs merge.
7. Add remote issue URLs back to the dedicated roadmap after creation.

Every child issue remains self-contained enough for a fresh agent session, but
links to its canonical roadmap section for architecture context. A child owns
one observable result and normally one PR. If new work crosses a declared layer
or target boundary, stop and return it to the integrator instead of expanding
the child silently.

## Issue Kinds And Dispatch Boundary

Every executable issue declares one of these kinds:

- `epic/integrator`: owns dependency state and integration, never a corrective
  implementation.
- `implementation`: owns one target, one architectural seam, and one observable
  behavior; normally one pull request and one agent invocation.
- `certification-gate`: evaluates an ordered acceptance ladder, never implements
  a discovered fix.
- `investigation`: produces bounded evidence or a decision and does not silently
  turn into implementation.

A certification gate stops at its first red rung. It records the exact failing
command, cartridge hash when applicable, first failing frame/cycle, and owner
seam, then links exactly one implementation child. One invocation handles one
implementation child; after that child is complete, control returns to the
integrator instead of chaining into the next rung.

An implementation issue must name its owner seam, single observable, named RED
reproduction, verification commands, and handoff destination before dispatch.
A gameplay-performance issue must also carry the immutable acceptance capsule
from `AgentContext.md`, including its perceptual terminal and remaining
review/correction budget. If diagnosis discovers a second target, owner seam, or
independently reviewable observable, split it instead of expanding the issue.

## Active-Time Checkpoints

The default active engineering budget is 90/120 minutes:

1. At 90 active minutes, checkpoint the named RED command or exact physical
   reproduction, cartridge hash when applicable, first player-visible failure or
   correlated failing frame/cycle, current owner seam, and next falsifiable
   hypothesis.
2. At 120 active minutes, stop forming new hypotheses and stop making new edits
   unless the focused acceptance is already green. Preserve the worktree and
   hand off or split the remaining work.
3. When the focused acceptance is green before the hard stop, only the issue's
   predetermined review/correction budget, full validation, or CI may continue
   past it. Reaching the perceptual terminal ends gameplay refinement even when
   time remains.

Active time is diagnosis, editing, and focused local verification. External
agent waits, queued CI, and infrastructure waits are recorded separately and do
not consume the active budget. A long build or test does not authorize unrelated
exploration while it runs.

### Evidence-yield stop rule

Elapsed time is a backstop, not permission to spend the whole budget refining
the same conclusion. Before each experiment, minimization step, or validation
run, state which resolution branch its result can change. A result has material
information gain only when it does at least one of these:

- establishes or removes reproducibility;
- eliminates a falsifiable hypothesis;
- changes the ranked owner seam; or
- changes the implementation, acceptance, split, or handoff decision.

Two consecutive completed steps with no material information gain stop the
investigation immediately. Checkpoint the best evidence and return to the
integrator; adding another metric, phase, emulator, confirmation run, or
rephrased version of the same hypothesis does not reset the count. Neither does
dispatching a fresh agent or reviewer. After the original observable is
perceptually green, a new proxy has material information gain only if it
demonstrates a player-visible/audible regression, corruption or unsafe writes,
or contradictory deterministic evidence.

Two matching deterministic runs are sufficient confirmation. A third is
allowed only when the first two disagree or the live issue names the concrete
risk that requires it. Run the complete solution or other broad closeout gate
once after selecting the final candidate, not between diagnostic iterations.
At the 90-minute checkpoint, add no new dimensions or hypotheses: run only the
cheapest already-named discriminator or hand off.

## Reproduce Before Repairing

An implementation issue already declares its named RED reproduction. For
gameplay work, that RED describes the player-visible or audible defect first;
the deterministic test is its repeatable guard, not a substitute objective.

1. Before editing, name the player action, scene, observer, presentation defect,
   safety constraints, and non-goals. Then express the defect as the smallest
   deterministic in-process observation that still measures that same fault.
   Prefer a compiled-snippet
   `GameBoyTestCpu`/`NesTestCpu` test in the style of `GameBoyRunnerLandingTests`
   over authoring a full `FunctionalAcceptance` scenario; the heavyweight
   scenario tier is for durable acceptance rungs, not first reproduction. Do not
   keep a smaller test when minimization removes the perceptual correlation.
2. Iterate against one perceptual observable and one correlated deterministic
   guard. A gameplay fix reaches its perceptual terminal when the named symptom
   is absent, applicable corruption and unsafe PPU/OAM writes are zero, and the
   focused guard stays GREEN across two matching runs. Run the broad gate once
   on that final candidate.
3. Spend at most two focused attempts reducing or correcting the observer. If a
   smaller deterministic test still cannot represent the defect, stop refining
   the proxy. Use the named runner/physical scenario as acceptance when it is
   reproducible, or return an `investigation` with the reproduction attempt,
   ranked owner seam, and first falsifiable hypothesis when nobody can reproduce
   it.

Returning a reproducing RED plus a ranked owner seam without a landed fix is a
first-class, low-stigma outcome. "Fixed but not solved" churn — repeated edits
that never flip a named test — is the failure this gate exists to prevent.
Prefer handing off a clean RED over another unfalsifiable edit. Equally, do not
turn a perceptually good ROM into an internal-model perfection project.

## Perceptual Terminal And Review Budget

The perceptual terminal is a hard stop for the current gameplay slice:

- the acceptance capsule's player action no longer produces its named visible
  or audible defect on the named observer;
- corruption and unsafe PPU/OAM writes are zero where applicable;
- the correlated deterministic guard is GREEN in two matching runs; and
- no in-scope build or public-contract failure prevents exercising the result.

After the first perceptually good candidate, allow at most one review round and
one correction round. A reviewer may block the slice only with direct evidence
of the named perceptual defect, corruption or unsafe writes, contradictory
deterministic runs, an in-scope build failure, or an in-scope public-contract
break. Precision improvements, alternative frame models, cleaner metrics,
architecture expansion, and unrelated findings are follow-up work. A new
reviewer does not reopen or reset the budget.

If reviewers disagree, the integrator adjudicates once against the acceptance
capsule and the product authority order in `AGENTS.md`; do not launch a third
investigation without new blocking evidence. Run global validation once. If it
fails, report and classify the failure before any edit. Only a causally related
failure expands this slice; publication policy may separately hold a candidate
without turning that hold into gameplay refinement.

## Machine-checkable issue gateway

`tools/agent/issue.py` owns the versioned `aex-1` issue contract and remote
claim protocol. An executable issue must pass both body-schema validation and
the native GitHub parent / `blocked_by` relationship checks before dispatch.
Textual `#123` references do not substitute for those tracker relations.

```bash
python3 tools/agent/issue.py lint --all-open
python3 tools/agent/issue.py claim <issue> --run-id <unique-run-id>
python3 tools/agent/issue.py worktree <issue> --lease-token <winner-token> ../RetroSharp-<task>
```

Claims use one unique commit at
`refs/heads/agent/claims/issue-<number>`. Remote creation and every later
mutation use compare-and-swap semantics, so separate clones cannot both win.
The gateway is anchored to the versioned canonical repository
`SuperJMN/RetroSharp`. It rejects a fork even when `gh` resolves that fork;
there is no caller-selected remote.
Only the CAS winner receives the immutable lease token required by worktree,
checkpoint, and release commands; tracker comments never contain it. The work
branch is derived as
`agent/work/issue-<number>-<token-fingerprint-prefix>` and is deliberately
separate: releasing a claim deletes only the lock ref. Canonical tracker
comments preserve the durable claim identity and contract hash with each
checkpoint/handoff receipt.

The lease remains bound to the `origin/master` SHA recorded at claim time.
Later `master` advancement does not invalidate parallel work. Contract or
native-relation changes block new work and checkpoints. An expired remote lease
can be replaced even while the tracker still says `agent:claimed`. The
same compare-and-swap path safely reconciles `agent:claimed` when an interrupted
rollback already removed the remote claim ref. Rollback removes the ref before
restoring the tracker label, so that crash window is retryable.
`release --state blocked|released` recovery path remains available after
contract/relation changes or expiry, and is idempotently retryable after a
partial comment, label, or claim-ref operation.

Every checkpoint, whether local or pushed, requires the recorded claim base to
remain an ancestor of its head. A verified release that already wrote its
canonical receipt and exclusive `agent:verified` label can also resume the final
claim-ref deletion safely.

Every checkpoint records `--evidence-gain` and
`--consecutive-no-gain`. The gateway rejects a second consecutive no-gain step
unless the checkpoint is explicitly `no-gain-stop`, making the required
handoff visible instead of silently authorizing another refinement loop.

Checkpoint pushes are disabled unless all of these are true:

- the issue's structured `Publication authority` says
  `Checkpoint push: allowed`;
- dispatch used `claim --allow-checkpoint-push`;
- execution uses `checkpoint --allow-checkpoint-push`;
- at least one validation result is recorded;
- the recorded worktree belongs to the gateway repository, is clean, its claim
  base remains an ancestor, and it passes submodule and `git diff --check`
  checks;
- the derived branch is pushed normally without force and exact remote
  alignment is verified afterward.

The gateway never creates a pull request or merges. Before a migration, preview
how legacy agent tasks will be translated into complete AEX-1 contracts and how
maps, integrators, and non-agent issues will be explicitly exempted:

```bash
python3 tools/agent/issue.py migrate --all-open --dry-run
```

Only an integrator with issue-edit authority may replace `--dry-run` with
`--apply`. Before any issue body or state mutation, `--apply` provisions and
rechecks all four AEX state labels; a provisioning failure leaves issues
untouched and retryable. Migration is reentrant: when a body update succeeded
but its label transition did not, a later run emits a state-only repair for a
valid contract with missing or conflicting agent-state labels. Exemptions repair
to blocked; dispatchable tasks derive blocked/ready from native dependencies.
After migration, run `lint --all-open` again; do not claim acceptance from the
preview alone.

## Worktree Ownership

Use one named worktree per implementation child. Creating another worktree
requires a distinct, independently dispatched issue; a scratch tree is not a
substitute for splitting scope. Record the worktree path and branch in the
checkpoint/handoff. Remove a worktree only after its branch is clean and its
work is merged or explicitly abandoned by the integrator.

## Execution Roles

### Integrator Agent

The integrator owns sequencing and merge hygiene.

Responsibilities:

- Seed issues and milestones.
- Assign or dispatch agents only to tasks whose dependencies are satisfied.
- Copy the immutable gameplay acceptance capsule into every fresh
  implementation and review dispatch.
- Enforce the perceptual terminal and the single review/correction budget.
- Keep `docs/ArchitectureRoadmap.md` current when task scope changes.
- Check that portable SDK APIs do not expose target hardware details.
- Merge PRs in dependency order.
- Run final validation for each iteration.

### Implementation Agent

An implementation agent owns one implementation issue.

Responsibilities:

- Inspect candidate files before editing.
- State the layer decision in the PR.
- Keep the task scope narrow.
- Preserve existing transitional APIs unless the task explicitly removes them.
- Run the verification commands from the issue.
- Update docs when public API, capabilities, or target support changes.

### Review Agent

A review agent checks architecture boundaries and validation.

Responsibilities:

- Look for Game Boy or NES details leaking into portable SDK APIs.
- Confirm capability checks exist before portable lowering.
- Confirm runner compatibility when the runner is affected.
- Confirm diagnostics are deterministic and target-specific.
- Do not redefine the acceptance capsule or promote an uncorrelated precision
  metric to a gameplay gate.
- Classify non-blocking precision, architecture, and unrelated findings as
  follow-ups instead of reopening a perceptually complete slice.

## Execution Waves

### Wave 0: Process Setup

Sequential.

Tasks:

- `AR-0.1`
- `AR-0.2`

Goal: roadmap, templates, and issue seeding are available.

### Wave 1: Capability Foundation

Mostly sequential at first.

Run `AR-1.1` first. After it merges, these can run in parallel:

- `AR-1.2`
- `AR-1.3`
- `AR-1.4`

Goal: every target exposes explicit 2D capabilities and consistent errors.

### Wave 2: SDK Operation Boundary

Sequential or one implementation agent plus one review agent.

Tasks:

- `AR-2.1`
- `AR-2.2`
- `AR-2.3`

Goal: portable operations exist before target-specific emission.

### Wave 3: Unified World Map

Sequential after `AR-3.1`.

Tasks:

- `AR-3.1`
- `AR-3.2`
- `AR-3.3`
- `AR-3.4`

Goal: visual map, streaming map, and collision flags share one source of truth.

### Wave 4: Camera And Sprite Branches

Can run as two branches after Waves 1-3.

Camera branch:

- `AR-4.1`
- `AR-4.2`
- `AR-4.3`
- `AR-5.1`
- `AR-5.2`
- `AR-5.3`

Sprite branch:

- `AR-6.1`
- `AR-6.2`
- `AR-6.3`
- `AR-7.1`
- `AR-7.2`
- `AR-7.3`

Goal: position-based camera, vertical scroll groundwork, logical sprite metadata, palette slots, and animation tables.

### Wave 5: Collision

Sequential.

Tasks:

- `AR-8.1`
- `AR-8.2`
- `AR-8.3`

Goal: collision queries use world coordinates and tile flags, not camera internals.

### Wave 6: NES Portability Spike

Sequential or two tightly coordinated agents.

Tasks:

- `AR-9.1`
- `AR-9.2`
- `AR-9.3`
- `AR-9.4`

Goal: prove the SDK subset is not Game Boy-only.

### Wave 7: HUD And Stabilization

Sequential.

Tasks:

- `AR-10.1`
- `AR-10.2`
- `AR-10.3`
- `AR-11.1`
- `AR-11.2`
- `AR-11.3`

Goal: optional HUD is capability-gated, transitional APIs are quarantined, and SDK v1 is documented.

## Branch And PR Naming

Use stable task ids in branch and PR titles.

Examples:

```text
agent/ar-1-1-capability-model
agent/ar-3-1-world-map-resource
agent/ar-6-2-portable-sprite-flip
```

PR title format:

```text
AR-1.1: Add capability model types
```

## Definition Of Done

An issue is done when:

- The task acceptance criteria are satisfied.
- The issue verification commands have run.
- The Game Boy runner still builds when affected.
- Capability checks exist for new portable SDK behavior.
- Docs are updated for any public API or support change.
- The PR template is filled with task id, layer, verification, and handoff notes.

## Stop Conditions

Stop and return to the integrator if:

- A task requires changing a different architectural layer than the issue declares.
- A portable API would need target-specific hardware constants in its signature.
- Two agents need to modify the same builder/compiler code in incompatible ways.
- A target cannot support the requested behavior within declared capabilities.
- The runner cannot be kept working without broad unrelated rewrites.
- Two consecutive experiments produce no material information gain.
- Two focused observer-minimization attempts cannot preserve the reported
  perceptual defect; use the named runner/physical scenario or return a bounded
  investigation carrying the reproduction attempt.
- Gameplay work started without a named acceptance capsule; stop and define the
  player action, observer, symptom, safety constraints, non-goals, and terminal
  verdict before editing.
- The perceptual terminal and allotted review/correction round are complete;
  checkpoint instead of opening another precision or architecture refinement.
