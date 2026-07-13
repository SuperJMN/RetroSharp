#!/usr/bin/env python3
"""Run the tracked NES runner under FCEUmm with deterministic CPU RAM patterns."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from tools.nes.runner_visual_parity import RetroArchNetworkSession as IsolatedRetroArchSession


DEFAULT_ROM = ROOT / "samples" / "runner" / "bin" / "runner.nes"
DEFAULT_CORE = (
    Path.home()
    / ".var"
    / "app"
    / "org.libretro.RetroArch"
    / "config"
    / "retroarch"
    / "cores"
    / "fceumm_libretro.so"
)

PLAYER_X_LOW = 0x0000
PLAYER_X_HIGH = 0x0001
PLAYER_Y_LOW = 0x0002
PLAYER_Y_HIGH = 0x0003
REQUESTED_CAMERA_X_LOW = 0x00E0
REQUESTED_CAMERA_X_HIGH = 0x0318
FRAME_COUNTER_LOW = 0x036E
REQUEST_COUNT = 0x0370
PREPARE_COUNT = 0x0371
RESIDENT_COUNT = 0x0372
COMMIT_COUNT = 0x0373
RELEASE_COUNT = 0x0374
BANK_WORK_IN_COMMIT = 0x0375
DIRECTORY_WORK_IN_COMMIT = 0x0376
DECODE_WORK_IN_COMMIT = 0x0377
LAST_TILE_WRITES = 0x0378
LAST_ATTRIBUTE_WRITES = 0x0379
CRITICAL_SECTION = 0x0380
SELECTED_SLOT = 0x0381
FRAME_PENDING = 0x038F
SLOT0 = 0x0390
SLOT1 = 0x03A0
PENDING_AXES = 0x03CA
VISIBLE_CAMERA_X_LOW = 0x03CB
VISIBLE_CAMERA_X_HIGH = 0x03CC
WORLD_PACK_VISUAL_CACHE0_VALID = 0x03B8
WORLD_PACK_BULK_READ_CURRENT_BANK = 0x03C2
WORLD_PACK_COLLISION_CACHE0_VALID = 0x03F1
COLLISION_DECODE_COUNT_LOW = 0x03F8
GAMEPLAY_TICK_COUNT = 0x03FA
AUDIO_TICK_COUNT = 0x03FB
WORLD_PACK_VALIDATION_STATE = 0x0326
BULK_READ_ACTIVE = 0x03C0
PACKED_STATUS = 0x03B3

EMPTY = 0
RELEASED = 5
NO_SLOT = 0xFF
SETTLE_FRAMES = 500
MEASURED_FRAMES = 120


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Boot the production NES runner in FCEUmm with $00, $FF, and a "
            "deterministic nonzero CPU RAM pattern, then verify scheduler/camera cadence."
        )
    )
    parser.add_argument("--rom", type=Path, default=DEFAULT_ROM)
    parser.add_argument("--core", type=Path, default=DEFAULT_CORE)
    parser.add_argument(
        "--retroarch-command",
        help="Command used to launch RetroArch (shell-style string).",
    )
    parser.add_argument(
        "--retroarch-default-config-command",
        default="flatpak run --command=cat org.libretro.RetroArch /app/etc/retroarch.cfg",
        help="Command that prints the immutable base config copied into the disposable session.",
    )
    parser.add_argument(
        "--artifacts",
        type=Path,
        default=ROOT / "artifacts" / "nes-runner-power-on",
    )
    parser.add_argument(
        "--patterns",
        nargs="+",
        choices=["fill-00", "fill-ff", "pattern-a5"],
        default=["fill-00", "fill-ff", "pattern-a5"],
    )
    return parser.parse_args()


def default_retroarch_command() -> list[str]:
    executable = shutil.which("retroarch")
    if executable:
        return [executable]
    if shutil.which("flatpak"):
        return [
            "flatpak",
            "run",
            "--command=retroarch",
            "org.libretro.RetroArch",
        ]
    raise RuntimeError("RetroArch was not found in PATH and Flatpak is unavailable.")


def delta16(before: int, after: int) -> int:
    return (after - before) & 0xFFFF


def delta8(before: int, after: int) -> int:
    return (after - before) & 0xFF


def deterministic_pattern(seed: int) -> bytes:
    return bytes((((address * 73) ^ (address >> 3) ^ seed) & 0xFF) for address in range(0x800))


def word(
    session: IsolatedRetroArchSession,
    low_address: int,
    high_address: int | None = None,
) -> int:
    if high_address is None:
        low, high = session.read(low_address, 2)
    else:
        low = session.read(low_address, 1)[0]
        high = session.read(high_address, 1)[0]
    return low | high << 8


def snapshot(session: IsolatedRetroArchSession) -> dict[str, object]:
    counters = session.read(REQUEST_COUNT, RELEASE_COUNT - REQUEST_COUNT + 1)
    visual_slots = [bytes(session.read(0x0400 + index * 64, 64)) for index in range(6)]
    return {
        "hardware_frames": session.frame_counter(),
        "gameplay_ticks": session.read(GAMEPLAY_TICK_COUNT, 1)[0],
        "audio_ticks": session.read(AUDIO_TICK_COUNT, 1)[0],
        "player_x": word(session, PLAYER_X_LOW, PLAYER_X_HIGH),
        "player_y": word(session, PLAYER_Y_LOW, PLAYER_Y_HIGH),
        "requested_camera_x": word(
            session,
            REQUESTED_CAMERA_X_LOW,
            REQUESTED_CAMERA_X_HIGH,
        ),
        "visible_camera_x": word(
            session,
            VISIBLE_CAMERA_X_LOW,
            VISIBLE_CAMERA_X_HIGH,
        ),
        "collision_decodes": word(session, COLLISION_DECODE_COUNT_LOW),
        "lifecycle": {
            "request": counters[0],
            "prepare": counters[1],
            "resident": counters[2],
            "commit": counters[3],
            "release": counters[4],
        },
        "slot_states": [
            session.read(SLOT0, 1)[0],
            session.read(SLOT1, 1)[0],
        ],
        "selected_slot": session.read(SELECTED_SLOT, 1)[0],
        "pending_axes": session.read(PENDING_AXES, 1)[0],
        "critical_section": session.read(CRITICAL_SECTION, 1)[0],
        "packed_status": session.read(PACKED_STATUS, 1)[0],
        "forbidden_commit_work": {
            "bank": session.read(BANK_WORK_IN_COMMIT, 1)[0],
            "directory": session.read(DIRECTORY_WORK_IN_COMMIT, 1)[0],
            "decode": session.read(DECODE_WORK_IN_COMMIT, 1)[0],
        },
        "last_commit_writes": {
            "tiles": session.read(LAST_TILE_WRITES, 1)[0],
            "attributes": session.read(LAST_ATTRIBUTE_WRITES, 1)[0],
        },
        "world_pack_state": {
            "validation": session.read(WORLD_PACK_VALIDATION_STATE, 1)[0],
            "visual_cache0_valid": session.read(WORLD_PACK_VISUAL_CACHE0_VALID, 1)[0],
            "bulk_read_active": session.read(BULK_READ_ACTIVE, 1)[0],
            "bulk_read_current_bank": session.read(WORLD_PACK_BULK_READ_CURRENT_BANK, 1)[0],
            "collision_cache0_valid": session.read(WORLD_PACK_COLLISION_CACHE0_VALID, 1)[0],
            "control_hex": bytes(session.read(0x0326, 0xDA)).hex(),
            "visual_slot_sha256": [hashlib.sha256(slot).hexdigest() for slot in visual_slots],
        },
    }


def verify_snapshot_pair(before: dict[str, object], after: dict[str, object], measured_frames: int) -> None:
    hardware_delta = delta16(int(before["hardware_frames"]), int(after["hardware_frames"]))
    gameplay_delta = delta8(int(before["gameplay_ticks"]), int(after["gameplay_ticks"]))
    audio_delta = delta8(int(before["audio_ticks"]), int(after["audio_ticks"]))
    player_delta = int(after["player_x"]) - int(before["player_x"])
    requested = int(after["requested_camera_x"])
    visible = int(after["visible_camera_x"])
    screen_x = int(after["player_x"]) - visible
    lifecycle_before = before["lifecycle"]
    lifecycle_after = after["lifecycle"]
    assert isinstance(lifecycle_before, dict)
    assert isinstance(lifecycle_after, dict)
    lifecycle_deltas = {
        name: delta8(int(lifecycle_before[name]), int(lifecycle_after[name]))
        for name in lifecycle_before
    }

    if hardware_delta != measured_frames:
        raise AssertionError(f"hardware frame delta {hardware_delta} != {measured_frames}")
    if gameplay_delta not in (measured_frames - 1, measured_frames):
        raise AssertionError(
            f"gameplay tick delta {gameplay_delta} is not within one paused-frontend "
            f"sampling phase of {measured_frames} hardware frames"
        )
    if audio_delta != gameplay_delta:
        raise AssertionError(
            f"audio tick delta {audio_delta} != gameplay tick delta {gameplay_delta}"
        )
    minimum_player_delta = gameplay_delta * 5 // 4
    maximum_player_delta = (gameplay_delta * 5 + 3) // 4
    if not minimum_player_delta <= player_delta <= maximum_player_delta:
        raise AssertionError(
            f"player X delta {player_delta} is outside the {minimum_player_delta}.."
            f"{maximum_player_delta} range for {gameplay_delta} gameplay ticks"
        )
    if int(before["player_y"]) != 273 or int(after["player_y"]) != 273:
        raise AssertionError(
            f"runner left the authored floor: {before['player_y']} -> {after['player_y']}"
        )
    if requested != visible:
        raise AssertionError(f"requested/visible camera diverged: {requested} != {visible}")
    if not 64 <= screen_x <= 96:
        raise AssertionError(f"player/background screen X {screen_x} left dead-zone bounds")
    if len(set(lifecycle_deltas.values())) != 1 or next(iter(lifecycle_deltas.values())) == 0:
        raise AssertionError(f"scheduler lifecycle deltas diverged: {lifecycle_deltas}")
    if any(int(value) != 0 for value in after["forbidden_commit_work"].values()):
        raise AssertionError(f"forbidden work occurred in commit: {after['forbidden_commit_work']}")
    if int(after["critical_section"]) != 0:
        raise AssertionError(f"critical section remained active: {after['critical_section']}")
    if int(after["last_commit_writes"]["tiles"]) > 32:
        raise AssertionError(f"tile commit exceeded 32 writes: {after['last_commit_writes']}")
    if int(after["last_commit_writes"]["attributes"]) > 9:
        raise AssertionError(f"attribute commit exceeded 9 writes: {after['last_commit_writes']}")
    if any(not EMPTY <= int(state) <= RELEASED for state in after["slot_states"]):
        raise AssertionError(f"invalid slot state after traversal: {after['slot_states']}")


def run_pattern(
    launch_command: list[str],
    core: Path,
    rom: Path,
    artifact_directory: Path,
    name: str,
    pattern: bytes,
    initial_fill: str,
    command_port: int,
    remote_port: int,
    settle_frames: int,
    measured_frames: int,
    base_config: str,
) -> dict[str, object]:
    run_directory = artifact_directory / name
    with IsolatedRetroArchSession(
        launch_command,
        core,
        rom,
        run_directory,
        command_port,
        remote_port,
        {"fceumm_ramstate": initial_fill},
        base_config,
    ) as session:
        session.set_paused(True)
        session.set_right(False)
        session.fill_cpu_ram(pattern)
        session.set_paused(False)
        session.action("RESET")
        # RetroArch builds that still migrate the legacy quit/reset confirmation
        # setting require a second edge. With confirmation disabled the second
        # reset is harmless and still restarts from the same deterministic RAM.
        time.sleep(0.05)
        session.action("RESET")

        session.wait_until(
            lambda: (
                all(EMPTY <= state <= RELEASED for state in session.read(SLOT0, 1) + session.read(SLOT1, 1))
                and session.read(FRAME_PENDING, 1)[0] <= 1
                and session.read(SELECTED_SLOT, 1)[0] in (0, 1, NO_SLOT)
                and session.read(CRITICAL_SECTION, 1)[0] == 0
                and session.frame_counter() > 0
            ),
            timeout=15,
            description="deterministic packed-camera initialization",
        )
        session.set_paused(True)
        for _ in range(settle_frames):
            session.advance_frame()

        expected_guard = pattern[0x0652]
        actual_guard = session.read(0x0652, 1)[0]
        if actual_guard != expected_guard:
            raise AssertionError(
                "packed-camera initialization crossed its runtime-owned staging boundary "
                f"at $0652: expected {expected_guard}, got {actual_guard}"
            )

        before = snapshot(session)
        session.set_right(True)
        for _ in range(measured_frames):
            session.advance_frame()
        session.set_right(False)
        after = snapshot(session)
        result = {
            "name": name,
            "initial_fill": initial_fill,
            "pattern_sha256": hashlib.sha256(pattern).hexdigest(),
            "first_non_owned_staging_byte": actual_guard,
            "before": before,
            "after": after,
            "verified": False,
        }
        result_path = run_directory / "result.json"
        result_path.write_text(
            json.dumps(result, indent=2) + "\n",
            encoding="utf-8",
        )
        verify_snapshot_pair(before, after, measured_frames)
        result["verified"] = True
        result_path.write_text(
            json.dumps(result, indent=2) + "\n",
            encoding="utf-8",
        )
        return result


def main() -> int:
    args = parse_args()
    rom = args.rom.resolve()
    core = args.core.resolve()
    if not rom.is_file():
        raise FileNotFoundError(f"Runner ROM was not found: {rom}")
    if not core.is_file():
        raise FileNotFoundError(f"FCEUmm core was not found: {core}")
    core_bytes = core.read_bytes()
    if b"(SVN) 3a84a6f" not in core_bytes:
        raise RuntimeError("FCEUmm core is not the required (SVN) 3a84a6f build.")

    launch_command = (
        shlex.split(args.retroarch_command)
        if args.retroarch_command
        else default_retroarch_command()
    )
    artifact_directory = args.artifacts.resolve()
    artifact_directory.mkdir(parents=True, exist_ok=True)
    patterns = [
        ("fill-00", bytes(0x800), "fill $00"),
        ("fill-ff", bytes([0xFF]) * 0x800, "fill $ff"),
        ("pattern-a5", deterministic_pattern(0xA5), "fill $00"),
    ]
    patterns = [pattern for pattern in patterns if pattern[0] in args.patterns]
    results = []
    base_config = subprocess.check_output(
        shlex.split(args.retroarch_default_config_command),
        text=True,
    )
    for index, (name, pattern, initial_fill) in enumerate(patterns):
        results.append(
            run_pattern(
                launch_command,
                core,
                rom,
                artifact_directory,
                name,
                pattern,
                initial_fill,
                55355 + index,
                55400 + index,
                SETTLE_FRAMES,
                MEASURED_FRAMES,
                base_config,
            )
        )

    summary = {
        "rom": str(rom),
        "rom_sha256": hashlib.sha256(rom.read_bytes()).hexdigest(),
        "core": str(core),
        "core_sha256": hashlib.sha256(core_bytes).hexdigest(),
        "core_version": "FCEUmm (SVN) 3a84a6f",
        "settle_frames": SETTLE_FRAMES,
        "measured_frames": MEASURED_FRAMES,
        "results": results,
    }
    summary_path = artifact_directory / "summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(summary_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
