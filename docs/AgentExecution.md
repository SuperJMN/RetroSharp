# Autonomous Agent Execution

Status: operational guide.
Last updated: 2026-07-10.

This document explains how to turn `docs/ArchitectureRoadmap.md` into GitHub milestones, labels, and issues that agents can execute with minimal coordination overhead.

For generic repo orientation, read `../AGENTS.md` first. For memory-derived context, known traps, recent changes, and validation commands, read `AgentContext.md`.

## Source Of Truth

- Architecture and broad iteration backlog: `docs/ArchitectureRoadmap.md`
- Dedicated epic execution plans: linked `docs/*Roadmap.md` files such as
  `docs/LargeWorldsRoadmap.md` and `docs/GeneratedCodePerformanceRoadmap.md`
- Agent entrypoint: `AGENTS.md`
- Agent memory/context: `docs/AgentContext.md`
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

## Execution Roles

### Integrator Agent

The integrator owns sequencing and merge hygiene.

Responsibilities:

- Seed issues and milestones.
- Assign or dispatch agents only to tasks whose dependencies are satisfied.
- Keep `docs/ArchitectureRoadmap.md` current when task scope changes.
- Check that portable SDK APIs do not expose target hardware details.
- Merge PRs in dependency order.
- Run final validation for each iteration.

### Implementation Agent

An implementation agent owns one issue or a small contiguous group of issues.

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
