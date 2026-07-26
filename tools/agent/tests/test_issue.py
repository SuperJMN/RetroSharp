#!/usr/bin/env python3
from __future__ import annotations

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

from issue_contract import lint, parse  # noqa: E402
from issue_gateway import GitClaimStore  # noqa: E402


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
        return self.invoke(
            clone,
            "claim",
            "408",
            "--run-id",
            run_id,
            *extra,
            expect=expect,
        )

    def worktree(self, run_id: str = "run-a") -> Path:
        destination = self.root / f"work-{run_id}"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--run-id",
            run_id,
            str(destination),
        )
        return destination

    def checkpoint_args(self, run_id: str = "run-a") -> list[str]:
        return [
            "checkpoint",
            "408",
            "--run-id",
            run_id,
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
            ("run-a", "run-b"),
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

    def test_native_parent_and_dependency_mismatch_fails(self) -> None:
        self.write_fixture(contract(dependencies="- #2"), parent=9, blocked_by=[])
        result = self.invoke(self.clone_a, "lint", "408", expect=28)
        report = json.loads(result.stdout)["reports"][0]
        self.assertEqual(2, len(report["native_errors"]))

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

    def test_sol_max_requires_explicit_escalation_justification(self) -> None:
        self.write_fixture(contract(model="sol-max"))
        self.invoke(self.clone_a, "lint", "408", expect=20)
        self.write_fixture(
            contract(model="sol-max", justification="Detector exhausted terra-xhigh.")
        )
        self.invoke(self.clone_a, "lint", "408")

    def test_contract_change_prevents_worktree_creation(self) -> None:
        self.claim(self.clone_a)
        self.write_fixture(contract().replace("Exactly one", "A changed"))
        destination = self.root / "never-created"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--run-id",
            "run-a",
            str(destination),
            expect=24,
        )
        self.assertFalse(destination.exists())

    def test_worktree_without_claim_is_denied_before_mutation(self) -> None:
        destination = self.root / "never-created"
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--run-id",
            "missing",
            str(destination),
            expect=25,
        )
        self.assertFalse(destination.exists())

    def test_origin_master_advance_invalidates_claim(self) -> None:
        self.claim(self.clone_a)
        (self.clone_b / "advance.txt").write_text("advance\n")
        git(self.clone_b, "add", "advance.txt")
        git(self.clone_b, "commit", "-m", "advance")
        git(self.clone_b, "push", "origin", "master")
        self.invoke(
            self.clone_a,
            "worktree",
            "408",
            "--run-id",
            "run-a",
            str(self.root / "never-created"),
            expect=24,
        )

    def test_expired_claim_has_stable_failure(self) -> None:
        self.claim(self.clone_a)
        store = GitClaimStore(self.clone_a)
        claim_sha, record = store.read(408)
        record["expires_at"] = "2000-01-01T00:00:00Z"
        store.update(408, claim_sha, record)
        self.invoke(
            self.clone_b,
            "release",
            "408",
            "--run-id",
            "run-a",
            "--state",
            "blocked",
            expect=23,
        )

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
            "--run-id",
            "run-a",
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
            "--run-id",
            "run-a",
            "--state",
            "verified",
        )
        refs = git(
            self.clone_a,
            "ls-remote",
            "origin",
            "refs/heads/agent/claims/issue-408",
            "refs/heads/agent/work/issue-408-run-a",
        ).stdout
        self.assertNotIn("agent/claims/issue-408", refs)
        self.assertIn("agent/work/issue-408-run-a", refs)
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
            if event["kind"] in {"comment", "checkpoint-comment"}
        ]
        self.assertEqual(3, len(comments))
        self.assertTrue(all("retrosharp-agent-state" in body for body in comments))

    def test_migration_dry_run_produces_explicit_lint_clean_exemption(self) -> None:
        self.write_fixture("legacy body")
        result = self.invoke(self.clone_a, "migrate", "--all-open", "--dry-run")
        action = json.loads(result.stdout)["actions"][0]
        self.assertEqual([], lint(parse(action["body"])))
        self.assertEqual([], self.events())

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
