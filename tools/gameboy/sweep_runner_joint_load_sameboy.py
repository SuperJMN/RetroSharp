#!/usr/bin/env python3
"""Sweep the full runner's jump/SFX phase on one authoritative SameBoy timeline."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import sys
import tempfile
from typing import Any, Callable


sys.path.insert(0, str(Path(__file__).resolve().parent))
import compare_runner_joint_load_sameboy as comparison  # noqa: E402
import observe_runner_joint_load_sameboy as observer  # noqa: E402


MATRIX_SCHEMA = "retrosharp-rph63-runner-phase-matrix-v2"
A_INPUT_BIT = 0b100
RIGHT_B_INPUT_BITS = 0b011
PHASE_RADIUS = 10
CONFIRMATION_RUNS = 2
DISAGREEMENT_RUNS = 3
A_DURATION_FRAMES = 6
EXPECTED_WARM_UP_FRAMES = 320
EXPECTED_OBSERVATION_FRAMES = 360
EXPECTED_AUTHORED_A_START = 340
EXPECTED_CADENCE = {
    "minimumGameplayTickRatio": 0,
    "maximumConsecutiveMissedGameplayTicks": 1,
    "maximumUnplannedAudioGapFrames": 1,
    "maximumRequestToVisibleFrames": 2,
}
CANARY_IDS = {
    "gameplay-freeze",
    "audio-freeze",
    "camera-visible-delay",
    "oam-corruption",
}
CADENCE_VERDICTS = {
    "NOT_REPRODUCED",
    "gameplay-cadence-gap",
    "audio-service-gap",
    "gameplay-tick-ratio",
}


def input_frames(replay: dict[str, Any], bit: int) -> list[int]:
    return [
        item["frame"]
        for item in replay["frames"]
        if item["inputMask"] & bit
    ]


def contiguous_span(frames: list[int], description: str) -> tuple[int, int]:
    if not frames or frames != list(range(frames[0], frames[-1] + 1)):
        raise ValueError(f"{description} must be one non-empty contiguous frame span.")
    return frames[0], len(frames)


def phase_starts(replay: dict[str, Any]) -> list[int]:
    authored_start, duration = contiguous_span(
        input_frames(replay, A_INPUT_BIT),
        "Authored A input",
    )
    first = authored_start - PHASE_RADIUS
    last = authored_start + PHASE_RADIUS
    timeline_last = replay["warmUpFrames"] + replay["observationFrames"]
    if first <= replay["warmUpFrames"] or last + duration - 1 > timeline_last:
        raise ValueError("The authored A span has insufficient room for the bounded phase sweep.")
    return list(range(first, last + 1))


def validate_full_load_replay(replay: dict[str, Any]) -> tuple[int, int, int]:
    if (
        replay["warmUpFrames"] != EXPECTED_WARM_UP_FRAMES
        or replay["observationFrames"] != EXPECTED_OBSERVATION_FRAMES
        or replay["cadence"] != EXPECTED_CADENCE
    ):
        raise ValueError("The replay does not match the fixed RPH-6.3 frame and budget contract.")
    observation_start = replay["warmUpFrames"] + 1
    observation_end = replay["warmUpFrames"] + replay["observationFrames"]
    observation_frames = list(range(observation_start, observation_end + 1))
    right_b_frames = [
        item["frame"]
        for item in replay["frames"]
        if (item["inputMask"] & RIGHT_B_INPUT_BITS) == RIGHT_B_INPUT_BITS
    ]
    if right_b_frames != observation_frames:
        raise ValueError("RIGHT+B must cover every observation frame in the full-load replay.")
    a_start, a_duration = contiguous_span(
        input_frames(replay, A_INPUT_BIT),
        "Authored A input",
    )
    if a_duration != A_DURATION_FRAMES:
        raise ValueError(f"Authored A input must last exactly {A_DURATION_FRAMES} frames.")
    if a_start != EXPECTED_AUTHORED_A_START:
        raise ValueError(
            f"Authored A input must start at frame {EXPECTED_AUTHORED_A_START}.",
        )
    if not all(
        item["audioServiceExpected"]
        for item in replay["frames"]
        if item["frame"] in observation_frames
    ):
        raise ValueError("Audio service must be expected on every observation frame.")
    return a_start, a_duration, observation_start


def replay_for_a_start(
    replay: dict[str, Any],
    start_frame: int,
) -> dict[str, Any]:
    _, duration = contiguous_span(input_frames(replay, A_INPUT_BIT), "Authored A input")
    mutated = copy.deepcopy(replay)
    by_frame = {item["frame"]: item for item in mutated["frames"]}
    for item in mutated["frames"]:
        item["inputMask"] &= ~A_INPUT_BIT
    for frame in range(start_frame, start_frame + duration):
        if frame not in by_frame:
            raise ValueError(f"A phase frame {frame} is outside the replay timeline.")
        by_frame[frame]["inputMask"] |= A_INPUT_BIT
    return mutated


def write_replay(path: Path, replay: dict[str, Any]) -> None:
    path.write_text(json.dumps(replay, indent=2) + "\n")


def frame_coverage(result: dict[str, Any]) -> dict[str, Any]:
    frames = result["frames"]
    states = [item["state"] for item in frames]
    return {
        "rightBInputFrames": sum(
            (item["inputMask"] & RIGHT_B_INPUT_BITS) == RIGHT_B_INPUT_BITS
            for item in frames
        ),
        "aInputFrames": sum(bool(item["inputMask"] & A_INPUT_BIT) for item in frames),
        "playerXRange": [min(state["playerX"] for state in states), max(state["playerX"] for state in states)],
        "playerYRange": [min(state["playerY"] for state in states), max(state["playerY"] for state in states)],
        "playerYEndpoints": [states[0]["playerY"], states[-1]["playerY"]],
        "visibleCameraXRange": [
            min(state["visibleCameraX"] for state in states),
            max(state["visibleCameraX"] for state in states),
        ],
        "cameraRequestDelta": states[-1]["camera"]["request"] - result["baseline"]["state"]["camera"]["request"],
        "visibleBanks": sorted({state["shadowRomBank"] for state in states}),
        "musicActiveFrames": sum(bool(state["musicActive"]) for state in states),
        "sfxActiveFrames": sum(bool(state["sfxActive"]) for state in states),
        "backgroundDigests": len({state["backgroundDigest"] for state in states}),
        "oamDigests": len({state["oamDigest"] for state in states}),
    }


def recomputed_observer_matches(
    result: dict[str, Any],
    replay: dict[str, Any],
) -> bool:
    try:
        rows = {
            result["baseline"]["frame"]: result["baseline"],
            **{
                frame["frame"]: {
                    "frame": frame["frame"],
                    "state": frame["state"],
                }
                for frame in result["frames"]
            },
        }
        classification = observer.authoritative_classification(rows, replay)
        canaries = observer.build_canary_proofs(rows, replay)
        canary_failure = observer.first_failed_canary(canaries)
        first_failure = observer.earliest_failure(
            classification["firstFailure"],
            canary_failure,
        )
        verdict = first_failure["code"] if first_failure else "NOT_REPRODUCED"
    except (KeyError, TypeError, ValueError):
        return False
    return (
        result.get("classification") == classification
        and result.get("canaries") == canaries
        and result.get("canariesPassed") == all(canary["passed"] for canary in canaries)
        and result.get("observerFirstFailure") == canary_failure
        and result.get("firstFailure") == first_failure
        and result.get("verdict") == verdict
    )


def observation_contract_errors(
    result: dict[str, Any],
    *,
    rom_sha256: str,
    timeline_sha256: str,
    library_sha256: str,
    replay: dict[str, Any],
) -> list[str]:
    errors = []
    authority = result.get("authority")
    replay_digests = result.get("replayDigests")
    expected_frames = list(range(
        replay["warmUpFrames"] + 1,
        replay["warmUpFrames"] + replay["observationFrames"] + 1,
    ))
    observed_frames = result.get("frames")
    expected_inputs = {
        item["frame"]: (
            item["inputMask"],
            item["audioServiceExpected"],
        )
        for item in replay["frames"]
        if item["frame"] in expected_frames
    }
    classification = result.get("classification")
    checks = (
        (result.get("schema") == observer.OBSERVER_SCHEMA, "observer schema"),
        (result.get("romSha256") == rom_sha256, "ROM provenance"),
        (result.get("timelineSha256") == timeline_sha256, "timeline provenance"),
        (result.get("sameBoyLibrarySha256") == library_sha256, "SameBoy library provenance"),
        (result.get("timelineSchema") == comparison.REPLAY_SCHEMA, "timeline schema"),
        (result.get("warmUpFrames") == replay["warmUpFrames"], "warm-up frame count"),
        (result.get("observedFrames") == replay["observationFrames"], "observation frame count"),
        (result.get("replayCount") == observer.REPLAY_COUNT, "SameBoy replay count"),
        (result.get("deterministic") is True, "physical determinism"),
        (result.get("canariesPassed") is True, "observer canaries"),
        (
            isinstance(authority, dict)
            and authority.get("backend") == "SameBoy"
            and authority.get("physicalFrameBoundary") == "GB_run_frame"
            and authority.get("inputAppliedBeforeFrameBoundary") is True
            and authority.get("gameBoyTestCpuPhysicalAuthority") is False
            and authority.get("hostCountersConsumed") == [],
            "physical authority",
        ),
        (
            isinstance(replay_digests, list)
            and len(replay_digests) == observer.REPLAY_COUNT
            and len(set(replay_digests)) == 1
            and result.get("deterministicDigest") == replay_digests[0],
            "replay digests",
        ),
        (
            isinstance(observed_frames, list)
            and [
                item.get("frame") if isinstance(item, dict) else None
                for item in observed_frames
            ] == expected_frames,
            "physical frame coverage",
        ),
        (
            isinstance(observed_frames, list)
            and all(
                isinstance(item, dict)
                and (
                    item.get("inputMask"),
                    item.get("audioServiceExpected"),
                ) == expected_inputs.get(item.get("frame"))
                for item in observed_frames
            ),
            "applied input timeline",
        ),
        (
            isinstance(classification, dict)
            and classification.get("verdict") == result.get("verdict")
            and classification.get("firstFailure") == result.get("firstFailure")
            and result.get("observerFirstFailure") is None,
            "physical classification",
        ),
        (recomputed_observer_matches(result, replay), "recomputed observer result"),
    )
    errors.extend(description for passed, description in checks if not passed)
    return errors


def load_coverage_errors(
    coverage: dict[str, Any] | None,
    observation_frames: int,
) -> list[str]:
    if coverage is None:
        return ["unreadable load coverage"]
    checks = (
        (coverage["rightBInputFrames"] == observation_frames, "RIGHT+B coverage"),
        (coverage["aInputFrames"] == A_DURATION_FRAMES, "A/jump/SFX coverage"),
        (coverage["playerXRange"][0] < coverage["playerXRange"][1], "player movement"),
        (
            coverage["playerYRange"][0] < coverage["playerYRange"][1]
            and coverage["playerYEndpoints"][0] == coverage["playerYEndpoints"][1],
            "jump and landing",
        ),
        (coverage["visibleCameraXRange"][0] < coverage["visibleCameraXRange"][1], "visible camera movement"),
        (coverage["cameraRequestDelta"] > 0, "camera requests"),
        (bool(coverage["visibleBanks"]), "bank observation"),
        (coverage["musicActiveFrames"] == observation_frames, "BGM activity"),
        (coverage["sfxActiveFrames"] > 0, "SFX activity"),
        (coverage["backgroundDigests"] > 1, "background streaming"),
        (coverage["oamDigests"] > 1, "OAM activity"),
    )
    return [description for passed, description in checks if not passed]


def summarize_observation(
    result: dict[str, Any],
    *,
    rom_sha256: str,
    timeline_sha256: str,
    library_sha256: str,
    replay: dict[str, Any],
) -> dict[str, Any]:
    contract_errors = observation_contract_errors(
        result,
        rom_sha256=rom_sha256,
        timeline_sha256=timeline_sha256,
        library_sha256=library_sha256,
        replay=replay,
    )
    coverage = None
    try:
        coverage = frame_coverage(result)
    except (KeyError, TypeError, ValueError):
        contract_errors.append("load coverage shape")
    coverage_errors = load_coverage_errors(coverage, replay["observationFrames"])
    return {
        "timelineSha256": result.get("timelineSha256"),
        "deterministicDigest": result.get("deterministicDigest"),
        "verdict": result.get("verdict"),
        "firstFailure": result.get("firstFailure"),
        "observerFirstFailure": result.get("observerFirstFailure"),
        "canaries": result.get("canaries"),
        "contractErrors": contract_errors,
        "loadCoverageErrors": coverage_errors,
        "coverage": coverage,
    }


def case_summary(
    case_id: str,
    a_start_frame: int,
    a_duration_frames: int,
    right_b_span: list[int],
    runs: list[dict[str, Any]],
) -> dict[str, Any]:
    digests = [run["deterministicDigest"] for run in runs]
    identities = [
        (run["verdict"], run["firstFailure"])
        for run in runs
    ]
    timelines = [run["timelineSha256"] for run in runs]
    canaries = [run["canaries"] for run in runs]
    coverages = [run["coverage"] for run in runs]
    contract_errors = sorted({
        error
        for run in runs
        for error in run["contractErrors"]
    })
    load_coverage_errors = sorted({
        error
        for run in runs
        for error in run["loadCoverageErrors"]
    })
    valid = all(
        not run["contractErrors"]
        and not run["loadCoverageErrors"]
        for run in runs
    )
    repeatable = (
        len(set(digests)) == 1
        and len(set(timelines)) == 1
        and all(identity == identities[0] for identity in identities)
        and all(canary == canaries[0] for canary in canaries)
        and all(coverage == coverages[0] for coverage in coverages)
    )
    first_failure = observer.earliest_failure(
        *(run["firstFailure"] for run in runs),
    )
    verdict = (
        "OBSERVER_INVALID"
        if not valid
        else "NON_DETERMINISTIC_CASE"
        if not repeatable
        else "OUT_OF_SCOPE_PHYSICAL_FAILURE"
        if runs[0]["verdict"] not in CADENCE_VERDICTS
        else runs[0]["verdict"]
    )
    return {
        "caseId": case_id,
        "parentCaseId": None,
        "mutation": {
            "input": "A",
            "startFrame": a_start_frame,
            "durationFrames": a_duration_frames,
            "rightBSpan": right_b_span,
        },
        "runCount": len(runs),
        "validObserverContract": valid,
        "repeatable": repeatable,
        "verdict": verdict,
        "firstFailure": first_failure,
        "timelineSha256": timelines[0] if len(set(timelines)) == 1 else None,
        "physicalDigest": digests[0] if repeatable else None,
        "runDigests": digests,
        "runVerdicts": [
            {
                "verdict": run_verdict,
                "firstFailure": run_failure,
            }
            for run_verdict, run_failure in identities
        ],
        "contractErrors": contract_errors,
        "loadCoverageErrors": load_coverage_errors,
        "canaries": canaries[0] if repeatable else None,
        "coverage": coverages[0] if repeatable else None,
    }


def validate_comparison(
    report: dict[str, Any],
    rom_sha256: str,
    timeline_sha256: str,
    library_sha256: str,
    replay: dict[str, Any],
) -> dict[str, Any]:
    replay_digests = report.get("sameboyReplayDigests")
    checks = (
        (report.get("schema") == comparison.COMPARISON_SCHEMA, "comparison schema"),
        (
            report.get("generatorSha256")
            == hashlib.sha256(Path(comparison.__file__).read_bytes()).hexdigest(),
            "comparison generator SHA-256",
        ),
        (report.get("sameBoyLibrarySha256") == library_sha256, "SameBoy library SHA-256"),
        (report.get("romSha256") == rom_sha256, "ROM SHA-256"),
        (report.get("timelineSha256") == timeline_sha256, "timeline SHA-256"),
        (report.get("timelineSchema") == comparison.REPLAY_SCHEMA, "timeline schema"),
        (report.get("observedFrames") == replay["observationFrames"], "observation frame count"),
        (report.get("sameboyReplayCount") == observer.REPLAY_COUNT, "SameBoy replay count"),
        (report.get("sameboyDeterministic") is True, "SameBoy determinism"),
        (
            isinstance(replay_digests, list)
            and len(replay_digests) == observer.REPLAY_COUNT
            and len(set(replay_digests)) == 1,
            "SameBoy replay digests",
        ),
    )
    failed = [description for passed, description in checks if not passed]
    if failed:
        raise ValueError(f"The in-process comparison has invalid {failed[0]}.")
    cadence = report.get("cadenceClassification")
    if not isinstance(cadence, dict):
        raise ValueError("The in-process comparison lacks cadenceClassification.")
    first_frame = replay["warmUpFrames"] + 1
    last_frame = replay["warmUpFrames"] + replay["observationFrames"]
    for name in ("inProcess", "sameboy"):
        observed = cadence.get(name)
        if not isinstance(observed, dict):
            raise ValueError(f"The comparison has invalid {name} cadence.")
        verdict = observed.get("verdict")
        failure = observed.get("firstFailure")
        ratio = observed.get("gameplayTickRatio")
        coherent_green = verdict == "NOT_REPRODUCED" and failure is None
        coherent_red = (
            verdict in CADENCE_VERDICTS - {"NOT_REPRODUCED"}
            and isinstance(failure, dict)
            and failure.get("code") == verdict
            and isinstance(failure.get("frame"), int)
            and first_frame <= failure["frame"] <= last_frame
        )
        if (
            not (coherent_green or coherent_red)
            or not isinstance(ratio, (int, float))
            or isinstance(ratio, bool)
            or not math.isfinite(ratio)
            or ratio < 0
        ):
            raise ValueError(f"The comparison has incoherent {name} cadence.")
    return {
        "role": "behavioral diagnostics; not physical-frame authority",
        "inProcess": cadence["inProcess"],
        "sameBoyComparison": cadence["sameboy"],
    }


def execute_matrix(
    library: Path,
    rom: Path,
    timeline_path: Path,
    comparison_report: dict[str, Any],
    replay: dict[str, Any],
    out: Path,
    *,
    only_start: int | None = None,
    observe: Callable[[Path, Path, Path, dict[str, Any]], dict[str, Any]] = observer.observe,
) -> dict[str, Any]:
    comparison.validate_replay_descriptor(replay, require_physical_camera_budgets=True)
    authored_start, a_duration, observation_start = validate_full_load_replay(replay)
    rom_sha256 = hashlib.sha256(rom.read_bytes()).hexdigest()
    if replay["romSha256"] != rom_sha256:
        raise ValueError("ROM SHA-256 does not match the replay timeline.")
    timeline_sha256 = hashlib.sha256(timeline_path.read_bytes()).hexdigest()
    library_sha256 = hashlib.sha256(library.read_bytes()).hexdigest()
    behavioral = validate_comparison(
        comparison_report,
        rom_sha256,
        timeline_sha256,
        library_sha256,
        replay,
    )
    starts = phase_starts(replay)
    right_b_span = [
        observation_start,
        observation_start + replay["observationFrames"] - 1,
    ]
    if only_start is not None:
        if only_start not in starts:
            raise ValueError(f"A start frame {only_start} is outside the phase matrix.")
        starts = [only_start]

    cases = []
    first_stop = None
    with tempfile.TemporaryDirectory(prefix="retrosharp-rph63-") as directory:
        scratch = Path(directory)
        case_replays = {
            start_frame: replay_for_a_start(replay, start_frame)
            for start_frame in starts
        }
        case_timelines = {}
        for start_frame in starts:
            case_id = f"a-start-{start_frame}"
            case_timeline = scratch / f"{case_id}.timeline.json"
            write_replay(case_timeline, case_replays[start_frame])
            case_timelines[start_frame] = case_timeline
        runs_by_start: dict[int, list[dict[str, Any]]] = {
            start_frame: []
            for start_frame in starts
        }

        def capture_run(start_frame: int) -> dict[str, Any]:
            case_timeline = case_timelines[start_frame]
            case_replay = case_replays[start_frame]
            run = summarize_observation(
                observe(library, rom, case_timeline, case_replay),
                rom_sha256=rom_sha256,
                timeline_sha256=hashlib.sha256(case_timeline.read_bytes()).hexdigest(),
                library_sha256=library_sha256,
                replay=case_replay,
            )
            runs_by_start[start_frame].append(run)
            return case_summary(
                f"a-start-{start_frame}",
                start_frame,
                a_duration,
                right_b_span,
                runs_by_start[start_frame],
            )

        stop = False
        for _ in range(CONFIRMATION_RUNS):
            for start_frame in starts:
                current = capture_run(start_frame)
                if (
                    current["validObserverContract"]
                    and not current["repeatable"]
                    and current["runCount"] == CONFIRMATION_RUNS
                ):
                    current = capture_run(start_frame)
                if (
                    not current["validObserverContract"]
                    or not current["repeatable"]
                ):
                    first_stop = {
                        "caseId": current["caseId"],
                        "verdict": current["verdict"],
                        "firstFailure": current["firstFailure"],
                        "runCount": current["runCount"],
                    }
                    stop = True
                    break
                if current["verdict"] != "NOT_REPRODUCED":
                    while len(runs_by_start[start_frame]) < CONFIRMATION_RUNS:
                        current = capture_run(start_frame)
                    if (
                        current["validObserverContract"]
                        and not current["repeatable"]
                        and current["runCount"] == CONFIRMATION_RUNS
                    ):
                        current = capture_run(start_frame)
                    first_stop = {
                        "caseId": current["caseId"],
                        "verdict": current["verdict"],
                        "firstFailure": current["firstFailure"],
                        "runCount": current["runCount"],
                    }
                    stop = True
                    break
            if stop:
                break
        cases = [
            case_summary(
                f"a-start-{start_frame}",
                start_frame,
                a_duration,
                right_b_span,
                runs_by_start[start_frame],
            )
            for start_frame in starts
            if runs_by_start[start_frame]
        ]

    behavioral_reds = [
        {"observer": name, "result": observed}
        for name, observed in (
            ("inProcess", behavioral["inProcess"]),
            ("sameBoyComparison", behavioral["sameBoyComparison"]),
        )
        if observed["verdict"] != "NOT_REPRODUCED"
    ]
    stop_is_invalid = (
        first_stop is not None
        and first_stop["verdict"] in {
            "OBSERVER_INVALID",
            "NON_DETERMINISTIC_CASE",
            "OUT_OF_SCOPE_PHYSICAL_FAILURE",
        }
    )
    first_red = None if first_stop is None or stop_is_invalid else first_stop
    observer_invalid = (
        first_stop
        if stop_is_invalid
        else {
            "code": "BEHAVIORAL_OBSERVER_DISAGREEMENT",
            "observations": behavioral_reds,
        }
        if first_stop is None and behavioral_reds
        else None
    )
    complete_green_matrix = (
        first_stop is None
        and len(cases) == len(starts)
        and all(case["runCount"] == CONFIRMATION_RUNS for case in cases)
    )
    verdict = (
        "OBSERVER_INVALID"
        if observer_invalid is not None
        else "RED_REPRODUCED"
        if first_red is not None
        else "NOT_REPRODUCED"
        if complete_green_matrix
        else "OBSERVER_INVALID"
    )
    result = {
        "schema": MATRIX_SCHEMA,
        "generator": {
            "sweepSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            "comparisonSha256": hashlib.sha256(Path(comparison.__file__).read_bytes()).hexdigest(),
            "observerSha256": hashlib.sha256(Path(observer.__file__).read_bytes()).hexdigest(),
        },
        "romSha256": rom_sha256,
        "baseTimelineSha256": timeline_sha256,
        "inProcessComparisonDigest": comparison.digest(comparison_report),
        "behavioralObserver": behavioral,
        "physicalAuthority": {
            "backend": "SameBoy",
            "boundary": "GB_run_frame",
            "observerSchema": observer.OBSERVER_SCHEMA,
            "sameBoyLibrarySha256": library_sha256,
            "replaysPerRun": observer.REPLAY_COUNT,
            "canaryIds": sorted(CANARY_IDS),
            "gameBoyTestCpuPhysicalAuthority": False,
        },
        "matrix": {
            "input": "A jump/SFX",
            "authoredStartFrame": authored_start,
            "radiusFrames": PHASE_RADIUS,
            "phaseStarts": starts,
            "rightBSpan": right_b_span,
            "repeatCount": CONFIRMATION_RUNS,
            "disagreementRepeatCount": DISAGREEMENT_RUNS,
            "sameBoyReplaysPerRun": observer.REPLAY_COUNT,
        },
        "verdict": verdict,
        "behavioralRed": behavioral_reds[0] if behavioral_reds else None,
        "firstRed": first_red,
        "observerInvalid": observer_invalid,
        "completedCases": len(cases),
        "cases": cases,
    }
    result["matrixDigest"] = comparison.digest({
        "schema": result["schema"],
        "generator": result["generator"],
        "romSha256": result["romSha256"],
        "baseTimelineSha256": result["baseTimelineSha256"],
        "inProcessComparisonDigest": result["inProcessComparisonDigest"],
        "behavioralObserver": result["behavioralObserver"],
        "physicalAuthority": result["physicalAuthority"],
        "matrix": result["matrix"],
        "verdict": result["verdict"],
        "cases": result["cases"],
    })
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, indent=2) + "\n")
    return result


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--rom", type=Path, required=True)
    parser.add_argument("--timeline", type=Path, required=True)
    parser.add_argument("--in-process-comparison", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--a-start-frame", type=int)
    return parser


def exit_code(result: dict[str, Any]) -> int:
    return {
        "NOT_REPRODUCED": 0,
        "RED_REPRODUCED": 1,
        "OBSERVER_INVALID": 125,
    }[result["verdict"]]


def main() -> int:
    args = build_parser().parse_args()
    try:
        replay = json.loads(args.timeline.read_text())
        comparison_report = json.loads(args.in_process_comparison.read_text())
        result = execute_matrix(
            args.library,
            args.rom,
            args.timeline,
            comparison_report,
            replay,
            args.out,
            only_start=args.a_start_frame,
        )
    except (OSError, ValueError, RuntimeError, KeyError) as error:
        print(str(error), file=sys.stderr)
        return 125
    print(json.dumps({
        "out": str(args.out),
        "verdict": result["verdict"],
        "completedCases": result["completedCases"],
        "firstRed": result["firstRed"],
        "matrixDigest": result["matrixDigest"],
    }, sort_keys=True))
    return exit_code(result)


if __name__ == "__main__":
    raise SystemExit(main())
