#!/usr/bin/env python3
from __future__ import annotations

import argparse
import subprocess
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Variant:
    name: str
    source: Path
    scenarios: tuple[str, ...]
    library_paths: tuple[Path, ...] = ()


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def variants(root: Path) -> list[Variant]:
    sample = root / "samples" / "runner"
    diagnostics = sample / "diagnostics"
    return [
        Variant("00-static-background", diagnostics / "00-static-background.rs", ("idle",)),
        Variant("01-world-platforms", diagnostics / "01-world-platforms.rs", ("idle",)),
        Variant("02-flat-ground-camera", diagnostics / "02-flat-ground-camera.rs", ("idle", "right", "left", "right-wrap")),
        Variant("02-player-camera", diagnostics / "02-player-camera.rs", ("idle", "right", "left", "right-wrap")),
        Variant("03-enemy-sprites", diagnostics / "03-enemy-sprites.rs", ("idle",)),
        Variant("04-full-runner", sample / "runner.retrosharp.json", ("idle", "right", "left")),
    ]


def run(command: list[str], cwd: Path) -> None:
    print("$ " + " ".join(command))
    completed = subprocess.run(command, cwd=cwd, text=True)
    if completed.returncode != 0:
        raise SystemExit(completed.returncode)


def compile_variant(root: Path, variant: Variant, output_dir: Path) -> Path:
    rom = output_dir / f"{variant.name}.gb"
    command = [
        "dotnet",
        "run",
        "--project",
        str(root / "src" / "RetroSharp.Cli" / "RetroSharp.Cli.csproj"),
        "--no-restore",
        "--",
        "--target",
        "gb",
    ]
    for library_path in variant.library_paths:
        command.extend(["--lib-path", str(library_path)])

    command.extend(["--out", str(rom), str(variant.source)])
    run(command, root)
    return rom


def import_pyboy():
    try:
        from pyboy import PyBoy
        from pyboy.utils import WindowEvent
    except ImportError as error:
        print("PyBoy is required for rendering diagnostics.", file=sys.stderr)
        print("Install it in an isolated target directory, for example:", file=sys.stderr)
        print("python3 -m pip install --target /tmp/retrosharp-pyboy-site pyboy pillow", file=sys.stderr)
        print("PYTHONPATH=/tmp/retrosharp-pyboy-site python3 tools/gameboy/runner_diagnostics.py", file=sys.stderr)
        raise SystemExit(2) from error

    return PyBoy, WindowEvent


def tick_scenario(pyboy, window_event, scenario: str, frames: int) -> None:
    scenario_frames = 360 if scenario == "right-wrap" else frames

    if scenario in ("right", "right-wrap"):
        pyboy.send_input(window_event.PRESS_ARROW_RIGHT)
    elif scenario == "left":
        pyboy.send_input(window_event.PRESS_ARROW_LEFT)

    for _ in range(scenario_frames):
        pyboy.tick()

    if scenario in ("right", "right-wrap"):
        pyboy.send_input(window_event.RELEASE_ARROW_RIGHT)
        pyboy.tick()
    elif scenario == "left":
        pyboy.send_input(window_event.RELEASE_ARROW_LEFT)
        pyboy.tick()


def render_rom(rom: Path, scenario: str, screenshot: Path, frames: int) -> dict[str, int]:
    PyBoy, WindowEvent = import_pyboy()
    pyboy = PyBoy(str(rom), window="null")
    try:
        tick_scenario(pyboy, WindowEvent, scenario, frames)
        image = pyboy.screen.image
        image.save(screenshot)
    finally:
        pyboy.stop()

    rgba = image.convert("RGBA")
    pixels = rgba.get_flattened_data() if hasattr(rgba, "get_flattened_data") else rgba.getdata()
    colors = Counter(pixels)
    dark_pixels = sum(
        count
        for (red, green, blue, _alpha), count in colors.items()
        if red <= 85 and green <= 85 and blue <= 85
    )
    mid_pixels = sum(
        count
        for (red, green, blue, _alpha), count in colors.items()
        if 86 <= red <= 170 and 86 <= green <= 170 and 86 <= blue <= 170
    )
    return {
        "unique_colors": len(colors),
        "dark_pixels": dark_pixels,
        "mid_pixels": mid_pixels,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Compile and render Game Boy runner diagnostics.")
    parser.add_argument("--frames", type=int, default=180)
    parser.add_argument(
        "--output",
        type=Path,
        default=repo_root() / "artifacts" / "gameboy-runner-diagnostics",
    )
    args = parser.parse_args()

    root = repo_root()
    output_dir = args.output
    output_dir.mkdir(parents=True, exist_ok=True)

    for variant in variants(root):
        print(f"== {variant.name} ==")
        rom = compile_variant(root, variant, output_dir)
        for scenario in variant.scenarios:
            screenshot = output_dir / f"{variant.name}-{scenario}.png"
            metrics = render_rom(rom, scenario, screenshot, args.frames)
            print(
                f"{screenshot}: "
                f"unique_colors={metrics['unique_colors']} "
                f"dark_pixels={metrics['dark_pixels']} "
                f"mid_pixels={metrics['mid_pixels']}"
            )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
