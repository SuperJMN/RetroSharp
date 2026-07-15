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
from tools.nes.runtime_abi import NesRuntimeAbi


DEFAULT_ROM = ROOT / "samples" / "runner" / "bin" / "runner.nes"
DEFAULT_RUNTIME_ABI = ROOT / "samples" / "runner" / "bin" / "runner.nes.runtime-abi.json"
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

_ACTIVE_RUNTIME_ABI: NesRuntimeAbi | None = None
SETTLE_FRAMES = 500
MEASURED_FRAMES = 120


def activate_runtime_abi(abi: NesRuntimeAbi) -> None:
    global _ACTIVE_RUNTIME_ABI
    _ACTIVE_RUNTIME_ABI = abi


def runtime_abi(override: NesRuntimeAbi | None = None) -> NesRuntimeAbi:
    if override is not None:
        return override
    if _ACTIVE_RUNTIME_ABI is not None:
        return _ACTIVE_RUNTIME_ABI
    return NesRuntimeAbi.load(DEFAULT_RUNTIME_ABI, DEFAULT_ROM)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Boot the production NES runner in FCEUmm with $00, $FF, and a "
            "deterministic nonzero CPU RAM pattern, then verify scheduler/camera cadence."
        )
    )
    parser.add_argument("--rom", type=Path, default=DEFAULT_ROM)
    parser.add_argument("--runtime-abi", type=Path, default=DEFAULT_RUNTIME_ABI)
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
    high_address: int,
) -> int:
    low = session.read(low_address, 1)[0]
    high = session.read(high_address, 1)[0]
    return low | high << 8


def variable_word(session: IsolatedRetroArchSession, variable) -> int:
    low, high = session.read(variable.address, variable.size)
    return low | high << 8


def snapshot(
    session: IsolatedRetroArchSession,
    abi: NesRuntimeAbi | None = None,
) -> dict[str, object]:
    contract = runtime_abi(abi)

    def address(name: str) -> int:
        return contract.address(name)

    visual_slot_regions = [
        contract.region(f"WorldPack.VisualSlot{index}")
        for index in range(6)
    ]
    visual_slots = [
        bytes(session.read(region.start, region.length))
        for region in visual_slot_regions
    ]
    player_x = contract.variable("player.x")
    player_y = contract.variable("player.y")
    control_ranges = [
        contract.range("WorldPackScalarState"),
        contract.range("PackedCameraAndWorldPackAuxiliaryState"),
    ]
    control_bytes = b"".join(
        bytes(session.read(value.start, value.length))
        for value in control_ranges
    )
    return {
        "hardware_frames": session.frame_counter(),
        "gameplay_ticks": session.read(address("WorldPack.GameplayTickCount"), 1)[0],
        "audio_ticks": session.read(address("WorldPack.AudioTickCount"), 1)[0],
        "player_x": variable_word(session, player_x),
        "player_y": variable_word(session, player_y),
        "requested_camera_x": word(
            session,
            address("camera.X"),
            address("camera.XHigh"),
        ),
        "visible_camera_x": word(
            session,
            address("packed camera.VisibleCameraXLow"),
            address("packed camera.VisibleCameraXHigh"),
        ),
        "collision_decodes": word(
            session,
            address("WorldPack.CollisionDecodeCountLow"),
            address("WorldPack.CollisionDecodeCountHigh"),
        ),
        "lifecycle": {
            "request": session.read(address("packed camera.RequestCount"), 1)[0],
            "prepare": session.read(address("packed camera.PrepareCount"), 1)[0],
            "resident": session.read(address("packed camera.ResidentCount"), 1)[0],
            "commit": session.read(address("packed camera.CommitCount"), 1)[0],
            "release": session.read(address("packed camera.ReleaseCount"), 1)[0],
        },
        "slot_states": [
            session.read(address("packed camera.Slot0"), 1)[0],
            session.read(address("packed camera.Slot1"), 1)[0],
        ],
        "selected_slot": session.read(address("packed camera.SelectedSlot"), 1)[0],
        "pending_axes": session.read(address("packed camera.PendingAxes"), 1)[0],
        "critical_section": session.read(address("packed camera.CriticalSection"), 1)[0],
        "packed_status": session.read(address("packed camera.Status"), 1)[0],
        "forbidden_commit_work": {
            "bank": session.read(address("packed camera.BankWorkInCommit"), 1)[0],
            "directory": session.read(address("packed camera.DirectoryWorkInCommit"), 1)[0],
            "decode": session.read(address("packed camera.DecodeWorkInCommit"), 1)[0],
        },
        "last_commit_writes": {
            "tiles": session.read(address("packed camera.LastTileWrites"), 1)[0],
            "attributes": session.read(address("packed camera.LastAttributeWrites"), 1)[0],
        },
        "world_pack_state": {
            "validation": session.read(address("WorldPack.ValidationState"), 1)[0],
            "visual_cache0_valid": session.read(address("WorldPack.VisualCache0Valid"), 1)[0],
            "bulk_read_active": session.read(address("WorldPack.BulkReadActive"), 1)[0],
            "bulk_read_current_bank": session.read(address("WorldPack.BulkReadCurrentBank"), 1)[0],
            "collision_cache0_valid": session.read(address("WorldPack.CollisionCache0Valid"), 1)[0],
            "control_hex": control_bytes.hex(),
            "visual_slot_sha256": [hashlib.sha256(slot).hexdigest() for slot in visual_slots],
        },
    }


def verify_snapshot_pair(before: dict[str, object], after: dict[str, object], measured_frames: int) -> None:
    contract = runtime_abi()
    empty = contract.constant("packed camera.Empty")
    released = contract.constant("packed camera.Released")
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
    if any(not empty <= int(state) <= released for state in after["slot_states"]):
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
    contract = runtime_abi()
    empty = contract.constant("packed camera.Empty")
    released = contract.constant("packed camera.Released")
    no_slot = contract.constant("packed camera.NoSlot")
    slot0 = contract.address("packed camera.Slot0")
    slot1 = contract.address("packed camera.Slot1")
    frame_pending = contract.address("packed camera.FramePending")
    selected_slot = contract.address("packed camera.SelectedSlot")
    critical_section = contract.address("packed camera.CriticalSection")
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
        frame_counter_addresses=(
            contract.address("packed camera.FrameCounterLow"),
            contract.address("packed camera.FrameCounterHigh"),
        ),
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
                all(empty <= state <= released for state in session.read(slot0, 1) + session.read(slot1, 1))
                and session.read(frame_pending, 1)[0] <= 1
                and session.read(selected_slot, 1)[0] in (0, 1, no_slot)
                and session.read(critical_section, 1)[0] == 0
                and session.frame_counter() > 0
            ),
            timeout=15,
            description="deterministic packed-camera initialization",
        )
        session.set_paused(True)
        for _ in range(settle_frames):
            session.advance_frame()

        guard_address = contract.range("WorldPackStaging").end_exclusive
        expected_guard = pattern[guard_address]
        actual_guard = session.read(guard_address, 1)[0]
        if actual_guard != expected_guard:
            raise AssertionError(
                "packed-camera initialization crossed its runtime-owned staging boundary "
                f"at ${guard_address:04X}: expected {expected_guard}, got {actual_guard}"
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
    activate_runtime_abi(NesRuntimeAbi.load(args.runtime_abi, args.rom))
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
