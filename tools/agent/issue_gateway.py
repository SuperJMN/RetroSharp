"""Remote claim CAS and tracker gateways used by the AEX-1 CLI."""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path
from typing import Any


STATE_LABELS = ("agent:ready", "agent:claimed", "agent:blocked", "agent:verified")
CANONICAL_REPOSITORY = "SuperJMN/RetroSharp"


class GatewayError(RuntimeError):
    def __init__(self, code: str, detail: str):
        super().__init__(detail)
        self.code = code
        self.detail = detail


def run(
    args: list[str],
    *,
    cwd: Path,
    check: bool = True,
    stdin: str | None = None,
    env: dict[str, str] | None = None,
) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        args,
        cwd=cwd,
        text=True,
        input=stdin,
        capture_output=True,
        env=env,
    )
    if check and result.returncode:
        raise GatewayError("command-failed", result.stderr.strip() or result.stdout.strip())
    return result


class GitClaimStore:
    """A compare-and-swap claim store backed by unique remote Git refs."""

    def __init__(self, repo: Path):
        self.repo = repo.resolve()
        self.origin = "origin"

    @staticmethod
    def ref(issue: int) -> str:
        return f"refs/heads/agent/claims/issue-{issue}"

    def refresh_base(self) -> str:
        fetch = run(
            [
                "git",
                "fetch",
                "--quiet",
                "--no-tags",
                self.origin,
                "+refs/heads/master:refs/remotes/origin/master",
            ],
            cwd=self.repo,
            check=False,
        )
        if fetch.returncode:
            raise GatewayError("origin-master-unavailable", fetch.stderr.strip())
        local = run(["git", "rev-parse", "--verify", "refs/remotes/origin/master"], cwd=self.repo).stdout.strip()
        remote = self._remote_sha("refs/heads/master")
        if not remote or remote != local:
            raise GatewayError("origin-master-unverified", f"local={local} remote={remote}")
        return remote

    def read(self, issue: int) -> tuple[str, dict[str, Any]] | None:
        ref = self.ref(issue)
        sha = self._remote_sha(ref)
        if not sha:
            return None
        fetch = run(["git", "fetch", "--quiet", self.origin, ref], cwd=self.repo, check=False)
        if fetch.returncode:
            raise GatewayError("claim-fetch-failed", fetch.stderr.strip())
        body = run(["git", "show", f"{sha}:claim.json"], cwd=self.repo).stdout
        return sha, json.loads(body)

    def create(self, record: dict[str, Any]) -> tuple[bool, str, dict[str, Any] | None]:
        issue = int(record["issue"])
        existing = self.read(issue)
        expected = ""
        parent = str(record["base"])
        if existing:
            old_sha, old_record = existing
            if not _expired(old_record):
                return False, old_sha, old_record
            expected = old_sha
            parent = old_sha
        commit = self._record_commit(record, parent)
        if not self._cas(issue, commit, expected):
            winner = self.read(issue)
            return False, winner[0] if winner else "", winner[1] if winner else None
        return True, commit, record

    def update(self, issue: int, expected_sha: str, record: dict[str, Any]) -> str:
        commit = self._record_commit(record, expected_sha)
        if not self._cas(issue, commit, expected_sha):
            raise GatewayError("claim-cas-lost", f"issue #{issue}")
        return commit

    def delete(self, issue: int, expected_sha: str) -> None:
        ref = self.ref(issue)
        result = run(
            [
                "git",
                "push",
                "--porcelain",
                f"--force-with-lease={ref}:{expected_sha}",
                self.origin,
                f":{ref}",
            ],
            cwd=self.repo,
            check=False,
        )
        if result.returncode:
            raise GatewayError("claim-release-cas-lost", result.stderr.strip() or result.stdout.strip())

    def _cas(self, issue: int, commit: str, expected: str) -> bool:
        ref = self.ref(issue)
        result = run(
            [
                "git",
                "push",
                "--porcelain",
                f"--force-with-lease={ref}:{expected}",
                self.origin,
                f"{commit}:{ref}",
            ],
            cwd=self.repo,
            check=False,
        )
        return result.returncode == 0

    def _record_commit(self, record: dict[str, Any], parent: str) -> str:
        raw = json.dumps(record, sort_keys=True, separators=(",", ":")) + "\n"
        blob = run(["git", "hash-object", "-w", "--stdin"], cwd=self.repo, stdin=raw).stdout.strip()
        tree = run(
            ["git", "mktree"],
            cwd=self.repo,
            stdin=f"100644 blob {blob}\tclaim.json\n",
        ).stdout.strip()
        environment = dict(os.environ)
        environment.update(
            {
                "GIT_AUTHOR_NAME": "RetroSharp Agent Gateway",
                "GIT_AUTHOR_EMAIL": "agent-gateway@retrosharp.invalid",
                "GIT_COMMITTER_NAME": "RetroSharp Agent Gateway",
                "GIT_COMMITTER_EMAIL": "agent-gateway@retrosharp.invalid",
            }
        )
        return run(
            ["git", "commit-tree", tree, "-p", parent],
            cwd=self.repo,
            stdin=f"AEX-1 claim state for issue #{record['issue']} run {record['run_id']}\n",
            env=environment,
        ).stdout.strip()

    def _remote_sha(self, ref: str) -> str | None:
        result = run(["git", "ls-remote", self.origin, ref], cwd=self.repo, check=False)
        if result.returncode:
            raise GatewayError("remote-unavailable", result.stderr.strip())
        line = result.stdout.strip()
        return line.split()[0] if line else None


def verify_canonical_origin(repo_root: Path) -> None:
    remote = run(
        ["git", "remote", "get-url", "origin"],
        cwd=repo_root,
    ).stdout.strip()
    match = re.search(
        r"(?:github\.com[/:])([^/:\s]+/[^/\s]+?)(?:\.git)?$",
        remote,
    )
    actual = match.group(1).removesuffix(".git") if match else None
    if actual is None or actual.lower() != CANONICAL_REPOSITORY.lower():
        raise GatewayError(
            "origin-repository-not-canonical",
            f"origin={remote} canonical={CANONICAL_REPOSITORY}",
        )


class GitHubTracker:
    def __init__(self, repo_root: Path):
        self.repo_root = repo_root
        result = run(["gh", "repo", "view", "--json", "nameWithOwner"], cwd=repo_root)
        self.repo = json.loads(result.stdout)["nameWithOwner"]

    def verify_origin(self) -> None:
        if self.repo.lower() != CANONICAL_REPOSITORY.lower():
            raise GatewayError(
                "github-repository-not-canonical",
                f"github={self.repo} canonical={CANONICAL_REPOSITORY}",
            )
        verify_canonical_origin(self.repo_root)

    def issue(self, number: int) -> dict[str, Any]:
        result = run(
            ["gh", "issue", "view", str(number), "--repo", self.repo, "--json", "number,body,state,title,labels"],
            cwd=self.repo_root,
        )
        return json.loads(result.stdout)

    def list_open(self) -> list[dict[str, Any]]:
        result = run(
            ["gh", "issue", "list", "--repo", self.repo, "--state", "open", "--limit", "1000", "--json", "number,body,state,title,labels"],
            cwd=self.repo_root,
        )
        return json.loads(result.stdout)

    def parent(self, number: int) -> int | None:
        result = run(
            ["gh", "api", f"repos/{self.repo}/issues/{number}/parent"],
            cwd=self.repo_root,
            check=False,
        )
        if result.returncode:
            if "404" in result.stderr:
                return None
            raise GatewayError("parent-query-failed", result.stderr.strip())
        return int(json.loads(result.stdout)["number"])

    def blocked_by(self, number: int) -> list[dict[str, Any]]:
        result = run(
            [
                "gh",
                "api",
                "--paginate",
                "--slurp",
                f"repos/{self.repo}/issues/{number}/dependencies/blocked_by",
            ],
            cwd=self.repo_root,
        )
        pages = json.loads(result.stdout)
        return [item for page in pages for item in page]

    def sub_issues(self, number: int) -> list[dict[str, Any]]:
        result = run(
            [
                "gh",
                "api",
                "--paginate",
                "--slurp",
                f"repos/{self.repo}/issues/{number}/sub_issues",
            ],
            cwd=self.repo_root,
        )
        pages = json.loads(result.stdout)
        return [item for page in pages for item in page]

    def transition(self, number: int, state: str) -> None:
        label = f"agent:{state}"
        args = ["gh", "issue", "edit", str(number), "--repo", self.repo]
        for candidate in STATE_LABELS:
            if candidate != label:
                args.extend(["--remove-label", candidate])
        args.extend(["--add-label", label])
        run(args, cwd=self.repo_root)

    def comment(self, number: int, body: str) -> None:
        run(
            ["gh", "issue", "comment", str(number), "--repo", self.repo, "--body", body],
            cwd=self.repo_root,
        )

    def upsert_checkpoint(self, number: int, body: str) -> None:
        existing = next(
            (
                item
                for item in reversed(self._comments(number))
                if "<!-- retrosharp-agent-checkpoint -->" in str(item.get("body", ""))
            ),
            None,
        )
        if existing:
            run(
                [
                    "gh",
                    "api",
                    "--method",
                    "PATCH",
                    f"repos/{self.repo}/issues/comments/{existing['id']}",
                    "-f",
                    f"body={body}",
                ],
                cwd=self.repo_root,
            )
            return
        self.comment(number, body)

    def release_receipt(self, number: int, fingerprint: str, state: str) -> bool:
        marker = f"<!-- retrosharp-agent-release:{fingerprint}:{state} -->"
        return any(
            marker in str(item.get("body", ""))
            for item in self._comments(number)
        )

    def upsert_release(
        self, number: int, fingerprint: str, state: str, body: str
    ) -> None:
        marker = f"<!-- retrosharp-agent-release:{fingerprint}:{state} -->"
        existing = next(
            (
                item
                for item in reversed(self._comments(number))
                if marker in str(item.get("body", ""))
            ),
            None,
        )
        if existing:
            run(
                [
                    "gh",
                    "api",
                    "--method",
                    "PATCH",
                    f"repos/{self.repo}/issues/comments/{existing['id']}",
                    "-f",
                    f"body={body}",
                ],
                cwd=self.repo_root,
            )
            return
        self.comment(number, body)

    def _comments(self, number: int) -> list[dict[str, Any]]:
        result = run(
            [
                "gh",
                "api",
                "--paginate",
                "--slurp",
                f"repos/{self.repo}/issues/{number}/comments",
            ],
            cwd=self.repo_root,
        )
        return [item for page in json.loads(result.stdout) for item in page]

    def update_body(self, number: int, body: str) -> None:
        run(
            ["gh", "issue", "edit", str(number), "--repo", self.repo, "--body", body],
            cwd=self.repo_root,
        )


class FixtureTracker:
    """Read-only tracker fixture plus append-only event log for local tests."""

    def __init__(self, fixture: Path, event_log: Path | None):
        self.fixture = fixture
        self.data = json.loads(fixture.read_text())
        self.event_log = event_log

    def issue(self, number: int) -> dict[str, Any]:
        return dict(self.data["issues"][str(number)])

    def verify_origin(self) -> None:
        """A fixture explicitly authorizes its local test remote."""

    def list_open(self) -> list[dict[str, Any]]:
        return [
            dict(issue)
            for issue in self.data["issues"].values()
            if str(issue.get("state", "OPEN")).upper() == "OPEN"
        ]

    def parent(self, number: int) -> int | None:
        value = self.data["issues"][str(number)].get("parent")
        return int(value) if value is not None else None

    def blocked_by(self, number: int) -> list[dict[str, Any]]:
        numbers = self.data["issues"][str(number)].get("blocked_by", [])
        return [dict(self.data["issues"][str(item)]) for item in numbers]

    def sub_issues(self, number: int) -> list[dict[str, Any]]:
        numbers = self.data["issues"][str(number)].get("sub_issues", [])
        return [dict(self.data["issues"][str(item)]) for item in numbers]

    def transition(self, number: int, state: str) -> None:
        added = f"agent:{state}"
        issue = self.data["issues"][str(number)]
        retained = [
            label
            for label in issue.get("labels", [])
            if str(label.get("name", "")) not in STATE_LABELS
        ]
        issue["labels"] = [*retained, {"name": added}]
        self.fixture.write_text(json.dumps(self.data, sort_keys=True))
        self._event(
            {
                "kind": "transition",
                "issue": number,
                "state": state,
                "added": added,
                "removed": [label for label in STATE_LABELS if label != added],
            }
        )

    def comment(self, number: int, body: str) -> None:
        self._event({"kind": "comment", "issue": number, "body": body})

    def upsert_checkpoint(self, number: int, body: str) -> None:
        self._event(
            {"kind": "checkpoint-comment", "issue": number, "body": body}
        )

    def release_receipt(self, number: int, fingerprint: str, state: str) -> bool:
        return f"{fingerprint}:{state}" in self.data.get("release_receipts", {}).get(
            str(number), []
        )

    def upsert_release(
        self, number: int, fingerprint: str, state: str, body: str
    ) -> None:
        receipts = self.data.setdefault("release_receipts", {}).setdefault(
            str(number), []
        )
        receipt = f"{fingerprint}:{state}"
        if receipt not in receipts:
            receipts.append(receipt)
        self.fixture.write_text(json.dumps(self.data, sort_keys=True))
        self._event(
            {
                "kind": "release-comment",
                "issue": number,
                "fingerprint": fingerprint,
                "body": body,
            }
        )

    def update_body(self, number: int, body: str) -> None:
        self.data["issues"][str(number)]["body"] = body
        self.fixture.write_text(json.dumps(self.data, sort_keys=True))
        self._event({"kind": "update-body", "issue": number, "body": body})

    def _event(self, event: dict[str, Any]) -> None:
        if not self.event_log:
            return
        with self.event_log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(event, sort_keys=True) + "\n")


def canonical_comment(event: str, payload: dict[str, Any], summary: str) -> str:
    machine = json.dumps(
        {"schema": "aex-1", "event": event, "payload": payload},
        sort_keys=True,
        separators=(",", ":"),
    )
    return (
        f"<!-- retrosharp-agent-{event} -->\n"
        f"<!-- retrosharp-agent-state\n{machine}\n-->\n"
        f"### Agent {event}\n\n{summary}\n"
    )


def worktree_snapshot(worktree: Path) -> dict[str, Any]:
    status = run(
        ["git", "status", "--short", "--untracked-files=all"],
        cwd=worktree,
    ).stdout
    tracked = run(["git", "diff", "--binary", "HEAD"], cwd=worktree).stdout
    staged = run(["git", "diff", "--cached", "--binary", "HEAD"], cwd=worktree).stdout
    untracked_raw = run(
        ["git", "ls-files", "--others", "--exclude-standard", "-z"],
        cwd=worktree,
    ).stdout
    untracked: list[dict[str, str]] = []
    for relative in sorted(item for item in untracked_raw.split("\0") if item):
        content = (worktree / relative).read_bytes()
        untracked.append(
            {
                "path": relative,
                "sha256": hashlib.sha256(content).hexdigest(),
            }
        )
    digest_input = json.dumps(
        {"tracked": tracked, "staged": staged, "untracked": untracked},
        sort_keys=True,
        separators=(",", ":"),
    )
    return {
        "git_status": status,
        "diff_sha256": hashlib.sha256(digest_input.encode()).hexdigest(),
        "untracked": untracked,
        "head": run(["git", "rev-parse", "HEAD"], cwd=worktree).stdout.strip(),
        "branch": run(["git", "branch", "--show-current"], cwd=worktree).stdout.strip(),
    }


def parse_time(value: str) -> dt.datetime:
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00"))


def _expired(record: dict[str, Any]) -> bool:
    return parse_time(str(record["expires_at"])) <= dt.datetime.now(dt.timezone.utc)
