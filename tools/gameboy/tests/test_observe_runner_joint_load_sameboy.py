import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


MODULE_PATH = Path(__file__).parents[1] / "observe_runner_joint_load_sameboy.py"
SPEC = importlib.util.spec_from_file_location("rph62a_sameboy_observer", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class SameBoyPhysicalObserverTests(unittest.TestCase):
    @staticmethod
    def replay() -> dict:
        layout = {
            field: index + 1
            for index, field in enumerate(MODULE.shared.LAYOUT_FIELDS)
        }
        layout["forbiddenCounters"] = [20, 21, 22, 23, 24]
        return {
            "schema": MODULE.shared.REPLAY_SCHEMA,
            "romSha256": hashlib.sha256(b"rom").hexdigest(),
            "warmUpFrames": 2,
            "observationFrames": 32,
            "layout": layout,
            "cadence": {
                "minimumGameplayTickRatio": 0,
                "maximumConsecutiveMissedGameplayTicks": 1,
                "maximumUnplannedAudioGapFrames": 1,
                "maximumRequestToVisibleFrames": 2,
            },
            "frames": [
                {
                    "frame": frame,
                    "inputMask": 0,
                    "audioServiceExpected": True,
                }
                for frame in range(1, 35)
            ],
        }

    @staticmethod
    def rows() -> dict[int, dict]:
        rows = {}
        for frame in range(2, 35):
            sequence = frame - 2
            rows[frame] = {
                "frame": frame,
                "state": {
                    "romGameplayTick": sequence,
                    "packedAudioTick": sequence,
                    "playerX": sequence,
                    "playerY": 10,
                    "visibleCameraX": sequence,
                    "visibleCameraY": 0,
                    "shadowRomBank": 1,
                    "forbiddenVideoWork": 0,
                    "musicActive": 1,
                    "sfxActive": 0,
                    "camera": {
                        "request": sequence,
                        "resident": sequence,
                        "commit": sequence,
                        "visible": sequence,
                    },
                    "backgroundDigest": f"background-{sequence}",
                    "oamDigest": f"oam-{sequence}",
                },
            }
        return rows

    def test_physical_descriptor_requires_camera_budget(self) -> None:
        replay = self.replay()
        del replay["cadence"]["maximumRequestToVisibleFrames"]

        with self.assertRaisesRegex(ValueError, "maximumRequestToVisibleFrames"):
            MODULE.shared.validate_replay_descriptor(
                replay,
                require_physical_camera_budgets=True,
            )

    def test_physical_descriptor_rejects_a_window_too_short_for_canaries(self) -> None:
        replay = self.replay()
        replay["observationFrames"] = 1
        replay["frames"] = replay["frames"][:3]

        with self.assertRaisesRegex(ValueError, "too short.*minimum 4"):
            MODULE.shared.validate_replay_descriptor(
                replay,
                require_physical_camera_budgets=True,
            )

    def test_descriptor_rejects_input_bits_the_runner_mapping_cannot_apply(self) -> None:
        replay = self.replay()
        replay["frames"][0]["inputMask"] = 0b1000

        with self.assertRaisesRegex(ValueError, "inputMask"):
            MODULE.shared.validate_replay_descriptor(replay)

    def test_cli_has_no_in_process_report_input(self) -> None:
        options = {
            option
            for action in MODULE.build_parser()._actions
            for option in action.option_strings
        }

        self.assertNotIn("--in-process-report", options)
        self.assertEqual(
            {"--library", "--rom", "--timeline", "--out"},
            {option for option in options if option not in {"-h", "--help"}},
        )

    def test_four_controlled_canaries_have_stable_code_frame_and_digest(self) -> None:
        replay = self.replay()
        rows = self.rows()

        first = MODULE.build_canary_proofs(rows, replay)
        second = MODULE.build_canary_proofs(rows, replay)

        self.assertEqual(first, second)
        self.assertTrue(all(canary["passed"] for canary in first))
        self.assertEqual(
            [
                ("gameplay-cadence-gap", 13),
                ("audio-service-gap", 13),
                ("camera-visible-gap", 22),
                ("sprite-oam", 26),
            ],
            [
                (
                    canary["observedFirstFailure"]["code"],
                    canary["observedFirstFailure"]["frame"],
                )
                for canary in first
            ],
        )

    def test_canaries_are_not_preempted_by_an_unrelated_real_red(self) -> None:
        replay = self.replay()
        rows = self.rows()
        rows[3]["state"]["romGameplayTick"] = 0
        rows[4]["state"]["romGameplayTick"] = 0

        proofs = MODULE.build_canary_proofs(rows, replay)

        self.assertTrue(all(canary["passed"] for canary in proofs))

    def test_authoritative_red_has_one_bounded_code_and_frame(self) -> None:
        replay = self.replay()
        rows = self.rows()
        rows[3]["state"]["romGameplayTick"] = 0
        rows[4]["state"]["romGameplayTick"] = 0

        classification = MODULE.authoritative_classification(rows, replay)

        self.assertEqual("gameplay-cadence-gap", classification["verdict"])
        self.assertEqual(
            {
                "code": "gameplay-cadence-gap",
                "frame": 4,
                "missedFrames": 2,
            },
            classification["firstFailure"],
        )

    def test_failed_canary_selection_uses_the_earliest_observed_frame(self) -> None:
        failure = MODULE.first_failed_canary([
            {
                "id": "gameplay-freeze",
                "expectedFirstFailure": {"code": "gameplay-cadence-gap", "frame": 20},
                "observedFirstFailure": None,
                "passed": False,
            },
            {
                "id": "audio-freeze",
                "expectedFirstFailure": {"code": "audio-service-gap", "frame": 12},
                "observedFirstFailure": {"code": "wrong-code", "frame": 9},
                "passed": False,
            },
        ])

        self.assertEqual(
            {
                "code": "observer-canary-failed",
                "frame": 9,
                "canary": "audio-freeze",
            },
            failure,
        )

    def test_determinism_failure_is_bounded_to_first_physical_frame(self) -> None:
        first = self.rows()
        second = copy.deepcopy(first)
        third = copy.deepcopy(first)
        second[10]["state"]["oamDigest"] = "corrupted"

        failure = MODULE.first_determinism_failure([first, second, third], 2)

        self.assertEqual(
            {
                "code": "non-deterministic-physical-timeline",
                "frame": 10,
                "replay": 2,
            },
            failure,
        )

    def test_capture_applies_each_input_to_exactly_one_sameboy_frame(self) -> None:
        replay = self.replay()

        class FakeSameBoy:
            inputs: list[int] = []

            def __init__(self, *_args) -> None:
                type(self).inputs = []

            def __enter__(self):
                return self

            def __exit__(self, *_args) -> None:
                pass

            def run_frame(self, input_mask: int) -> None:
                type(self).inputs.append(input_mask)

            @staticmethod
            def memory(_address: int, length: int = 1) -> list[int]:
                return [0] * length

            @staticmethod
            def oam() -> list[int]:
                return [0] * 160

        with patch.object(MODULE.shared, "SameBoy", FakeSameBoy):
            MODULE.shared.sameboy_rows(
                Path("unused-library"),
                Path("unused-rom"),
                replay,
                {replay["warmUpFrames"], replay["warmUpFrames"] + 1},
            )

        self.assertEqual(
            [item["inputMask"] for item in replay["frames"]],
            FakeSameBoy.inputs,
        )

    def test_observer_emits_sameboy_as_the_only_physical_authority(self) -> None:
        replay = self.replay()
        rows = self.rows()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            library.write_bytes(b"sameboy")
            rom.write_bytes(b"rom")
            timeline.write_text(json.dumps(replay))

            with patch.object(
                MODULE.shared,
                "sameboy_rows",
                side_effect=lambda *_args, **_kwargs: copy.deepcopy(rows),
            ) as run:
                result = MODULE.observe(library, rom, timeline, replay)

        self.assertEqual(3, run.call_count)
        self.assertEqual("SameBoy", result["authority"]["backend"])
        self.assertEqual("GB_run_frame", result["authority"]["physicalFrameBoundary"])
        self.assertFalse(result["authority"]["gameBoyTestCpuPhysicalAuthority"])
        self.assertEqual([], result["authority"]["hostCountersConsumed"])
        self.assertTrue(result["deterministic"])
        self.assertEqual(1, len(set(result["replayDigests"])))
        self.assertEqual(result["replayDigests"][0], result["deterministicDigest"])
        self.assertEqual("NOT_REPRODUCED", result["verdict"])
        self.assertIsNone(result["firstFailure"])
        self.assertTrue(result["canariesPassed"])
        self.assertEqual(32, len(result["frames"]))
        self.assertIn("inputMask", result["frames"][0])
        self.assertIn("audioServiceExpected", result["frames"][0])
        self.assertNotIn("gameplayTicks", result["frames"][0])
        self.assertNotIn("audioServiceTicks", result["frames"][0])

    def test_first_failure_is_chronological_across_physical_and_observer_failures(self) -> None:
        replay = self.replay()
        first = self.rows()
        first[3]["state"]["romGameplayTick"] = 0
        first[4]["state"]["romGameplayTick"] = 0
        second = copy.deepcopy(first)
        second[10]["state"]["oamDigest"] = "non-deterministic"
        third = copy.deepcopy(first)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            library = root / "libsameboy.so"
            rom = root / "runner.gb"
            timeline = root / "runner.timeline.json"
            library.write_bytes(b"sameboy")
            rom.write_bytes(b"rom")
            timeline.write_text(json.dumps(replay))

            with patch.object(
                MODULE.shared,
                "sameboy_rows",
                side_effect=[first, second, third],
            ):
                result = MODULE.observe(library, rom, timeline, replay)

        self.assertFalse(result["deterministic"])
        self.assertEqual(4, result["firstFailure"]["frame"])
        self.assertEqual("gameplay-cadence-gap", result["firstFailure"]["code"])
        self.assertEqual(10, result["observerFirstFailure"]["frame"])
        self.assertEqual(
            "non-deterministic-physical-timeline",
            result["observerFirstFailure"]["code"],
        )


if __name__ == "__main__":
    unittest.main()
