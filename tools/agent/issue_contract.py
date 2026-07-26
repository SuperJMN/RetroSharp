"""Versioned, machine-checkable contracts for autonomous-agent issues."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass


SCHEMA_VERSION = "aex-1"
KINDS = {"implementation", "certification-gate", "investigation", "epic/integrator"}
LAYERS = {"language", "sdk-2d", "target-intrinsic", "documentation", "validation"}
TARGETS = {"none", "gb", "nes", "gb+nes"}
REQUIRED = (
    "Kind", "Parent", "Dependencies", "Layer", "Target", "Owner seam",
    "Single observable", "No-goals", "Exact RED", "Verification",
    "Publication authority", "Dispatch metadata", "Handoff destination",
)
POLICY = "90-minute checkpoint / 120-minute hard stop"


@dataclass(frozen=True)
class Contract:
    sections: dict[str, str]
    body: str

    @property
    def digest(self) -> str:
        canonical = "\n".join(
            f"{name}:{self.sections.get(name, '').strip()}" for name in sorted(self.sections)
        )
        return hashlib.sha256(canonical.encode()).hexdigest()

    @property
    def exemption(self) -> str | None:
        return self.sections.get("Dispatch exemption")


def parse(body: str) -> Contract:
    matches = list(re.finditer(r"(?m)^#{2,3} ([^\n]+)\s*$", body))
    sections: dict[str, str] = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(body)
        title = match.group(1).strip()
        sections[_FORM_ALIASES.get(title.lower(), title)] = body[match.end():end].strip()
    return Contract(sections, body)


def lint(contract: Contract) -> list[str]:
    if contract.exemption:
        return []
    errors = [f"missing:{name}" for name in REQUIRED if not contract.sections.get(name)]
    if not errors and contract.sections.get("Schema") != SCHEMA_VERSION:
        errors.append("schema:expected-aex-1")
    kind = _value(contract, "Kind")
    if kind and kind not in KINDS:
        errors.append("kind:invalid")
    layer = _value(contract, "Layer")
    if layer and layer not in LAYERS:
        errors.append("layer:invalid")
    target = _value(contract, "Target")
    if target and target not in TARGETS:
        errors.append("target:invalid")
    dispatch = contract.sections.get("Dispatch metadata", "")
    if dispatch and not re.search(r"(?i)model:\s*(terra-high|terra-xhigh|sol-max)", dispatch):
        errors.append("dispatch:model-required")
    if dispatch and not re.search(r"(?i)effort:\s*(low|medium|high|xhigh|max)", dispatch):
        errors.append("dispatch:effort-required")
    parent = contract.sections.get("Parent", "").strip()
    if kind == "implementation" and not re.fullmatch(r"#\d+", parent):
        errors.append("parent:implementation-requires-native-parent")
    dependencies = contract.sections.get("Dependencies", "").strip()
    if dependencies and dependencies != "None":
        for dependency in re.findall(r"(?m)^-\s*(.+)$", dependencies):
            if not re.fullmatch(r"#\d+", dependency.strip()):
                errors.append("dependencies:must-use-native-issue-numbers")
                break
    if POLICY not in contract.body:
        errors.append("policy:90-120-required")
    return sorted(set(errors))


def dependencies(contract: Contract) -> list[int]:
    value = contract.sections.get("Dependencies", "").strip()
    return [int(number) for number in re.findall(r"(?m)^-\s*#(\d+)\s*$", value)]


def render_exemption(*, source: str, reason: str) -> str:
    return f"""## Schema

{SCHEMA_VERSION}

## Dispatch exemption

{reason}

## Source

{source}

## Active engineering policy

{POLICY}
"""


def _value(contract: Contract, name: str) -> str:
    return contract.sections.get(name, "").strip().strip("`").lower()


_FORM_ALIASES = {
    "issue kind": "Kind", "kind": "Kind", "parent": "Parent", "dependencies": "Dependencies",
    "layer": "Layer", "target": "Target", "owner seam": "Owner seam",
    "single observable": "Single observable", "no-goals": "No-goals", "exact red": "Exact RED",
    "verification": "Verification", "publication authority": "Publication authority",
    "dispatch metadata": "Dispatch metadata", "handoff destination": "Handoff destination",
}
