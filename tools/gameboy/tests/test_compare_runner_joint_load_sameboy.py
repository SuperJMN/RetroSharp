import hashlib
import importlib.util
from pathlib import Path
import unittest


MODULE_PATH = Path(__file__).parents[1] / "compare_runner_joint_load_sameboy.py"
SPEC = importlib.util.spec_from_file_location("rph62_sameboy", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class SameCounterComparisonTests(unittest.TestCase):
    @staticmethod
    def valid_contract() -> tuple[dict, dict]:
        state = {
            "romGameplayTick": 0,
            "packedAudioTick": 0,
            "playerX": 0,
            "playerY": 0,
            "visibleCameraX": 0,
            "visibleCameraY": 0,
            "shadowRomBank": 1,
            "forbiddenVideoWork": 0,
            "musicActive": 1,
            "sfxActive": 0,
            "camera": {"request": 0, "resident": 0, "commit": 0, "visible": 0},
            "backgroundDigest": "a",
            "oamDigest": "b",
        }
        baseline = {"frame": 2, "gameplayTicks": 0, "audioServiceTicks": 0, "state": state}
        frames = [
            {"frame": frame, "gameplayTicks": frame - 2, "audioServiceTicks": frame - 2,
             "state": {**state, "romGameplayTick": frame - 2, "packedAudioTick": frame - 2}}
            for frame in (3, 4)
        ]
        report = {"schema": MODULE.REPORT_SCHEMA, "romSha256": "abc", "baseline": baseline, "frames": frames}
        layout = {field: index + 1 for index, field in enumerate(MODULE.LAYOUT_FIELDS)}
        layout["forbiddenCounters"] = [20, 21, 22, 23, 24]
        replay = {
            "schema": MODULE.REPLAY_SCHEMA,
            "romSha256": "abc",
            "warmUpFrames": 2,
            "observationFrames": 2,
            "layout": layout,
            "cadence": {
                "minimumGameplayTickRatio": 0.9,
                "maximumConsecutiveMissedGameplayTicks": 1,
                "maximumUnplannedAudioGapFrames": 1,
            },
            "frames": [
                {"frame": frame, "inputMask": 0, "audioServiceExpected": True}
                for frame in range(1, 5)
            ],
        }
        return report, replay

    def test_unwrap_preserves_byte_wrap(self) -> None:
        self.assertEqual(258, MODULE.unwrap(255, 257, 0))

    def test_counter_delta_mismatch_ignores_a_power_on_offset_and_reports_values(self) -> None:
        expected = {
            321: {"frame": 321, "state": {"romGameplayTick": 11, "packedAudioTick": 21}},
            322: {"frame": 322, "state": {"romGameplayTick": 12, "packedAudioTick": 22}},
            323: {"frame": 323, "state": {"romGameplayTick": 12, "packedAudioTick": 23}},
        }
        actual = {
            321: {"frame": 321, "state": {"romGameplayTick": 101, "packedAudioTick": 201}},
            322: {"frame": 322, "state": {"romGameplayTick": 102, "packedAudioTick": 202}},
            323: {"frame": 323, "state": {"romGameplayTick": 102, "packedAudioTick": 204}},
        }
        expected_baseline = {"frame": 320, "state": {"romGameplayTick": 10, "packedAudioTick": 20}}
        actual_baseline = {"frame": 320, "state": {"romGameplayTick": 100, "packedAudioTick": 200}}

        mismatches = MODULE.first_counter_delta_mismatches(
            expected,
            actual,
            expected_baseline,
            actual_baseline,
            ("romGameplayTick", "packedAudioTick"),
        )

        self.assertIsNone(mismatches["romGameplayTick"])
        self.assertEqual(
            {"frame": 323, "expectedDelta": 1, "actualDelta": 2, "expectedValue": 23, "actualValue": 204},
            mismatches["packedAudioTick"],
        )

    def test_rom_identity_requires_the_same_bytes_for_both_artifacts(self) -> None:
        rom = b"same bytes"
        sha = hashlib.sha256(rom).hexdigest()
        self.assertEqual(sha, MODULE.verify_rom_identity(rom, {"romSha256": sha}, {"romSha256": sha}))
        with self.assertRaises(ValueError):
            MODULE.verify_rom_identity(rom, {"romSha256": sha}, {"romSha256": "different"})

    def test_digest_is_stable_for_repeated_normalized_rows(self) -> None:
        rows = {1: {"romGameplayTick": 0, "packedAudioTick": 1}}
        self.assertEqual(MODULE.digest(rows), MODULE.digest(rows.copy()))

    def test_host_projection_uses_rom_counter_deltas_not_host_marker_deltas(self) -> None:
        rows = {
            1: {"gameplayTicks": 10, "audioServiceTicks": 10, "state": {"romGameplayTick": 10, "packedAudioTick": 10}},
            2: {"gameplayTicks": 11, "audioServiceTicks": 11, "state": {"romGameplayTick": 10, "packedAudioTick": 12}},
        }

        self.assertEqual(
            {
                "gameplay": {"frame": 2, "expectedDelta": 1, "actualDelta": 0},
                "audio": {"frame": 2, "expectedDelta": 1, "actualDelta": 2},
            },
            MODULE.host_projection_first_mismatch(rows),
        )

    def test_counter_delta_alignment_reports_bounded_mismatch_rows(self) -> None:
        expected = {
            321: {"frame": 321, "state": {"romGameplayTick": 0}},
            322: {"frame": 322, "state": {"romGameplayTick": 1}},
        }
        actual = {
            320: {"frame": 320, "state": {"romGameplayTick": 0}},
            321: {"frame": 321, "state": {"romGameplayTick": 0}},
            322: {"frame": 322, "state": {"romGameplayTick": 0}},
            323: {"frame": 323, "state": {"romGameplayTick": 1}},
        }
        baseline = {"frame": 320, "state": {"romGameplayTick": 0}}

        alignment = MODULE.counter_delta_alignment(expected, actual, baseline, baseline, "romGameplayTick")

        self.assertEqual(1, alignment["bestFrameOffset"])
        self.assertEqual(0, alignment["mismatchCount"])
        self.assertEqual([], alignment["mismatchRows"])

    def test_replay_digest_normalizes_only_the_absolute_camera_power_on_value(self) -> None:
        first = {
            320: {"frame": 320, "state": {"camera": {"request": 7}}},
            321: {"frame": 321, "state": {"camera": {"request": 8}}},
        }
        second = {
            320: {"frame": 320, "state": {"camera": {"request": 51}}},
            321: {"frame": 321, "state": {"camera": {"request": 52}}},
        }

        self.assertEqual(
            MODULE.normalized_replay_digest(first, 320),
            MODULE.normalized_replay_digest(second, 320),
        )

    def test_contract_validation_accepts_contiguous_schema_matched_evidence(self) -> None:
        report, replay = self.valid_contract()

        MODULE.validate_replay_contract(report, replay)

    def test_contract_validation_rejects_wrong_schema_and_non_contiguous_frames(self) -> None:
        report, replay = self.valid_contract()
        report["schema"] = "wrong"
        with self.assertRaisesRegex(ValueError, "Unsupported in-process report schema"):
            MODULE.validate_replay_contract(report, replay)

        report, replay = self.valid_contract()
        replay["frames"][2]["frame"] = 2
        with self.assertRaisesRegex(ValueError, "Timeline frames must be unique and contiguous"):
            MODULE.validate_replay_contract(report, replay)

    def test_cadence_classification_reports_the_first_gap_or_not_reproduced(self) -> None:
        report, replay = self.valid_contract()
        rows = {row["frame"]: row for row in report["frames"]}

        self.assertEqual("NOT_REPRODUCED", MODULE.classify_cadence(rows, report["baseline"], replay)["verdict"])

        with_prebaseline_rows = {
            0: {"frame": 0, "state": {"romGameplayTick": 100, "packedAudioTick": 100}},
            1: {"frame": 1, "state": {"romGameplayTick": 101, "packedAudioTick": 101}},
            **rows,
        }
        self.assertEqual(
            MODULE.classify_cadence(rows, report["baseline"], replay),
            MODULE.classify_cadence(with_prebaseline_rows, report["baseline"], replay, set(rows)),
        )

        rows[3]["state"]["romGameplayTick"] = 0
        rows[4]["state"]["romGameplayTick"] = 0
        classification = MODULE.classify_cadence(rows, report["baseline"], replay)
        self.assertEqual("gameplay-cadence-gap", classification["verdict"])
        self.assertEqual(4, classification["firstFailure"]["frame"])

    def test_cadence_classification_applies_the_emitted_gameplay_ratio_budget(self) -> None:
        report, replay = self.valid_contract()
        rows = {row["frame"]: row for row in report["frames"]}
        rows[3]["state"]["romGameplayTick"] = 0
        rows[4]["state"]["romGameplayTick"] = 1

        classification = MODULE.classify_cadence(rows, report["baseline"], replay)

        self.assertEqual("gameplay-tick-ratio", classification["verdict"])
        self.assertEqual(4, classification["firstFailure"]["frame"])


if __name__ == "__main__":
    unittest.main()
