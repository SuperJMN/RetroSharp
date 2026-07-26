#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
AGENT = ROOT / "tools" / "agent"
CLI = AGENT / "issue.py"
sys.path.insert(0, str(AGENT))

from issue_contract import lint, parse, render_exemption  # noqa: E402
from issue_gateway import (  # noqa: E402
    FixtureTracker,
    GatewayError,
    GitClaimStore,
    GitHubTracker,
)


def token_fingerprint_for_test(token: str) -> str:
    return hashlib.sha256(token.encode()).hexdigest()


def canonical_payload(body: str) -> dict[str, object]:
    state = body.split("<!-- retrosharp-agent-state\n", 1)[1].split("\n-->", 1)[0]
    return json.loads(state)["payload"]


def contract(
    *,
    parent: str = "#1",
    dependencies: str = "None",
    checkpoint_push: str = "forbidden",
    model: str = "terra-high",
    justification: str | None = None,
) -> str:
    justification_line = (
        f"\nEscalation justification: {justification}" if justification else ""
    )
    return f"""## Schema

aex-1

## Kind

implementation

## Parent

{parent}

## Dependencies

{dependencies}

## Layer

validation

## Target

none

## Owner seam

Remote issue claim gateway

## Single observable

Exactly one remote claimant owns the issue.

## No-goals

No production edits.

## Exact RED

python3 tools/agent/issue.py claim 408 --run-id red-a

## Verification

python3 tools/agent/tests/test_issue.py

## Publication authority

Local commit: allowed
Checkpoint push: {checkpoint_push}
Pull request: forbidden
Merge: forbidden

## Dispatch metadata

Model: {model}
Effort: high{justification_line}

## Handoff destination

Integrator #1.

## Active engineering policy

90-minute checkpoint / 120-minute hard stop
"""


def legacy_task_body() -> str:
    return """## Kind

`implementation` in validation tooling.

## Parent

Legacy symbolic parent.

## Owner seam

One legacy seam.

## Single observable

One legacy observable.

## Exact RED

python3 missing-tool.py

## Verification

python3 focused-tests.py

## Non-goals

No production changes.
"""


def git(cwd: Path, *args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["git", *args],
        cwd=cwd,
        text=True,
        capture_output=True,
    )
    if check and result.returncode:
        raise AssertionError(result.stderr + result.stdout)
    return result


class IssueCliTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.remote = self.root / "remote.git"
        git(self.root, "init", "--bare", str(self.remote))
        self.seed = self.root / "seed"
        git(self.root, "init", "--initial-branch=master", str(self.seed))
        git(self.seed, "config", "user.name", "AEX Test")
        git(self.seed, "config", "user.email", "aex@example.invalid")
        (self.seed / "README.md").write_text("base\n")
        git(self.seed, "add", "README.md")
        git(self.seed, "commit", "-m", "base")
        git(self.seed, "remote", "add", "origin", str(self.remote))
        git(self.seed, "push", "-u", "origin", "master")
        self.clone_a = self.root / "clone-a"
        self.clone_b = self.root / "clone-b"
        git(self.root, "clone", str(self.remote), str(self.clone_a))
        git(self.root, "clone", str(self.remote), str(self.clone_b))
        for clone in (self.clone_a, self.clone_b):
            git(clone, "config", "user.name", "AEX Test")
            git(clone, "config", "user.email", "aex@example.invalid")
        self.fixture = self.root / "issues.json"
        self.log = self.root / "tracker.jsonl"
        self.tokens: dict[str, str] = {}
        self.branches: dict[str, str] = {}
        self.write_fixture(contract())

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def write_fixture(
        self,
        body: str,
        *,
        parent: int | None = 1,
        blocked_by: list[int] | None = None,
        dependency_state: str = "CLOSED",
    ) -> None:
        blocked_by = blocked_by or []
        issues = {
            "1": {
                "number": 1,
                "body": "parent",
                "state": "CLOSED",
                "parent": None,
                "blocked_by": [],
                "sub_issues": [408],
                "labels": [],
            },
            "408": {
                "number": 408,
                "body": body,
                "state": "OPEN",
                "parent": parent,
                "blocked_by": blocked_by,
                "sub_issues": [],
                "labels": [{"name": "agent:ready"}],
            },
        }
        if 2 in blocked_by:
            issues["2"] = {
                "number": 2,
                "body": "dependency",
                "state": dependency_state,
                "parent": None,
                "blocked_by": [],
                "sub_issues": [],
                "labels": [],
            }
        self.fixture.write_text(json.dumps({"issues": issues}))

    def invoke(
        self,
        clone: Path,
        *args: str,
        expect: int = 0,
        log: Path | None = None,
    ) -> subprocess.CompletedProcess[str]:
        command = [
            sys.executable,
            str(CLI),
            *args,
            "--tracker-fixture",
            str(self.fixture),
            "--tracker-log",
            str(log or self.log),
        ]
        result = subprocess.run(
            command,
            cwd=clone,
            text=True,
            capture_output=True,
        )
        self.assertEqual(expect, result.returncode, result.stderr + result.stdout)
        return result

    def claim(
        self,
        clone: Path,
        run_id: str = "run-a",
        *extra: str,
        expect: int = 0,
    ) -> subprocess.CompletedProcess[str]:
        result = self.invoke(
            clone,
            "claim",
            "408",
            "--run-id",
            run_id,
            *extra,
            expect=expect,
        )
        if expect == 0:
            payload = json.loads(result.stdout)
            self.tokens[run_id] = payload["lease_token"]
            self.branches[run_id] = payload["branch"]
        return result

    def worktree(self, run_id: str = "run-a") -> Path:
        destination = self.root / f"work-{run_id}"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--lease-token",
            self.tokens[run_id],
            str(destination),
        )
        return destination

    def checkpoint_args(self, run_id: str = "run-a") -> list[str]:
        return [
            "checkpoint",
            "408",
            "--lease-token",
            self.tokens[run_id],
            "--red",
            "focused-red",
            "--red-exit-code",
            "1",
            "--first-signature",
            "first failure",
            "--next-check",
            "next falsifiable check",
            "--active-minutes",
            "10",
            "--validation",
            "focused:green",
        ]

    def events(self) -> list[dict[str, object]]:
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def test_incomplete_contract_has_stable_code(self) -> None:
        self.write_fixture("## Kind\n\nimplementation\n")
        self.invoke(self.clone_a, "lint", "408", expect=20)

    def test_two_independent_clones_have_exactly_one_remote_cas_winner(self) -> None:
        logs = (self.root / "a.jsonl", self.root / "b.jsonl")
        commands = []
        for clone, run_id, log in zip(
            (self.clone_a, self.clone_b),
            ("same-run", "same-run"),
            logs,
            strict=True,
        ):
            commands.append(
                (
                    clone,
                    [
                        sys.executable,
                        str(CLI),
                        "claim",
                        "408",
                        "--run-id",
                        run_id,
                        "--tracker-fixture",
                        str(self.fixture),
                        "--tracker-log",
                        str(log),
                    ],
                )
            )
        processes = [
            subprocess.Popen(command, cwd=clone, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            for clone, command in commands
        ]
        outputs = [process.communicate() for process in processes]
        codes = sorted(process.returncode for process in processes)
        self.assertEqual([0, 22], codes, outputs)
        parsed = [
            json.loads(stdout)
            for stdout, _ in outputs
            if stdout.strip()
        ]
        self.assertEqual(
            1,
            sum("lease_token" in item for item in parsed),
        )
        winning_token = next(
            item["lease_token"] for item in parsed if "lease_token" in item
        )
        remote_claim = git(
            self.clone_a,
            "ls-remote",
            "origin",
            "refs/heads/agent/claims/issue-408",
        ).stdout.strip()
        self.assertTrue(remote_claim)
        events = []
        for log in logs:
            if log.exists():
                events.extend(json.loads(line) for line in log.read_text().splitlines())
        self.assertEqual(1, sum(event["kind"] == "transition" for event in events))
        self.assertEqual(1, sum(event["kind"] == "comment" for event in events))
        self.assertTrue(
            all(winning_token not in str(event.get("body", "")) for event in events)
        )

    def test_stale_token_cannot_use_active_claim(self) -> None:
        self.claim(self.clone_a)
        destination = self.root / "never-created"
        self.invoke(
            self.clone_b,
            "worktree",
            "408",
            "--lease-token",
            "different-token",
            str(destination),
            expect=25,
        )
        self.assertFalse(destination.exists())

    def test_expired_claim_is_taken_over_while_tracker_still_claimed(self) -> None:
        self.claim(self.clone_a)
        old_token = self.tokens["run-a"]
        store = GitClaimStore(self.clone_a)
        claim_sha, record = store.read(408)
        record["expires_at"] = "2000-01-01T00:00:00Z"
        store.update(408, claim_sha, record)
        self.claim(self.clone_b, run_id="run-b")
        self.assertNotEqual(old_token, self.tokens["run-b"])
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--lease-token",
            old_token,
            str(self.root / "stale"),
            expect=25,
        )

    def test_claim_reconciles_claimed_tracker_state_without_remote_ref(self) -> None:
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"] = [{"name": "agent:claimed"}]
        self.fixture.write_text(json.dumps(data))

        self.claim(self.clone_a)

        claim_ref = git(
            self.clone_a,
            "ls-remote",
            "origin",
            "refs/heads/agent/claims/issue-408",
        ).stdout
        self.assertTrue(claim_ref.strip())
        issue = json.loads(self.fixture.read_text())["issues"]["408"]
        self.assertEqual([{"name": "agent:claimed"}], issue["labels"])

    def test_native_parent_and_dependency_mismatch_fails(self) -> None:
        self.write_fixture(contract(dependencies="- #2"), parent=9, blocked_by=[])
        result = self.invoke(self.clone_a, "lint", "408", expect=28)
        report = json.loads(result.stdout)["reports"][0]
        self.assertEqual(2, len(report["native_errors"]))

    def test_lint_rejects_zero_or_multiple_agent_state_labels(self) -> None:
        for labels in (
            [{"name": "agent-task"}],
            [{"name": "agent:ready"}, {"name": "agent:blocked"}],
        ):
            data = json.loads(self.fixture.read_text())
            data["issues"]["408"]["labels"] = labels
            self.fixture.write_text(json.dumps(data))
            result = self.invoke(self.clone_a, "lint", "408", expect=28)
            report = json.loads(result.stdout)["reports"][0]
            self.assertTrue(
                any(
                    error.startswith("tracker-state:")
                    for error in report["native_errors"]
                )
            )

    def test_parent_must_list_issue_as_native_subissue(self) -> None:
        data = json.loads(self.fixture.read_text())
        data["issues"]["1"]["sub_issues"] = []
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(self.clone_a, "lint", "408", expect=28)
        report = json.loads(result.stdout)["reports"][0]
        self.assertTrue(
            any(error.startswith("native-subissue:") for error in report["native_errors"])
        )

    def test_closed_native_dependency_is_not_reported_open(self) -> None:
        self.write_fixture(
            contract(dependencies="- #2"),
            blocked_by=[2],
            dependency_state="CLOSED",
        )
        self.invoke(self.clone_a, "lint", "408")
        self.claim(self.clone_a)

    def test_open_native_dependency_blocks_claim(self) -> None:
        self.write_fixture(
            contract(dependencies="- #2"),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        self.claim(self.clone_a, expect=21)

    def test_missing_origin_master_fails_without_head_fallback(self) -> None:
        empty_remote = self.root / "empty.git"
        empty_clone = self.root / "empty-clone"
        git(self.root, "init", "--bare", str(empty_remote))
        git(self.root, "init", str(empty_clone))
        git(empty_clone, "remote", "add", "origin", str(empty_remote))
        self.claim(empty_clone, expect=27)

    def test_live_tracker_rejects_noncanonical_origin_identity(self) -> None:
        live_tracker = GitHubTracker.__new__(GitHubTracker)
        live_tracker.repo_root = self.clone_a
        live_tracker.repo = "SuperJMN/RetroSharp"
        with self.assertRaises(GatewayError):
            live_tracker.verify_origin()
        git(
            self.clone_a,
            "remote",
            "set-url",
            "origin",
            "git@github.com:SuperJMN/RetroSharp.git",
        )
        live_tracker.verify_origin()

    def test_live_tracker_rejects_fork_when_gh_resolves_the_fork(self) -> None:
        live_tracker = GitHubTracker.__new__(GitHubTracker)
        live_tracker.repo_root = self.clone_a
        live_tracker.repo = "fork-owner/RetroSharp"
        git(
            self.clone_a,
            "remote",
            "set-url",
            "origin",
            "git@github.com:fork-owner/RetroSharp.git",
        )
        with self.assertRaises(GatewayError):
            live_tracker.verify_origin()

    def test_ttl_is_bounded_to_one_through_120(self) -> None:
        for ttl in ("0", "121"):
            result = subprocess.run(
                [
                    sys.executable,
                    str(CLI),
                    "claim",
                    "408",
                    "--run-id",
                    f"ttl-{ttl}",
                    "--ttl-minutes",
                    ttl,
                ],
                cwd=self.clone_a,
                text=True,
                capture_output=True,
            )
            self.assertEqual(2, result.returncode)

    def test_run_id_is_a_safe_short_slug(self) -> None:
        for run_id in ("UPPER", "ends-", "x" * 33, "has/slash"):
            result = subprocess.run(
                [
                    sys.executable,
                    str(CLI),
                    "claim",
                    "408",
                    "--run-id",
                    run_id,
                ],
                cwd=self.clone_a,
                text=True,
                capture_output=True,
            )
            self.assertEqual(2, result.returncode)

    def test_sol_max_requires_explicit_escalation_justification(self) -> None:
        self.write_fixture(contract(model="sol-max"))
        self.invoke(self.clone_a, "lint", "408", expect=20)
        self.write_fixture(
            contract(model="sol-max", justification="Detector exhausted terra-xhigh.")
        )
        self.invoke(self.clone_a, "lint", "408")

    def test_policy_text_is_not_required_in_user_contract_body(self) -> None:
        body = contract().replace(
            "## Active engineering policy\n\n"
            "90-minute checkpoint / 120-minute hard stop\n",
            "",
        )
        self.write_fixture(body)
        self.invoke(self.clone_a, "lint", "408")

    def test_contract_change_prevents_worktree_creation(self) -> None:
        self.claim(self.clone_a)
        self.write_fixture(contract().replace("Exactly one", "A changed"))
        destination = self.root / "never-created"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            str(destination),
            expect=24,
        )
        self.assertFalse(destination.exists())

    def test_contract_change_prevents_checkpoint(self) -> None:
        self.claim(self.clone_a)
        self.worktree()
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["body"] = contract().replace(
            "Exactly one", "Changed observable"
        )
        self.fixture.write_text(json.dumps(data))
        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            expect=24,
        )

    def test_worktree_without_claim_is_denied_before_mutation(self) -> None:
        destination = self.root / "never-created"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--lease-token",
            "stale-token",
            str(destination),
            expect=25,
        )
        self.assertFalse(destination.exists())

    def test_origin_master_advance_does_not_invalidate_claim(self) -> None:
        self.claim(self.clone_a)
        (self.clone_b / "advance.txt").write_text("advance\n")
        git(self.clone_b, "add", "advance.txt")
        git(self.clone_b, "commit", "-m", "advance")
        git(self.clone_b, "push", "origin", "master")
        self.worktree()

    def test_release_survives_master_contract_and_dependency_changes(self) -> None:
        self.claim(self.clone_a)
        (self.clone_b / "advance.txt").write_text("advance\n")
        git(self.clone_b, "add", "advance.txt")
        git(self.clone_b, "commit", "-m", "advance")
        git(self.clone_b, "push", "origin", "master")
        self.write_fixture(
            contract().replace("Exactly one", "Changed contract"),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            "--state",
            "blocked",
        )

    def test_expired_claim_can_be_released_blocked(self) -> None:
        self.claim(self.clone_a)
        store = GitClaimStore(self.clone_a)
        claim_sha, record = store.read(408)
        record["expires_at"] = "2000-01-01T00:00:00Z"
        store.update(408, claim_sha, record)
        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            "--state",
            "blocked",
        )

    def test_release_retry_handles_target_label_and_ref_already_present_or_absent(self) -> None:
        self.claim(self.clone_a)
        token = self.tokens["run-a"]
        fingerprint = token_fingerprint_for_test(token)
        fixture_tracker = FixtureTracker(self.fixture, self.log)
        fixture_tracker.upsert_release(
            408,
            fingerprint,
            "blocked",
            "pre-existing canonical release receipt",
        )
        fixture_tracker.transition(408, "blocked")
        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--lease-token",
            token,
            "--state",
            "blocked",
        )
        FixtureTracker(self.fixture, self.log).transition(408, "ready")
        self.invoke(
            self.clone_a,
            "release",
            "408",
            "--lease-token",
            token,
            "--state",
            "blocked",
        )
        data = json.loads(self.fixture.read_text())
        receipts = data["release_receipts"]["408"]
        self.assertEqual([f"{fingerprint}:blocked"], receipts)

    def test_verified_release_retries_after_receipt_and_label_before_ref_delete(self) -> None:
        self.claim(self.clone_a)
        self.worktree()
        self.invoke(self.clone_b, *self.checkpoint_args())
        token = self.tokens["run-a"]
        fingerprint = token_fingerprint_for_test(token)
        fixture_tracker = FixtureTracker(self.fixture, self.log)
        fixture_tracker.upsert_release(
            408,
            fingerprint,
            "verified",
            "pre-existing verified release receipt",
        )
        fixture_tracker.transition(408, "verified")

        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--lease-token",
            token,
            "--state",
            "verified",
        )

        claim_ref = git(
            self.clone_a,
            "ls-remote",
            "origin",
            "refs/heads/agent/claims/issue-408",
        ).stdout
        self.assertFalse(claim_ref.strip())

    def test_checkpoint_uses_recorded_worktree_from_wrong_cwd_and_hashes_untracked(self) -> None:
        self.claim(self.clone_a)
        worktree = self.worktree()
        (worktree / "untracked.txt").write_text("one\n")
        first = self.invoke(self.clone_b, *self.checkpoint_args())
        first_report = json.loads(first.stdout.splitlines()[0])
        self.assertIn("?? untracked.txt", first_report["git_status"])
        self.assertEqual(
            git(worktree, "rev-parse", "HEAD").stdout.strip(),
            first_report["head"],
        )
        self.assertEqual("untracked.txt", first_report["untracked"][0]["path"])
        repeated = self.invoke(self.clone_b, *self.checkpoint_args())
        repeated_report = json.loads(repeated.stdout.splitlines()[0])
        self.assertEqual(first_report, repeated_report)
        (worktree / "untracked.txt").write_text("two\n")
        second = self.invoke(self.clone_b, *self.checkpoint_args())
        second_report = json.loads(second.stdout.splitlines()[0])
        self.assertNotEqual(first_report["diff_sha256"], second_report["diff_sha256"])

    def test_worktree_retry_is_idempotent_for_recorded_path(self) -> None:
        self.claim(self.clone_a)
        destination = self.worktree()
        result = self.invoke(
            self.clone_b,
            "worktree",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            str(destination),
        )
        self.assertTrue(json.loads(result.stdout)["idempotent"])

    def test_worktree_rejects_a_same_branch_fork_clone(self) -> None:
        self.claim(self.clone_a)
        fork_remote = self.root / "fork.git"
        fork_clone = self.root / "fork-clone"
        git(self.root, "clone", "--bare", str(self.remote), str(fork_remote))
        git(self.root, "clone", str(fork_remote), str(fork_clone))
        git(fork_clone, "checkout", "-b", self.branches["run-a"])

        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            str(fork_clone),
            expect=26,
        )

    def test_checkpoint_rejects_a_fork_clone_recorded_as_worktree(self) -> None:
        self.write_fixture(contract(checkpoint_push="allowed"))
        self.claim(self.clone_a, "run-a", "--allow-checkpoint-push")
        fork_remote = self.root / "fork.git"
        fork_clone = self.root / "fork-clone"
        git(self.root, "clone", "--bare", str(self.remote), str(fork_remote))
        git(self.root, "clone", str(fork_remote), str(fork_clone))
        git(fork_clone, "checkout", "-b", self.branches["run-a"])
        store = GitClaimStore(self.clone_a)
        claim_sha, record = store.read(408)
        record["worktree"] = str(fork_clone)
        record["gateway_repo"] = str(self.clone_a.resolve())
        store.update(408, claim_sha, record)

        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            "--allow-checkpoint-push",
            expect=26,
        )
        self.assertFalse(
            git(
                fork_clone,
                "ls-remote",
                "origin",
                f"refs/heads/{self.branches['run-a']}",
            ).stdout.strip()
        )

    def test_checkpoint_push_requires_contract_and_dispatch_authority(self) -> None:
        self.claim(self.clone_a)
        self.worktree()
        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            "--allow-checkpoint-push",
            expect=29,
        )
        self.invoke(
            self.clone_a,
            "release",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            "--state",
            "released",
        )
        self.write_fixture(contract(checkpoint_push="allowed"))
        self.claim(self.clone_a, run_id="run-b")
        self.worktree(run_id="run-b")
        self.invoke(
            self.clone_b,
            *self.checkpoint_args(run_id="run-b"),
            "--allow-checkpoint-push",
            expect=29,
        )

    def test_checkpoint_push_requires_recorded_validation(self) -> None:
        self.write_fixture(contract(checkpoint_push="allowed"))
        self.claim(self.clone_a, "run-a", "--allow-checkpoint-push")
        self.worktree()
        args = self.checkpoint_args()
        validation_index = args.index("--validation")
        del args[validation_index : validation_index + 2]
        self.invoke(
            self.clone_b,
            *args,
            "--allow-checkpoint-push",
            expect=29,
        )

    def test_checkpoint_push_requires_claim_base_ancestry(self) -> None:
        self.write_fixture(contract(checkpoint_push="allowed"))
        self.claim(self.clone_a, "run-a", "--allow-checkpoint-push")
        worktree = self.worktree()
        empty_tree = subprocess.run(
            ["git", "mktree"],
            cwd=worktree,
            text=True,
            input="",
            capture_output=True,
            check=True,
        ).stdout.strip()
        unrelated = subprocess.run(
            ["git", "commit-tree", empty_tree],
            cwd=worktree,
            text=True,
            input="unrelated\n",
            capture_output=True,
            check=True,
        ).stdout.strip()
        git(worktree, "reset", "--hard", unrelated)
        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            "--allow-checkpoint-push",
            expect=26,
        )

    def test_checkpoint_without_push_requires_claim_base_ancestry(self) -> None:
        self.claim(self.clone_a)
        worktree = self.worktree()
        empty_tree = subprocess.run(
            ["git", "mktree"],
            cwd=worktree,
            text=True,
            input="",
            capture_output=True,
            check=True,
        ).stdout.strip()
        unrelated = subprocess.run(
            ["git", "commit-tree", empty_tree],
            cwd=worktree,
            text=True,
            input="unrelated\n",
            capture_output=True,
            check=True,
        ).stdout.strip()
        git(worktree, "reset", "--hard", unrelated)

        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            expect=26,
        )

    def test_worktree_rejects_origin_and_branch_overrides(self) -> None:
        self.claim(self.clone_a)
        for option, value in (("--origin", "elsewhere"), ("--branch", "custom")):
            result = subprocess.run(
                [
                    sys.executable,
                    str(CLI),
                    "worktree",
                    "408",
                    "--lease-token",
                    self.tokens["run-a"],
                    str(self.root / "work-override"),
                    option,
                    value,
                ],
                cwd=self.clone_a,
                text=True,
                capture_output=True,
            )
            self.assertEqual(2, result.returncode)

    def test_remote_state_evidence_and_work_branch_survive_release(self) -> None:
        self.write_fixture(contract(checkpoint_push="allowed"))
        self.claim(self.clone_a, "run-a", "--allow-checkpoint-push")
        self.worktree()
        self.invoke(
            self.clone_b,
            *self.checkpoint_args(),
            "--allow-checkpoint-push",
        )
        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--lease-token",
            self.tokens["run-a"],
            "--state",
            "verified",
        )
        refs = git(
            self.clone_a,
            "ls-remote",
            "origin",
            "refs/heads/agent/claims/issue-408",
            f"refs/heads/{self.branches['run-a']}",
        ).stdout
        self.assertNotIn("agent/claims/issue-408", refs)
        self.assertIn(self.branches["run-a"], refs)
        events = self.events()
        transitions = [
            event["state"] for event in events if event["kind"] == "transition"
        ]
        self.assertEqual(["claimed", "verified"], transitions)
        for event in (item for item in events if item["kind"] == "transition"):
            self.assertEqual(f"agent:{event['state']}", event["added"])
            self.assertNotIn(event["added"], event["removed"])
            self.assertEqual(3, len(event["removed"]))
        comments = [
            event["body"]
            for event in events
            if event["kind"]
            in {"comment", "checkpoint-comment", "release-comment"}
        ]
        self.assertEqual(3, len(comments))
        self.assertTrue(all("retrosharp-agent-state" in body for body in comments))
        self.assertTrue(
            all(self.tokens["run-a"] not in body for body in comments)
        )
        payloads = {
            event["kind"]: canonical_payload(str(event["body"]))
            for event in events
            if event["kind"]
            in {"comment", "checkpoint-comment", "release-comment"}
        }
        claim_id = f"issue-408-{token_fingerprint_for_test(self.tokens['run-a'])}"
        contract_hash = parse(contract(checkpoint_push="allowed")).digest
        for kind in ("checkpoint-comment", "release-comment"):
            self.assertEqual(claim_id, payloads[kind]["claim_id"])
            self.assertEqual(contract_hash, payloads[kind]["contract_sha256"])
        self.assertEqual(claim_id, payloads["comment"]["claim_id"])
        self.assertEqual(contract_hash, payloads["comment"]["contract_sha256"])

    def test_lint_rejects_duplicate_contract_headings_and_keys(self) -> None:
        body = contract().replace(
            "## Owner seam\n\nRemote issue claim gateway\n",
            "## Owner seam\n\nRemote issue claim gateway\n\n"
            "## Issue kind\n\nimplementation\n",
        ).replace(
            "Local commit: allowed",
            "Local commit: allowed\nLocal commit: forbidden",
        ).replace(
            "Model: terra-high",
            "Model: terra-high\nModel: terra-xhigh",
        )
        self.write_fixture(body)
        result = self.invoke(self.clone_a, "lint", "408", expect=20)
        errors = json.loads(result.stdout)["reports"][0]["errors"]
        self.assertIn("duplicate-section:Kind", errors)
        self.assertIn("publication:duplicate-local-commit", errors)
        self.assertIn("dispatch:duplicate-model", errors)

    def test_migration_dry_run_produces_explicit_lint_clean_exemption(self) -> None:
        self.write_fixture("legacy body")
        result = self.invoke(self.clone_a, "migrate", "--all-open", "--dry-run")
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual([], lint(parse(action["body"])))
        self.assertEqual([], self.events())

    def test_migration_repairs_missing_state_for_valid_exemption(self) -> None:
        self.write_fixture(
            render_exemption(source="fixture", reason="Not dispatchable.")
        )
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"] = []
        self.fixture.write_text(json.dumps(data))

        preview = self.invoke(
            self.clone_a, "migrate", "--all-open", "--dry-run"
        )
        action = json.loads(preview.stdout)["actions"][0]
        self.assertEqual("state-only", action["action"])
        self.assertEqual("blocked", action["tracker_state"])
        self.assertNotIn("body", action)

        self.invoke(self.clone_a, "migrate", "--all-open", "--apply")
        self.invoke(self.clone_a, "lint", "--all-open")

    def test_migration_moves_ready_exemption_to_blocked(self) -> None:
        self.write_fixture(
            render_exemption(source="fixture", reason="Not dispatchable.")
        )
        preview = self.invoke(
            self.clone_a, "migrate", "--all-open", "--dry-run"
        )
        action = json.loads(preview.stdout)["actions"][0]
        self.assertEqual("state-only", action["action"])
        self.assertEqual("blocked", action["tracker_state"])

        self.invoke(self.clone_a, "migrate", "--all-open", "--apply")
        issue = json.loads(self.fixture.read_text())["issues"]["408"]
        self.assertEqual([{"name": "agent:blocked"}], issue["labels"])

    def test_migration_moves_ready_task_with_open_dependency_to_blocked(self) -> None:
        self.write_fixture(
            contract(dependencies="- #2"),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        preview = self.invoke(
            self.clone_a, "migrate", "--all-open", "--dry-run"
        )
        action = json.loads(preview.stdout)["actions"][0]
        self.assertEqual("state-only", action["action"])
        self.assertEqual("blocked", action["tracker_state"])

        self.invoke(self.clone_a, "migrate", "--all-open", "--apply")
        self.invoke(self.clone_a, "lint", "--all-open")

    def test_migration_preserves_claimed_and_verified_states(self) -> None:
        self.write_fixture(
            contract(dependencies="- #2"),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        for state in ("claimed", "verified"):
            data = json.loads(self.fixture.read_text())
            data["issues"]["408"]["labels"] = [{"name": f"agent:{state}"}]
            self.fixture.write_text(json.dumps(data))
            preview = self.invoke(
                self.clone_a, "migrate", "--all-open", "--dry-run"
            )
            actions = json.loads(preview.stdout)["actions"]
            self.assertFalse(
                any(action["issue"] == 408 for action in actions)
            )

    def test_migration_repairs_multiple_states_from_open_dependencies(self) -> None:
        self.write_fixture(
            contract(dependencies="- #2"),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"].append({"name": "agent:verified"})
        self.fixture.write_text(json.dumps(data))

        preview = self.invoke(
            self.clone_a, "migrate", "--all-open", "--dry-run"
        )
        action = json.loads(preview.stdout)["actions"][0]
        self.assertEqual("state-only", action["action"])
        self.assertEqual("blocked", action["tracker_state"])
        self.assertEqual([2], action["open_dependencies"])

        self.invoke(self.clone_a, "migrate", "--all-open", "--apply")
        self.invoke(self.clone_a, "lint", "--all-open")

    def test_agent_task_migration_translates_to_dispatchable_contract(self) -> None:
        self.write_fixture(legacy_task_body())
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["title"] = "LEG-1: migrate me"
        data["issues"]["408"]["labels"].extend(
            [{"name": "agent-task"}, {"name": "layer:validation"}]
        )
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(self.clone_a, "migrate", "--all-open", "--dry-run")
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual("translate", action["action"])
        self.assertEqual("ready", action["tracker_state"])
        self.assertEqual([], lint(parse(action["body"])))
        self.assertIn("> ## Kind", action["body"])

    def test_task_migration_fails_closed_when_semantics_are_missing(self) -> None:
        self.write_fixture("## Kind\n\nimplementation\n")
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"].extend(
            [{"name": "agent-task"}, {"name": "layer:validation"}]
        )
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(
            self.clone_a,
            "migrate",
            "--all-open",
            "--dry-run",
            expect=31,
        )
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual("error", action["action"])
        self.assertFalse(self.log.exists())

    def test_task_migration_fails_closed_on_ambiguous_layer_labels(self) -> None:
        self.write_fixture(
            legacy_task_body()
            + "\nThe implementation is target-private and must stay target-private.\n"
        )
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"].extend(
            [
                {"name": "agent-task"},
                {"name": "layer:documentation"},
                {"name": "layer:sdk-2d"},
            ]
        )
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(
            self.clone_a,
            "migrate",
            "--all-open",
            "--dry-run",
            expect=31,
        )
        action = json.loads(result.stdout)["actions"][0]
        self.assertIn("migration:ambiguous-layer-labels", action["errors"])

    def test_template_and_seeder_default_to_blocked_without_editable_policy(self) -> None:
        template = (
            ROOT / ".github" / "ISSUE_TEMPLATE" / "agent-roadmap-task.yml"
        ).read_text()
        seeder = (ROOT / "tools" / "roadmap" / "seed_github_issues.py").read_text()
        self.assertIn("agent:blocked", template)
        self.assertNotIn("id: active_policy", template)
        self.assertIn(
            '["roadmap", "agent-task", "needs-integration", "agent:blocked"]',
            seeder,
        )

    def test_investigation_migration_uses_declared_question_and_resolution(self) -> None:
        body = legacy_task_body()
        body = body.replace(
            "`implementation` in validation tooling.",
            "`investigation` producing bounded evidence.",
        ).replace(
            "## Owner seam\n\nOne legacy seam.\n\n## Single observable\n\nOne legacy observable.\n\n",
            "## Question\n\nWhich seam owns the failure?\n\n"
            "## Resolution contract\n\nName one owner or a bounded discrepancy.\n\n",
        )
        self.write_fixture(body)
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"].extend(
            [{"name": "agent-task"}, {"name": "layer:validation"}]
        )
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(self.clone_a, "migrate", "--all-open", "--dry-run")
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual("translate", action["action"])
        translated = parse(action["body"])
        self.assertEqual("Which seam owns the failure?", translated.sections["Owner seam"])
        self.assertIn("Name one owner", translated.sections["Single observable"])

    def test_applied_fixture_migration_is_all_open_lint_clean_with_blocked_dependency(self) -> None:
        self.write_fixture(
            legacy_task_body(),
            blocked_by=[2],
            dependency_state="OPEN",
        )
        data = json.loads(self.fixture.read_text())
        data["issues"]["408"]["labels"].extend(
            [{"name": "agent-task"}, {"name": "layer:validation"}]
        )
        self.fixture.write_text(json.dumps(data))
        result = self.invoke(self.clone_a, "migrate", "--all-open", "--apply")
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual("translate", action["action"])
        self.assertEqual("blocked", action["tracker_state"])
        self.invoke(self.clone_a, "lint", "--all-open")


if __name__ == "__main__":
    unittest.main()
