import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


MODULE_PATH = Path(__file__).parents[1] / "sweep_runner_joint_load_sameboy.py"
SPEC = importlib.util.spec_from_file_location("rph63_sameboy_matrix", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class RunnerPhaseMatrixTests(unittest.TestCase):
    @staticmethod
    def replay() -> dict:
        layout = {
            field: index + 1
            for index, field in enumerate(MODULE.comparison.LAYOUT_FIELDS)
        }
        layout["forbiddenCounters"] = [20, 21, 22, 23, 24]
        frames = []
        for frame in range(1, 681):
            mask = 0
            if frame >= 321:
                mask |= MODULE.RIGHT_B_INPUT_BITS
            if 340 <= frame <= 345:
                mask |= MODULE.A_INPUT_BIT
            frames.append({
                "frame": frame,
                "inputMask": mask,
                "audioServiceExpected": True,
            })
        return {
            "schema": MODULE.comparison.REPLAY_SCHEMA,
            "romSha256": hashlib.sha256(b"rom").hexdigest(),
            "warmUpFrames": 320,
            "observationFrames": 360,
            "layout": layout,
            "cadence": {
                "minimumGameplayTickRatio": 0,
                "maximumConsecutiveMissedGameplayTicks": 1,
                "maximumUnplannedAudioGapFrames": 1,
                "maximumRequestToVisibleFrames": 2,
            },
            "frames": frames,
        }

    @staticmethod
    def comparison_report(replay: dict) -> dict:
        green = {
            "verdict": "NOT_REPRODUCED",
            "firstFailure": None,
            "gameplayTickRatio": 1,
        }
        return {
            "schema": MODULE.comparison.COMPARISON_SCHEMA,
            "generatorSha256": hashlib.sha256(
                Path(MODULE.comparison.__file__).read_bytes(),
            ).hexdigest(),
            "sameBoyLibrarySha256": hashlib.sha256(b"library").hexdigest(),
            "romSha256": replay["romSha256"],
            "timelineSha256": hashlib.sha256(
                (json.dumps(replay, indent=2) + "\n").encode(),
            ).hexdigest(),
            "timelineSchema": MODULE.comparison.REPLAY_SCHEMA,
            "observedFrames": replay["observationFrames"],
            "sameboyReplayCount": MODULE.observer.REPLAY_COUNT,
            "sameboyReplayDigests": ["comparison-digest"] * MODULE.observer.REPLAY_COUNT,
            "sameboyDeterministic": True,
            "cadenceClassification": {
                "inProcess": copy.deepcopy(green),
                "sameboy": copy.deepcopy(green),
            },
        }

    @staticmethod
    def fake_result(
        replay: dict,
        timeline: Path,
        *,
        red: bool = False,
        camera_red: bool = False,
    ) -> dict:
        observed = range(
            replay["warmUpFrames"] + 1,
            replay["warmUpFrames"] + replay["observationFrames"] + 1,
        )
        masks = {
            item["frame"]: item["inputMask"]
            for item in replay["frames"]
        }
        frames = []
        for index, frame in enumerate(observed, start=1):
            frames.append({
                "frame": frame,
                "inputMask": masks[frame],
                "audioServiceExpected": True,
                "state": {
                    "romGameplayTick": index,
                    "packedAudioTick": index,
                    "playerX": index,
                    "playerY": 10 + (5 if masks[frame] & MODULE.A_INPUT_BIT else 0),
                    "visibleCameraX": index,
                    "visibleCameraY": 0,
                    "shadowRomBank": 1,
                    "forbiddenVideoWork": 0,
                    "musicActive": 1,
                    "sfxActive": int(bool(masks[frame] & MODULE.A_INPUT_BIT)),
                    "camera": {
                        "request": index,
                        "resident": index,
                        "commit": index,
                        "visible": index,
                    },
                    "backgroundDigest": f"background-{index}",
                    "oamDigest": f"oam-{index}",
                },
            })
        baseline = {
            "frame": replay["warmUpFrames"],
            "state": {
                "romGameplayTick": 0,
                "packedAudioTick": 0,
                "playerX": 0,
                "playerY": 10,
                "visibleCameraX": 0,
                "visibleCameraY": 0,
                "shadowRomBank": 1,
                "forbiddenVideoWork": 0,
                "musicActive": 1,
                "sfxActive": 0,
                "camera": {
                    "request": 0,
                    "resident": 0,
                    "commit": 0,
                    "visible": 0,
                },
                "backgroundDigest": "background-baseline",
                "oamDigest": "oam-baseline",
            },
        }
        if red:
            frames[0]["state"]["packedAudioTick"] = 0
            frames[1]["state"]["packedAudioTick"] = 0
        if camera_red:
            for frame in frames[:4]:
                frame["state"]["camera"]["request"] = 1
                frame["state"]["camera"]["resident"] = 1
                frame["state"]["camera"]["commit"] = 1
                frame["state"]["camera"]["visible"] = 0
        rows = {
            baseline["frame"]: baseline,
            **{
                frame["frame"]: {
                    "frame": frame["frame"],
                    "state": frame["state"],
                }
                for frame in frames
            },
        }
        digest = MODULE.comparison.digest(rows)
        classification = MODULE.observer.authoritative_classification(rows, replay)
        canaries = MODULE.observer.build_canary_proofs(rows, replay)
        failure = classification["firstFailure"]
        return {
            "schema": MODULE.observer.OBSERVER_SCHEMA,
            "romSha256": replay["romSha256"],
            "timelineSha256": hashlib.sha256(timeline.read_bytes()).hexdigest(),
            "sameBoyLibrarySha256": hashlib.sha256(b"library").hexdigest(),
            "timelineSchema": MODULE.comparison.REPLAY_SCHEMA,
            "warmUpFrames": replay["warmUpFrames"],
            "observedFrames": replay["observationFrames"],
            "replayCount": 3,
            "replayDigests": [digest] * 3,
            "deterministic": True,
            "deterministicDigest": digest,
            "canariesPassed": True,
            "observerFirstFailure": None,
            "authority": {
                "backend": "SameBoy",
                "physicalFrameBoundary": "GB_run_frame",
                "inputAppliedBeforeFrameBoundary": True,
                "gameBoyTestCpuPhysicalAuthority": False,
                "hostCountersConsumed": [],
            },
            "verdict": classification["verdict"],
            "firstFailure": failure,
            "classification": classification,
            "canaries": canaries,
            "baseline": baseline,
            "frames": frames,
        }

    def test_phase_domain_is_every_frame_around_the_authored_a_start(self) -> None:
        self.assertEqual(list(range(330, 351)), MODULE.phase_starts(self.replay()))

    def test_phase_mutation_moves_only_the_a_span(self) -> None:
        replay = self.replay()
        original_without_masks = copy.deepcopy(replay)
        for item in original_without_masks["frames"]:
            del item["inputMask"]

        mutated = MODULE.replay_for_a_start(replay, 332)
        mutated_without_masks = copy.deepcopy(mutated)
        for item in mutated_without_masks["frames"]:
            del item["inputMask"]

        self.assertEqual(original_without_masks, mutated_without_masks)
        self.assertEqual(list(range(332, 338)), MODULE.input_frames(mutated, MODULE.A_INPUT_BIT))
        self.assertEqual(
            MODULE.input_frames(replay, MODULE.RIGHT_B_INPUT_BITS),
            MODULE.input_frames(mutated, MODULE.RIGHT_B_INPUT_BITS),
        )

    def test_full_load_contract_rejects_input_gaps_and_changed_a_duration(self) -> None:
        replay = self.replay()
        replay["observationFrames"] = 359
        with self.assertRaisesRegex(ValueError, "fixed RPH-6.3 frame and budget contract"):
            MODULE.validate_full_load_replay(replay)

        replay = self.replay()
        replay["frames"][330]["inputMask"] &= ~MODULE.RIGHT_B_INPUT_BITS
        with self.assertRaisesRegex(ValueError, "RIGHT\\+B must cover every observation frame"):
            MODULE.validate_full_load_replay(replay)

        replay = self.replay()
        replay["frames"][344]["inputMask"] &= ~MODULE.A_INPUT_BIT
        with self.assertRaisesRegex(ValueError, "A input must last exactly 6 frames"):
            MODULE.validate_full_load_replay(replay)

    def test_complete_green_matrix_confirms_every_phase_twice(self) -> None:
        replay = self.replay()
        calls = []

        def observe(_library, _rom, timeline, case_replay):
            calls.append(MODULE.input_frames(case_replay, MODULE.A_INPUT_BIT)[0])
            return self.fake_result(case_replay, timeline)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                observe=observe,
            )

        self.assertEqual("NOT_REPRODUCED", result["verdict"])
        self.assertIsNone(result["firstRed"])
        self.assertEqual(21, result["completedCases"])
        self.assertEqual(42, len(calls))
        self.assertEqual([330, 331, 332], calls[:3])
        self.assertEqual([348, 349, 350], calls[-3:])
        self.assertEqual(2, calls.count(330))
        self.assertEqual(2, calls.count(350))
        self.assertTrue(all(case["repeatable"] for case in result["cases"]))
        self.assertTrue(all(case["validObserverContract"] for case in result["cases"]))
        self.assertTrue(all(len(case["runDigests"]) == 2 for case in result["cases"]))
        self.assertTrue(all(len(case["canaries"]) == 4 for case in result["cases"]))
        self.assertTrue(all("runs" not in case for case in result["cases"]))
        self.assertEqual(0, MODULE.exit_code(result))

    def test_first_red_stops_the_primary_sweep(self) -> None:
        replay = self.replay()

        def observe(_library, _rom, timeline, case_replay):
            start = MODULE.input_frames(case_replay, MODULE.A_INPUT_BIT)[0]
            return self.fake_result(case_replay, timeline, red=start == 338)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                observe=observe,
            )

        self.assertEqual("RED_REPRODUCED", result["verdict"])
        self.assertEqual("a-start-338", result["firstRed"]["caseId"])
        self.assertEqual(9, result["completedCases"])
        self.assertEqual(2, result["firstRed"]["runCount"])
        self.assertEqual(2, result["cases"][-1]["runCount"])
        self.assertEqual(1, MODULE.exit_code(result))

    def test_non_cadence_physical_failure_is_not_a_bisect_red(self) -> None:
        replay = self.replay()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                only_start=340,
                observe=lambda _library, _rom, case_timeline, case_replay:
                    self.fake_result(case_replay, case_timeline, camera_red=True),
            )

        self.assertEqual("OBSERVER_INVALID", result["verdict"])
        self.assertIsNone(result["firstRed"])
        self.assertEqual(
            "OUT_OF_SCOPE_PHYSICAL_FAILURE",
            result["observerInvalid"]["verdict"],
        )
        self.assertEqual(125, MODULE.exit_code(result))

    def test_cross_round_digest_change_stops_as_non_deterministic(self) -> None:
        replay = self.replay()
        calls_by_start = {}

        def observe(_library, _rom, timeline, case_replay):
            start = MODULE.input_frames(case_replay, MODULE.A_INPUT_BIT)[0]
            calls_by_start[start] = calls_by_start.get(start, 0) + 1
            result = self.fake_result(case_replay, timeline)
            if start == 330 and calls_by_start[start] == 2:
                result["replayDigests"] = ["changed-between-matrix-rounds"] * 3
                result["deterministicDigest"] = "changed-between-matrix-rounds"
            return result

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                observe=observe,
            )

        self.assertEqual("OBSERVER_INVALID", result["verdict"])
        self.assertIsNone(result["firstRed"])
        self.assertEqual("a-start-330", result["observerInvalid"]["caseId"])
        self.assertEqual("NON_DETERMINISTIC_CASE", result["observerInvalid"]["verdict"])
        self.assertEqual(3, result["cases"][0]["runCount"])
        self.assertEqual(3, calls_by_start[330])
        self.assertEqual(125, MODULE.exit_code(result))

    def test_behavioral_red_prevents_a_not_reproduced_closeout(self) -> None:
        replay = self.replay()
        report = self.comparison_report(replay)
        report["cadenceClassification"]["inProcess"] = {
            "verdict": "gameplay-cadence-gap",
            "firstFailure": {"code": "gameplay-cadence-gap", "frame": 324},
            "gameplayTickRatio": 0.5,
        }

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                report,
                replay,
                out,
                only_start=340,
                observe=lambda _library, _rom, case_timeline, case_replay:
                    self.fake_result(case_replay, case_timeline),
            )

        self.assertEqual("OBSERVER_INVALID", result["verdict"])
        self.assertEqual("inProcess", result["behavioralRed"]["observer"])
        self.assertEqual("BEHAVIORAL_OBSERVER_DISAGREEMENT", result["observerInvalid"]["code"])
        self.assertEqual(125, MODULE.exit_code(result))

    def test_wrong_physical_timeline_provenance_invalidates_the_observer(self) -> None:
        replay = self.replay()

        def observe(_library, _rom, timeline, case_replay):
            result = self.fake_result(case_replay, timeline)
            result["timelineSha256"] = "wrong-timeline"
            return result

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                observe=observe,
            )

        self.assertEqual("OBSERVER_INVALID", result["verdict"])
        self.assertIn(
            "timeline provenance",
            result["cases"][0]["contractErrors"],
        )
        self.assertEqual(125, MODULE.exit_code(result))

    def test_missing_full_load_activity_invalidates_the_observer(self) -> None:
        replay = self.replay()

        def observe(_library, _rom, timeline, case_replay):
            result = self.fake_result(case_replay, timeline)
            for frame in result["frames"]:
                frame["state"]["musicActive"] = 0
            return result

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            out = root / "matrix.json"
            library.write_bytes(b"library")
            rom.write_bytes(b"rom")
            MODULE.write_replay(timeline, replay)
            result = MODULE.execute_matrix(
                library,
                rom,
                timeline,
                self.comparison_report(replay),
                replay,
                out,
                observe=observe,
            )

        self.assertEqual("OBSERVER_INVALID", result["verdict"])
        self.assertIn(
            "BGM activity",
            result["cases"][0]["loadCoverageErrors"],
        )

    def test_rows_cannot_hide_a_gap_behind_a_self_reported_green(self) -> None:
        replay = self.replay()
        with tempfile.TemporaryDirectory() as directory:
            timeline = Path(directory) / "timeline.json"
            MODULE.write_replay(timeline, replay)
            result = self.fake_result(replay, timeline)
            result["frames"][0]["state"]["romGameplayTick"] = 0
            result["frames"][1]["state"]["romGameplayTick"] = 0
            errors = MODULE.observation_contract_errors(
                result,
                rom_sha256=replay["romSha256"],
                timeline_sha256=hashlib.sha256(timeline.read_bytes()).hexdigest(),
                library_sha256=hashlib.sha256(b"library").hexdigest(),
                replay=replay,
            )

        self.assertIn("recomputed observer result", errors)

    def test_comparison_must_describe_the_exact_timeline(self) -> None:
        replay = self.replay()
        report = self.comparison_report(replay)
        report["timelineSha256"] = "different"
        with self.assertRaisesRegex(ValueError, "invalid timeline SHA-256"):
            MODULE.validate_comparison(
                report,
                replay["romSha256"],
                self.comparison_report(replay)["timelineSha256"],
                report["sameBoyLibrarySha256"],
                replay,
            )

    def test_comparison_rejects_a_green_verdict_with_a_failure(self) -> None:
        replay = self.replay()
        report = self.comparison_report(replay)
        report["cadenceClassification"]["sameboy"]["firstFailure"] = {
            "code": "audio-service-gap",
            "frame": 324,
        }
        with self.assertRaisesRegex(ValueError, "incoherent sameboy cadence"):
            MODULE.validate_comparison(
                report,
                replay["romSha256"],
                report["timelineSha256"],
                report["sameBoyLibrarySha256"],
                replay,
            )

    def test_comparison_must_use_the_same_sameboy_library(self) -> None:
        replay = self.replay()
        report = self.comparison_report(replay)
        with self.assertRaisesRegex(ValueError, "invalid SameBoy library SHA-256"):
            MODULE.validate_comparison(
                report,
                replay["romSha256"],
                report["timelineSha256"],
                "different-library",
                replay,
            )

    def test_comparison_must_describe_the_same_rom(self) -> None:
        with self.assertRaisesRegex(ValueError, "invalid ROM SHA-256"):
            replay = self.replay()
            report = self.comparison_report(replay)
            report["romSha256"] = "different"
            MODULE.validate_comparison(
                report,
                replay["romSha256"],
                report["timelineSha256"],
                report["sameBoyLibrarySha256"],
                replay,
            )


if __name__ == "__main__":
    unittest.main()
