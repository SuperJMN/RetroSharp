#!/usr/bin/env python3
"""Regenerate tracked RetroSharp sample ROMs."""

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
    target: str
    output: Path
    library_paths: tuple[Path, ...] = ()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Regenerate tracked RetroSharp sample ROMs.",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="build every manifest sample for every declared target, including diagnostics and cross-target samples",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="print the commands without running them",
    )
    parser.add_argument(
        "samples",
        nargs="*",
        help="optional sample .rs or .retrosharp.json paths to build instead of the default tracked ROM outputs",
    )
    args = parser.parse_args(argv)

    repo_root = Path(__file__).resolve().parents[2]
    builds = select_builds(repo_root, args.all, args.samples)
    if not builds:
        print("No sample ROMs selected.", file=sys.stderr)
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
            build.target,
        ]
        for library_path in build.library_paths:
            command.extend(["--lib-path", str(library_path.relative_to(repo_root))])

        command.extend(
            [
                "--out",
                str(build.output.relative_to(repo_root)),
                str(build.source.relative_to(repo_root)),
            ]
        )
        print(shell_join(command), flush=True)
        if not args.dry_run:
            subprocess.run(command, cwd=repo_root, check=True)

    return 0


def select_builds(repo_root: Path, include_all: bool, requested_samples: list[str]) -> list[SampleBuild]:
    manifest = load_manifest(repo_root)
    manifest_builds = [
        SampleBuild(
            source=repo_root / sample["path"],
            target=target,
            output=sample_output_path(repo_root / sample["path"], target),
            library_paths=tuple(repo_root / library_path for library_path in sample.get("libraryPaths", [])),
        )
        for sample in manifest["samples"]
        for target in sample.get("targets", [])
    ]

    if requested_samples:
        requested = {normalize_sample_path(repo_root, sample) for sample in requested_samples}
        known_sources = {build.source for build in manifest_builds}
        unknown = sorted(requested - known_sources)
        if unknown:
            known = "\n".join(str(path.relative_to(repo_root)) for path in sorted(known_sources))
            raise SystemExit(
                "Unknown sample(s): "
                + ", ".join(str(path.relative_to(repo_root)) for path in unknown)
                + "\nKnown samples:\n"
                + known
            )

        builds = [build for build in manifest_builds if build.source in requested]
    elif include_all:
        builds = manifest_builds
    else:
        tracked_files = load_tracked_files(repo_root)
        if tracked_files is None:
            builds = [build for build in manifest_builds if build.output.exists()]
        else:
            builds = [
                build
                for build in manifest_builds
                if build.output.relative_to(repo_root).as_posix() in tracked_files
                or project_declared_output(build.source, build.target) is not None
            ]

    return builds


def target_extension(target: str) -> str:
    if target == "gb":
        return ".gb"
    if target == "nes":
        return ".nes"

    raise SystemExit(f"Unsupported sample target '{target}'.")


def sample_output_path(source: Path, target: str) -> Path:
    project_output = project_declared_output(source, target)
    if project_output is not None:
        return project_output

    extension = target_extension(target)
    suffix = ".retrosharp.json"
    if source.name.endswith(suffix):
        return source.with_name(source.name[: -len(suffix)] + extension)

    return source.with_suffix(extension)


def project_declared_output(source: Path, target: str) -> Path | None:
    suffix = ".retrosharp.json"
    if not source.name.endswith(suffix):
        return None

    with source.open(encoding="utf-8") as stream:
        project = json.load(stream)

    outputs = project.get("outputs")
    if isinstance(outputs, dict):
        output = outputs.get(target)
        if isinstance(output, str) and output:
            return resolve_project_path(source, output)

    output = project.get("output") or project.get("outputPath")
    if isinstance(output, str) and output and project_declares_single_target(project, target):
        return resolve_project_path(source, output)

    return None


def project_declares_single_target(project: dict, target: str) -> bool:
    project_target = project.get("target")
    if isinstance(project_target, str):
        return project_target == target

    targets = project.get("targets")
    if isinstance(targets, list):
        return len(targets) == 1 and targets[0] == target

    return False


def resolve_project_path(project_path: Path, value: str) -> Path:
    path = Path(value)
    if path.is_absolute():
        return path

    return project_path.parent / path


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
