from __future__ import annotations

import hashlib
import json
from dataclasses import replace
import struct
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
from pathlib import Path

from PIL import Image

from tools.nes import verify_runner_visual_parity as parity
from tools.nes.runtime_abi import NesRuntimeAbi
from tools.nes.runner_visual_parity import (
    ConfigIntegrityGuard,
    build_retroarch_command,
    build_retroarch_config,
    changed_screenshot_files,
    snapshot_screenshot_files,
)
from tools.nes.verify_runner_visual_parity import (
    authored_collision_evidence,
    parse_fceumm_state,
    parse_nestopia_state,
    visible_background_cells,
)


class RetroArchIsolationTests(unittest.TestCase):
    def test_screenshot_detection_accepts_an_overwritten_timestamp_name(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            screenshot = root / "runner-20260713-120000.png"
            Image.new("RGB", (8, 8), (1, 2, 3)).save(screenshot)
            before = snapshot_screenshot_files(root)

            Image.new("RGB", (8, 8), (4, 5, 6)).save(screenshot)

            self.assertEqual(
                [screenshot],
                changed_screenshot_files(root, before),
            )

    def test_launch_uses_full_config_instead_of_appendconfig(self) -> None:
        command = build_retroarch_command(
            ["retroarch"],
            config_path=Path("/tmp/isolated/retroarch.cfg"),
            core_path=Path("/tmp/cores/fceumm_libretro.so"),
            rom_path=Path("/tmp/runner.nes"),
        )

        self.assertIn("--config=/tmp/isolated/retroarch.cfg", command)
        self.assertNotIn("--appendconfig", " ".join(command))
        self.assertEqual(
            ["-L", "/tmp/cores/fceumm_libretro.so", "/tmp/runner.nes"],
            command[-3:],
        )

    def test_complete_config_is_disposable_and_never_saves_on_exit(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            config = build_retroarch_config(
                work_directory=root,
                command_port=55381,
                remote_port=55481,
                core_options_path=root / "core-options.cfg",
            )

        self.assertIn('config_save_on_exit = "false"', config)
        self.assertIn('global_core_options = "true"', config)
        self.assertIn('network_cmd_enable = "true"', config)
        self.assertIn('video_driver = "null"', config)
        self.assertIn('audio_driver = "null"', config)
        self.assertIn(f'core_options_path = "{root / "core-options.cfg"}"', config)
        self.assertIn(f'screenshot_directory = "{root / "screenshots"}"', config)
        self.assertIn(f'savestate_directory = "{root / "states"}"', config)

    def test_disposable_overrides_replace_conflicting_base_config_values(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            config = build_retroarch_config(
                work_directory=root,
                command_port=55381,
                remote_port=55481,
                core_options_path=root / "core-options.cfg",
                base_config=(
                    'config_save_on_exit = "true"\n'
                    'core_options_path = "/persistent/FCEUmm.opt"\n'
                    'playlist_directory = "/persistent/playlists"\n'
                ),
            )

        self.assertEqual(1, config.count("config_save_on_exit ="))
        self.assertEqual(1, config.count("core_options_path ="))
        self.assertEqual(1, config.count("playlist_directory ="))
        self.assertNotIn("/persistent", config)

    def test_integrity_guard_rejects_a_change_to_either_persistent_file(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            paths = [root / "retroarch.cfg", root / "FCEUmm.opt"]
            for path in paths:
                path.write_bytes(b"user settings\n")
            guard = ConfigIntegrityGuard(paths)
            self.assertEqual(
                {path.resolve(): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths},
                guard.before_sha256,
            )
            paths[1].write_bytes(b"changed\n")

            with self.assertRaisesRegex(AssertionError, "persistent RetroArch config changed"):
                guard.verify_unchanged()


class NesMcpSurfaceTests(unittest.TestCase):
    def test_surface_requires_published_007_server_and_rich_tools(self) -> None:
        tools = set(parity.REQUIRED_NES_MCP_TOOLS)

        report = parity.validate_nes_mcp_surface(
            {"name": "Nes.Mcp", "version": "0.0.7.0"},
            tools,
        )

        self.assertEqual("0.0.7.0", report["version"])
        self.assertEqual([], report["missing_tools"])

        with self.assertRaisesRegex(RuntimeError, "0.0.7"):
            parity.validate_nes_mcp_surface(
                {"name": "Nes.Mcp", "version": "0.0.6.0"},
                tools,
            )
        with self.assertRaisesRegex(RuntimeError, "observe_execution"):
            parity.validate_nes_mcp_surface(
                {"name": "Nes.Mcp", "version": "0.0.7.0"},
                tools - {"observe_execution"},
            )


class ExactEmulatorStateTests(unittest.TestCase):
    @staticmethod
    def fce_field(name: bytes, value: bytes) -> bytes:
        return name.ljust(4, b"\0") + struct.pack("<I", len(value)) + value

    @classmethod
    def fce_chunk(cls, kind: int, fields: list[tuple[bytes, bytes]]) -> bytes:
        payload = b"".join(cls.fce_field(name, value) for name, value in fields)
        return bytes((kind,)) + struct.pack("<I", len(payload)) + payload

    def test_fceumm_state_extracts_all_four_physical_nametables_and_palette(self) -> None:
        chunks = b"".join(
            (
                self.fce_chunk(1, [(b"RAM", bytes([0x11]) * 0x800)]),
                self.fce_chunk(
                    3,
                    [(b"NTAR", bytes([0x22]) * 0x800), (b"PRAM", bytes(range(0x20)))],
                ),
                self.fce_chunk(0x10, [(b"EXNR", bytes([0x33]) * 0x800)]),
            )
        )
        state = b"RASTATE\x01MEM " + struct.pack("<I", len(chunks) + 16)
        state += b"FCS\xFF" + struct.pack("<I", len(chunks)) + bytes(8) + chunks

        fields = parse_fceumm_state(state)

        self.assertEqual(bytes([0x22]) * 0x800, fields["NTAR"])
        self.assertEqual(bytes([0x33]) * 0x800, fields["EXNR"])
        self.assertEqual(bytes(range(0x20)), fields["PRAM"])

    @staticmethod
    def nest_chunk(name: bytes, value: bytes) -> bytes:
        return name + struct.pack("<I", len(value)) + value

    def test_nestopia_state_uses_the_four_mapped_vram_pages(self) -> None:
        chunk = self.nest_chunk
        ram = bytes([0x41]) * 0x800
        nametables = bytes(index // 0x400 for index in range(0x1000))
        palette = bytes(range(0x20))
        cpu = chunk(b"RAM\0", b"\0" + ram)
        ppu = chunk(b"REG\0", bytes(11)) + chunk(b"PAL\0", b"\0" + palette)
        mapping = bytes((1, 0, 0, 1, 1, 0, 1, 2, 0, 1, 3, 0))
        mapper = chunk(b"VRM\0", b"\0" + nametables) + chunk(
            b"NMT\0", chunk(b"BNK\0", mapping)
        )
        top = chunk(b"CPU\0", cpu) + chunk(b"PPU\0", ppu) + chunk(
            b"IMG\0", chunk(b"MPR\0", mapper)
        )
        state = chunk(b"NST\x1A", top) + bytes(8)

        fields = parse_nestopia_state(state)

        self.assertEqual(ram, fields["RAM"])
        self.assertEqual(nametables, fields["NAMETABLES"])
        self.assertEqual(palette, fields["PALETTE"])

    def test_visible_signature_uses_exact_tile_and_attribute_palette_identity(self) -> None:
        nametables = bytearray(0x1000)
        camera_x = 304
        camera_y = 96
        tile_x = camera_x // 8
        tile_y = camera_y // 8
        table = (tile_y // 30 & 1) * 2 + (tile_x // 32 & 1)
        local_x = tile_x % 32
        local_y = tile_y % 30
        start = table * 0x400
        nametables[start + local_y * 32 + local_x] = 0x91
        attribute_address = start + 0x3C0 + local_y // 4 * 8 + local_x // 4
        shift = (local_y % 4 // 2) * 4 + (local_x % 4 // 2) * 2
        nametables[attribute_address] = 3 << shift

        cells = visible_background_cells(bytes(nametables), camera_x, camera_y)

        self.assertEqual((0x91, 3), cells[0])

    def test_tracked_stage1_collision_is_aligned_with_runner_feet(self) -> None:
        evidence = authored_collision_evidence({"player_x": 400, "player_y": 273})

        self.assertTrue(evidence["aligned"])
        self.assertEqual(304, evidence["foot_y"])
        self.assertEqual(304, evidence["collision_top"])
        self.assertTrue(any(cell["solid"] for cell in evidence["cells"]))

    def test_tracked_stage1_one_way_platform_is_landable_from_above(self) -> None:
        evidence = authored_collision_evidence({"player_x": 752, "player_y": 241})

        self.assertTrue(evidence["aligned"])
        self.assertEqual(272, evidence["foot_y"])
        self.assertEqual(272, evidence["collision_top"])
        self.assertTrue(any(cell["platform"] for cell in evidence["cells"]))
        self.assertFalse(any(cell["solid"] for cell in evidence["cells"]))


class TransientFrameAcceptanceTests(unittest.TestCase):
    @staticmethod
    def jump_signature(airborne_frames: int, *, minimum_y: int = 243) -> dict[str, object]:
        return {
            "start_player_x": 403,
            "end_player_x": 403,
            "start_y": 273,
            "minimum_y": minimum_y,
            "landed_y": 273,
            "airborne_frames": airborne_frames,
            "hold_frames": 6,
            "landed_collision": {"aligned": True, "collision_top": 304},
        }

    def test_jump_semantics_accept_one_frame_observation_phase_skew(self) -> None:
        signatures = {
            "aprnes": self.jump_signature(22),
            "fceumm": self.jump_signature(21),
            "nestopia": self.jump_signature(21),
        }

        self.assertTrue(parity.jump_semantics_match(signatures))

    def test_jump_semantics_reject_trajectory_or_two_frame_difference(self) -> None:
        for signatures in (
            {
                "aprnes": self.jump_signature(23),
                "fceumm": self.jump_signature(21),
                "nestopia": self.jump_signature(21),
            },
            {
                "aprnes": self.jump_signature(21, minimum_y=242),
                "fceumm": self.jump_signature(21),
                "nestopia": self.jump_signature(21),
            },
        ):
            with self.subTest(signatures=signatures):
                self.assertFalse(parity.jump_semantics_match(signatures))

    @staticmethod
    def frame_state(step: int) -> dict[str, object]:
        return {
            "player_x": 100 + step,
            "player_y": 273,
            "requested_camera_x": step,
            "requested_camera_y": 0,
            "visible_camera_x": step,
            "visible_camera_y": 80,
            "lifecycle": {
                "request": step,
                "prepare": step,
                "resident": step,
                "commit": step,
                "release": step,
            },
            "last_commit_writes": {"tiles": 32, "attributes": 8},
        }

    def test_commit_windows_include_two_frames_before_and_after_each_commit(self) -> None:
        self.assertTrue(
            hasattr(parity, "commit_frame_windows"),
            "The differential harness must retain consecutive per-commit frame windows.",
        )
        frames = [
            {
                "step": step,
                "state": {"lifecycle": {"commit": commit}},
                "framebuffer": Image.new("RGB", (8, 8), (step, 0, 0)),
            }
            for step, commit in enumerate((0, 0, 0, 1, 1, 1, 2, 2, 2))
        ]

        windows = parity.commit_frame_windows(frames, radius=2)

        self.assertEqual([1, 2], [window["commit"] for window in windows])
        self.assertEqual(
            [[1, 2, 3, 4, 5], [4, 5, 6, 7, 8]],
            [[frame["step"] for frame in window["frames"]] for window in windows],
        )

    def test_commit_windows_reject_an_incomplete_final_window(self) -> None:
        frames = [
            {
                "step": step,
                "state": {"lifecycle": {"commit": commit}},
                "screen": {"hash": f"sha256:{step}"},
            }
            for step, commit in enumerate((0, 0, 0, 1, 1))
        ]

        with self.assertRaisesRegex(AssertionError, "lacks a complete"):
            parity.commit_frame_windows(frames, radius=2)

    def test_commit_window_artifacts_retain_compact_aprnes_frames_without_pngs(self) -> None:
        frames = [
            {
                "step": step,
                "phase": "right",
                "state": {"lifecycle": {"commit": 0 if step < 2 else 1}},
                "screen": {"hash": f"sha256:{step}"},
                "screen_anomaly": {"corrupt": False},
            }
            for step in range(5)
        ]
        with tempfile.TemporaryDirectory() as temporary_directory:
            report = parity.save_commit_frame_windows(
                Path(temporary_directory),
                {"aprnes": {"frames": frames}},
            )

        retained = report["aprnes"]["windows"][0]["frames"]
        self.assertTrue(report["aprnes"]["complete"])
        self.assertEqual(5, len(retained))
        self.assertEqual("sha256:0", retained[0]["screen"]["hash"])
        self.assertNotIn("framebuffer", retained[0])

    def test_focal_ppu_trace_framebuffers_are_saved_before_json_serialization(self) -> None:
        reports = {
            "first-column": {
                "commit": 1,
                "validation": {"valid": True},
                "framebuffer": Image.new("RGB", (8, 8), (1, 2, 3)),
            }
        }
        with tempfile.TemporaryDirectory() as temporary_directory:
            artifacts = Path(temporary_directory)
            serialized = parity.save_ppu_commit_trace_artifacts(artifacts, reports)
            encoded = json.dumps(serialized)
            framebuffer_path = artifacts / serialized["first-column"]["framebuffer"]

            self.assertTrue(framebuffer_path.is_file())
            self.assertIn("framebuffer_sha256", encoded)

    def test_observation_metadata_rejects_trace_truncation(self) -> None:
        observation = {
            "framesRequested": 2,
            "framesRun": 2,
            "frames": [{}, {}],
            "ppuEventCount": 2_000,
            "ppuEventsObserved": 2_001,
            "ppuTraceTruncated": True,
            "truncated": True,
            "stopReason": "framesComplete",
            "timeline": {"frames": 502, "cycles": 1_000, "instructions": 300},
        }

        with self.assertRaisesRegex(AssertionError, "truncated"):
            parity.validate_execution_observation(observation)

    def test_observation_metadata_rejects_authoritative_non_vblank_ppudata(self) -> None:
        snapshot = {
            "scanline": 12,
            "dot": 80,
            "vblank": False,
            "renderingActive": True,
            "v": "0x2400",
            "t": "0x2400",
            "x": 0,
            "w": False,
        }
        observation = {
            "framesRequested": 1,
            "framesRun": 1,
            "frames": [
                {
                    "ppuState": {
                        "renderingEnabled": True,
                        "backgroundEnabled": True,
                        "spritesEnabled": True,
                    }
                }
            ],
            "ppuEvents": [
                {
                    "address": "0x2007",
                    "before": snapshot,
                    "after": snapshot,
                }
            ],
            "ppuEventCount": 1,
            "ppuEventsObserved": 1,
            "ppuTraceTruncated": False,
            "truncated": False,
            "stopReason": "framesComplete",
            "timeline": {"frames": 501, "cycles": 1_000, "instructions": 300},
        }

        with self.assertRaisesRegex(AssertionError, "outside VBlank"):
            parity.validate_execution_observation(observation)

    def test_observation_uses_physical_vblank_when_ppustatus_latch_was_cleared(self) -> None:
        snapshot = {
            "scanline": 245,
            "dot": 80,
            "vblank": False,
            "renderingActive": False,
            "v": "0x2400",
            "t": "0x2400",
            "x": 0,
            "w": False,
        }
        observation = {
            "framesRequested": 1,
            "framesRun": 1,
            "frames": [{}],
            "ppuEvents": [
                {
                    "address": "0x2007",
                    "before": snapshot,
                    "after": snapshot,
                }
            ],
            "ppuEventCount": 1,
            "ppuEventsObserved": 1,
            "ppuTraceTruncated": False,
            "truncated": False,
            "stopReason": "framesComplete",
            "timeline": {"frames": 501, "cycles": 1_000, "instructions": 300},
        }

        report = parity.validate_execution_observation(observation)

        self.assertEqual(0, report["ppudata_outside_vblank"])

    def test_observation_checks_every_frame_for_complete_contiguous_oam(self) -> None:
        snapshot = {
            "scanline": 245,
            "dot": 80,
            "vblank": False,
            "renderingActive": False,
            "v": "0x2400",
            "t": "0x2400",
            "x": 0,
            "w": False,
        }
        events = [
            {
                "frameOffset": 0,
                "address": "0x2003",
                "value": "0x00",
                "before": snapshot,
                "after": snapshot,
            },
            *[
                {
                    "frameOffset": 0,
                    "address": "0x2004",
                    "value": f"0x{index:02X}",
                    "before": snapshot,
                    "after": snapshot,
                }
                for index in range(76)
            ],
        ]
        observation = {
            "framesRequested": 1,
            "framesRun": 1,
            "frames": [
                {
                    "ppuState": {
                        "renderingEnabled": True,
                        "backgroundEnabled": True,
                        "spritesEnabled": True,
                    }
                }
            ],
            "ppuEvents": events,
            "ppuEventCount": len(events),
            "ppuEventsObserved": len(events),
            "ppuTraceTruncated": False,
            "truncated": False,
            "stopReason": "framesComplete",
        }

        self.assertTrue(parity.validate_execution_observation(observation)["physical_valid"])

        events.insert(
            10,
            {
                "frameOffset": 0,
                "address": "0x2000",
                "value": "0x80",
                "before": snapshot,
                "after": snapshot,
            },
        )
        observation["ppuEventCount"] = len(events)
        observation["ppuEventsObserved"] = len(events)

        report = parity.validate_execution_observation(observation)

        self.assertFalse(report["physical_valid"])
        self.assertTrue(any("interleaved" in item for item in report["physical_violations"]))

    def test_observation_accepts_a_stale_frame_with_no_physical_publication(self) -> None:
        observation = {
            "framesRequested": 1,
            "framesRun": 1,
            "frames": [
                {
                    "ppuState": {
                        "renderingEnabled": True,
                        "backgroundEnabled": True,
                        "spritesEnabled": True,
                    }
                }
            ],
            "ppuEvents": [],
            "ppuEventCount": 0,
            "ppuEventsObserved": 0,
            "ppuTraceTruncated": False,
            "truncated": False,
            "stopReason": "framesComplete",
        }

        self.assertTrue(parity.validate_execution_observation(observation)["physical_valid"])

    def test_observation_rejects_discontinuous_ppudata_after_a_valid_address_pair(self) -> None:
        def snapshot(*, v: int, t: int, w: bool) -> dict[str, object]:
            return {
                "scanline": 245,
                "dot": 80,
                "vblank": False,
                "renderingActive": False,
                "v": f"0x{v:04X}",
                "t": f"0x{t:04X}",
                "x": 0,
                "w": w,
            }

        events = [
            {
                "frameOffset": 0,
                "address": "0x2006",
                "value": "0x24",
                "before": snapshot(v=0x2142, t=0x0140, w=False),
                "after": snapshot(v=0x2142, t=0x2400, w=True),
            },
            {
                "frameOffset": 0,
                "address": "0x2006",
                "value": "0x00",
                "before": snapshot(v=0x2142, t=0x2400, w=True),
                "after": snapshot(v=0x2400, t=0x2400, w=False),
            },
            {
                "frameOffset": 0,
                "address": "0x2007",
                "value": "0x11",
                "before": snapshot(v=0x2400, t=0x2400, w=False),
                "after": snapshot(v=0x2420, t=0x2400, w=False),
            },
            {
                "frameOffset": 0,
                "address": "0x2007",
                "value": "0x22",
                "before": snapshot(v=0x2999, t=0x2400, w=False),
                "after": snapshot(v=0x29B9, t=0x2400, w=False),
            },
        ]

        violations = parity.observed_physical_sequence_violations(
            {"framesRun": 1, "frames": [{}], "ppuEvents": events}
        )

        self.assertTrue(any("discontinuous" in item for item in violations))

    def test_observation_accepts_compact_ppudata_snapshots_with_next_before_target(self) -> None:
        def snapshot(*, v: int, t: int, w: bool) -> dict[str, object]:
            return {
                "scanline": 245,
                "dot": 80,
                "vblank": False,
                "renderingActive": False,
                "v": f"0x{v:04X}",
                "t": f"0x{t:04X}",
                "x": 0,
                "w": w,
            }

        events = [
            {
                "frameOffset": 0,
                "address": "0x2006",
                "value": "0x24",
                "before": snapshot(v=0x2142, t=0x0140, w=False),
                "after": snapshot(v=0x2142, t=0x2400, w=True),
            },
            {
                "frameOffset": 0,
                "address": "0x2006",
                "value": "0x00",
                "before": snapshot(v=0x2142, t=0x2400, w=True),
                "after": snapshot(v=0x2400, t=0x2400, w=False),
            },
            {
                "frameOffset": 0,
                "address": "0x2007",
                "value": "0x11",
                "before": snapshot(v=0x2400, t=0x2400, w=False),
                "after": snapshot(v=0x2400, t=0x2400, w=False),
            },
            {
                "frameOffset": 0,
                "address": "0x2007",
                "value": "0x22",
                "before": snapshot(v=0x2420, t=0x2400, w=False),
                "after": snapshot(v=0x2420, t=0x2400, w=False),
            },
        ]

        violations = parity.observed_physical_sequence_violations(
            {"framesRun": 1, "frames": [{}], "ppuEvents": events}
        )

        self.assertFalse(any("discontinuous" in item for item in violations))
        self.assertFalse(any("malformed PPUADDR" in item for item in violations))

    def test_nametable_dump_helpers_preserve_all_four_physical_tables(self) -> None:
        dump = {
            "nametables": [
                {
                    "address": f"0x{0x2000 + index * 0x400:04X}",
                    "hash": f"sha256:{index}",
                    "tileHash": f"sha256:tile-{index}",
                    "attributeHash": f"sha256:attribute-{index}",
                    "detail": {
                        "rows": [
                            " ".join(f"{index:02X}" for _ in range(32))
                            for _ in range(30)
                        ],
                        "attributeRows": [
                            " ".join(f"{index:02X}" for _ in range(8))
                            for _ in range(8)
                        ],
                    },
                }
                for index in range(4)
            ]
        }

        compact = parity.compact_nametable_hashes(dump)
        raw = parity.nametable_dump_bytes(dump)

        self.assertEqual(
            ["0x2000", "0x2400", "0x2800", "0x2C00"],
            [entry["address"] for entry in compact],
        )
        self.assertEqual(4_096, len(raw))
        self.assertEqual(bytes([0]) * 1_024, raw[:1_024])
        self.assertEqual(bytes([3]) * 1_024, raw[-1_024:])

    def test_observed_frame_decodes_runner_ram_through_relocated_abi(self) -> None:
        def probe(address: int, values: list[int]) -> dict[str, object]:
            return {
                "address": f"0x{address:04X}",
                "length": len(values),
                "bytesHex": " ".join(f"{value:02X}" for value in values),
            }

        root = Path(__file__).resolve().parents[3]
        real_abi = NesRuntimeAbi.load(
            root / "samples" / "runner" / "bin" / "runner.nes.runtime-abi.json",
            root / "samples" / "runner" / "bin" / "runner.nes",
        )

        class RelocatedAbi:
            offset = 0x1000

            def address(self, name: str) -> int:
                return real_abi.address(name) + self.offset

            def variable(self, name: str):
                variable = real_abi.variable(name)
                return replace(variable, address=variable.address + self.offset)

            def region(self, name: str):
                region = real_abi.region(name)
                return replace(region, start=region.start + self.offset)

        abi = RelocatedAbi()
        frame = {
            "screen": {"frameOffset": 1, "totalFrame": 501, "hash": "sha256:x"},
            "memory": [
                probe(abi.variable("player.x").address, [0x34, 0x12, 0x11, 0x01]),
                probe(abi.address("camera.X"), [0x06]),
                probe(abi.address("camera.Y"), [0x50]),
                probe(abi.address("camera.XHigh"), [0x01, 0x00]),
                probe(abi.address("packed camera.FrameCounterLow"), [0xF5, 0x01, 7, 7, 7, 7, 7, 0, 0, 0, 32, 8]),
                probe(abi.address("packed camera.CriticalSection"), [0, 1, 1, 2, 0, 0, 33, 0, 0, 32, 0, 0]),
                probe(abi.address("packed camera.Slot0"), [5]),
                probe(abi.address("packed camera.Slot0CommitPhase"), [2]),
                probe(abi.address("packed camera.Slot0PayloadCursor"), [30]),
                probe(abi.address("packed camera.Slot1"), [0]),
                probe(abi.address("packed camera.Slot1CommitPhase"), [0]),
                probe(abi.address("packed camera.Slot1PayloadCursor"), [0]),
                probe(abi.address("packed camera.PendingAxes"), [0, 0x06, 0x01, 0x50, 0x00]),
                probe(abi.address("WorldPack.CollisionDecodeCountLow"), [3, 0, 0xAA, 0xBB]),
                probe(
                    abi.region("WorldPack.EdgeSlot0").start,
                    list(range(41)),
                ),
                probe(
                    abi.region("WorldPack.EdgeSlot1").start,
                    [0] * 41,
                ),
            ],
            "ppuState": {"ppuctrl": "0x81", "v": "0x0D48", "t": "0x0540"},
        }

        observed = parity.observed_runtime_frame(frame, step=17, phase="right", abi=abi)

        self.assertEqual(0x1234, observed["state"]["player_x"])
        self.assertEqual(273, observed["state"]["player_y"])
        self.assertEqual(262, observed["state"]["requested_camera_x"])
        self.assertEqual(80, observed["state"]["requested_camera_y"])
        self.assertEqual(262, observed["state"]["visible_camera_x"])
        self.assertEqual(7, observed["state"]["lifecycle"]["commit"])
        self.assertEqual(
            {"state": 5, "commit_phase": 2, "payload_cursor": 30},
            observed["state"]["slots"][0],
        )
        self.assertEqual(list(range(41)), observed["state"]["slot_payloads"][0])
        self.assertEqual(
            {
                "axis": 1,
                "direction": 2,
                "target": 33,
                "payload_length": 32,
                "target_start": 0,
            },
            observed["state"]["commit_descriptor"],
        )
        self.assertEqual([5, 0], observed["state"]["slot_states"])
        self.assertEqual(32, observed["state"]["last_commit_writes"]["tiles"])
        self.assertEqual("sha256:x", observed["screen"]["hash"])
        self.assertEqual("0x81", observed["ppu_state"]["ppuctrl"])

    def test_focal_commit_selection_covers_both_256_crossings_and_target(self) -> None:
        def window(commit: int, phase: str, visible_x: int) -> dict[str, object]:
            frames = []
            for relative in range(-2, 3):
                frames.append(
                    {
                        "step": commit * 10 + relative,
                        "phase": phase,
                        "state": {
                            "visible_camera_x": visible_x + relative,
                            "lifecycle": {"commit": commit if relative >= 0 else commit - 1},
                        },
                    }
                )
            return {"commit": commit, "center_step": commit * 10, "frames": frames}

        windows = [
            window(1, "right", 9),
            window(30, "right", 249),
            window(31, "right", 257),
            window(38, "right-post", 306),
            window(46, "left-post", 255),
        ]

        selected = parity.select_focal_commit_windows(windows, target_camera_x=304)

        self.assertEqual(
            {
                "first-column": 1,
                "right-before-256": 30,
                "right-cross-256": 31,
                "right-target": 38,
                "left-cross-256": 46,
            },
            {label: window["commit"] for label, window in selected.items()},
        )
        self.assertEqual(set(selected), parity.REQUIRED_FOCAL_TRACE_LABELS)

    def test_palette_calibration_compares_equivalent_pixels_not_emulator_rgb(self) -> None:
        self.assertTrue(
            hasattr(parity, "calibrate_framebuffer_palette"),
            "The framebuffer comparison needs an emulator-palette calibration.",
        )
        self.assertTrue(
            hasattr(parity, "framebuffer_corruption_report"),
            "The harness needs an automatic transient-frame corruption report.",
        )
        reference = Image.new("RGB", (64, 64), (10, 20, 30))
        candidate = Image.new("RGB", (64, 64), (90, 80, 70))
        for y in range(32, 64):
            for x in range(64):
                reference.putpixel((x, y), (40, 50, 60))
                candidate.putpixel((x, y), (120, 110, 100))

        mapping = parity.calibrate_framebuffer_palette(reference, candidate)
        report = parity.framebuffer_corruption_report(reference, candidate, mapping)

        self.assertFalse(report["corrupt"])
        self.assertEqual(0, report["mismatch_pixels"])

    def test_transient_horizontal_line_and_vertical_column_are_rejected(self) -> None:
        self.assertTrue(hasattr(parity, "calibrate_framebuffer_palette"))
        self.assertTrue(hasattr(parity, "framebuffer_corruption_report"))
        reference = Image.new("RGB", (64, 64), (10, 20, 30))
        baseline = Image.new("RGB", (64, 64), (90, 80, 70))
        mapping = parity.calibrate_framebuffer_palette(reference, baseline)

        horizontal = baseline.copy()
        for x in range(64):
            horizontal.putpixel((x, 20), (1, 2, 3))
        horizontal_report = parity.framebuffer_corruption_report(
            reference,
            horizontal,
            mapping,
        )

        vertical = baseline.copy()
        for y in range(4, 60):
            for x in range(20, 28):
                vertical.putpixel((x, y), (1, 2, 3))
        vertical_report = parity.framebuffer_corruption_report(
            reference,
            vertical,
            mapping,
        )

        self.assertTrue(horizontal_report["corrupt"])
        self.assertTrue(horizontal_report["horizontal_lines"])
        self.assertTrue(vertical_report["corrupt"])
        self.assertTrue(vertical_report["vertical_columns"])

    def test_compact_observation_rejects_one_frame_line_and_column_anomalies(self) -> None:
        horizontal = {
            "hash": "sha256:horizontal",
            "changedPixels": 256,
            "changedBounds": {"x": 0, "y": 64, "width": 256, "height": 1},
            "changedTileRows": [{"row": 8, "mask": "0xFFFFFFFF"}],
        }
        vertical = {
            "hash": "sha256:vertical",
            "changedPixels": 960,
            "changedBounds": {"x": 80, "y": 32, "width": 8, "height": 120},
            "changedTileRows": [
                {"row": row, "mask": "0x00000400"} for row in range(4, 19)
            ],
        }

        horizontal_report = parity.compact_screen_anomaly_report(horizontal)
        vertical_report = parity.compact_screen_anomaly_report(vertical)

        self.assertTrue(horizontal_report["corrupt"])
        self.assertEqual([8], horizontal_report["horizontal_lines"])
        self.assertTrue(vertical_report["corrupt"])
        self.assertTrue(vertical_report["vertical_columns"])

    def test_tile_or_attribute_palette_difference_is_rejected(self) -> None:
        reference = Image.new("RGB", (32, 32), (10, 20, 30))
        candidate = Image.new("RGB", (32, 32), (90, 80, 70))
        mapping = parity.calibrate_framebuffer_palette(reference, candidate)
        for y in range(8, 16):
            for x in range(16, 24):
                candidate.putpixel((x, y), (1, 2, 3))

        report = parity.framebuffer_corruption_report(reference, candidate, mapping)

        self.assertTrue(report["corrupt"])
        self.assertEqual(64, report["mismatch_pixels"])

    def test_only_explicit_oam_rectangles_are_excluded_from_background_comparison(self) -> None:
        self.assertTrue(hasattr(parity, "calibrate_framebuffer_palette"))
        self.assertTrue(hasattr(parity, "framebuffer_corruption_report"))
        reference = Image.new("RGB", (64, 64), (10, 20, 30))
        candidate = Image.new("RGB", (64, 64), (90, 80, 70))
        mapping = parity.calibrate_framebuffer_palette(reference, candidate)
        for y in range(10, 26):
            for x in range(12, 20):
                candidate.putpixel((x, y), (1, 2, 3))

        unmasked = parity.framebuffer_corruption_report(reference, candidate, mapping)
        masked = parity.framebuffer_corruption_report(
            reference,
            candidate,
            mapping,
            ignored_rectangles=[(12, 10, 20, 26)],
        )

        self.assertTrue(unmasked["corrupt"])
        self.assertFalse(masked["corrupt"])

    def test_aprnes_oam_is_converted_to_cropped_framebuffer_masks(self) -> None:
        self.assertTrue(
            hasattr(parity, "aprnes_oam_rectangles"),
            "Per-frame sprite differences must be masked from background parity using OAM.",
        )
        oam = {
            "sprites": [
                {"x": 72, "y": 192, "visible": True},
                {"x": 255, "y": 255, "visible": False},
            ]
        }

        rectangles = parity.aprnes_oam_rectangles(oam)

        self.assertEqual([(71, 184, 81, 194)], rectangles)

    def test_complete_frame_runs_align_by_runtime_state_and_reject_one_bad_frame(self) -> None:
        self.assertTrue(
            hasattr(parity, "compare_transient_frame_runs"),
            "The three emulator frame sequences must be compared automatically.",
        )
        aprnes_frames = []
        fceumm_frames = []
        nestopia_frames = []
        for step in range(2):
            state = self.frame_state(step)
            aprnes = Image.new("RGB", (64, 64), (10, 20, 30))
            fceumm = Image.new("RGB", (64, 64), (90, 80, 70))
            nestopia = Image.new("RGB", (64, 64), (130, 140, 150))
            aprnes_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "framebuffer": aprnes,
                    "oam_rectangles": [],
                }
            )
            fceumm_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "framebuffer": fceumm,
                }
            )
            nestopia_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "framebuffer": nestopia,
                }
            )
        for x in range(64):
            fceumm_frames[1]["framebuffer"].putpixel((x, 20), (1, 2, 3))

        report = parity.compare_transient_frame_runs(
            {
                "aprnes": {"frames": aprnes_frames},
                "fceumm": {"frames": fceumm_frames},
                "nestopia": {"frames": nestopia_frames},
            }
        )

        self.assertFalse(report["verified"])
        self.assertEqual(2, report["emulators"]["fceumm"]["aligned_frames"])
        self.assertEqual(1, len(report["emulators"]["fceumm"]["corrupt_frames"]))
        self.assertEqual(0, len(report["emulators"]["nestopia"]["corrupt_frames"]))

    def test_compact_aprnes_frames_triangulate_two_full_rgb_frontends(self) -> None:
        aprnes_frames = []
        fceumm_frames = []
        nestopia_frames = []
        aprnes_indices = [0] * (64 * 64)
        aprnes_hash = f"sha256:{hashlib.sha256(bytes(aprnes_indices)).hexdigest()}"
        for step in range(2):
            state = self.frame_state(step)
            state = {
                **state,
                "player_x": 20,
                "player_y": 20,
                "visible_camera_x": 0,
                "visible_camera_y": 0,
            }
            regions = parity.background_regions(state, width=64, height=64)
            background_digest, _ = parity.canonical_background_region_hash(
                Image.new("RGB", (64, 64), (0, 0, 0)),
                {(0, 0, 0): 0},
                regions,
            )
            aprnes_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "screen": {
                        "hash": aprnes_hash,
                        "backgroundRegions": regions,
                        "backgroundRegionDigest": background_digest,
                    },
                    "screen_anomaly": {
                        "corrupt": False,
                        "horizontal_lines": [],
                        "vertical_columns": [],
                    },
                }
            )
            fceumm_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "framebuffer": Image.new("RGB", (64, 64), (90, 80, 70)),
                    "_raw_framebuffer": Image.new("RGB", (64, 64), (90, 80, 70)),
                }
            )
            nestopia_frames.append(
                {
                    "step": step,
                    "phase": "right",
                    "state": state,
                    "framebuffer": Image.new("RGB", (64, 64), (10, 20, 30)),
                    "_raw_framebuffer": Image.new("RGB", (64, 64), (10, 20, 30)),
                }
            )

        def checkpoints(color: tuple[int, int, int]) -> dict[str, object]:
            return {
                name: {
                    "_palette_indices": aprnes_indices,
                    "framebuffer": Image.new("RGB", (64, 64), color),
                    "_raw_framebuffer": Image.new("RGB", (64, 64), color),
                }
                for name in ("initial", "camera-target", "camera-return")
            }

        captures = {
            "aprnes": {
                "frames": aprnes_frames,
                "checkpoints": checkpoints((0, 0, 0)),
            },
            "fceumm": {
                "frames": fceumm_frames,
                "checkpoints": checkpoints((90, 80, 70)),
            },
            "nestopia": {
                "frames": nestopia_frames,
                "checkpoints": checkpoints((10, 20, 30)),
            },
        }
        clean = parity.compare_transient_frame_runs(captures)
        for y in range(8, 16):
            for x in range(16, 24):
                fceumm_frames[1]["framebuffer"].putpixel((x, y), (1, 2, 3))
        tile_corrupt = parity.compare_transient_frame_runs(captures)
        aprnes_frames[1]["screen"]["backgroundRegionDigest"] = (
            "sha256:isolated-aprnes-tile"
        )
        aprnes_corrupt = parity.compare_transient_frame_runs(captures)

        self.assertTrue(clean["verified"])
        self.assertEqual(2, clean["emulators"]["fceumm"]["aligned_frames"])
        self.assertEqual([], clean["emulators"]["aprnes"]["corrupt_frames"])
        self.assertFalse(tile_corrupt["verified"])
        self.assertFalse(aprnes_corrupt["verified"])
        self.assertEqual(
            1,
            len(aprnes_corrupt["emulators"]["aprnes"]["corrupt_frames"]),
        )

    def test_compact_acceptance_rejects_any_unmatched_retained_frame(self) -> None:
        state = self.frame_state(0)
        indices = [0] * 4
        digest = f"sha256:{hashlib.sha256(bytes(indices)).hexdigest()}"
        regions = [{"x": 0, "y": 0, "width": 2, "height": 2}]
        background_digest, _ = parity.canonical_background_region_hash(
            Image.new("RGB", (2, 2), (1, 2, 3)),
            {(1, 2, 3): 0},
            regions,
        )
        aprnes = {
            "step": 0,
            "phase": "right",
            "state": state,
            "screen": {
                "hash": digest,
                "backgroundRegions": regions,
                "backgroundRegionDigest": background_digest,
            },
            "screen_anomaly": {"corrupt": False},
        }
        external = {
            "step": 0,
            "phase": "right",
            "state": state,
            "framebuffer": Image.new("RGB", (2, 2), (1, 2, 3)),
            "_raw_framebuffer": Image.new("RGB", (2, 2), (1, 2, 3)),
        }
        calibration = {
            name: {
                "_palette_indices": indices,
                "framebuffer": Image.new("RGB", (2, 2), (1, 2, 3)),
                "_raw_framebuffer": Image.new("RGB", (2, 2), (1, 2, 3)),
            }
            for name in ("initial", "camera-target", "camera-return")
        }
        unmatched = {
            **external,
            "step": 1,
            "state": self.frame_state(99),
            "framebuffer": Image.new("RGB", (2, 2), (9, 9, 9)),
        }

        report = parity.compare_transient_frame_runs(
            {
                "aprnes": {"frames": [aprnes], "checkpoints": calibration},
                "fceumm": {
                    "frames": [external, unmatched],
                    "checkpoints": calibration,
                },
                "nestopia": {"frames": [external], "checkpoints": calibration},
            }
        )

        self.assertFalse(report["verified"])
        self.assertEqual(
            [{"step": 1, "phase": "right"}],
            report["emulators"]["fceumm"]["unmatched_candidate_frames"],
        )


class PpuCommitTraceTests(unittest.TestCase):
    @staticmethod
    def snapshot(
        *,
        v: int,
        t: int | None = None,
        x: int = 0,
        w: bool = False,
        vblank: bool = True,
        dot: int = 80,
    ) -> dict[str, object]:
        return {
            "scanline": 241,
            "dot": dot,
            "vblank": vblank,
            "renderingActive": False,
            "v": f"0x{v:04X}",
            "t": f"0x{(v if t is None else t):04X}",
            "x": x,
            "w": w,
        }

    @classmethod
    def event(
        cls,
        address: int,
        value: int,
        cpu_cycle: int,
        before: dict[str, object],
        after: dict[str, object],
    ) -> dict[str, object]:
        return {
            "frameOffset": 0,
            "frame": 700,
            "cpuCycle": cpu_cycle,
            "instructionCounter": 120_000 + cpu_cycle,
            "pc": "0xD234",
            "address": f"0x{address:04X}",
            "register": {
                0x2000: "PPUCTRL",
                0x2001: "PPUMASK",
                0x2003: "OAMADDR",
                0x2004: "OAMDATA",
                0x2005: "PPUSCROLL",
                0x2006: "PPUADDR",
                0x2007: "PPUDATA",
            }[address],
            "value": f"0x{value:02X}",
            "before": before,
            "after": after,
        }

    @classmethod
    def valid_events(
        cls,
        *,
        payload_length: int = 32,
        target_start: int = 0,
    ) -> list[dict[str, object]]:
        cycle = 100
        events = [
            cls.event(
                0x2003,
                0,
                cycle,
                cls.snapshot(v=0x2142),
                cls.snapshot(v=0x2142),
            ),
            *[
                cls.event(
                    0x2004,
                    index,
                    cycle + 4 + index * 4,
                    cls.snapshot(v=0x2142),
                    cls.snapshot(v=0x2142),
                )
                for index in range(76)
            ],
        ]
        cycle += 4 + 76 * 4
        events.append(
            cls.event(
                0x2000,
                0x84,
                cycle,
                cls.snapshot(v=0x2142),
                cls.snapshot(v=0x2142),
            )
        )
        cycle += 10
        previous_address: int | None = None
        address = 0x2142
        for index in range(payload_length):
            tile_row = (target_start + index) % 60
            vertical_table = tile_row // 30
            local_row = tile_row % 30
            tile_address = 0x2401 + vertical_table * 0x800 + local_row * 32
            if previous_address is None or tile_address != previous_address + 32:
                events.extend(
                    (
                        cls.event(
                            0x2006,
                            tile_address >> 8,
                            cycle,
                            cls.snapshot(v=address, t=0x0140, w=False),
                            cls.snapshot(
                                v=address,
                                t=tile_address & 0x3F00,
                                w=True,
                            ),
                        ),
                        cls.event(
                            0x2006,
                            tile_address & 0xFF,
                            cycle + 10,
                            cls.snapshot(
                                v=address,
                                t=tile_address & 0x3F00,
                                w=True,
                            ),
                            cls.snapshot(v=tile_address, t=tile_address, w=False),
                        ),
                    )
                )
                cycle += 20
            events.append(
                cls.event(
                    0x2007,
                    index,
                    cycle,
                    cls.snapshot(v=tile_address),
                    cls.snapshot(v=(tile_address + 32) & 0x7FFF),
                )
            )
            cycle += 20
            address = (tile_address + 32) & 0x7FFF
            previous_address = tile_address
        events.append(
            cls.event(
                0x2000,
                0x80,
                cycle,
                cls.snapshot(v=address),
                cls.snapshot(v=address),
            )
        )
        cycle += 10
        attribute_count = (target_start % 4 + payload_length + 3) // 4
        for index in range(attribute_count):
            tile_row = ((target_start & 0xFC) + index * 4) % 60
            vertical_table = tile_row // 30
            local_row = tile_row % 30
            attribute_address = (
                0x27C0 + vertical_table * 0x800 + (local_row // 4) * 8
            )
            events.extend(
                (
                    cls.event(
                        0x2006,
                        attribute_address >> 8,
                        cycle,
                        cls.snapshot(v=address, t=0x0140, w=False),
                        cls.snapshot(v=address, t=attribute_address & 0x3F00, w=True),
                    ),
                    cls.event(
                        0x2006,
                        attribute_address & 0xFF,
                        cycle + 10,
                        cls.snapshot(
                            v=address,
                            t=attribute_address & 0x3F00,
                            w=True,
                        ),
                        cls.snapshot(v=attribute_address, t=attribute_address, w=False),
                    ),
                    cls.event(
                        0x2007,
                        index,
                        cycle + 20,
                        cls.snapshot(v=attribute_address),
                        cls.snapshot(v=attribute_address + 1),
                    ),
                )
            )
            cycle += 30
        events.extend(
            (
                cls.event(
                    0x2000,
                    0x80,
                    cycle,
                    cls.snapshot(v=address, t=0x0140, w=False),
                    cls.snapshot(v=address, t=0x0140, w=False),
                ),
                cls.event(
                    0x2005,
                    8,
                    cycle + 10,
                    cls.snapshot(v=address, t=0x0140, x=0, w=False),
                    cls.snapshot(v=address, t=0x0141, x=0, w=True),
                ),
                cls.event(
                    0x2005,
                    80,
                    cycle + 20,
                    cls.snapshot(v=address, t=0x0141, x=0, w=True),
                    cls.snapshot(v=address, t=0x0141, x=0, w=False),
                ),
            )
        )
        return events

    @classmethod
    def valid_trace(
        cls,
        *,
        payload_length: int = 32,
        target_start: int = 0,
    ) -> dict[str, object]:
        events = cls.valid_events(
            payload_length=payload_length,
            target_start=target_start,
        )
        return {
            "framesRequested": 1,
            "framesRun": 1,
            "initialPpuState": {
                "renderingEnabled": True,
                "backgroundEnabled": True,
                "spritesEnabled": True,
                "timeline": {"cycles": 10_000},
            },
            "finalPpuState": {
                "renderingEnabled": True,
                "backgroundEnabled": True,
                "spritesEnabled": True,
                "w": False,
                "timeline": {"cycles": 12_000},
            },
            "events": events,
            "eventCount": len(events),
            "eventsObserved": len(events),
            "truncated": False,
            "hitBreakpoint": False,
            "stopReason": "framesComplete",
            "timeline": {"frames": 701, "cycles": 12_000, "instructions": 4_000},
        }

    @staticmethod
    def runtime_state(
        commit: int,
        *,
        payload_length: int = 32,
        target_start: int = 0,
    ) -> dict[str, object]:
        final = commit == 8
        attribute_count = (target_start % 4 + payload_length + 3) // 4
        payload = list(range(32)) + list(range(9))
        return {
            "requested_camera_x": 8,
            "visible_camera_x": 7 if commit == 7 else 9,
            "visible_camera_y": 80,
            "lifecycle": {
                "request": 8,
                "prepare": 8,
                "resident": 8,
                "commit": commit,
                "release": commit,
            },
            "forbidden_commit_work": {"bank": 0, "directory": 0, "decode": 0},
            "last_commit_writes": {
                "tiles": payload_length,
                "attributes": attribute_count,
            },
            "critical_section": 0,
            "selected_slot": 0,
            "slots": [
                {
                    "state": 5 if final else 3,
                    "commit_phase": 0,
                    "payload_cursor": 0,
                },
                {"state": 0, "commit_phase": 0, "payload_cursor": 0},
            ],
            "slot_payloads": [payload, [0] * len(payload)],
            "pending_axes": 0 if final else 1,
            "commit_descriptor": {
                "axis": 1,
                "direction": 2,
                "target": 33,
                "payload_length": payload_length,
                "target_start": target_start,
            },
        }

    def validate(self, trace: dict[str, object]) -> dict[str, object]:
        return parity.validate_ppu_commit_trace(
            trace,
            before_state=self.runtime_state(7),
            after_state=self.runtime_state(8),
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

    def test_commit_trace_requires_32_tiles_8_attributes_and_vblank_ordering(self) -> None:
        self.assertTrue(
            hasattr(parity, "validate_ppu_commit_trace"),
            "The AprNes path must validate the real per-commit PPU trace.",
        )

        report = self.validate(self.valid_trace())

        self.assertTrue(report["valid"])
        self.assertEqual(32, report["tile_writes"])
        self.assertEqual(8, report["attribute_writes"])
        self.assertEqual([], report["violations"])

    def test_commit_trace_accepts_runner_payload_30_start_10_without_false_positive(self) -> None:
        trace = self.valid_trace(payload_length=30, target_start=10)
        report = parity.validate_ppu_commit_trace(
            trace,
            before_state=self.runtime_state(
                7,
                payload_length=30,
                target_start=10,
            ),
            after_state=self.runtime_state(
                8,
                payload_length=30,
                target_start=10,
            ),
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertTrue(report["valid"], report["violations"])
        self.assertEqual(30, report["tile_writes"])
        self.assertEqual(8, report["attribute_writes"])
        self.assertEqual(
            ["0x2541", "0x2C01"],
            [pair["address"] for pair in report["ppuaddr_pairs"] if pair["kind"] == "tiles"],
        )

    def test_commit_trace_accepts_exact_runner_final_attribute_only_phase(self) -> None:
        trace = self.valid_trace(payload_length=30, target_start=10)
        events = trace["events"]
        vertical = next(
            index
            for index, event in enumerate(events)
            if event["register"] == "PPUCTRL" and event["value"] == "0x84"
        )
        horizontal = next(
            index
            for index, event in enumerate(events)
            if index > vertical
            and event["register"] == "PPUCTRL"
            and event["value"] == "0x80"
        )
        del events[vertical:horizontal]
        del events[vertical + 1 : vertical + 7]
        trace["eventCount"] = len(events)
        trace["eventsObserved"] = len(events)
        before = self.runtime_state(7, payload_length=30, target_start=10)
        before["slots"][0] = {
            "state": 4,
            "commit_phase": 2,
            "payload_cursor": 34,
        }
        after = self.runtime_state(8, payload_length=30, target_start=10)
        after["slots"][0] = {
            "state": 5,
            "commit_phase": 3,
            "payload_cursor": 40,
        }
        after["last_commit_writes"] = {"tiles": 0, "attributes": 6}

        report = parity.validate_ppu_commit_trace(
            trace,
            before_state=before,
            after_state=after,
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertTrue(report["valid"], report["violations"])
        self.assertEqual(0, report["tile_writes"])
        self.assertEqual(6, report["attribute_writes"])

    def test_cli_defaults_to_full_and_accepts_explicit_physical_gate(self) -> None:
        with mock.patch("sys.argv", ["verify_runner_visual_parity.py"]):
            self.assertEqual("full", parity.parse_args().gate)
        with mock.patch(
            "sys.argv",
            ["verify_runner_visual_parity.py", "--gate", "physical"],
        ):
            self.assertEqual("physical", parity.parse_args().gate)

    def test_physical_gate_does_not_require_external_emulator_files(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            rom = root / "runner.nes"
            rom.write_bytes(b"NES\x1A")
            args = SimpleNamespace(
                rom=rom,
                runtime_abi=root / "runner.nes.runtime-abi.json",
                artifacts=root / "artifacts",
                gate="physical",
                fceumm_core=root / "missing-fceumm.so",
            )
            abi = object()
            with (
                mock.patch.object(parity, "parse_args", return_value=args),
                mock.patch.object(parity.NesRuntimeAbi, "load", return_value=abi),
                mock.patch.object(parity, "activate_runtime_abi"),
                mock.patch.object(parity, "run_physical_gate", return_value=0) as gate,
            ):
                self.assertEqual(0, parity.main())

            gate.assert_called_once_with(args, rom.resolve(), args.artifacts.resolve())

    def test_commit_trace_requires_one_contiguous_76_byte_oam_publication(self) -> None:
        trace = self.valid_trace()
        events = trace["events"]
        events.insert(10, self.event(
            0x2001,
            0x1E,
            130,
            self.snapshot(v=0x2142),
            self.snapshot(v=0x2142),
        ))
        trace["eventCount"] = len(events)
        trace["eventsObserved"] = len(events)

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("contiguous" in item for item in report["violations"]))

    def test_commit_trace_rejects_rendering_suppression(self) -> None:
        trace = self.valid_trace()
        events = trace["events"]
        events.insert(0, self.event(
            0x2001,
            0x00,
            96,
            self.snapshot(v=0x2142),
            self.snapshot(v=0x2142),
        ))
        trace["eventCount"] = len(events)
        trace["eventsObserved"] = len(events)

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("rendering" in item.lower() for item in report["violations"]))

    def test_commit_trace_requires_rendering_enabled_at_both_frame_boundaries(self) -> None:
        trace = self.valid_trace()
        trace["finalPpuState"]["spritesEnabled"] = False

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("rendering" in item.lower() for item in report["violations"]))

    def test_commit_trace_requires_coherent_final_slot_commit_and_release(self) -> None:
        after = self.runtime_state(8)
        after["lifecycle"] = {**after["lifecycle"], "release": 7}
        after["slots"][0] = {**after["slots"][0], "state": 4}
        after["pending_axes"] = 1

        report = parity.validate_ppu_commit_trace(
            self.valid_trace(),
            before_state=self.runtime_state(7),
            after_state=after,
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertFalse(report["valid"])
        self.assertTrue(any("release" in item.lower() for item in report["violations"]))
        self.assertTrue(any("selected slot" in item.lower() for item in report["violations"]))
        self.assertTrue(any("pending axis" in item.lower() for item in report["violations"]))

    def test_commit_trace_preserves_unrelated_pending_axis_bits(self) -> None:
        before = self.runtime_state(7)
        before["pending_axes"] = 3
        after = self.runtime_state(8)
        after["pending_axes"] = 0

        report = parity.validate_ppu_commit_trace(
            self.valid_trace(),
            before_state=before,
            after_state=after,
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertFalse(report["valid"])
        self.assertTrue(any("unrelated" in item for item in report["violations"]))

    def test_commit_trace_rejects_ppudata_after_vblank(self) -> None:
        trace = self.valid_trace()
        ppudata = next(
            event for event in trace["events"] if event["register"] == "PPUDATA"
        )
        ppudata["before"] = {
            **ppudata["before"],
            "scanline": 261,
            "dot": 12,
            "vblank": False,
            "renderingActive": True,
        }

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(
            any("outside VBlank" in violation for violation in report["violations"])
        )

    def test_commit_trace_accepts_cleared_ppustatus_latch_during_physical_vblank(self) -> None:
        trace = self.valid_trace()
        for event in trace["events"]:
            event["before"] = {**event["before"], "vblank": False}
            event["after"] = {**event["after"], "vblank": False}
        trace["events"][2]["after"] = {
            **trace["events"][2]["after"],
            "v": "0x2142",
        }

        report = self.validate(trace)

        self.assertTrue(report["valid"], report["violations"])

    def test_commit_trace_accepts_a_second_vertical_segment_at_a_physical_boundary(self) -> None:
        report = self.validate(self.valid_trace())

        self.assertTrue(report["valid"], report["violations"])
        self.assertEqual(
            ["0x2401", "0x2C01"],
            [pair["address"] for pair in report["ppuaddr_pairs"] if pair["kind"] == "tiles"],
        )

    def test_commit_trace_rejects_31_or_33_tiles(self) -> None:
        for tile_count in (31, 33):
            with self.subTest(tile_count=tile_count):
                trace = self.valid_trace()
                events = trace["events"]
                restore_index = next(
                    index
                    for index, event in enumerate(events)
                    if event["register"] == "PPUCTRL" and event["value"] == "0x80"
                )
                if tile_count == 31:
                    del events[restore_index - 1]
                else:
                    events.insert(restore_index, dict(events[restore_index - 1]))
                trace["eventCount"] = len(events)
                trace["eventsObserved"] = len(events)

                report = self.validate(trace)

                self.assertFalse(report["valid"])
                self.assertTrue(
                    any(f"{tile_count} tiles" in item for item in report["violations"])
                )

    def test_commit_trace_rejects_7_or_9_attributes(self) -> None:
        for attribute_count in (7, 9):
            with self.subTest(attribute_count=attribute_count):
                trace = self.valid_trace()
                events = trace["events"]
                attribute_data = [
                    event for event in events if event["register"] == "PPUDATA"
                ][32:]
                last_attribute_index = events.index(attribute_data[-1])
                if attribute_count == 7:
                    del events[last_attribute_index - 2 : last_attribute_index + 1]
                else:
                    events[last_attribute_index + 1 : last_attribute_index + 1] = [
                        dict(event)
                        for event in events[last_attribute_index - 2 : last_attribute_index + 1]
                    ]
                trace["eventCount"] = len(events)
                trace["eventsObserved"] = len(events)

                report = self.validate(trace)

                self.assertFalse(report["valid"])
                self.assertTrue(
                    any(
                        f"{attribute_count} attributes" in item
                        for item in report["violations"]
                    )
                )

    def test_commit_trace_rejects_unrestored_ppuctrl_and_truncation(self) -> None:
        unrestored = self.valid_trace()
        restore = next(
            event
            for event in unrestored["events"]
            if event["register"] == "PPUCTRL" and event["value"] == "0x80"
        )
        restore["value"] = "0x84"
        truncated = self.valid_trace()
        truncated["truncated"] = True
        truncated["eventsObserved"] = truncated["eventCount"] + 1

        unrestored_report = self.validate(unrestored)
        truncated_report = self.validate(truncated)

        self.assertFalse(unrestored_report["valid"])
        self.assertTrue(
            any("PPUCTRL" in item for item in unrestored_report["violations"])
        )
        self.assertFalse(truncated_report["valid"])
        self.assertTrue(
            any("truncated" in item.lower() for item in truncated_report["violations"])
        )

    def test_commit_trace_rejects_missing_scroll_restore(self) -> None:
        trace = self.valid_trace()
        del trace["events"][-2:]
        trace["eventCount"] = len(trace["events"])
        trace["eventsObserved"] = len(trace["events"])

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("PPUSCROLL" in item for item in report["violations"]))

    def test_commit_trace_rejects_semantically_wrong_attribute_stride(self) -> None:
        trace = self.valid_trace()
        attribute_data = [
            event for event in trace["events"] if event["register"] == "PPUDATA"
        ][32:]
        second_data = attribute_data[1]
        data_index = trace["events"].index(second_data)
        wrong_address = 0x27C1
        high, low = trace["events"][data_index - 2 : data_index]
        low["value"] = f"0x{wrong_address & 0xFF:02X}"
        low["after"] = self.snapshot(v=wrong_address, t=wrong_address, w=False)
        second_data["before"] = self.snapshot(v=wrong_address)
        second_data["after"] = self.snapshot(v=wrong_address + 1)

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("attribute address sequence" in item for item in report["violations"]))

    def test_commit_trace_rejects_ordered_ppudata_that_disagrees_with_slot_payload(self) -> None:
        trace = self.valid_trace()
        tile_data = [
            event for event in trace["events"] if event["register"] == "PPUDATA"
        ][:32]
        tile_data[0]["value"], tile_data[1]["value"] = (
            tile_data[1]["value"],
            tile_data[0]["value"],
        )

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(any("selected slot payload" in item for item in report["violations"]))

    def test_commit_trace_rejects_incoherent_final_phase_and_cursor_transition(self) -> None:
        before = self.runtime_state(7)
        after = self.runtime_state(8)
        before["slots"][0] = {
            "state": 4,
            "commit_phase": 31,
            "payload_cursor": 31,
        }
        after["slots"][0] = {
            "state": 5,
            "commit_phase": 17,
            "payload_cursor": 1,
        }

        report = parity.validate_ppu_commit_trace(
            self.valid_trace(),
            before_state=before,
            after_state=after,
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertFalse(report["valid"])
        self.assertTrue(any("phase" in item.lower() for item in report["violations"]))

    def test_commit_trace_rejects_column_shifted_away_from_runtime_target(self) -> None:
        trace = self.valid_trace()
        restore_index = next(
            index
            for index, event in enumerate(trace["events"])
            if event["register"] == "PPUCTRL" and event["value"] == "0x80"
        )
        for event in trace["events"][:restore_index]:
            if event["register"] == "PPUADDR" and event["value"] == "0x01":
                event["value"] = "0x02"
            for side in ("before", "after"):
                snapshot = event[side]
                for field in ("v", "t"):
                    address = int(snapshot[field], 16)
                    if 0x2000 <= address < 0x3000 and address % 32 == 1:
                        snapshot[field] = f"0x{address + 1:04X}"
        before = self.runtime_state(7)
        before["commit_descriptor"] = {
            **before["commit_descriptor"],
            "target": 34,
        }

        report = parity.validate_ppu_commit_trace(
            trace,
            before_state=before,
            after_state=self.runtime_state(8),
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertFalse(report["valid"])
        self.assertTrue(
            any("runtime commit target" in item for item in report["violations"])
        )

    def test_commit_trace_rejects_scroll_values_that_disagree_with_runtime_camera(self) -> None:
        trace = self.valid_trace()
        trace["events"][-3]["value"] = "0x83"
        trace["events"][-2]["value"] = "0xAA"
        trace["events"][-1]["value"] = "0xAA"

        report = self.validate(trace)

        self.assertFalse(report["valid"])
        self.assertTrue(
            any("runtime camera" in item for item in report["violations"])
        )

    def test_commit_trace_rejects_bad_ppuaddr_and_over_budget_commit(self) -> None:
        bad_address = self.valid_trace()
        ppuaddr = [
            event for event in bad_address["events"] if event["register"] == "PPUADDR"
        ]
        low = ppuaddr[1]
        low["after"] = {**low["after"], "v": "0x2402", "t": "0x2402"}
        over_budget = self.valid_trace()
        [
            event for event in over_budget["events"] if event["register"] == "PPUDATA"
        ][-1]["cpuCycle"] = 3_000

        bad_address_report = self.validate(bad_address)
        over_budget_report = self.validate(over_budget)

        self.assertFalse(bad_address_report["valid"])
        self.assertTrue(
            any("PPUADDR" in item for item in bad_address_report["violations"])
        )
        self.assertFalse(over_budget_report["valid"])
        self.assertTrue(
            any("budget" in item for item in over_budget_report["violations"])
        )

    def test_commit_trace_rejects_worldpack_work_inside_commit(self) -> None:
        after = self.runtime_state(8)
        after["forbidden_commit_work"] = {"bank": 0, "directory": 1, "decode": 0}

        report = parity.validate_ppu_commit_trace(
            self.valid_trace(),
            before_state=self.runtime_state(7),
            after_state=after,
            max_cpu_cycles=parity.PPU_COMMIT_BUDGET_CYCLES,
        )

        self.assertFalse(report["valid"])
        self.assertTrue(
            any("WorldPack" in item for item in report["violations"])
        )


if __name__ == "__main__":
    unittest.main()
