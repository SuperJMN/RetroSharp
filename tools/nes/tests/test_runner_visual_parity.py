from __future__ import annotations

import hashlib
import struct
import tempfile
import unittest
from pathlib import Path

from tools.nes.runner_visual_parity import (
    ConfigIntegrityGuard,
    build_retroarch_command,
    build_retroarch_config,
)
from tools.nes.verify_runner_visual_parity import (
    authored_collision_evidence,
    parse_fceumm_state,
    parse_nestopia_state,
    visible_background_cells,
)


class RetroArchIsolationTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
