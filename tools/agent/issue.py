#!/usr/bin/env python3
"""Lint, claim, prepare and hand off machine-checkable GitHub issue work."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import subprocess
from pathlib import Path
from typing import Any

from issue_contract import (
    POLICY,
    dependencies,
    dispatch_metadata,
    lint,
    parent_number,
    parse,
    publication_authority,
    render_exemption,
    translate_legacy_task,
)
from issue_gateway import (
    FixtureTracker,
    GatewayError,
    GitClaimStore,
    GitHubTracker,
    canonical_comment,
    run,
    worktree_snapshot,
)


OK = 0
LINT_INVALID = 20
DEPENDENCY_OPEN = 21
CLAIM_HELD = 22
CLAIM_EXPIRED = 23
CONTRACT_CHANGED = 24
NOT_CLAIMANT = 25
WORKTREE_DENIED = 26
BASE_UNAVAILABLE = 27
NATIVE_MISMATCH = 28
PUSH_DENIED = 29
REMOTE_ERROR = 30
MIGRATION_ERROR = 31
NOT_READY = 32


def utcnow() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def iso(value: dt.datetime) -> str:
    return value.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def repository_root() -> Path:
    result = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        text=True,
        capture_output=True,
    )
    if result.returncode:
        raise GatewayError("not-a-git-repository", result.stderr.strip())
    return Path(result.stdout.strip())


def tracker(args: argparse.Namespace, root: Path) -> FixtureTracker | GitHubTracker:
    if args.tracker_fixture:
        return FixtureTracker(args.tracker_fixture, args.tracker_log)
    return GitHubTracker(root)


def write_json(value: dict[str, Any]) -> None:
    print(json.dumps(value, sort_keys=True, separators=(",", ":")))


def label_names(issue: dict[str, Any]) -> set[str]:
    result: set[str] = set()
    for label in issue.get("labels", []):
        if isinstance(label, dict):
            result.add(str(label.get("name", "")))
        else:
            result.add(str(label))
    return result


def agent_states(issue: dict[str, Any]) -> list[str]:
    return sorted(
        name.removeprefix("agent:")
        for name in label_names(issue)
        if name in {"agent:ready", "agent:claimed", "agent:blocked", "agent:verified"}
    )


def agent_state(issue: dict[str, Any]) -> str | None:
    states = agent_states(issue)
    return states[0] if len(states) == 1 else None


def native_errors(
    issue: dict[str, Any],
    contract: Any,
    issue_tracker: FixtureTracker | GitHubTracker,
) -> tuple[list[str], list[int]]:
    if contract.exemption:
        return [], []
    errors: list[str] = []
    declared_parent = parent_number(contract)
    native_parent = issue_tracker.parent(int(issue["number"]))
    if declared_parent != native_parent:
        errors.append(
            f"native-parent:mismatch:declared={declared_parent}:actual={native_parent}"
        )
    if declared_parent is not None:
        native_children = {
            int(item["number"]) for item in issue_tracker.sub_issues(declared_parent)
        }
        if int(issue["number"]) not in native_children:
            errors.append(
                f"native-subissue:parent-{declared_parent}-does-not-list-{issue['number']}"
            )
    native_dependencies = issue_tracker.blocked_by(int(issue["number"]))
    declared_dependencies = sorted(dependencies(contract))
    actual_dependencies = sorted(int(item["number"]) for item in native_dependencies)
    if declared_dependencies != actual_dependencies:
        errors.append(
            f"native-dependencies:mismatch:declared={declared_dependencies}:actual={actual_dependencies}"
        )
    open_dependencies = [
        int(item["number"])
        for item in native_dependencies
        if str(item.get("state", "OPEN")).upper() != "CLOSED"
    ]
    return errors, sorted(open_dependencies)


def lint_one(
    issue: dict[str, Any],
    issue_tracker: FixtureTracker | GitHubTracker,
) -> dict[str, Any]:
    contract = parse(str(issue.get("body", "")))
    errors = lint(contract)
    relation_errors: list[str] = []
    open_dependencies: list[int] = []
    if not errors:
        relation_errors, open_dependencies = native_errors(issue, contract, issue_tracker)
    return {
        "issue": int(issue["number"]),
        "contract_sha256": contract.digest,
        "exempt": contract.exemption,
        "errors": errors,
        "native_errors": relation_errors,
        "open_dependencies": open_dependencies,
        "tracker_state": agent_state(issue),
    }


def command_lint(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    selected = issue_tracker.list_open() if args.all_open else [issue_tracker.issue(args.number)]
    reports = [lint_one(issue, issue_tracker) for issue in selected]
    write_json({"policy": POLICY, "reports": reports})
    if any(report["errors"] for report in reports):
        return LINT_INVALID
    if any(report["native_errors"] for report in reports):
        return NATIVE_MISMATCH
    if any(
        report["open_dependencies"]
        and not (args.all_open and report["tracker_state"] == "blocked")
        for report in reports
    ):
        return DEPENDENCY_OPEN
    return OK


def command_migrate(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    actions: list[dict[str, Any]] = []
    migration_errors = False
    for issue in issue_tracker.list_open():
        contract = parse(str(issue.get("body", "")))
        contract_errors = lint(contract)
        if not contract_errors:
            relation_errors, _ = native_errors(issue, contract, issue_tracker)
            if relation_errors:
                actions.append(
                    {
                        "issue": int(issue["number"]),
                        "action": "error",
                        "errors": relation_errors,
                    }
                )
                migration_errors = True
            continue
        labels = label_names(issue)
        is_task = bool(
            labels
            & {
                "agent-task",
                "wayfinder:task",
                "wayfinder:research",
            }
        )
        if is_task:
            native_parent = issue_tracker.parent(int(issue["number"]))
            native_dependencies = issue_tracker.blocked_by(int(issue["number"]))
            body, translation_errors = translate_legacy_task(
                issue,
                native_parent=native_parent,
                native_dependencies=native_dependencies,
            )
            if translation_errors or body is None:
                actions.append(
                    {
                        "issue": int(issue["number"]),
                        "action": "error",
                        "errors": translation_errors,
                    }
                )
                migration_errors = True
                continue
            open_dependencies = [
                int(item["number"])
                for item in native_dependencies
                if str(item.get("state", "OPEN")).upper() != "CLOSED"
            ]
            action = {
                "issue": int(issue["number"]),
                "action": "translate",
                "tracker_state": "blocked" if open_dependencies else "ready",
                "open_dependencies": sorted(open_dependencies),
                "body": body,
            }
        else:
            body = render_exemption(
                source=f"GitHub issue #{issue['number']}",
                reason=(
                    "Legacy map, integrator, or non-agent issue is explicitly "
                    "non-dispatchable. Its preserved body remains tracker evidence."
                ),
                legacy_body=str(issue.get("body", "")),
            )
            action = {
                "issue": int(issue["number"]),
                "action": "exempt",
                "tracker_state": "blocked",
                "body": body,
            }
        actions.append(action)
    if args.apply and not migration_errors:
        for action in actions:
            issue_tracker.update_body(int(action["issue"]), str(action["body"]))
            issue_tracker.transition(
                int(action["issue"]),
                str(action["tracker_state"]),
            )
    write_json({"mode": "apply" if args.apply else "dry-run", "actions": actions})
    return MIGRATION_ERROR if migration_errors else OK


def validated_contract(
    number: int,
    issue_tracker: FixtureTracker | GitHubTracker,
) -> tuple[dict[str, Any], Any]:
    issue = issue_tracker.issue(number)
    contract = parse(str(issue.get("body", "")))
    errors = lint(contract)
    if errors:
        raise CliError(LINT_INVALID, {"issue": number, "errors": errors})
    if contract.exemption:
        raise CliError(
            LINT_INVALID,
            {"issue": number, "error": "non-dispatchable-exemption"},
        )
    relation_errors, open_dependencies = native_errors(issue, contract, issue_tracker)
    if relation_errors:
        raise CliError(
            NATIVE_MISMATCH,
            {"issue": number, "native_errors": relation_errors},
        )
    if open_dependencies:
        raise CliError(
            DEPENDENCY_OPEN,
            {"issue": number, "open_dependencies": open_dependencies},
        )
    return issue, contract


def command_claim(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    issue, contract = validated_contract(args.number, issue_tracker)
    states = agent_states(issue)
    if states != ["ready"]:
        raise CliError(
            NOT_READY,
            {
                "issue": args.number,
                "error": "issue-not-ready",
                "tracker_states": states,
            },
        )
    authority, _ = publication_authority(contract)
    metadata, _ = dispatch_metadata(contract)
    assert authority is not None and metadata is not None
    if args.allow_checkpoint_push and not authority.checkpoint_push:
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "contract-forbids-checkpoint-push"},
        )
    claim_store = GitClaimStore(root, args.origin)
    base = claim_store.refresh_base()
    now = utcnow()
    record = {
        "schema": "aex-1",
        "issue": args.number,
        "run_id": args.run_id,
        "contract_sha256": contract.digest,
        "base": base,
        "claimed_at": iso(now),
        "expires_at": iso(now + dt.timedelta(minutes=args.ttl_minutes)),
        "publication_authority": authority.as_dict(),
        "dispatch_metadata": metadata.as_dict(),
        "checkpoint_push_allowed_at_dispatch": args.allow_checkpoint_push,
        "branch": None,
        "worktree": None,
        "checkpoint": None,
    }
    acquired, claim_sha, winner = claim_store.create(record)
    if not acquired:
        raise CliError(
            CLAIM_HELD,
            {
                "issue": args.number,
                "claim": "held",
                "run_id": winner.get("run_id") if winner else None,
                "claim_sha": claim_sha,
            },
        )
    try:
        issue_tracker.transition(args.number, "claimed")
        issue_tracker.comment(
            args.number,
            canonical_comment(
                "claim",
                {**record, "claim_sha": claim_sha},
                f"Run `{args.run_id}` claimed issue #{args.number} at `{base}`.",
            ),
        )
    except Exception:
        try:
            existing_states = [
                str(label.get("name", "")).removeprefix("agent:")
                for label in issue.get("labels", [])
                if str(label.get("name", "")).startswith("agent:")
            ]
            issue_tracker.transition(
                args.number,
                existing_states[0] if existing_states else "ready",
            )
        finally:
            claim_store.delete(args.number, claim_sha)
        raise
    write_json(
        {
            "issue": args.number,
            "claim": "acquired",
            "claim_sha": claim_sha,
            **record,
        }
    )
    return OK


def live_claim(
    args: argparse.Namespace,
    root: Path,
    issue_tracker: FixtureTracker | GitHubTracker,
) -> tuple[GitClaimStore, str, dict[str, Any], Any]:
    claim_store = GitClaimStore(root, args.origin)
    current = claim_store.read(args.number)
    if not current or current[1].get("run_id") != args.run_id:
        raise CliError(
            NOT_CLAIMANT,
            {"issue": args.number, "error": "not-live-claimant"},
        )
    claim_sha, record = current
    expiry = dt.datetime.fromisoformat(str(record["expires_at"]).replace("Z", "+00:00"))
    if expiry <= utcnow():
        raise CliError(CLAIM_EXPIRED, {"issue": args.number, "error": "expired-claim"})
    issue, contract = validated_contract(args.number, issue_tracker)
    base = claim_store.refresh_base()
    if contract.digest != record["contract_sha256"] or base != record["base"]:
        raise CliError(
            CONTRACT_CHANGED,
            {
                "issue": args.number,
                "error": "claim-binding-changed",
                "claim_contract": record["contract_sha256"],
                "current_contract": contract.digest,
                "claim_base": record["base"],
                "current_base": base,
            },
        )
    states = agent_states(issue)
    if states != ["claimed"]:
        raise CliError(
            NOT_READY,
            {
                "issue": args.number,
                "error": "claim-tracker-state-mismatch",
                "tracker_states": states,
            },
        )
    return claim_store, claim_sha, record, contract


def command_worktree(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    claim_store, claim_sha, record, _ = live_claim(args, root, issue_tracker)
    destination = args.path.resolve()
    if destination.exists():
        raise CliError(
            WORKTREE_DENIED,
            {"issue": args.number, "error": "worktree-path-exists"},
        )
    branch = args.branch or f"agent/work/issue-{args.number}-{args.run_id}"
    result = run(
        [
            "git",
            "worktree",
            "add",
            "-b",
            branch,
            str(destination),
            str(record["base"]),
        ],
        cwd=root,
        check=False,
    )
    if result.returncode:
        raise CliError(
            WORKTREE_DENIED,
            {
                "issue": args.number,
                "error": "worktree-create-failed",
                "detail": result.stderr.strip(),
            },
        )
    record.update(
        {
            "branch": branch,
            "worktree": str(destination),
            "gateway_repo": str(root.resolve()),
        }
    )
    claim_sha = claim_store.update(args.number, claim_sha, record)
    write_json(
        {
            "issue": args.number,
            "worktree": str(destination),
            "branch": branch,
            "claim_sha": claim_sha,
        }
    )
    return OK


def push_checkpoint(
    args: argparse.Namespace,
    claim_store: GitClaimStore,
    record: dict[str, Any],
    worktree: Path,
) -> None:
    authority = record["publication_authority"]
    if not record.get("checkpoint_push_allowed_at_dispatch"):
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "push-not-authorized-at-dispatch"},
        )
    if not authority.get("checkpoint_push"):
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "contract-forbids-checkpoint-push"},
        )
    if authority.get("pull_request") or authority.get("merge"):
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "unsafe-publication-authority"},
        )
    status = run(
        ["git", "status", "--short", "--branch", "--untracked-files=all"],
        cwd=worktree,
    ).stdout
    dirty_lines = [
        line for line in status.splitlines() if line and not line.startswith("## ")
    ]
    if dirty_lines:
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "checkpoint-push-requires-clean-worktree"},
        )
    run(["git", "submodule", "status", "--recursive"], cwd=worktree)
    run(["git", "diff", "--check"], cwd=worktree)
    branch = str(record["branch"])
    result = run(
        [
            "git",
            "push",
            "--set-upstream",
            claim_store.origin,
            f"HEAD:refs/heads/{branch}",
        ],
        cwd=worktree,
        check=False,
    )
    if result.returncode:
        raise CliError(
            PUSH_DENIED,
            {
                "issue": args.number,
                "error": "checkpoint-push-failed",
                "detail": result.stderr.strip(),
            },
        )
    run(
        [
            "git",
            "fetch",
            "--quiet",
            claim_store.origin,
            f"refs/heads/{branch}:refs/remotes/origin/{branch}",
        ],
        cwd=worktree,
    )
    counts = run(
        [
            "git",
            "rev-list",
            "--left-right",
            "--count",
            f"HEAD...refs/remotes/origin/{branch}",
        ],
        cwd=worktree,
    ).stdout.strip()
    if counts != "0\t0":
        raise CliError(
            PUSH_DENIED,
            {"issue": args.number, "error": "checkpoint-remote-not-aligned", "counts": counts},
        )


def command_checkpoint(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    claim_store, claim_sha, record, contract = live_claim(
        args,
        root,
        issue_tracker,
    )
    if not record.get("worktree") or not record.get("branch"):
        raise CliError(
            WORKTREE_DENIED,
            {"issue": args.number, "error": "checkpoint-requires-recorded-worktree"},
        )
    worktree = Path(str(record["worktree"]))
    if not worktree.is_dir():
        raise CliError(
            WORKTREE_DENIED,
            {"issue": args.number, "error": "recorded-worktree-missing"},
        )
    snapshot = worktree_snapshot(worktree)
    if snapshot["branch"] != record["branch"]:
        raise CliError(
            WORKTREE_DENIED,
            {
                "issue": args.number,
                "error": "recorded-worktree-branch-mismatch",
                "expected": record["branch"],
                "actual": snapshot["branch"],
            },
        )
    pushed = False
    if args.allow_checkpoint_push:
        push_checkpoint(args, claim_store, record, worktree)
        pushed = True
    checkpoint = {
        "issue": args.number,
        "run_id": args.run_id,
        "base": record["base"],
        "head": snapshot["head"],
        "branch": record["branch"],
        "worktree": record["worktree"],
        "owner_seam": contract.sections["Owner seam"],
        "red": args.red,
        "red_exit_code": args.red_exit_code,
        "first_signature": args.first_signature,
        "rom_identity": args.rom_identity,
        "hypotheses": args.hypothesis[:3],
        "next_falsifiable_check": args.next_check,
        "active_minutes": args.active_minutes,
        "dispatch_metadata": record["dispatch_metadata"],
        "publication_authority": record["publication_authority"],
        "git_status": snapshot["git_status"],
        "diff_sha256": snapshot["diff_sha256"],
        "untracked": snapshot["untracked"],
        "validation": args.validation,
        "checkpoint_push": "pushed" if pushed else "not-requested",
        "checkpoint_commit": snapshot["head"] if not snapshot["git_status"].strip() else None,
    }
    record["checkpoint"] = checkpoint
    claim_store.update(args.number, claim_sha, record)
    issue_tracker.upsert_checkpoint(
        args.number,
        canonical_comment(
            "checkpoint",
            checkpoint,
            (
                f"Run `{args.run_id}` checkpointed `{record['branch']}` at "
                f"`{snapshot['head']}`. Next: {args.next_check}"
            ),
        ),
    )
    write_json(checkpoint)
    print(
        f"\nIssue #{args.number}: {contract.sections['Owner seam']}\n"
        f"RED: {args.red}\nNext: {args.next_check}"
    )
    return OK


def command_release(args: argparse.Namespace, root: Path) -> int:
    issue_tracker = tracker(args, root)
    claim_store, claim_sha, record, _ = live_claim(args, root, issue_tracker)
    if args.state == "verified" and not record.get("checkpoint"):
        raise CliError(
            NOT_CLAIMANT,
            {"issue": args.number, "error": "verified-requires-checkpoint"},
        )
    tracker_state = "ready" if args.state == "released" else args.state
    payload = {
        "issue": args.number,
        "run_id": args.run_id,
        "state": tracker_state,
        "claim_sha": claim_sha,
        "branch": record.get("branch"),
        "worktree": record.get("worktree"),
        "checkpoint": record.get("checkpoint"),
    }
    issue_tracker.comment(
        args.number,
        canonical_comment(
            "release",
            payload,
            (
                f"Run `{args.run_id}` released the claim as `{tracker_state}`. "
                f"Checkpoint evidence and work branch are preserved."
            ),
        ),
    )
    issue_tracker.transition(args.number, tracker_state)
    claim_store.delete(args.number, claim_sha)
    write_json({"issue": args.number, "released": tracker_state})
    return OK


def common_tracker_arguments(item: argparse.ArgumentParser) -> None:
    item.add_argument("--tracker-fixture", type=Path)
    item.add_argument("--tracker-log", type=Path)


def common_claim_arguments(item: argparse.ArgumentParser) -> None:
    item.add_argument("number", type=int)
    item.add_argument("--run-id", required=True)
    item.add_argument("--origin", default="origin")
    common_tracker_arguments(item)


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser()
    sub = root.add_subparsers(dest="command", required=True)

    lint_parser = sub.add_parser("lint")
    lint_parser.add_argument("number", type=int, nargs="?")
    lint_parser.add_argument("--all-open", action="store_true")
    common_tracker_arguments(lint_parser)

    migrate = sub.add_parser("migrate")
    migrate.add_argument("--all-open", action="store_true", required=True)
    mode = migrate.add_mutually_exclusive_group()
    mode.add_argument("--dry-run", action="store_true")
    mode.add_argument("--apply", action="store_true")
    common_tracker_arguments(migrate)

    claim = sub.add_parser("claim")
    common_claim_arguments(claim)
    claim.add_argument("--ttl-minutes", type=int, choices=range(1, 121), default=120)
    claim.add_argument("--allow-checkpoint-push", action="store_true")

    for name in ("worktree", "checkpoint", "release"):
        common_claim_arguments(sub.add_parser(name))

    worktree = sub.choices["worktree"]
    worktree.add_argument("path", type=Path)
    worktree.add_argument("--branch")

    checkpoint = sub.choices["checkpoint"]
    checkpoint.add_argument("--red", required=True)
    checkpoint.add_argument("--red-exit-code", type=int, required=True)
    checkpoint.add_argument("--first-signature", required=True)
    checkpoint.add_argument("--rom-identity", default="not-applicable")
    checkpoint.add_argument("--hypothesis", action="append", default=[])
    checkpoint.add_argument("--next-check", required=True)
    checkpoint.add_argument("--active-minutes", type=int, choices=range(0, 121), required=True)
    checkpoint.add_argument("--validation", action="append", default=[])
    checkpoint.add_argument("--allow-checkpoint-push", action="store_true")

    release = sub.choices["release"]
    release.add_argument(
        "--state",
        choices=("released", "blocked", "verified"),
        required=True,
    )
    return root


class CliError(RuntimeError):
    def __init__(self, code: int, payload: dict[str, Any]):
        super().__init__(str(payload))
        self.code = code
        self.payload = payload


def main() -> int:
    args = parser().parse_args()
    if args.command == "lint" and not args.all_open and args.number is None:
        parser().error("lint requires an issue number or --all-open")
    try:
        root = repository_root()
        return {
            "lint": command_lint,
            "migrate": command_migrate,
            "claim": command_claim,
            "worktree": command_worktree,
            "checkpoint": command_checkpoint,
            "release": command_release,
        }[args.command](args, root)
    except CliError as error:
        write_json(error.payload)
        return error.code
    except GatewayError as error:
        write_json({"error": error.code, "detail": error.detail})
        if error.code.startswith("origin-master"):
            return BASE_UNAVAILABLE
        return REMOTE_ERROR


if __name__ == "__main__":
    raise SystemExit(main())
