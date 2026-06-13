#!/usr/bin/env python3
"""Regenerate Game Boy ROMs for RetroSharp samples."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class SampleBuild:
    source: Path
    output: Path


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Regenerate .gb ROMs for RetroSharp Game Boy samples.",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="build every manifest sample that declares the gb target, including diagnostics and cross-target samples",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="print the commands without running them",
    )
    parser.add_argument(
        "samples",
        nargs="*",
        help="optional sample .rs paths to build instead of the default tracked .gb outputs",
    )
    args = parser.parse_args(argv)

    repo_root = Path(__file__).resolve().parents[2]
    builds = select_builds(repo_root, args.all, args.samples)
    if not builds:
        print("No Game Boy sample ROMs selected.", file=sys.stderr)
        return 1

    cli_project = repo_root / "src" / "RetroSharp.Cli" / "RetroSharp.Cli.csproj"
    for build in builds:
        command = [
            "dotnet",
            "run",
            "--project",
            str(cli_project.relative_to(repo_root)),
            "--no-launch-profile",
            "--",
            "--target",
            "gb",
            "--out",
            str(build.output.relative_to(repo_root)),
            str(build.source.relative_to(repo_root)),
        ]
        print(shell_join(command), flush=True)
        if not args.dry_run:
            subprocess.run(command, cwd=repo_root, check=True)

    return 0


def select_builds(repo_root: Path, include_all: bool, requested_samples: list[str]) -> list[SampleBuild]:
    manifest = load_manifest(repo_root)
    gb_sources = [
        repo_root / sample["path"]
        for sample in manifest["samples"]
        if "gb" in sample.get("targets", [])
    ]

    if requested_samples:
        requested = {normalize_sample_path(repo_root, sample) for sample in requested_samples}
        unknown = sorted(requested - set(gb_sources))
        if unknown:
            known = "\n".join(str(path.relative_to(repo_root)) for path in gb_sources)
            raise SystemExit(
                "Unknown Game Boy sample(s): "
                + ", ".join(str(path.relative_to(repo_root)) for path in unknown)
                + "\nKnown Game Boy samples:\n"
                + known
            )

        sources = [source for source in gb_sources if source in requested]
    elif include_all:
        sources = gb_sources
    else:
        tracked_files = load_tracked_files(repo_root)
        if tracked_files is None:
            sources = [source for source in gb_sources if source.with_suffix(".gb").exists()]
        else:
            sources = [
                source
                for source in gb_sources
                if source.with_suffix(".gb").relative_to(repo_root).as_posix() in tracked_files
            ]

    return [SampleBuild(source=source, output=source.with_suffix(".gb")) for source in sources]


def load_manifest(repo_root: Path) -> dict:
    manifest_path = repo_root / "samples" / "manifest.json"
    with manifest_path.open(encoding="utf-8") as stream:
        return json.load(stream)


def load_tracked_files(repo_root: Path) -> set[str] | None:
    try:
        result = subprocess.run(
            ["git", "ls-files", "--", "samples"],
            cwd=repo_root,
            check=True,
            capture_output=True,
            text=True,
        )
    except (FileNotFoundError, subprocess.CalledProcessError):
        return None

    return {line for line in result.stdout.splitlines() if line}


def normalize_sample_path(repo_root: Path, sample: str) -> Path:
    path = Path(sample)
    if not path.is_absolute():
        path = repo_root / path

    return path.resolve()


def shell_join(command: list[str]) -> str:
    return " ".join(shell_quote(part) for part in command)


def shell_quote(value: str) -> str:
    if value and all(ch.isalnum() or ch in "/._=-" for ch in value):
        return value

    return "'" + value.replace("'", "'\"'\"'") + "'"


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
