#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
CLI = ROOT / "tools" / "agent" / "issue.py"

VALID = """## Schema

aex-1

## Kind

implementation

## Parent

#1

## Dependencies

None

## Layer

validation

## Target

none

## Owner seam

Issue lease

## Single observable

One winner owns the issue.

## No-goals

No production edits.

## Exact RED

python3 tools/agent/issue.py claim 408 --run-id red-a

## Verification

python3 tools/agent/tests/test_issue.py

## Publication authority

No push, PR, or merge.

## Dispatch metadata

Model: terra-high
Effort: high

## Handoff destination

Integrator #1.

## Active engineering policy

90-minute checkpoint / 120-minute hard stop
"""


class IssueCliTests(unittest.TestCase):
    def invoke(self, *args: str, cwd: Path, expect: int = 0) -> subprocess.CompletedProcess[str]:
        result = subprocess.run([sys.executable, str(CLI), *args], cwd=ROOT, text=True, capture_output=True)
        self.assertEqual(expect, result.returncode, result.stderr + result.stdout)
        return result

    def test_rejects_incomplete_contract_with_stable_code(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); issue = root / "issue.md"; issue.write_text("## Kind\n\nimplementation\n")
            self.invoke("lint", "408", "--issue-file", str(issue), cwd=root, expect=20)

    def test_concurrent_claims_have_one_winner(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); issue = root / "issue.md"; state = root / "claims"; issue.write_text(VALID)
            commands = [[sys.executable, str(CLI), "claim", "408", "--run-id", run_id, "--issue-file", str(issue), "--state-dir", str(state)] for run_id in ("a", "b")]
            processes = [subprocess.Popen(command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE) for command in commands]
            results = [process.communicate() for process in processes]
            codes = sorted(process.returncode for process in processes)
            self.assertEqual([0, 22], codes)
            self.assertTrue(all(not stderr for _, stderr in results))
            self.assertTrue((state / "issue-408.json").exists())

    def test_open_dependency_rejects_claim_with_stable_code(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); issue = root / "issue.md"; states = root / "states.json"
            issue.write_text(VALID.replace("None\n\n## Layer", "- #2\n\n## Layer"))
            states.write_text(json.dumps({"2": {"state": "OPEN"}}))
            self.invoke("claim", "408", "--run-id", "a", "--issue-file", str(issue), "--state-dir", str(root / "claims"), "--dependency-state-file", str(states), cwd=root, expect=21)

    def test_contract_change_invalidates_worktree_before_mutation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); issue = root / "issue.md"; state = root / "claims"; destination = root / "never-created"; issue.write_text(VALID)
            self.invoke("claim", "408", "--run-id", "a", "--issue-file", str(issue), "--state-dir", str(state), cwd=root)
            issue.write_text(VALID.replace("One winner", "Changed winner"))
            self.invoke("worktree", "408", "--run-id", "a", "--issue-file", str(issue), "--state-dir", str(state), str(destination), cwd=root, expect=24)
            self.assertFalse(destination.exists())

    def test_expired_claim_can_not_be_used(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); issue = root / "issue.md"; state = root / "claims"; issue.write_text(VALID)
            state.mkdir(); (state / "issue-408.json").write_text(json.dumps({"issue": 408, "run_id": "a", "contract_sha256": "x", "base": "x", "expires_at": "2000-01-01T00:00:00Z"}))
            self.invoke("release", "408", "--run-id", "a", "--issue-file", str(issue), "--state-dir", str(state), "--state", "blocked", cwd=root, expect=23)


if __name__ == "__main__":
    unittest.main()
