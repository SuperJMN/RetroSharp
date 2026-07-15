from __future__ import annotations

import copy
from dataclasses import replace
import hashlib
import importlib
import json
from pathlib import Path
import tempfile
import unittest

from tools.gameboy import generate_sample_roms
from tools.nes.runner_visual_parity import RetroArchNetworkSession
from tools.nes import verify_runner_power_on_ram as power


ROOT = Path(__file__).resolve().parents[3]
RUNNER_ROM = ROOT / "samples" / "runner" / "bin" / "runner.nes"
RUNNER_ABI = ROOT / "samples" / "runner" / "bin" / "runner.nes.runtime-abi.json"


class NesRuntimeAbiLoaderTests(unittest.TestCase):
    @staticmethod
    def runtime_abi_module():
        try:
            return importlib.import_module("tools.nes.runtime_abi")
        except ModuleNotFoundError as error:
            raise AssertionError("The shared NES runtime ABI loader is missing.") from error

    def load_fixture(self, mutate=None, *, refresh_fingerprint: bool = True):
        module = self.runtime_abi_module()
        contract = json.loads(RUNNER_ABI.read_text(encoding="utf-8"))
        if mutate is not None:
            mutate(contract)
            if refresh_fingerprint:
                layout = {
                    "ranges": contract["ranges"],
                    "addresses": contract["addresses"],
                    "rangeAliases": contract["rangeAliases"],
                    "addressAliases": contract["addressAliases"],
                    "constants": contract["constants"],
                    "runtimeRegions": contract["runtimeRegions"],
                    "userVariables": contract["userVariables"],
                }
                contract["abiFingerprint"] = hashlib.sha256(
                    json.dumps(layout, separators=(",", ":")).encode("utf-8")
                ).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runtime-abi.json"
            path.write_text(json.dumps(contract), encoding="utf-8")
            return module.NesRuntimeAbi.load(path, RUNNER_ROM)

    def test_checked_in_contract_loads_representative_runtime_fields(self) -> None:
        abi = self.load_fixture()

        self.assertEqual(0x00E0, abi.address("camera.X"))
        self.assertEqual(0x00EA, abi.address("camera.Y"))
        self.assertEqual(0x0318, abi.address("camera.XHigh"))
        self.assertEqual(0x036E, abi.address("packed camera.FrameCounterLow"))
        self.assertEqual(0x03CB, abi.address("packed camera.VisibleCameraXLow"))
        self.assertEqual(0x03F8, abi.address("WorldPack.CollisionDecodeCountLow"))
        self.assertEqual(0x03FA, abi.address("WorldPack.GameplayTickCount"))
        self.assertEqual(0x03FB, abi.address("WorldPack.AudioTickCount"))
        self.assertEqual((0x0400, 594, 0x0652), (
            abi.range("WorldPackStaging").start,
            abi.range("WorldPackStaging").length,
            abi.range("WorldPackStaging").end_exclusive,
        ))
        self.assertEqual((0x0000, 2), (
            abi.variable("player.x").address,
            abi.variable("player.x").size,
        ))
        self.assertEqual((0x0002, 2), (
            abi.variable("player.y").address,
            abi.variable("player.y").size,
        ))
        self.assertEqual(
            [(0x0400 + index * 64, 64) for index in range(6)],
            [
                (abi.region(f"WorldPack.VisualSlot{index}").start,
                 abi.region(f"WorldPack.VisualSlot{index}").length)
                for index in range(6)
            ],
        )

    def test_retroarch_frame_counter_reads_explicit_low_and_high_addresses(self) -> None:
        session = object.__new__(RetroArchNetworkSession)
        session.frame_counter_addresses = (0x1234, 0x4567)
        reads: list[tuple[int, int]] = []

        def read(address: int, length: int) -> list[int]:
            reads.append((address, length))
            return [0x34 if address == 0x1234 else 0x12]

        session.read = read

        self.assertEqual(0x1234, session.frame_counter())
        self.assertEqual([(0x1234, 1), (0x4567, 1)], reads)

    def test_missing_required_address_names_the_field(self) -> None:
        def remove_frame_counter(contract: dict[str, object]) -> None:
            contract["addresses"] = [
                address
                for address in contract["addresses"]
                if not (
                    address["domain"] == "packed camera"
                    and address["name"] == "FrameCounterLow"
                )
            ]

        module = self.runtime_abi_module()
        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"missing required address 'packed camera\.FrameCounterLow'",
        ):
            self.load_fixture(remove_frame_counter)

    def test_missing_required_runtime_region_names_the_field(self) -> None:
        module = self.runtime_abi_module()

        def remove_visual_slot(contract: dict[str, object]) -> None:
            contract["runtimeRegions"] = [
                region
                for region in contract["runtimeRegions"]
                if region["name"] != "WorldPack.VisualSlot3"
            ]

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"missing required runtime region 'WorldPack\.VisualSlot3'",
        ):
            self.load_fixture(remove_visual_slot)

    def test_incompatible_required_variable_shape_fails_before_capture(self) -> None:
        module = self.runtime_abi_module()

        def shrink_player_x(contract: dict[str, object]) -> None:
            player_x = next(
                variable
                for variable in contract["userVariables"]
                if variable["name"] == "player.x"
            )
            player_x["size"] = 1

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"user variable 'player\.x' must occupy exactly two bytes",
        ):
            self.load_fixture(shrink_player_x)

    def test_incompatible_version_fails_clearly(self) -> None:
        module = self.runtime_abi_module()

        def change_version(contract: dict[str, object]) -> None:
            contract["version"] = 2

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"unsupported NES runtime ABI version 2; expected 1",
        ):
            self.load_fixture(change_version)

    def test_contract_for_another_rom_fails_clearly(self) -> None:
        module = self.runtime_abi_module()

        def change_hash(contract: dict[str, object]) -> None:
            contract["romSha256"] = "0" * 64

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"does not match ROM.*expected 0{64}",
        ):
            self.load_fixture(change_hash)

    def test_duplicate_or_out_of_range_entries_are_rejected(self) -> None:
        module = self.runtime_abi_module()

        def duplicate(contract: dict[str, object]) -> None:
            contract["addresses"].append(copy.deepcopy(contract["addresses"][0]))

        with self.assertRaisesRegex(module.NesRuntimeAbiError, r"duplicate address field"):
            self.load_fixture(duplicate)

        def out_of_range(contract: dict[str, object]) -> None:
            contract["addresses"][0]["address"] = 0x10000

        with self.assertRaisesRegex(module.NesRuntimeAbiError, r"outside 0\.\.65535"):
            self.load_fixture(out_of_range)

    def test_layout_tampering_fails_the_abi_fingerprint(self) -> None:
        module = self.runtime_abi_module()

        def move_camera_x(contract: dict[str, object]) -> None:
            camera_x = next(
                address
                for address in contract["addresses"]
                if address["domain"] == "camera" and address["name"] == "X"
            )
            camera_x["address"] += 1

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"ABI fingerprint does not match",
        ):
            self.load_fixture(move_camera_x, refresh_fingerprint=False)

    def test_user_variable_tampering_fails_the_abi_fingerprint(self) -> None:
        module = self.runtime_abi_module()

        def move_player_x(contract: dict[str, object]) -> None:
            player_x = next(
                variable
                for variable in contract["userVariables"]
                if variable["name"] == "player.x"
            )
            player_x["address"] += 1

        with self.assertRaisesRegex(
            module.NesRuntimeAbiError,
            r"ABI fingerprint does not match",
        ):
            self.load_fixture(move_player_x, refresh_fingerprint=False)

    def test_sample_generator_owns_the_tracked_runner_sidecar(self) -> None:
        builds = generate_sample_roms.select_builds(ROOT, False, [])
        runner = next(
            build
            for build in builds
            if build.source == ROOT / "samples" / "runner" / "runner.retrosharp.json"
            and build.target == "nes"
        )

        self.assertEqual(RUNNER_ABI, getattr(runner, "runtime_abi_output", None))

    def test_power_on_snapshot_reads_through_a_relocated_contract(self) -> None:
        real_abi = self.load_fixture()

        class RelocatedAbi:
            offset = 0x1000

            address_offsets = {
                "packed camera.PrepareCount": 0x20,
                "packed camera.ResidentCount": 0x40,
                "packed camera.CommitCount": 0x60,
                "packed camera.ReleaseCount": 0x80,
            }

            def address(self, name: str) -> int:
                return real_abi.address(name) + self.offset + self.address_offsets.get(name, 0)

            def range(self, name: str):
                value = real_abi.range(name)
                return replace(value, start=value.start + self.offset)

            def variable(self, name: str):
                value = real_abi.variable(name)
                return replace(value, address=value.address + self.offset)

            def region(self, name: str):
                value = real_abi.region(name)
                index = int(name.removeprefix("WorldPack.VisualSlot"))
                return replace(value, start=value.start + self.offset + index * 0x20)

            def constant(self, name: str) -> int:
                return real_abi.constant(name)

        class RecordingSession:
            def __init__(self) -> None:
                self.reads: list[tuple[int, int]] = []

            def read(self, address: int, length: int) -> list[int]:
                self.reads.append((address, length))
                return [0] * length

            def frame_counter(self) -> int:
                return 0

        abi = RelocatedAbi()
        session = RecordingSession()

        power.snapshot(session, abi=abi)

        self.assertIn((abi.variable("player.x").address, 2), session.reads)
        self.assertIn((abi.address("camera.X"), 1), session.reads)
        for name in ("RequestCount", "PrepareCount", "ResidentCount", "CommitCount", "ReleaseCount"):
            self.assertIn((abi.address(f"packed camera.{name}"), 1), session.reads)
        for index in range(6):
            region = abi.region(f"WorldPack.VisualSlot{index}")
            self.assertIn((region.start, region.length), session.reads)
        for range_id in ("WorldPackScalarState", "PackedCameraAndWorldPackAuxiliaryState"):
            value = abi.range(range_id)
            self.assertIn((value.start, value.length), session.reads)


if __name__ == "__main__":
    unittest.main()
