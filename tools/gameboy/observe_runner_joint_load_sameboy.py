#!/usr/bin/env python3
"""Emit the runner's authoritative physical-frame timeline through SameBoy.

The input descriptor owns ROM identity, input, layout, and reviewed budgets.
GameBoyTestCpu reports and host counters are deliberately not accepted here.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys
from typing import Any


sys.path.insert(0, str(Path(__file__).resolve().parent))
import compare_runner_joint_load_sameboy as shared  # noqa: E402


OBSERVER_SCHEMA = "retrosharp-rph62a-sameboy-physical-timeline-v1"
REPLAY_COUNT = 3


def measurement_frames(replay: dict[str, Any]) -> set[int]:
    first = replay["warmUpFrames"] + 1
    return set(range(first, first + replay["observationFrames"]))


def verify_observer_inputs(
    library: Path,
    rom: Path,
    timeline: Path,
    replay: dict[str, Any],
) -> dict[str, str]:
    shared.validate_replay_descriptor(replay, require_physical_camera_budgets=True)
    rom_bytes = rom.read_bytes()
    rom_sha256 = hashlib.sha256(rom_bytes).hexdigest()
    if rom_sha256 != replay["romSha256"]:
        raise ValueError("ROM SHA-256 does not match the replay timeline.")
    return {
        "romSha256": rom_sha256,
        "timelineSha256": hashlib.sha256(timeline.read_bytes()).hexdigest(),
        "sameBoyLibrarySha256": hashlib.sha256(library.read_bytes()).hexdigest(),
    }


def normalized_physical_rows(
    rows: dict[int, dict[str, Any]],
    baseline_frame: int,
) -> dict[int, dict[str, Any]]:
    return shared.normalize_replay_rows(rows, baseline_frame)


def first_determinism_failure(
    replays: list[dict[int, dict[str, Any]]],
    baseline_frame: int,
) -> dict[str, Any] | None:
    normalized = [
        normalized_physical_rows(rows, baseline_frame)
        for rows in replays
    ]
    expected = normalized[0]
    for replay_index, actual in enumerate(normalized[1:], start=2):
        for frame in sorted(expected):
            if actual.get(frame) != expected[frame]:
                return {
                    "code": "non-deterministic-physical-timeline",
                    "frame": frame,
                    "replay": replay_index,
                }
    return None


def first_forbidden_video_failure(
    rows: dict[int, dict[str, Any]],
    frames: set[int],
) -> dict[str, Any] | None:
    for frame in sorted(frames):
        count = shared.state_value(rows[frame], "forbiddenVideoWork")
        if count:
            return {
                "code": "forbidden-video-work",
                "frame": frame,
                "count": count,
            }
    return None


def first_camera_visible_failure(
    rows: dict[int, dict[str, Any]],
    baseline: dict[str, Any],
    frames: set[int],
    maximum_request_to_visible_frames: int,
) -> dict[str, Any] | None:
    previous_request = shared.state_value(baseline, "camera.request")
    pending: list[tuple[int, int]] = []
    for frame in sorted(frames):
        request = shared.state_value(rows[frame], "camera.request")
        visible = shared.state_value(rows[frame], "camera.visible")
        pending.extend(
            (sequence, frame)
            for sequence in range(previous_request + 1, request + 1)
        )
        previous_request = request
        pending = [
            (sequence, requested_frame)
            for sequence, requested_frame in pending
            if sequence > visible
        ]
        overdue = next(
            (
                (sequence, requested_frame)
                for sequence, requested_frame in pending
                if frame - requested_frame > maximum_request_to_visible_frames
            ),
            None,
        )
        if overdue is not None:
            sequence, requested_frame = overdue
            return {
                "code": "camera-visible-gap",
                "frame": frame,
                "sequence": sequence,
                "requestedFrame": requested_frame,
                "maximumFrames": maximum_request_to_visible_frames,
            }
    return None


def authoritative_classification(
    rows: dict[int, dict[str, Any]],
    replay: dict[str, Any],
) -> dict[str, Any]:
    frames = measurement_frames(replay)
    baseline = rows[replay["warmUpFrames"]]
    cadence = shared.classify_cadence(rows, baseline, replay, frames)
    camera = first_camera_visible_failure(
        rows,
        baseline,
        frames,
        replay["cadence"]["maximumRequestToVisibleFrames"],
    )
    forbidden = first_forbidden_video_failure(rows, frames)
    failures = [
        failure
        for failure in (cadence["firstFailure"], camera, forbidden)
        if failure is not None
    ]
    first_failure = min(
        failures,
        key=lambda failure: (failure["frame"], failure["code"]),
        default=None,
    )
    return {
        "verdict": first_failure["code"] if first_failure else "NOT_REPRODUCED",
        "firstFailure": first_failure,
        "cadence": cadence,
        "cameraVisible": camera,
        "forbiddenVideoWork": forbidden,
    }


def freeze_counter(
    rows: dict[int, dict[str, Any]],
    field: str,
    start_frame: int,
    duration_frames: int,
) -> None:
    frozen_value = shared.state_value(rows[start_frame - 1], field)
    for frame in range(start_frame, start_frame + duration_frames):
        rows[frame]["state"][field] = frozen_value


def first_counter_gap_failure(
    rows: dict[int, dict[str, Any]],
    field: str,
    frames: set[int],
    maximum_missed_frames: int,
    code: str,
    *,
    audio_expected: dict[int, bool] | None = None,
) -> dict[str, Any] | None:
    missed = 0
    for frame in sorted(frames):
        if audio_expected is not None and not audio_expected.get(frame, False):
            missed = 0
            continue
        previous = rows[frame - 1]
        delta = (
            shared.state_value(rows[frame], field)
            - shared.state_value(previous, field)
        ) & 0xFF
        missed = missed + 1 if delta <= 0 else 0
        if missed > maximum_missed_frames:
            return {
                "code": code,
                "frame": frame,
                "missedFrames": missed,
            }
    return None


def canary_start(frames: set[int], duration_frames: int, preferred_offset: int) -> int:
    first = min(frames)
    latest = max(frames) - duration_frames + 1
    if latest < first:
        raise ValueError(
            f"Observation window is too short for a {duration_frames}-frame canary."
        )
    return min(first + preferred_offset, latest)


def canary_proof(
    canary_id: str,
    expected: dict[str, Any],
    observed: dict[str, Any] | None,
    rows: dict[int, dict[str, Any]],
) -> dict[str, Any]:
    expected_identity = (expected["code"], expected["frame"])
    observed_identity = None if observed is None else (observed["code"], observed["frame"])
    return {
        "id": canary_id,
        "expectedFirstFailure": expected,
        "observedFirstFailure": observed,
        "transcriptDigest": shared.digest(rows),
        "passed": observed_identity == expected_identity,
    }


def earliest_failure(
    *failures: dict[str, Any] | None,
) -> dict[str, Any] | None:
    return min(
        (failure for failure in failures if failure is not None),
        key=lambda failure: (failure["frame"], failure["code"]),
        default=None,
    )


def first_failed_canary(
    canaries: list[dict[str, Any]],
) -> dict[str, Any] | None:
    failed = [canary for canary in canaries if not canary["passed"]]
    if not failed:
        return None
    canary = min(
        failed,
        key=lambda item: (
            (item["observedFirstFailure"] or item["expectedFirstFailure"])["frame"],
            item["id"],
        ),
    )
    failure = canary["observedFirstFailure"] or canary["expectedFirstFailure"]
    return {
        "code": "observer-canary-failed",
        "frame": failure["frame"],
        "canary": canary["id"],
    }


def build_canary_proofs(
    authoritative_rows: dict[int, dict[str, Any]],
    replay: dict[str, Any],
) -> list[dict[str, Any]]:
    frames = measurement_frames(replay)
    first_frame = min(frames)

    gameplay_duration = replay["cadence"]["maximumConsecutiveMissedGameplayTicks"] + 1
    gameplay_start = canary_start(frames, gameplay_duration, preferred_offset=9)
    gameplay = copy.deepcopy(authoritative_rows)
    freeze_counter(gameplay, "romGameplayTick", gameplay_start, gameplay_duration)
    gameplay_canary_frames = set(range(gameplay_start, gameplay_start + gameplay_duration))
    gameplay_failure = first_counter_gap_failure(
        gameplay,
        "romGameplayTick",
        gameplay_canary_frames,
        replay["cadence"]["maximumConsecutiveMissedGameplayTicks"],
        "gameplay-cadence-gap",
    )
    gameplay_expected = {
        "code": "gameplay-cadence-gap",
        "frame": gameplay_start + gameplay_duration - 1,
    }

    audio_duration = replay["cadence"]["maximumUnplannedAudioGapFrames"] + 1
    audio_start = canary_start(frames, audio_duration, preferred_offset=9)
    audio = copy.deepcopy(authoritative_rows)
    freeze_counter(audio, "packedAudioTick", audio_start, audio_duration)
    audio_canary_frames = set(range(audio_start, audio_start + audio_duration))
    audio_expected_by_frame = {
        item["frame"]: item["audioServiceExpected"]
        for item in replay["frames"]
    }
    for frame in audio_canary_frames:
        audio_expected_by_frame[frame] = True
    audio_failure = first_counter_gap_failure(
        audio,
        "packedAudioTick",
        audio_canary_frames,
        replay["cadence"]["maximumUnplannedAudioGapFrames"],
        "audio-service-gap",
        audio_expected=audio_expected_by_frame,
    )
    audio_expected = {
        "code": "audio-service-gap",
        "frame": audio_start + audio_duration - 1,
    }

    camera_limit = replay["cadence"]["maximumRequestToVisibleFrames"]
    camera_duration = camera_limit + 2
    camera_request_frame = canary_start(frames, camera_duration, preferred_offset=16)
    camera = copy.deepcopy(authoritative_rows)
    camera_baseline = camera[camera_request_frame - 1]
    held_visible = shared.state_value(camera_baseline, "camera.visible")
    injected_request = shared.state_value(camera_baseline, "camera.request") + 1
    camera_canary_frames = set(
        range(camera_request_frame, camera_request_frame + camera_duration)
    )
    for frame in camera_canary_frames:
        camera[frame]["state"]["camera"]["request"] = injected_request
        camera[frame]["state"]["camera"]["visible"] = held_visible
    camera_failure = first_camera_visible_failure(
        camera,
        camera_baseline,
        camera_canary_frames,
        camera_limit,
    )
    camera_expected = {
        "code": "camera-visible-gap",
        "frame": camera_request_frame + camera_limit + 1,
    }

    oam_frame = min(first_frame + 23, max(frames))
    oam = copy.deepcopy(authoritative_rows)
    oam[oam_frame]["state"]["oamDigest"] = shared.digest(
        ["controlled-oam-corruption", oam[oam_frame]["state"]["oamDigest"]]
    )
    oam_mismatch = shared.first_visual_mismatches(authoritative_rows, oam)["oamDigest"]
    oam_failure = None if oam_mismatch is None else {
        "code": "sprite-oam",
        "frame": oam_mismatch["frame"],
    }
    oam_expected = {"code": "sprite-oam", "frame": oam_frame}

    return [
        canary_proof("gameplay-freeze", gameplay_expected, gameplay_failure, gameplay),
        canary_proof("audio-freeze", audio_expected, audio_failure, audio),
        canary_proof("camera-visible-delay", camera_expected, camera_failure, camera),
        canary_proof("oam-corruption", oam_expected, oam_failure, oam),
    ]


def observe(
    library: Path,
    rom: Path,
    timeline: Path,
    replay: dict[str, Any],
) -> dict[str, Any]:
    provenance = verify_observer_inputs(library, rom, timeline, replay)
    frames = measurement_frames(replay)
    baseline_frame = replay["warmUpFrames"]
    frames_to_keep = frames | {baseline_frame}
    replays = [
        shared.sameboy_rows(library, rom, replay, frames_to_keep)
        for _ in range(REPLAY_COUNT)
    ]
    replay_digests = [
        shared.digest(normalized_physical_rows(rows, baseline_frame))
        for rows in replays
    ]
    determinism_failure = first_determinism_failure(replays, baseline_frame)
    normalized = normalized_physical_rows(replays[0], baseline_frame)
    classification = authoritative_classification(normalized, replay)
    canaries = build_canary_proofs(normalized, replay)
    canaries_passed = all(canary["passed"] for canary in canaries)
    canary_failure = first_failed_canary(canaries)
    first_failure = earliest_failure(
        determinism_failure,
        classification["firstFailure"],
        canary_failure,
    )
    replay_frames = {
        item["frame"]: item
        for item in replay["frames"]
    }
    return {
        "schema": OBSERVER_SCHEMA,
        "authority": {
            "backend": "SameBoy",
            "physicalFrameBoundary": "GB_run_frame",
            "inputAppliedBeforeFrameBoundary": True,
            "gameBoyTestCpuPhysicalAuthority": False,
            "hostCountersConsumed": [],
        },
        **provenance,
        "timelineSchema": replay["schema"],
        "warmUpFrames": replay["warmUpFrames"],
        "observedFrames": replay["observationFrames"],
        "replayCount": len(replays),
        "replayDigests": replay_digests,
        "deterministic": determinism_failure is None,
        "deterministicDigest": replay_digests[0] if determinism_failure is None else None,
        "observerFirstFailure": earliest_failure(
            determinism_failure,
            canary_failure,
        ),
        "verdict": first_failure["code"] if first_failure else "NOT_REPRODUCED",
        "firstFailure": first_failure,
        "classification": classification,
        "canariesPassed": canaries_passed,
        "canaries": canaries,
        "baseline": normalized[baseline_frame],
        "frames": [
            {
                **normalized[frame],
                "inputMask": replay_frames[frame]["inputMask"],
                "audioServiceExpected": replay_frames[frame]["audioServiceExpected"],
            }
            for frame in sorted(frames)
        ],
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True, help="path to libgameboy_debug_sameboy.so")
    parser.add_argument("--rom", type=Path, required=True, help="fresh runner ROM")
    parser.add_argument("--timeline", type=Path, required=True, help="ROM/input/layout replay descriptor")
    parser.add_argument("--out", type=Path, required=True, help="authoritative physical-frame JSON")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    replay = json.loads(args.timeline.read_text())
    try:
        result = observe(args.library, args.rom, args.timeline, replay)
    except (OSError, ValueError, RuntimeError) as error:
        raise SystemExit(str(error)) from error
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({
        "out": str(args.out),
        "verdict": result["verdict"],
        "firstFailure": result["firstFailure"],
        "deterministicDigest": result["deterministicDigest"],
        "canariesPassed": result["canariesPassed"],
    }, sort_keys=True))
    return 0 if result["deterministic"] and result["canariesPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
