#!/usr/bin/env python3
"""Seed GitHub labels, milestones, and issues from docs/ArchitectureRoadmap.md.

The roadmap is the source of truth. This script parses AR-x.y task cards and
creates GitHub issues through the gh CLI. It is dry-run by default.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ROADMAP = ROOT / "docs" / "ArchitectureRoadmap.md"

LABELS: dict[str, tuple[str, str]] = {
    "roadmap": ("6f42c1", "Architecture roadmap work"),
    "agent-task": ("0e8a16", "Task intended for autonomous agent execution"),
    "needs-integration": ("fbca04", "Integrator should review sequencing and merge impact"),
    "parallel-safe": ("2da44e", "Can run in parallel after dependencies are satisfied"),
    "risk:high": ("d73a4a", "High-risk architecture or target-lowering change"),
    "layer:language": ("bfd4f2", "RetroSharp language layer"),
    "layer:sdk-2d": ("1d76db", "Portable 2D SDK layer"),
    "layer:target-intrinsic": ("d4c5f9", "Target-specific intrinsic or lowering layer"),
    "layer:documentation": ("cfd3d7", "Documentation and process work"),
    "layer:validation": ("5319e7", "Validation, acceptance, or test infrastructure"),
    "target:gb": ("8dd6f9", "Game Boy target"),
    "target:nes": ("fef2c0", "NES target"),
}

PARALLEL_SAFE = {
    "AR-1.2",
    "AR-1.3",
    "AR-1.4",
    "AR-6.1",
    "AR-10.3",
    "AR-11.2",
}

HIGH_RISK = {
    "AR-2.2",
    "AR-4.2",
    "AR-5.2",
    "AR-9.3",
    "AR-10.2",
}

EXTRA_LABELS: dict[str, list[str]] = {
    "AR-1.1": ["layer:sdk-2d"],
    "AR-1.2": ["layer:sdk-2d", "target:gb"],
    "AR-1.3": ["layer:sdk-2d", "target:nes"],
    "AR-1.4": ["layer:sdk-2d"],
    "AR-2.1": ["layer:sdk-2d"],
    "AR-2.2": ["layer:sdk-2d", "target:gb"],
    "AR-2.3": ["layer:sdk-2d", "layer:target-intrinsic", "target:gb"],
    "AR-3.1": ["layer:sdk-2d"],
    "AR-3.2": ["layer:sdk-2d", "target:gb"],
    "AR-3.3": ["layer:sdk-2d", "target:gb"],
    "AR-3.4": ["layer:sdk-2d", "target:gb"],
    "AR-4.1": ["layer:sdk-2d", "target:gb"],
    "AR-4.2": ["layer:target-intrinsic", "target:gb"],
    "AR-4.3": ["layer:sdk-2d", "target:gb"],
    "AR-5.1": ["layer:sdk-2d", "target:gb"],
    "AR-5.2": ["layer:target-intrinsic", "target:gb"],
    "AR-5.3": ["layer:sdk-2d"],
    "AR-6.1": ["layer:sdk-2d", "target:gb"],
    "AR-6.2": ["layer:sdk-2d", "target:gb"],
    "AR-6.3": ["layer:sdk-2d", "target:gb", "target:nes"],
    "AR-7.1": ["layer:sdk-2d"],
    "AR-7.2": ["layer:sdk-2d"],
    "AR-7.3": ["layer:sdk-2d", "target:gb"],
    "AR-8.1": ["layer:sdk-2d", "target:gb"],
    "AR-8.2": ["layer:sdk-2d"],
    "AR-8.3": ["layer:sdk-2d", "target:gb"],
    "AR-9.1": ["layer:sdk-2d", "layer:target-intrinsic", "target:nes"],
    "AR-9.2": ["layer:sdk-2d", "layer:target-intrinsic", "target:nes"],
    "AR-9.3": ["layer:sdk-2d", "layer:target-intrinsic", "target:nes"],
    "AR-9.4": ["layer:sdk-2d", "target:gb", "target:nes"],
    "AR-10.1": ["layer:sdk-2d"],
    "AR-10.2": ["layer:sdk-2d", "layer:target-intrinsic", "target:gb"],
    "AR-10.3": ["layer:sdk-2d", "target:nes"],
    "AR-11.1": ["layer:documentation", "layer:sdk-2d"],
    "AR-11.2": ["layer:documentation"],
    "AR-11.3": ["layer:validation", "target:gb", "target:nes"],
}

DEPENDENCIES: dict[str, list[str]] = {
    "AR-1.2": ["AR-1.1"],
    "AR-1.3": ["AR-1.1"],
    "AR-1.4": ["AR-1.1"],
    "AR-2.1": ["AR-1.1"],
    "AR-2.2": ["AR-2.1"],
    "AR-2.3": ["AR-2.2"],
    "AR-3.1": ["AR-2.1"],
    "AR-3.2": ["AR-3.1"],
    "AR-3.3": ["AR-3.2"],
    "AR-3.4": ["AR-3.1"],
    "AR-4.1": ["AR-2.1"],
    "AR-4.2": ["AR-4.1"],
    "AR-4.3": ["AR-4.2"],
    "AR-5.1": ["AR-4.3"],
    "AR-5.2": ["AR-5.1"],
    "AR-5.3": ["AR-5.2"],
    "AR-6.1": ["AR-2.1"],
    "AR-6.2": ["AR-6.1"],
    "AR-6.3": ["AR-6.1"],
    "AR-7.1": ["AR-6.1"],
    "AR-7.2": ["AR-7.1"],
    "AR-7.3": ["AR-7.2"],
    "AR-8.1": ["AR-3.4"],
    "AR-8.2": ["AR-8.1"],
    "AR-8.3": ["AR-8.2"],
    "AR-9.1": ["AR-2.3", "AR-1.3"],
    "AR-9.2": ["AR-6.1", "AR-1.3"],
    "AR-9.3": ["AR-3.3", "AR-4.3", "AR-1.3"],
    "AR-9.4": ["AR-9.1", "AR-9.2", "AR-9.3"],
    "AR-10.1": ["AR-1.4"],
    "AR-10.2": ["AR-10.1", "AR-5.3"],
    "AR-10.3": ["AR-10.1", "AR-9.4"],
    "AR-11.1": ["AR-9.4"],
    "AR-11.2": ["AR-11.1"],
    "AR-11.3": ["AR-11.2"],
}


@dataclass(frozen=True)
class RoadmapTask:
    task_id: str
    title: str
    iteration: int
    body: str
    line: int

    @property
    def issue_title(self) -> str:
        return f"{self.task_id}: {self.title}"


def run(args: list[str], *, cwd: Path = ROOT, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(args, cwd=cwd, text=True, capture_output=True, check=check)


def gh_json(args: list[str]) -> object:
    result = run(["gh", *args])
    if not result.stdout.strip():
        return None
    return json.loads(result.stdout)


def parse_iterations(text: str) -> dict[int, str]:
    iterations: dict[int, str] = {}
    for line in text.splitlines():
        match = re.match(r"^### Iteration (\d+)(?: Tasks)?: (.+)$", line)
        if match:
            iterations[int(match.group(1))] = match.group(2).strip()
    return iterations


def parse_tasks(text: str) -> list[RoadmapTask]:
    lines = text.splitlines()
    tasks: list[RoadmapTask] = []
    current: tuple[str, str, int, int] | None = None
    body_lines: list[str] = []

    def flush() -> None:
        nonlocal current, body_lines
        if current is None:
            return
        task_id, title, iteration, line_no = current
        tasks.append(RoadmapTask(task_id, title, iteration, "\n".join(body_lines).strip(), line_no))
        current = None
        body_lines = []

    for index, line in enumerate(lines, start=1):
        match = re.match(r"^#### (AR-(\d+)\.\d+): (.+)$", line)
        if match:
            flush()
            current = (match.group(1), match.group(3).strip(), int(match.group(2)), index)
            continue
        if current is not None:
            if line.startswith("#### ") or line.startswith("### ") or line.startswith("## "):
                flush()
            else:
                body_lines.append(line)
    flush()
    return tasks


def layer_labels(task: RoadmapTask) -> list[str]:
    match = re.search(r"^- Layer: ([^.]+)\.", task.body, flags=re.MULTILINE)
    if not match:
        return []
    body = match.group(1).lower()
    labels: list[str] = []
    if "documentation" in body or "api governance" in body:
        labels.append("layer:documentation")
    if "validation" in body:
        labels.append("layer:validation")
    if "portable sdk" in body or "sdk" in body:
        labels.append("layer:sdk-2d")
    if "target intrinsic" in body or "lowering" in body:
        labels.append("layer:target-intrinsic")
    if "language" in body:
        labels.append("layer:language")
    return sorted(set(labels))


def labels_for(task: RoadmapTask) -> list[str]:
    labels = ["roadmap", "agent-task", "needs-integration"]
    labels.extend(layer_labels(task))
    labels.extend(EXTRA_LABELS.get(task.task_id, []))
    if task.task_id in PARALLEL_SAFE:
        labels.append("parallel-safe")
    if task.task_id in HIGH_RISK:
        labels.append("risk:high")
    return sorted(set(labels))


def issue_body(task: RoadmapTask) -> str:
    dependencies = DEPENDENCIES.get(task.task_id, [])
    dependency_text = "\n".join(f"- {dependency}" for dependency in dependencies) if dependencies else "- None"
    return f"""Source: `docs/ArchitectureRoadmap.md` line {task.line}

## Dependencies

{dependency_text}

## Roadmap Task

{task.body}

## Agent Checklist

- [ ] Inspect the current code path before editing.
- [ ] Confirm the layer decision still matches the task.
- [ ] Keep the scope to this task id unless the integrator approves a contiguous group.
- [ ] Preserve `samples/gameboy-runner/runner.rs` compatibility when affected.
- [ ] Update docs if public API, target support, or roadmap status changes.
- [ ] Fill the PR template with verification and handoff notes.
"""


def milestone_title(iterations: dict[int, str], iteration: int) -> str:
    name = iterations.get(iteration, "Architecture Roadmap")
    return f"Iteration {iteration}: {name}"


def resolve_repo(explicit_repo: str | None) -> str:
    if explicit_repo:
        return explicit_repo
    data = gh_json(["repo", "view", "--json", "nameWithOwner"])
    if not isinstance(data, dict) or "nameWithOwner" not in data:
        raise RuntimeError("Could not resolve GitHub repository. Pass --repo owner/name.")
    return str(data["nameWithOwner"])


def existing_issue_titles(repo: str) -> set[str]:
    data = gh_json(["issue", "list", "--repo", repo, "--state", "all", "--limit", "200", "--json", "title"])
    if not isinstance(data, list):
        return set()
    return {str(item["title"]) for item in data if isinstance(item, dict) and "title" in item}


def existing_labels(repo: str) -> set[str]:
    data = gh_json(["label", "list", "--repo", repo, "--limit", "200", "--json", "name"])
    if not isinstance(data, list):
        return set()
    return {str(item["name"]) for item in data if isinstance(item, dict) and "name" in item}


def existing_milestones(repo: str) -> set[str]:
    data = gh_json(["api", "--method", "GET", f"repos/{repo}/milestones", "-f", "state=all", "--paginate"])
    if not isinstance(data, list):
        return set()
    return {str(item["title"]) for item in data if isinstance(item, dict) and "title" in item}


def ensure_labels(repo: str, apply: bool) -> None:
    present = existing_labels(repo) if apply else set()
    for name, (color, description) in LABELS.items():
        if apply and name in present:
            print(f"label exists: {name}")
            continue
        print(f"{'create' if apply else 'would create'} label: {name}")
        if apply:
            run(["gh", "label", "create", name, "--repo", repo, "--color", color, "--description", description], check=False)


def ensure_milestones(repo: str, titles: list[str], apply: bool) -> None:
    present = existing_milestones(repo) if apply else set()
    for title in titles:
        if apply and title in present:
            print(f"milestone exists: {title}")
            continue
        print(f"{'create' if apply else 'would create'} milestone: {title}")
        if apply:
            run(["gh", "api", f"repos/{repo}/milestones", "-f", f"title={title}", "-f", "state=open"])


def create_issues(repo: str, tasks: list[RoadmapTask], iterations: dict[int, str], apply: bool) -> None:
    present = existing_issue_titles(repo) if apply else set()
    for task in tasks:
        title = task.issue_title
        milestone = milestone_title(iterations, task.iteration)
        labels = labels_for(task)
        if apply and title in present:
            print(f"issue exists: {title}")
            continue
        print(f"{'create' if apply else 'would create'} issue: {title}")
        print(f"  milestone: {milestone}")
        print(f"  labels: {', '.join(labels)}")
        if apply:
            args = ["gh", "issue", "create", "--repo", repo, "--title", title, "--body", issue_body(task), "--milestone", milestone]
            for label in labels:
                args.extend(["--label", label])
            run(args)


def filter_tasks(tasks: list[RoadmapTask], iterations: set[int] | None) -> list[RoadmapTask]:
    if not iterations:
        return tasks
    return [task for task in tasks if task.iteration in iterations]


def parse_iteration_filter(value: str | None) -> set[int] | None:
    if not value:
        return None
    result: set[int] = set()
    for part in value.split(","):
        part = part.strip()
        if not part:
            continue
        result.add(int(part))
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", help="GitHub repository in owner/name form. Defaults to gh repo view.")
    parser.add_argument("--roadmap", type=Path, default=ROADMAP, help="Path to ArchitectureRoadmap.md.")
    parser.add_argument("--iterations", help="Comma-separated iteration numbers to seed, for example 1,2,3.")
    parser.add_argument("--apply", action="store_true", help="Create labels, milestones, and issues on GitHub.")
    parser.add_argument("--dry-run", action="store_true", help="Print actions without changing GitHub. This is the default.")
    args = parser.parse_args()

    if args.apply and args.dry_run:
        parser.error("--apply and --dry-run are mutually exclusive")

    roadmap_text = args.roadmap.read_text(encoding="utf-8")
    iterations = parse_iterations(roadmap_text)
    tasks = filter_tasks(parse_tasks(roadmap_text), parse_iteration_filter(args.iterations))
    if not tasks:
        raise RuntimeError("No roadmap tasks matched the requested filter.")

    apply = args.apply
    repo = resolve_repo(args.repo)
    milestone_titles = sorted({milestone_title(iterations, task.iteration) for task in tasks})

    print(f"repo: {repo}")
    print(f"mode: {'apply' if apply else 'dry-run'}")
    print(f"tasks: {len(tasks)}")

    ensure_labels(repo, apply)
    ensure_milestones(repo, milestone_titles, apply)
    create_issues(repo, tasks, iterations, apply)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as exc:
        print(exc.stderr or exc.stdout, file=sys.stderr)
        raise SystemExit(exc.returncode)
