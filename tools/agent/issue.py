#!/usr/bin/env python3
"""Lint, claim, prepare and hand off machine-checkable GitHub issue work."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

from issue_contract import POLICY, dependencies, lint, parse


OK = 0
LINT_INVALID = 20
DEPENDENCY_OPEN = 21
CLAIM_HELD = 22
CLAIM_EXPIRED = 23
CONTRACT_CHANGED = 24
NOT_CLAIMANT = 25
WORKTREE_DENIED = 26


def utcnow() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def iso(value: dt.datetime) -> str:
    return value.replace(microsecond=0).isoformat().replace("+00:00", "Z")


def run(args: list[str], *, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(args, text=True, capture_output=True, check=check)


def repo_root() -> Path:
    return Path(run(["git", "rev-parse", "--show-toplevel"]).stdout.strip())


def claim_dir(explicit: Path | None) -> Path:
    if explicit:
        return explicit
    common = run(["git", "rev-parse", "--git-common-dir"]).stdout.strip()
    return (repo_root() / common / "agent-claims").resolve()


def origin_master() -> str:
    result = run(["git", "rev-parse", "origin/master"], check=False)
    if result.returncode:
        return run(["git", "rev-parse", "HEAD"]).stdout.strip()
    return result.stdout.strip()


def issue_from_file(number: int, path: Path | None) -> dict[str, Any]:
    if path:
        data = json.loads(path.read_text()) if path.suffix == ".json" else {"body": path.read_text()}
        data.setdefault("number", number)
        data.setdefault("state", "OPEN")
        return data
    result = run(["gh", "issue", "view", str(number), "--json", "number,body,state,title,labels"])
    return json.loads(result.stdout)


def write_json(value: dict[str, Any]) -> None:
    print(json.dumps(value, sort_keys=True, separators=(",", ":")))


def lease_path(directory: Path, number: int) -> Path:
    return directory / f"issue-{number}.json"


def read_lease(path: Path) -> dict[str, Any] | None:
    try:
        return json.loads(path.read_text())
    except FileNotFoundError:
        return None


def expired(lease: dict[str, Any]) -> bool:
    return dt.datetime.fromisoformat(lease["expires_at"].replace("Z", "+00:00")) <= utcnow()


def current_contract(issue: dict[str, Any]) -> tuple[Any, list[str]]:
    contract = parse(str(issue.get("body", "")))
    return contract, lint(contract)


def dependency_errors(contract: Any, issues: dict[int, dict[str, Any]] | None) -> list[int]:
    if issues is None:
        return []
    return [number for number in dependencies(contract) if str(issues.get(number, {}).get("state", "OPEN")).upper() != "CLOSED"]


def command_lint(args: argparse.Namespace) -> int:
    if args.all_open:
        data = json.loads(run(["gh", "issue", "list", "--state", "open", "--limit", "1000", "--json", "number,body,state,title"]).stdout)
        issues = {int(item["number"]): item for item in data}
        selected = list(issues.values())
    else:
        selected = [issue_from_file(args.number, args.issue_file)]
        issues = None
    reports = []
    bad = False
    for issue in selected:
        contract, errors = current_contract(issue)
        blocked = dependency_errors(contract, issues)
        reports.append({"issue": issue["number"], "contract_sha256": contract.digest,
                        "exempt": contract.exemption, "errors": errors, "open_dependencies": blocked})
        bad = bad or bool(errors or blocked)
    write_json({"policy": POLICY, "reports": reports})
    return LINT_INVALID if bad else OK


def command_claim(args: argparse.Namespace) -> int:
    issue = issue_from_file(args.number, args.issue_file)
    contract, errors = current_contract(issue)
    if errors:
        write_json({"issue": args.number, "errors": errors})
        return LINT_INVALID
    if contract.exemption:
        write_json({"issue": args.number, "error": "non-dispatchable-exemption"})
        return LINT_INVALID
    open_dependencies = []
    supplied_states = json.loads(args.dependency_state_file.read_text()) if args.dependency_state_file else {}
    for dependency in dependencies(contract):
        supplied = supplied_states.get(str(dependency), supplied_states.get(dependency))
        state = supplied["state"] if supplied else json.loads(run(["gh", "issue", "view", str(dependency), "--json", "state"]).stdout)["state"]
        if str(state).upper() != "CLOSED":
            open_dependencies.append(dependency)
    if open_dependencies:
        write_json({"issue": args.number, "open_dependencies": open_dependencies})
        return DEPENDENCY_OPEN
    directory = claim_dir(args.state_dir)
    directory.mkdir(parents=True, exist_ok=True)
    path = lease_path(directory, args.number)
    existing = read_lease(path)
    if existing:
        if not expired(existing):
            write_json({"issue": args.number, "claim": "held", "run_id": existing["run_id"]})
            return CLAIM_HELD
        expiry_lock = directory / f"issue-{args.number}.expiry-lock"
        try:
            os.mkdir(expiry_lock)
        except FileExistsError:
            write_json({"issue": args.number, "claim": "expiry-recovery-in-progress"})
            return CLAIM_HELD
        try:
            existing = read_lease(path)
            if existing and not expired(existing):
                write_json({"issue": args.number, "claim": "held", "run_id": existing["run_id"]})
                return CLAIM_HELD
            if existing:
                path.unlink()
        finally:
            os.rmdir(expiry_lock)
    now = utcnow()
    lease = {"issue": args.number, "run_id": args.run_id, "contract_sha256": contract.digest,
             "base": origin_master(), "claimed_at": iso(now),
             "expires_at": iso(now + dt.timedelta(minutes=args.ttl_minutes)), "branch": None, "worktree": None}
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        write_json({"issue": args.number, "claim": "held"})
        return CLAIM_HELD
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        json.dump(lease, stream, sort_keys=True)
    write_json({"issue": args.number, "claim": "acquired", **lease})
    return OK


def assert_live(args: argparse.Namespace) -> tuple[dict[str, Any], Any, dict[str, Any], Path] | None:
    path = lease_path(claim_dir(args.state_dir), args.number)
    lease = read_lease(path)
    if not lease or lease.get("run_id") != args.run_id:
        write_json({"issue": args.number, "error": "not-live-claimant"})
        args.live_error = NOT_CLAIMANT
        return None
    if expired(lease):
        write_json({"issue": args.number, "error": "expired-claim"})
        args.live_error = CLAIM_EXPIRED
        return None
    issue = issue_from_file(args.number, args.issue_file)
    contract, errors = current_contract(issue)
    if errors or contract.digest != lease["contract_sha256"] or origin_master() != lease["base"]:
        write_json({"issue": args.number, "error": "claim-binding-changed", "lint_errors": errors})
        args.live_error = CONTRACT_CHANGED
        return None
    return issue, contract, lease, path


def command_worktree(args: argparse.Namespace) -> int:
    live = assert_live(args)
    if not live:
        return args.live_error
    _, _, lease, path = live
    destination = args.path.resolve()
    if destination.exists():
        write_json({"issue": args.number, "error": "worktree-path-exists"})
        return WORKTREE_DENIED
    branch = args.branch or f"agent/issue-{args.number}-{args.run_id}"
    result = run(["git", "worktree", "add", "-b", branch, str(destination), lease["base"]], check=False)
    if result.returncode:
        print(result.stderr, file=sys.stderr)
        return WORKTREE_DENIED
    lease.update({"branch": branch, "worktree": str(destination)})
    path.write_text(json.dumps(lease, sort_keys=True))
    write_json({"issue": args.number, "worktree": str(destination), "branch": branch})
    return OK


def git_status_digest() -> tuple[str, str]:
    status = run(["git", "status", "--short"]).stdout
    diff = run(["git", "diff", "--binary", "HEAD"]).stdout
    return status, hashlib.sha256(diff.encode()).hexdigest()


def command_checkpoint(args: argparse.Namespace) -> int:
    live = assert_live(args)
    if not live:
        return args.live_error
    _, contract, lease, path = live
    status, digest = git_status_digest()
    if args.allow_checkpoint_push:
        if not lease.get("branch") or not lease.get("worktree"):
            write_json({"issue": args.number, "error": "checkpoint-push-requires-recorded-worktree"})
            return WORKTREE_DENIED
        pushed = run(["git", "-C", lease["worktree"], "push", "--set-upstream", "origin", lease["branch"]], check=False)
        if pushed.returncode:
            print(pushed.stderr, file=sys.stderr)
            return WORKTREE_DENIED
    checkpoint = {"issue": args.number, "run_id": args.run_id, "claim": lease,
                  "owner_seam": contract.sections["Owner seam"], "red": args.red,
                  "red_exit_code": args.red_exit_code, "first_signature": args.first_signature,
                  "rom_identity": args.rom_identity, "hypotheses": args.hypothesis[:3],
                  "next_falsifiable_check": args.next_check, "active_minutes": args.active_minutes,
                  "dispatch_metadata": contract.sections["Dispatch metadata"],
                  "git_status": status, "diff_sha256": digest, "validation": args.validation,
                  "checkpoint_push": "pushed" if args.allow_checkpoint_push else "not-requested",
                  "checkpoint_commit": run(["git", "rev-parse", "HEAD"]).stdout.strip()}
    lease["checkpoint"] = checkpoint
    path.write_text(json.dumps(lease, sort_keys=True))
    write_json(checkpoint)
    print(f"\nIssue #{args.number}: {contract.sections['Owner seam']}\nRED: {args.red}\nNext: {args.next_check}")
    return OK


def command_release(args: argparse.Namespace) -> int:
    live = assert_live(args)
    if not live:
        return args.live_error
    _, _, lease, path = live
    if args.state == "verified" and not lease.get("checkpoint"):
        write_json({"issue": args.number, "error": "verified-requires-checkpoint"})
        return NOT_CLAIMANT
    path.unlink()
    write_json({"issue": args.number, "released": args.state})
    return OK


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser()
    sub = root.add_subparsers(dest="command", required=True)
    lint_parser = sub.add_parser("lint")
    lint_parser.add_argument("number", type=int, nargs="?")
    lint_parser.add_argument("--all-open", action="store_true")
    lint_parser.add_argument("--issue-file", type=Path)
    claim = sub.add_parser("claim")
    claim.add_argument("number", type=int); claim.add_argument("--run-id", required=True)
    claim.add_argument("--ttl-minutes", type=int, default=120); claim.add_argument("--issue-file", type=Path)
    claim.add_argument("--state-dir", type=Path)
    claim.add_argument("--dependency-state-file", type=Path, help="Fixture-only dependency state map for deterministic local validation.")
    for name in ("worktree", "checkpoint", "release"):
        item = sub.add_parser(name); item.add_argument("number", type=int); item.add_argument("--run-id", required=True)
        item.add_argument("--issue-file", type=Path); item.add_argument("--state-dir", type=Path)
    worktree = sub.choices["worktree"]; worktree.add_argument("path", type=Path); worktree.add_argument("--branch")
    checkpoint = sub.choices["checkpoint"]
    checkpoint.add_argument("--red", required=True); checkpoint.add_argument("--red-exit-code", type=int, required=True)
    checkpoint.add_argument("--first-signature", required=True); checkpoint.add_argument("--rom-identity", default="not-applicable")
    checkpoint.add_argument("--hypothesis", action="append", default=[]); checkpoint.add_argument("--next-check", required=True)
    checkpoint.add_argument("--active-minutes", type=int, choices=range(0, 121), required=True); checkpoint.add_argument("--validation", action="append", default=[])
    checkpoint.add_argument("--allow-checkpoint-push", action="store_true", help="Push only this recorded claimed branch; never opens a PR or merge.")
    release = sub.choices["release"]; release.add_argument("--state", choices=("released", "blocked", "verified"), required=True)
    return root


def main() -> int:
    args = parser().parse_args()
    if args.command == "lint":
        if not args.all_open and args.number is None:
            parser().error("lint requires an issue number or --all-open")
        return command_lint(args)
    return {"claim": command_claim, "worktree": command_worktree, "checkpoint": command_checkpoint, "release": command_release}[args.command](args)


if __name__ == "__main__":
    raise SystemExit(main())
