#!/usr/bin/env python3
"""Run the tracked NES runner under FCEUmm with deterministic CPU RAM patterns."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import signal
import socket
import struct
import subprocess
import time
from typing import Callable


ROOT = Path(__file__).resolve().parents[2]
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
RETRO_DEVICE_JOYPAD = 1
RETRO_DEVICE_ID_JOYPAD_RIGHT = 7
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


class RetroArchSession:
    def __init__(
        self,
        launch_command: list[str],
        core: Path,
        rom: Path,
        work_directory: Path,
        command_port: int,
        remote_port: int,
        initial_fill: str,
    ) -> None:
        self.launch_command = launch_command
        self.core = core.resolve()
        self.rom = rom.resolve()
        self.work_directory = work_directory.resolve()
        self.command_port = command_port
        self.remote_port = remote_port
        self.initial_fill = initial_fill
        self.process: subprocess.Popen[str] | None = None
        self.log_file = None
        self.command_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.command_socket.settimeout(1.0)
        self.remote_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def __enter__(self) -> "RetroArchSession":
        self.work_directory.mkdir(parents=True, exist_ok=True)
        core_options = self.work_directory / "fceumm-core-options.cfg"
        core_options.write_text(
            f'fceumm_ramstate = "{self.initial_fill}"\n',
            encoding="utf-8",
        )
        append_config = self.work_directory / "retroarch-append.cfg"
        append_config.write_text(
            "\n".join(
                [
                    'network_cmd_enable = "true"',
                    f'network_cmd_port = "{self.command_port}"',
                    'network_remote_enable = "true"',
                    'network_remote_enable_user_p1 = "true"',
                    f'network_remote_base_port = "{self.remote_port}"',
                    f'core_options_path = "{core_options}"',
                    'game_specific_options = "false"',
                    'input_driver = "null"',
                    'joypad_driver = "null"',
                    'audio_driver = "null"',
                    'video_driver = "null"',
                    'menu_driver = "null"',
                    'confirm_reset = "false"',
                    'confirm_quit = "false"',
                    'rewind_enable = "false"',
                    'run_ahead_enabled = "false"',
                ]
            )
            + "\n",
            encoding="utf-8",
        )
        log_path = self.work_directory / "retroarch.log"
        self.log_file = log_path.open("w", encoding="utf-8")
        command = [
            *self.launch_command,
            f"--appendconfig={append_config}",
            "-L",
            str(self.core),
            "--max-frames=1000000",
            str(self.rom),
        ]
        self.process = subprocess.Popen(
            command,
            cwd=ROOT,
            stdout=self.log_file,
            stderr=subprocess.STDOUT,
            text=True,
            start_new_session=True,
        )
        self.wait_until(
            lambda: self.status().startswith("GET_STATUS PLAYING"),
            timeout=15,
            description="RetroArch to start playing",
        )
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        try:
            self.action("QUIT")
            if self.process is not None:
                self.process.wait(timeout=5)
        except (OSError, subprocess.TimeoutExpired):
            if self.process is not None:
                os.killpg(self.process.pid, signal.SIGTERM)
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(self.process.pid, signal.SIGKILL)
                    self.process.wait(timeout=3)
        finally:
            self.command_socket.close()
            self.remote_socket.close()
            if self.log_file is not None:
                self.log_file.close()

    def wait_until(
        self,
        predicate: Callable[[], bool],
        timeout: float,
        description: str,
        interval: float = 0.01,
    ) -> None:
        deadline = time.monotonic() + timeout
        last_error: Exception | None = None
        while time.monotonic() < deadline:
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError(
                    f"RetroArch exited with code {self.process.returncode} while waiting for {description}."
                )
            try:
                if predicate():
                    return
            except (OSError, TimeoutError, ValueError) as error:
                last_error = error
            time.sleep(interval)
        suffix = f" Last error: {last_error}" if last_error is not None else ""
        raise TimeoutError(f"Timed out waiting for {description}.{suffix}")

    def query(self, command: str) -> str:
        self.command_socket.sendto(
            (command + "\n").encode("ascii"),
            ("127.0.0.1", self.command_port),
        )
        response, _ = self.command_socket.recvfrom(65535)
        return response.decode("ascii").strip()

    def action(self, command: str) -> None:
        self.command_socket.sendto(
            (command + "\n").encode("ascii"),
            ("127.0.0.1", self.command_port),
        )

    def status(self) -> str:
        return self.query("GET_STATUS")

    def set_paused(self, paused: bool) -> None:
        expected = "GET_STATUS PAUSED" if paused else "GET_STATUS PLAYING"
        if self.status().startswith(expected):
            return
        self.action("PAUSE_TOGGLE")
        self.wait_until(
            lambda: self.status().startswith(expected),
            timeout=3,
            description=f"RetroArch to become {'paused' if paused else 'playing'}",
        )

    def read(self, address: int, length: int) -> list[int]:
        response = self.query(f"READ_CORE_MEMORY {address:x} {length}")
        parts = response.split()
        if len(parts) != length + 2 or parts[0] != "READ_CORE_MEMORY":
            raise RuntimeError(f"Unexpected RetroArch memory response: {response}")
        return [int(value, 16) for value in parts[2:]]

    def write(self, address: int, values: list[int]) -> None:
        encoded = " ".join(f"{value:02X}" for value in values)
        response = self.query(f"WRITE_CORE_MEMORY {address:x} {encoded}")
        parts = response.split()
        if parts[:2] != ["WRITE_CORE_MEMORY", f"{address:x}"] or parts[2:] != [str(len(values))]:
            raise RuntimeError(f"Unexpected RetroArch memory-write response: {response}")

    def fill_cpu_ram(self, pattern: bytes) -> None:
        if len(pattern) != 0x800:
            raise ValueError("CPU RAM pattern must contain exactly 2 KiB.")
        for start in range(0, len(pattern), 128):
            self.write(start, list(pattern[start : start + 128]))

    def set_right(self, pressed: bool) -> None:
        packet = struct.pack(
            "<iiiiHxx",
            0,
            RETRO_DEVICE_JOYPAD,
            0,
            RETRO_DEVICE_ID_JOYPAD_RIGHT,
            1 if pressed else 0,
        )
        self.remote_socket.sendto(packet, ("127.0.0.1", self.remote_port))

    def frame_counter(self) -> int:
        low, high = self.read(FRAME_COUNTER_LOW, 2)
        return low | high << 8

    def advance_frame(self) -> None:
        before = self.frame_counter()
        deadline = time.monotonic() + 2
        after = before
        while time.monotonic() < deadline and after == before:
            self.action("FRAMEADVANCE")
            retry_deadline = time.monotonic() + 0.05
            while time.monotonic() < retry_deadline:
                after = self.frame_counter()
                if after != before:
                    break
                time.sleep(0.001)
        if after == before:
            raise TimeoutError("Timed out waiting for one paused frontend frame.")
        if delta16(before, after) != 1:
            raise RuntimeError(
                f"FRAMEADVANCE changed the NES hardware counter by {delta16(before, after)}, expected 1."
            )
        # Let the command interface publish an unpressed poll between edges.
        time.sleep(0.003)


def deterministic_pattern(seed: int) -> bytes:
    return bytes((((address * 73) ^ (address >> 3) ^ seed) & 0xFF) for address in range(0x800))


def word(session: RetroArchSession, low_address: int, high_address: int | None = None) -> int:
    if high_address is None:
        low, high = session.read(low_address, 2)
    else:
        low = session.read(low_address, 1)[0]
        high = session.read(high_address, 1)[0]
    return low | high << 8


def snapshot(session: RetroArchSession) -> dict[str, object]:
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
    if gameplay_delta != measured_frames:
        raise AssertionError(f"gameplay tick delta {gameplay_delta} != {measured_frames}")
    if audio_delta != measured_frames:
        raise AssertionError(f"audio tick delta {audio_delta} != {measured_frames}")
    if player_delta != 150:
        raise AssertionError(f"player X delta {player_delta} != 150")
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
) -> dict[str, object]:
    run_directory = artifact_directory / name
    with RetroArchSession(
        launch_command,
        core,
        rom,
        run_directory,
        command_port,
        remote_port,
        initial_fill,
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
