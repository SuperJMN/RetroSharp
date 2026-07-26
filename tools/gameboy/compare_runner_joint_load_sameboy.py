#!/usr/bin/env python3
"""Compare RPH-6.2's ROM-visible runner counters through SameBoy's existing C ABI.

The in-process report and timeline are emitted only when RETROSHARP_RPH62_REPORT
is set for GameBoyRunnerJointLoadCadenceTests.  This tool intentionally compares
ROM state first; host opcode/read instrumentation is recorded as provenance, not
as a cross-emulator equality gate.
"""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
from typing import Any


RIGHT = 1 << 0
A = 1 << 4
B = 1 << 5
PALETTE = 0xFF47
REPORT_SCHEMA = "retrosharp-rph62-in-process-v2"
REPLAY_SCHEMA = "retrosharp-rph62-replay-v1"
COMPARISON_SCHEMA = "retrosharp-rph62-same-counter-comparison-v4"
LAYOUT_FIELDS = (
    "playerX", "playerY", "gameplayTick", "audioTick",
    "cameraRequest", "cameraResident", "cameraCommit", "cameraVisible",
    "visibleCameraX", "visibleCameraY", "shadowBank", "musicActive", "sfxActive",
)
STATE_FIELDS = (
    "romGameplayTick", "packedAudioTick", "playerX", "playerY",
    "visibleCameraX", "visibleCameraY", "shadowRomBank", "forbiddenVideoWork",
    "musicActive", "sfxActive", "camera", "backgroundDigest", "oamDigest",
)


class SameBoy:
    def __init__(self, library: Path, rom: Path) -> None:
        self._lib = ctypes.CDLL(str(library))
        self._lib.gbmcp_create.restype = ctypes.c_void_p
        self._lib.gbmcp_destroy.argtypes = [ctypes.c_void_p]
        self._lib.gbmcp_destroy.restype = None
        self._lib.gbmcp_load_rom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p, ctypes.c_size_t]
        self._lib.gbmcp_load_rom.restype = ctypes.c_int
        self._lib.gbmcp_set_joypad.argtypes = [ctypes.c_void_p, ctypes.c_ubyte]
        self._lib.gbmcp_set_joypad.restype = ctypes.c_int
        self._lib.gbmcp_run_frame.argtypes = [ctypes.c_void_p]
        self._lib.gbmcp_run_frame.restype = ctypes.c_int
        self._lib.gbmcp_read_memory.argtypes = [ctypes.c_void_p, ctypes.c_ushort, ctypes.c_void_p, ctypes.c_size_t]
        self._lib.gbmcp_read_memory.restype = ctypes.c_int
        self._lib.gbmcp_read_oam.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t]
        self._lib.gbmcp_read_oam.restype = ctypes.c_int
        self._session = self._lib.gbmcp_create()
        if not self._session:
            raise RuntimeError("gbmcp_create failed")
        try:
            title = ctypes.create_string_buffer(32)
            model = ctypes.create_string_buffer(16)
            self._check(self._lib.gbmcp_load_rom(self._session, os.fsencode(rom), title, len(title), model, len(model)), "load ROM")
        except Exception:
            self._lib.gbmcp_destroy(self._session)
            self._session = None
            raise

    def __enter__(self) -> "SameBoy":
        return self

    def __exit__(self, *_: object) -> None:
        if self._session:
            self._lib.gbmcp_destroy(self._session)
            self._session = None

    def _check(self, result: int, operation: str) -> None:
        if result != 0:
            raise RuntimeError(f"SameBoy {operation} failed ({result})")

    def run_frame(self, input_mask: int) -> None:
        sameboy_mask = (RIGHT if input_mask & 1 else 0) | (B if input_mask & 2 else 0) | (A if input_mask & 4 else 0)
        self._check(self._lib.gbmcp_set_joypad(self._session, sameboy_mask), "set joypad")
        self._check(self._lib.gbmcp_run_frame(self._session), "run frame")

    def memory(self, address: int, length: int = 1) -> list[int]:
        values = (ctypes.c_ubyte * length)()
        self._check(self._lib.gbmcp_read_memory(self._session, address, values, length), f"read memory 0x{address:04X}")
        return list(values)

    def oam(self) -> list[int]:
        values = (ctypes.c_ubyte * 160)()
        self._check(self._lib.gbmcp_read_oam(self._session, values, len(values)), "read OAM")
        return list(values)


def word(session: SameBoy, address: int) -> int:
    low, high = session.memory(address, 2)
    return low | high << 8


def digest(value: Any) -> str:
    return hashlib.sha256(json.dumps(value, separators=(",", ":"), sort_keys=True).encode()).hexdigest()


def ordered_digest(values: list[str]) -> str:
    return hashlib.sha256("|".join(values).encode()).hexdigest()


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def _validate_state(row: dict[str, Any], description: str) -> None:
    _require(isinstance(row, dict) and isinstance(row.get("frame"), int), f"{description} must contain an integer frame.")
    state = row.get("state")
    _require(isinstance(state, dict), f"{description} must contain state.")
    missing = [field for field in STATE_FIELDS if field not in state]
    _require(not missing, f"{description} is missing state fields: {', '.join(missing)}.")
    _require(isinstance(state["camera"], dict) and all(
        isinstance(state["camera"].get(field), int) for field in ("request", "resident", "commit", "visible")
    ), f"{description} has an invalid camera state.")


def validate_replay_descriptor(
    replay: dict[str, Any],
    *,
    require_physical_camera_budgets: bool = False,
) -> None:
    """Reject a malformed or ambiguous ROM/input replay descriptor."""
    _require(replay.get("schema") == REPLAY_SCHEMA, f"Unsupported replay schema: {replay.get('schema')!r}.")
    _require(isinstance(replay.get("romSha256"), str) and replay["romSha256"], "Timeline ROM SHA-256 is required.")
    _require(isinstance(replay.get("warmUpFrames"), int) and replay["warmUpFrames"] >= 0, "Timeline warmUpFrames is invalid.")
    _require(isinstance(replay.get("observationFrames"), int) and replay["observationFrames"] > 0, "Timeline observationFrames is invalid.")
    layout = replay.get("layout")
    _require(isinstance(layout, dict), "Timeline layout is required.")
    missing_layout = [field for field in LAYOUT_FIELDS if not isinstance(layout.get(field), int)]
    _require(not missing_layout, f"Timeline layout is missing integer addresses: {', '.join(missing_layout)}.")
    _require(isinstance(layout.get("forbiddenCounters"), list) and len(layout["forbiddenCounters"]) == 5
             and all(isinstance(address, int) for address in layout["forbiddenCounters"]),
             "Timeline requires exactly five forbidden counter addresses.")
    cadence = replay.get("cadence")
    _require(isinstance(cadence, dict), "Timeline cadence budgets are required.")
    _require(isinstance(cadence.get("minimumGameplayTickRatio"), (int, float)), "Timeline minimumGameplayTickRatio is invalid.")
    _require(
        isinstance(cadence.get("maximumConsecutiveMissedGameplayTicks"), int)
        and cadence["maximumConsecutiveMissedGameplayTicks"] >= 0,
        "Timeline maximumConsecutiveMissedGameplayTicks is invalid.",
    )
    _require(
        isinstance(cadence.get("maximumUnplannedAudioGapFrames"), int)
        and cadence["maximumUnplannedAudioGapFrames"] >= 0,
        "Timeline maximumUnplannedAudioGapFrames is invalid.",
    )
    if require_physical_camera_budgets:
        _require(
            isinstance(cadence.get("maximumRequestToVisibleFrames"), int)
            and cadence["maximumRequestToVisibleFrames"] >= 0,
            "Timeline maximumRequestToVisibleFrames is required by the physical observer.",
        )
        minimum_canary_frames = max(
            cadence["maximumConsecutiveMissedGameplayTicks"] + 1,
            cadence["maximumUnplannedAudioGapFrames"] + 1,
            cadence["maximumRequestToVisibleFrames"] + 2,
        )
        _require(
            replay["observationFrames"] >= minimum_canary_frames,
            "Timeline observationFrames is too short for the physical observer canaries "
            f"(minimum {minimum_canary_frames}).",
        )
    timeline = replay.get("frames")
    _require(isinstance(timeline, list), "Timeline frames are required.")
    expected_timeline_frames = list(range(1, replay["warmUpFrames"] + replay["observationFrames"] + 1))
    _require([item.get("frame") if isinstance(item, dict) else None for item in timeline] == expected_timeline_frames,
             "Timeline frames must be unique and contiguous from frame 1.")
    _require(all(
        isinstance(item.get("inputMask"), int)
        and 0 <= item["inputMask"] <= 0b111
        and isinstance(item.get("audioServiceExpected"), bool)
        for item in timeline
    ),
             "Each timeline frame requires inputMask and audioServiceExpected.")


def validate_replay_contract(report: dict[str, Any], replay: dict[str, Any]) -> None:
    """Reject malformed or ambiguous evidence before any cross-backend comparison."""
    validate_replay_descriptor(replay)
    _require(report.get("schema") == REPORT_SCHEMA, f"Unsupported in-process report schema: {report.get('schema')!r}.")
    _require(isinstance(report.get("romSha256"), str) and report["romSha256"], "Report ROM SHA-256 is required.")
    _require(report.get("romSha256") == replay.get("romSha256"), "Report and timeline ROM SHA-256 differ.")
    _validate_state(report.get("baseline"), "Baseline")
    baseline = report["baseline"]
    _require(baseline["frame"] == replay["warmUpFrames"], "Baseline frame must equal timeline warmUpFrames.")
    rows = report.get("frames")
    _require(isinstance(rows, list) and len(rows) == replay["observationFrames"], "Report frame count must equal observationFrames.")
    for row in rows:
        _validate_state(row, "Report frame")
        _require(isinstance(row.get("gameplayTicks"), int) and isinstance(row.get("audioServiceTicks"), int),
                 "Report frame requires integer host counters.")
    expected_report_frames = list(range(baseline["frame"] + 1, baseline["frame"] + 1 + len(rows)))
    _require([row["frame"] for row in rows] == expected_report_frames, "Report frames must be unique and contiguous after baseline.")


def normalize_replay_rows(
    rows: dict[int, dict[str, Any]],
    baseline_frame: int,
    *,
    camera_modulus: int | None = None,
) -> dict[int, dict[str, Any]]:
    """Normalize only SameBoy's unspecified absolute camera power-on value."""
    baseline_camera = rows[baseline_frame]["state"]["camera"]
    normalized = {}
    for frame, row in rows.items():
        state = dict(row["state"])
        state["camera"] = {
            name: (
                (value - baseline_camera[name]) % camera_modulus
                if camera_modulus is not None
                else value - baseline_camera[name]
            )
            for name, value in state["camera"].items()
        }
        normalized[frame] = {"frame": row["frame"], "state": state}
    return normalized


def normalized_replay_digest(rows: dict[int, dict[str, Any]], baseline_frame: int) -> str:
    """Ignores only SameBoy's unspecified absolute camera power-on value."""
    return digest(normalize_replay_rows(rows, baseline_frame, camera_modulus=256))


def in_process_rows(report: dict[str, Any]) -> dict[int, dict[str, Any]]:
    return {row["frame"]: row for row in report["frames"]}


def unwrap(previous_raw: int, previous_unwrapped: int, current_raw: int) -> int:
    return previous_unwrapped + ((current_raw - previous_raw) & 0xFF)


def sameboy_rows(library: Path, rom: Path, replay: dict[str, Any], frames_to_keep: set[int]) -> dict[int, dict[str, Any]]:
    layout = replay["layout"]
    rows: dict[int, dict[str, Any]] = {}
    raw_stages = {name: 0 for name in ("request", "resident", "commit", "visible")}
    stages = dict(raw_stages)
    with SameBoy(library, rom) as session:
        for item in replay["frames"]:
            frame = item["frame"]
            session.run_frame(item["inputMask"])
            current_raw = {
                "request": session.memory(layout["cameraRequest"])[0],
                "resident": session.memory(layout["cameraResident"])[0],
                "commit": session.memory(layout["cameraCommit"])[0],
                "visible": session.memory(layout["cameraVisible"])[0],
            }
            for name, value in current_raw.items():
                stages[name] = unwrap(raw_stages[name], stages[name], value)
            raw_stages = current_raw
            if frame not in frames_to_keep:
                continue
            visible_x, visible_y = word(session, layout["visibleCameraX"]), word(session, layout["visibleCameraY"])
            width, height = (20 if visible_x % 8 == 0 else 21), (18 if visible_y % 8 == 0 else 19)
            tilemap = session.memory(0x9800, 1024)
            palette = session.memory(PALETTE)[0]
            background = []
            for y in range(height):
                for x in range(width):
                    offset = (((visible_y // 8 + y) & 31) * 32) + ((visible_x // 8 + x) & 31)
                    background.append(f"screen:{x:02d},{y:02d}:{tilemap[offset]}:{palette}")
            # GameBoyTestCpu normalizes by location; SameBoy naturally visits the
            # viewport in y-major order.  The digest must not encode that traversal.
            background.sort()
            rows[frame] = {
                "frame": frame,
                "state": {
                    "romGameplayTick": session.memory(layout["gameplayTick"])[0],
                    "packedAudioTick": session.memory(layout["audioTick"])[0],
                    "playerX": word(session, layout["playerX"]),
                    "playerY": word(session, layout["playerY"]),
                    "visibleCameraX": visible_x,
                    "visibleCameraY": visible_y,
                    "camera": dict(stages),
                    "shadowRomBank": session.memory(layout["shadowBank"])[0] or 1,
                    "forbiddenVideoWork": sum(session.memory(address)[0] for address in layout["forbiddenCounters"]),
                    "musicActive": session.memory(layout["musicActive"])[0],
                    "sfxActive": session.memory(layout["sfxActive"])[0],
                    "backgroundDigest": ordered_digest(background),
                    "oamDigest": ordered_digest([str(value) for value in session.oam()]),
                },
            }
    return rows


def state_value(row: dict[str, Any], field: str) -> int | str:
    state = row["state"]
    if field.startswith("camera."):
        return state["camera"][field.removeprefix("camera.")]
    return state[field]


def previous_row(rows: dict[int, dict[str, Any]], baseline: dict[str, Any], frame: int) -> dict[str, Any] | None:
    return rows.get(frame - 1) or (baseline if baseline["frame"] == frame - 1 else None)


def first_counter_delta_mismatches(
    expected: dict[int, dict[str, Any]],
    actual: dict[int, dict[str, Any]],
    expected_baseline: dict[str, Any],
    actual_baseline: dict[str, Any],
    fields: tuple[str, ...],
) -> dict[str, dict[str, int] | None]:
    result: dict[str, dict[str, int] | None] = {}
    for field in fields:
        mismatch: dict[str, int] | None = None
        for frame in sorted(expected):
            expected_previous = previous_row(expected, expected_baseline, frame)
            actual_previous = previous_row(actual, actual_baseline, frame)
            # The baseline establishes each backend's power-on offset.  Its first
            # successor is not a comparable physical-frame interval; compare only
            # transitions wholly inside the observed replay window.
            if (
                expected_previous is None
                or actual_previous is None
                or expected_previous["frame"] == expected_baseline["frame"]
                or actual_previous["frame"] == actual_baseline["frame"]
            ):
                continue
            expected_value = state_value(expected[frame], field)
            actual_value = state_value(actual[frame], field)
            expected_delta = (expected_value - state_value(expected_previous, field)) & 0xFF
            actual_delta = (actual_value - state_value(actual_previous, field)) & 0xFF
            if expected_delta != actual_delta:
                if field.startswith("camera."):
                    expected_value = (expected_value - state_value(expected_baseline, field)) & 0xFF
                    actual_value = (actual_value - state_value(actual_baseline, field)) & 0xFF
                mismatch = {
                    "frame": frame,
                    "expectedDelta": expected_delta,
                    "actualDelta": actual_delta,
                    "expectedValue": expected_value,
                    "actualValue": actual_value,
                }
                break
        result[field] = mismatch
    return result


def first_visual_mismatches(
    expected: dict[int, dict[str, Any]], actual: dict[int, dict[str, Any]]
) -> dict[str, dict[str, Any] | None]:
    fields = ("backgroundDigest", "oamDigest")
    result: dict[str, dict[str, Any] | None] = {}
    for field in fields:
        result[field] = next(
            (
                {"frame": frame, "expected": state_value(expected[frame], field), "actual": state_value(actual[frame], field)}
                for frame in sorted(expected)
                if state_value(expected[frame], field) != state_value(actual[frame], field)
            ),
            None,
        )
    return result


def first_state_mismatches(
    expected: dict[int, dict[str, Any]], actual: dict[int, dict[str, Any]]
) -> dict[str, dict[str, int] | None]:
    fields = (
        "playerX", "playerY", "visibleCameraX", "visibleCameraY",
        "shadowRomBank", "forbiddenVideoWork", "musicActive", "sfxActive",
    )
    result: dict[str, dict[str, int] | None] = {}
    for field in fields:
        result[field] = next(
            (
                {"frame": frame, "expected": state_value(expected[frame], field), "actual": state_value(actual[frame], field)}
                for frame in sorted(expected)
                if state_value(expected[frame], field) != state_value(actual[frame], field)
            ),
            None,
        )
    return result


def verify_rom_identity(rom_bytes: bytes, report: dict[str, Any], replay: dict[str, Any]) -> str:
    rom_sha256 = hashlib.sha256(rom_bytes).hexdigest()
    if rom_sha256 != report["romSha256"] or rom_sha256 != replay["romSha256"]:
        raise ValueError("ROM SHA-256 does not match the in-process report and replay timeline.")
    return rom_sha256


def host_projection_first_mismatch(rows: dict[int, dict[str, Any]]) -> dict[str, dict[str, int] | None]:
    previous: dict[str, Any] | None = None
    gameplay: dict[str, int] | None = None
    audio: dict[str, int] | None = None
    for frame in sorted(rows):
        current = rows[frame]
        if previous is not None:
            host_gameplay = current["gameplayTicks"] - previous["gameplayTicks"]
            rom_gameplay = (state_value(current, "romGameplayTick") - state_value(previous, "romGameplayTick")) & 0xFF
            host_audio = current["audioServiceTicks"] - previous["audioServiceTicks"]
            rom_audio = (state_value(current, "packedAudioTick") - state_value(previous, "packedAudioTick")) & 0xFF
            if gameplay is None and host_gameplay != rom_gameplay:
                gameplay = {"frame": frame, "expectedDelta": host_gameplay, "actualDelta": rom_gameplay}
            if audio is None and host_audio != rom_audio:
                audio = {"frame": frame, "expectedDelta": host_audio, "actualDelta": rom_audio}
        previous = current
    return {"gameplay": gameplay, "audio": audio}


def classify_cadence(
    rows: dict[int, dict[str, Any]],
    baseline: dict[str, Any],
    replay: dict[str, Any],
    measurement_frames: set[int] | None = None,
) -> dict[str, Any]:
    """Apply the emitted runner cadence budgets to ROM-visible timeline counters."""
    budgets = replay["cadence"]
    audio_expected = {item["frame"]: item["audioServiceExpected"] for item in replay["frames"]}
    missed_gameplay = 0
    missed_audio = 0
    gameplay_ticks = 0
    gameplay_intervals = 0
    for frame in sorted(measurement_frames if measurement_frames is not None else rows):
        previous = previous_row(rows, baseline, frame)
        if previous is None:
            continue
        gameplay_delta = (state_value(rows[frame], "romGameplayTick") - state_value(previous, "romGameplayTick")) & 0xFF
        gameplay_ticks += gameplay_delta
        gameplay_intervals += 1
        missed_gameplay = missed_gameplay + 1 if gameplay_delta <= 0 else 0
        if missed_gameplay > budgets["maximumConsecutiveMissedGameplayTicks"]:
            return {
                "verdict": "gameplay-cadence-gap",
                "firstFailure": {"code": "gameplay-cadence-gap", "frame": frame, "missedFrames": missed_gameplay},
                "gameplayTickRatio": gameplay_ticks / gameplay_intervals,
            }
        if not audio_expected.get(frame, False):
            missed_audio = 0
            continue
        audio_delta = (state_value(rows[frame], "packedAudioTick") - state_value(previous, "packedAudioTick")) & 0xFF
        missed_audio = missed_audio + 1 if audio_delta <= 0 else 0
        if missed_audio > budgets["maximumUnplannedAudioGapFrames"]:
            return {
                "verdict": "audio-service-gap",
                "firstFailure": {"code": "audio-service-gap", "frame": frame, "missedFrames": missed_audio},
                "gameplayTickRatio": gameplay_ticks / gameplay_intervals,
            }
    gameplay_tick_ratio = gameplay_ticks / gameplay_intervals if gameplay_intervals else 0
    if gameplay_tick_ratio < budgets["minimumGameplayTickRatio"]:
        last_frame = max(measurement_frames if measurement_frames is not None else rows)
        return {
            "verdict": "gameplay-tick-ratio",
            "firstFailure": {"code": "gameplay-tick-ratio", "frame": last_frame, "observed": gameplay_tick_ratio},
            "gameplayTickRatio": gameplay_tick_ratio,
        }
    return {"verdict": "NOT_REPRODUCED", "firstFailure": None, "gameplayTickRatio": gameplay_tick_ratio}


def owner_if(condition: bool, owner: str) -> str | None:
    return owner if condition else None


def counter_delta_alignment(
    expected: dict[int, dict[str, Any]],
    actual: dict[int, dict[str, Any]],
    expected_baseline: dict[str, Any],
    actual_baseline: dict[str, Any],
    field: str,
) -> dict[str, Any]:
    candidates: list[dict[str, Any]] = []
    for offset in range(-2, 3):
        mismatches = []
        comparisons = 0
        for frame in sorted(expected):
            actual_frame = frame + offset
            expected_previous = previous_row(expected, expected_baseline, frame)
            actual_previous = previous_row(actual, actual_baseline, actual_frame)
            if (
                actual_frame not in actual
                or expected_previous is None
                or actual_previous is None
                or expected_previous["frame"] == expected_baseline["frame"]
                or actual_previous["frame"] == actual_baseline["frame"]
            ):
                continue
            comparisons += 1
            expected_delta = (state_value(expected[frame], field) - state_value(expected_previous, field)) & 0xFF
            actual_delta = (state_value(actual[actual_frame], field) - state_value(actual_previous, field)) & 0xFF
            if expected_delta != actual_delta and len(mismatches) < 8:
                mismatches.append(
                    {
                        "frame": frame,
                        "sameboyFrame": actual_frame,
                        "expectedDelta": expected_delta,
                        "actualDelta": actual_delta,
                    }
                )
        # Keep the full count even though the JSON deliberately bounds examples.
        mismatch_count = sum(
            1
            for frame in expected
            if previous_row(expected, expected_baseline, frame) is not None
            and previous_row(actual, actual_baseline, frame + offset) is not None
            and frame + offset in actual
            and previous_row(expected, expected_baseline, frame)["frame"] != expected_baseline["frame"]
            and previous_row(actual, actual_baseline, frame + offset)["frame"] != actual_baseline["frame"]
            and ((state_value(expected[frame], field) - state_value(previous_row(expected, expected_baseline, frame), field)) & 0xFF)
            != ((state_value(actual[frame + offset], field) - state_value(previous_row(actual, actual_baseline, frame + offset), field)) & 0xFF)
        )
        candidates.append(
            {
                "frameOffset": offset,
                "comparisons": comparisons,
                "mismatchCount": mismatch_count,
                "mismatchRows": mismatches,
            }
        )
    best = min(
        candidates,
        key=lambda candidate: (candidate["mismatchCount"], -candidate["comparisons"], abs(candidate["frameOffset"])),
    )
    return {
        "bestFrameOffset": best["frameOffset"],
        "comparisons": best["comparisons"],
        "mismatchCount": best["mismatchCount"],
        "mismatchRows": best["mismatchRows"],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True, help="path to libgameboy_debug_sameboy.so")
    parser.add_argument("--rom", type=Path, required=True, help="fresh ROM emitted by the in-process gate")
    parser.add_argument("--timeline", type=Path, required=True, help="replay timeline emitted by the in-process gate")
    parser.add_argument("--in-process-report", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    report = json.loads(args.in_process_report.read_text())
    replay = json.loads(args.timeline.read_text())
    try:
        validate_replay_contract(report, replay)
        rom_sha256 = verify_rom_identity(args.rom.read_bytes(), report, replay)
    except ValueError as error:
        raise SystemExit(str(error)) from error

    in_process = in_process_rows(report)
    in_process_baseline = report["baseline"]
    host_projection = host_projection_first_mismatch(in_process)
    frames_to_keep = set(in_process) | {in_process_baseline["frame"] + offset for offset in range(-2, 3)}
    replays = [sameboy_rows(args.library, args.rom, replay, frames_to_keep) for _ in range(3)]
    replay_digests = [normalized_replay_digest(rows, in_process_baseline["frame"]) for rows in replays]
    if len(set(replay_digests)) != 1:
        raise SystemExit(f"SameBoy repeated replays were not deterministic: {replay_digests}")
    sameboy = replays[0]
    sameboy_baseline = sameboy[in_process_baseline["frame"]]
    counter_mismatches = first_counter_delta_mismatches(
        in_process,
        sameboy,
        in_process_baseline,
        sameboy_baseline,
        ("romGameplayTick", "packedAudioTick", "camera.request", "camera.resident", "camera.commit", "camera.visible"),
    )
    state_mismatches = first_state_mismatches(in_process, sameboy)
    visual_mismatches = first_visual_mismatches(in_process, sameboy)
    same_counter_failed = any(value is not None for value in counter_mismatches.values())
    state_visual_failed = any(value is not None for value in state_mismatches.values()) or any(
        value is not None for value in visual_mismatches.values()
    )
    same_counter_verdict = "BACKEND_FRAME_BOUNDARY_DISCREPANCY" if same_counter_failed else "MATCH"
    host_projection_failed = any(frame is not None for frame in host_projection.values())
    state_visual_verdict = "STATE_OR_VISUAL_DISCREPANCY" if state_visual_failed else "MATCH"
    in_process_cadence = classify_cadence(in_process, in_process_baseline, replay)
    sameboy_cadence = classify_cadence(sameboy, sameboy_baseline, replay, set(in_process))
    backend_owner = owner_if(
        same_counter_failed,
        "GameBoyTestCpu fixed-cycle frame bucketing; not production runtime or lowering",
    )
    projection_owner = owner_if(
        host_projection_failed,
        "AudioUpdateCalls projection; not production runtime or lowering",
    )
    state_visual_owner = owner_if(
        state_visual_failed,
        "GameBoyTestCpu frame observation boundary; not production runtime or lowering",
    )
    cadence_owner = owner_if(
        in_process_cadence["verdict"] != sameboy_cadence["verdict"],
        "GameBoyTestCpu fixed-cycle frame bucketing; not production runtime or lowering",
    )
    result = {
        "schema": COMPARISON_SCHEMA,
        "generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "sameBoyLibrarySha256": hashlib.sha256(args.library.read_bytes()).hexdigest(),
        "romSha256": rom_sha256,
        "timelineSha256": hashlib.sha256(args.timeline.read_bytes()).hexdigest(),
        "timelineSchema": replay["schema"],
        "observedFrames": len(in_process),
        "sameboyReplayCount": len(replays),
        "sameboyReplayDigests": replay_digests,
        "sameboyDeterministic": True,
        "sameCounterFirstMismatch": {
            "gameplay": counter_mismatches["romGameplayTick"],
            "audio": counter_mismatches["packedAudioTick"],
        },
        "counterDeltaFirstMismatch": counter_mismatches,
        "stateFirstMismatch": state_mismatches,
        "visualFirstMismatch": visual_mismatches,
        "counterDeltaAlignment": {
            "gameplay": counter_delta_alignment(in_process, sameboy, in_process_baseline, sameboy_baseline, "romGameplayTick"),
            "audio": counter_delta_alignment(in_process, sameboy, in_process_baseline, sameboy_baseline, "packedAudioTick"),
        },
        "sameCounterVerdict": same_counter_verdict,
        "hostProjectionFirstMismatch": host_projection,
        "stateVisualVerdict": state_visual_verdict,
        "projectionVerdict": "COUNTER_PROJECTION_DISCREPANCY" if host_projection_failed else "MATCH",
        "cadenceClassification": {
            "inProcess": in_process_cadence,
            "sameboy": sameboy_cadence,
        },
        "overallVerdict": {
            "backendFrameBoundary": same_counter_verdict,
            "hostCounterProjection": "COUNTER_PROJECTION_DISCREPANCY" if host_projection_failed else "MATCH",
            "stateOrVisual": state_visual_verdict,
        },
        "owners": {
            "backendFrameBoundary": backend_owner,
            "hostCounterProjection": projection_owner,
            "stateOrVisual": state_visual_owner,
            "cadence": cadence_owner,
        },
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
