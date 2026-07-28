"""Versioned, machine-checkable contracts for autonomous-agent issues."""

from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass


SCHEMA_VERSION = "aex-1"
KINDS = {"implementation", "certification-gate", "investigation", "epic/integrator"}
LAYERS = {"language", "sdk-2d", "target-intrinsic", "documentation", "validation"}
TARGETS = {"none", "gb", "nes", "gb+nes"}
POLICY = (
    "90-minute checkpoint / 120-minute hard stop / "
    "two consecutive no-gain experiments stop / "
    "gameplay perceptual terminal stops refinement"
)
REQUIRED = (
    "Kind",
    "Parent",
    "Dependencies",
    "Layer",
    "Target",
    "Owner seam",
    "Single observable",
    "No-goals",
    "Exact RED",
    "Verification",
    "Publication authority",
    "Dispatch metadata",
    "Handoff destination",
)


@dataclass(frozen=True)
class PublicationAuthority:
    local_commit: bool
    checkpoint_push: bool
    pull_request: bool
    merge: bool

    def as_dict(self) -> dict[str, bool]:
        return {
            "local_commit": self.local_commit,
            "checkpoint_push": self.checkpoint_push,
            "pull_request": self.pull_request,
            "merge": self.merge,
        }


@dataclass(frozen=True)
class DispatchMetadata:
    model: str
    effort: str
    escalation_justification: str | None

    def as_dict(self) -> dict[str, str | None]:
        return {
            "model": self.model,
            "effort": self.effort,
            "escalation_justification": self.escalation_justification,
        }


@dataclass(frozen=True)
class Contract:
    sections: dict[str, str]
    body: str
    parse_errors: tuple[str, ...] = ()

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
    parse_errors: list[str] = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(body)
        title = match.group(1).strip()
        canonical_title = _FORM_ALIASES.get(title.lower(), title)
        if canonical_title in sections:
            parse_errors.append(f"duplicate-section:{canonical_title}")
            continue
        sections[canonical_title] = body[match.end():end].strip()
    return Contract(sections, body, tuple(sorted(set(parse_errors))))


def lint(contract: Contract) -> list[str]:
    errors = list(contract.parse_errors)
    if contract.exemption:
        if contract.sections.get("Schema") != SCHEMA_VERSION:
            errors.append("schema:expected-aex-1")
        return sorted(set(errors))

    errors.extend(
        f"missing:{name}" for name in REQUIRED if not contract.sections.get(name)
    )
    if contract.sections.get("Schema") != SCHEMA_VERSION:
        errors.append("schema:expected-aex-1")

    kind = value(contract, "Kind")
    if kind and kind not in KINDS:
        errors.append("kind:invalid")
    layer = value(contract, "Layer")
    if layer and layer not in LAYERS:
        errors.append("layer:invalid")
    target = value(contract, "Target")
    if target and target not in TARGETS:
        errors.append("target:invalid")

    parent = contract.sections.get("Parent", "").strip()
    if parent and parent != "None" and not re.fullmatch(r"#\d+", parent):
        errors.append("parent:must-use-native-issue-number-or-none")
    if kind == "implementation" and not re.fullmatch(r"#\d+", parent):
        errors.append("parent:implementation-requires-native-parent")

    dependency_text = contract.sections.get("Dependencies", "").strip()
    if dependency_text and dependency_text != "None":
        lines = [line.strip() for line in dependency_text.splitlines() if line.strip()]
        if not lines or any(not re.fullmatch(r"-\s*#\d+", line) for line in lines):
            errors.append("dependencies:must-use-native-issue-numbers")

    authority, authority_errors = publication_authority(contract)
    errors.extend(authority_errors)
    metadata, metadata_errors = dispatch_metadata(contract)
    errors.extend(metadata_errors)
    if authority and (authority.pull_request or authority.merge):
        errors.append("publication:pr-and-merge-must-remain-forbidden")
    if metadata and metadata.model == "sol-max" and not metadata.escalation_justification:
        errors.append("dispatch:sol-max-requires-justification")
    return sorted(set(errors))


def parent_number(contract: Contract) -> int | None:
    match = re.fullmatch(r"#(\d+)", contract.sections.get("Parent", "").strip())
    return int(match.group(1)) if match else None


def dependencies(contract: Contract) -> list[int]:
    return [
        int(number)
        for number in re.findall(
            r"(?m)^-\s*#(\d+)\s*$", contract.sections.get("Dependencies", "").strip()
        )
    ]


def publication_authority(
    contract: Contract,
) -> tuple[PublicationAuthority | None, list[str]]:
    section = contract.sections.get("Publication authority", "")
    fields, duplicate_fields = _key_values(section)
    required = ("local commit", "checkpoint push", "pull request", "merge")
    errors = [
        f"publication:duplicate-{field.replace(' ', '-')}"
        for field in duplicate_fields
    ] + [
        f"publication:missing-{name.replace(' ', '-')}" for name in required if name not in fields
    ]
    for name in required:
        if name in fields and fields[name].lower() not in {"allowed", "forbidden"}:
            errors.append(f"publication:{name.replace(' ', '-')}-must-be-allowed-or-forbidden")
    if errors:
        return None, errors
    return PublicationAuthority(
        local_commit=fields["local commit"].lower() == "allowed",
        checkpoint_push=fields["checkpoint push"].lower() == "allowed",
        pull_request=fields["pull request"].lower() == "allowed",
        merge=fields["merge"].lower() == "allowed",
    ), []


def dispatch_metadata(contract: Contract) -> tuple[DispatchMetadata | None, list[str]]:
    fields, duplicate_fields = _key_values(
        contract.sections.get("Dispatch metadata", "")
    )
    errors = [
        f"dispatch:duplicate-{field.replace(' ', '-')}"
        for field in duplicate_fields
    ]
    model = fields.get("model", "").lower()
    effort = fields.get("effort", "").lower()
    if model not in {"terra-high", "terra-xhigh", "sol-max"}:
        errors.append("dispatch:model-invalid")
    if effort not in {"low", "medium", "high", "xhigh", "max"}:
        errors.append("dispatch:effort-invalid")
    if errors:
        return None, errors
    justification = fields.get("escalation justification", "").strip() or None
    return DispatchMetadata(model, effort, justification), []


def render_exemption(*, source: str, reason: str, legacy_body: str | None = None) -> str:
    result = f"""## Schema

{SCHEMA_VERSION}

## Dispatch exemption

{reason}

## Source

{source}

## Active engineering policy

{POLICY}
"""
    if legacy_body:
        quoted = "\n".join(f"> {line}" for line in legacy_body.strip().splitlines())
        result += "\n## Legacy body\n\n" + quoted + "\n"
    return result


def translate_legacy_task(
    issue: dict[str, object],
    *,
    native_parent: int | None,
    native_dependencies: list[dict[str, object]],
) -> tuple[str | None, list[str]]:
    """Translate an issue-shaped legacy task without inventing semantic scope."""

    legacy_body = str(issue.get("body", ""))
    legacy = parse(legacy_body)
    kind_text = legacy.sections.get("Kind", "")
    kind_match = re.search(
        r"\b(implementation|certification-gate|investigation|epic/integrator)\b",
        kind_text,
        flags=re.IGNORECASE,
    )
    kind = kind_match.group(1).lower() if kind_match else ""
    labels = {
        str(item.get("name", ""))
        for item in issue.get("labels", [])  # type: ignore[union-attr]
        if isinstance(item, dict)
    }
    layer = _migration_layer(kind, labels, kind_text, legacy_body)
    target = _migration_target(labels)
    owner = legacy.sections.get("Owner seam", "")
    observable = legacy.sections.get("Single observable", "")
    if kind == "investigation":
        owner = owner or legacy.sections.get("Question", "")
        observable = observable or legacy.sections.get("Resolution contract", "")
    no_goals = legacy.sections.get("No-goals") or legacy.sections.get("Non-goals", "")
    red = (
        legacy.sections.get("Exact RED")
        or legacy.sections.get("Exact RED contract")
        or legacy.sections.get("Current RED")
        or legacy.sections.get("Question", "")
    )
    verification = (
        legacy.sections.get("Verification")
        or legacy.sections.get("Acceptance")
        or legacy.sections.get("Required evidence", "")
    )
    errors: list[str] = []
    for field, value_text in (
        ("kind", kind),
        ("layer", layer or ""),
        ("owner-seam", owner),
        ("single-observable", observable),
        ("no-goals", no_goals),
        ("exact-red", red),
        ("verification", verification),
    ):
        if not value_text.strip():
            errors.append(f"migration:missing-{field}")
    if layer is None:
        errors = [
            error for error in errors if error != "migration:missing-layer"
        ]
        errors.append("migration:ambiguous-layer-labels")
    if kind == "implementation" and native_parent is None:
        errors.append("migration:implementation-missing-native-parent")
    if errors:
        return None, sorted(errors)

    dependency_text = (
        "\n".join(
            f"- #{int(item['number'])}"
            for item in sorted(native_dependencies, key=lambda item: int(item["number"]))
        )
        or "None"
    )
    model = "terra-xhigh" if _is_detector(issue, legacy_body) else "terra-high"
    effort = "xhigh" if model == "terra-xhigh" else "high"
    parent_text = f"#{native_parent}" if native_parent is not None else "None"
    quoted = "\n".join(f"> {line}" for line in legacy_body.strip().splitlines())
    body = f"""## Schema

{SCHEMA_VERSION}

## Kind

{kind}

## Parent

{parent_text}

## Dependencies

{dependency_text}

## Layer

{layer}

## Target

{target}

## Owner seam

{owner.strip()}

## Single observable

{observable.strip()}

## No-goals

{no_goals.strip()}

## Exact RED

{red.strip()}

## Verification

{verification.strip()}

## Publication authority

Local commit: allowed
Checkpoint push: forbidden
Pull request: forbidden
Merge: forbidden

## Dispatch metadata

Model: {model}
Effort: {effort}

## Handoff destination

Parent issue {parent_text}; preserve the claimed branch, worktree and canonical checkpoint.

## Active engineering policy

{POLICY}

## Legacy body

{quoted}
"""
    translated = parse(body)
    translated_errors = lint(translated)
    if translated_errors:
        return None, [f"migration:translated:{error}" for error in translated_errors]
    return body, []


def value(contract: Contract, name: str) -> str:
    return contract.sections.get(name, "").strip().strip("`").lower()


def _key_values(section: str) -> tuple[dict[str, str], list[str]]:
    result: dict[str, str] = {}
    duplicates: list[str] = []
    for line in section.splitlines():
        match = re.fullmatch(r"\s*([^:]+):\s*(.+?)\s*", line)
        if match:
            key = match.group(1).strip().lower()
            if key in result:
                duplicates.append(key)
                continue
            result[key] = match.group(2).strip()
    return result, sorted(set(duplicates))


def _migration_layer(
    kind: str,
    labels: set[str],
    kind_text: str,
    body: str,
) -> str | None:
    lowered = f"{kind_text}\n{body}".lower()
    declared = {
        candidate.removeprefix("layer:")
        for candidate in labels
        if candidate.startswith("layer:")
    }
    if len(declared) > 1:
        return None
    if kind in {"investigation", "certification-gate"} or any(
        phrase in lowered
        for phrase in (
            "no production code may change",
            "does not authorize production",
            "test locality",
            "validation automation",
            "process/validation",
            "release automation",
        )
    ):
        return "validation"
    if "target-private" in lowered or "target intrinsic" in lowered:
        return "target-intrinsic"
    if declared:
        return next(iter(declared))
    return ""


def _migration_target(labels: set[str]) -> str:
    gb = "target:gb" in labels
    nes = "target:nes" in labels
    if gb and nes:
        return "gb+nes"
    if gb:
        return "gb"
    if nes:
        return "nes"
    return "none"


def _is_detector(issue: dict[str, object], body: str) -> bool:
    title = str(issue.get("title", "")).lower()
    lowered = body.lower()
    return "detector" in title or "creates the detector only" in lowered


_FORM_ALIASES = {
    "issue kind": "Kind",
    "kind": "Kind",
    "parent": "Parent",
    "dependencies": "Dependencies",
    "layer": "Layer",
    "target": "Target",
    "owner seam": "Owner seam",
    "single observable": "Single observable",
    "no-goals": "No-goals",
    "exact red": "Exact RED",
    "verification": "Verification",
    "publication authority": "Publication authority",
    "dispatch metadata": "Dispatch metadata",
    "handoff destination": "Handoff destination",
}
