#!/usr/bin/env python3
"""Differential visual acceptance for RetroSharp issue #327."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shlex
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
import xml.etree.ElementTree as ElementTree

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))

from tools.nes.runner_visual_parity import (
    ConfigIntegrityGuard,
    LibretroSession,
    McpClient,
    RETRO_DEVICE_ID_JOYPAD_A,
    RETRO_DEVICE_ID_JOYPAD_LEFT,
    RETRO_DEVICE_ID_JOYPAD_RIGHT,
    RetroArchNetworkSession,
)


DEFAULT_ROM = ROOT / "samples" / "runner" / "bin" / "runner.nes"
DEFAULT_FCEUMM_CORE = (
    Path.home()
    / ".var"
    / "app"
    / "org.libretro.RetroArch"
    / "config"
    / "retroarch"
    / "cores"
    / "fceumm_libretro.so"
)
PERSISTENT_RETROARCH_CONFIG = (
    Path.home()
    / ".var"
    / "app"
    / "org.libretro.RetroArch"
    / "config"
    / "retroarch"
    / "retroarch.cfg"
)
PERSISTENT_FCEUMM_OPTIONS = (
    Path.home()
    / ".var"
    / "app"
    / "org.libretro.RetroArch"
    / "config"
    / "retroarch"
    / "config"
    / "FCEUmm"
    / "FCEUmm.opt"
)
NESTOPIA_CORE_URL = (
    "https://buildbot.libretro.com/nightly/linux/x86_64/latest/"
    "nestopia_libretro.so.zip"
)
IDLE_FRAMES = 500
RIGHT_FRAMES = 360
TARGET_CAMERA_X = 304
LEFT_FRAMES = 160
RETURN_CAMERA_X = 240
JUMP_HOLD_FRAMES = 6

PLAYER_X_LOW = 0x0000
PLAYER_Y_LOW = 0x0002
REQUESTED_CAMERA_X_LOW = 0x00E0
REQUESTED_CAMERA_Y_LOW = 0x00E1
RUNTIME_BLOCK_START = 0x0318
RUNTIME_BLOCK_END = 0x03FB


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Hold RIGHT on the exact tracked runner and compare every frame across "
            "AprNes/NesMcp, RetroArch FCEUmm, and Nestopia."
        )
    )
    parser.add_argument("--rom", type=Path, default=DEFAULT_ROM)
    parser.add_argument("--fceumm-core", type=Path, default=DEFAULT_FCEUMM_CORE)
    parser.add_argument("--nestopia-core", type=Path)
    parser.add_argument("--nestopia-core-url", default=NESTOPIA_CORE_URL)
    parser.add_argument("--mcp-command", default="dnx Nes.Mcp --yes")
    parser.add_argument(
        "--retroarch-command",
        default="flatpak run --command=retroarch org.libretro.RetroArch",
    )
    parser.add_argument(
        "--retroarch-default-config-command",
        default="flatpak run --command=cat org.libretro.RetroArch /app/etc/retroarch.cfg",
    )
    parser.add_argument(
        "--artifacts",
        type=Path,
        default=ROOT / "artifacts" / "nes-runner-visual-parity",
    )
    parser.add_argument("--idle-frames", type=int, default=IDLE_FRAMES)
    parser.add_argument(
        "--right-frames",
        type=int,
        default=RIGHT_FRAMES,
        help="Maximum RIGHT frames allowed while reaching --target-camera-x.",
    )
    parser.add_argument("--target-camera-x", type=int, default=TARGET_CAMERA_X)
    parser.add_argument(
        "--left-frames",
        type=int,
        default=LEFT_FRAMES,
        help="Maximum LEFT frames allowed while returning through camera X 256.",
    )
    parser.add_argument("--return-camera-x", type=int, default=RETURN_CAMERA_X)
    parser.add_argument("--jump-hold-frames", type=int, default=JUMP_HOLD_FRAMES)
    return parser.parse_args()


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def parse_bytes(result: dict[str, object]) -> list[int]:
    return [int(value, 16) for value in str(result["bytesHex"]).split()]


def runtime_snapshot(read) -> dict[str, object]:
    player = read(PLAYER_X_LOW, 4)
    requested_low = read(REQUESTED_CAMERA_X_LOW, 2)
    runtime = read(RUNTIME_BLOCK_START, RUNTIME_BLOCK_END - RUNTIME_BLOCK_START + 1)

    def byte(address: int) -> int:
        return runtime[address - RUNTIME_BLOCK_START]

    def word(address: int) -> int:
        return byte(address) | byte(address + 1) << 8

    return {
        "player_x": player[0] | player[1] << 8,
        "player_y": player[2] | player[3] << 8,
        "requested_camera_x": requested_low[0] | byte(0x0318) << 8,
        "requested_camera_y": requested_low[1] | byte(0x0319) << 8,
        "hardware_frame": word(0x036E),
        "lifecycle": {
            "request": byte(0x0370),
            "prepare": byte(0x0371),
            "resident": byte(0x0372),
            "commit": byte(0x0373),
            "release": byte(0x0374),
        },
        "forbidden_commit_work": {
            "bank": byte(0x0375),
            "directory": byte(0x0376),
            "decode": byte(0x0377),
        },
        "last_commit_writes": {
            "tiles": byte(0x0378),
            "attributes": byte(0x0379),
        },
        "critical_section": byte(0x0380),
        "selected_slot": byte(0x0381),
        "slot_states": [byte(0x0390), byte(0x03A0)],
        "pending_axes": byte(0x03CA),
        "visible_camera_x": word(0x03CB),
        "visible_camera_y": word(0x03CD),
        "collision_decodes": word(0x03F8),
        "gameplay_ticks": byte(0x03FA),
        "audio_ticks": byte(0x03FB),
    }


def normalize_frame(image: Image.Image) -> Image.Image:
    image = image.convert("RGB")
    if image.size == (256, 240):
        return image.crop((0, 8, 256, 232))
    if image.size != (256, 224):
        raise ValueError(f"Unexpected NES framebuffer size: {image.size}")
    return image


def parse_fceumm_state(data: bytes) -> dict[str, bytes]:
    start = data.find(b"FCS\xFF")
    if start < 0:
        raise ValueError("RetroArch state does not contain an FCEUmm FCS payload.")
    state = data[start:]
    total_size = struct.unpack_from("<I", state, 4)[0]
    position = 16
    remaining = total_size
    fields: dict[str, bytes] = {}
    while remaining:
        if position + 5 > len(state):
            raise ValueError("Truncated FCEUmm state chunk.")
        chunk_size = struct.unpack_from("<I", state, position + 1)[0]
        chunk_end = position + 5 + chunk_size
        if chunk_end > len(state) or chunk_size + 5 > remaining:
            raise ValueError("Invalid FCEUmm state chunk length.")
        entry = position + 5
        while entry < chunk_end:
            if entry + 8 > chunk_end:
                raise ValueError("Truncated FCEUmm state field.")
            name = state[entry : entry + 4].rstrip(b"\0").decode("ascii")
            length = struct.unpack_from("<I", state, entry + 4)[0]
            value_start = entry + 8
            value_end = value_start + length
            if value_end > chunk_end:
                raise ValueError(f"Invalid FCEUmm field length for {name}.")
            fields[name] = state[value_start:value_end]
            entry = value_end
        position = chunk_end
        remaining -= chunk_size + 5
    for required, length in (("RAM", 0x800), ("NTAR", 0x800), ("EXNR", 0x800), ("PRAM", 0x20)):
        if len(fields.get(required, b"")) != length:
            raise ValueError(f"FCEUmm state field {required} is missing or has the wrong size.")
    return fields


def nestopia_chunks(data: bytes, start: int, end: int) -> dict[bytes, bytes]:
    chunks: dict[bytes, bytes] = {}
    position = start
    while position + 8 <= end:
        name = data[position : position + 4]
        length = struct.unpack_from("<I", data, position + 4)[0]
        value_start = position + 8
        value_end = value_start + length
        if value_end > end:
            raise ValueError(f"Invalid Nestopia chunk length for {name!r}.")
        chunks[name] = data[value_start:value_end]
        position = value_end
    if position != end:
        raise ValueError("Trailing bytes in Nestopia state chunk.")
    return chunks


def nestopia_uncompressed(data: bytes, expected_length: int) -> bytes:
    if len(data) != expected_length + 1 or data[0] != 0:
        raise ValueError("Nestopia libretro state unexpectedly compressed a required field.")
    return data[1:]


def parse_nestopia_state(data: bytes) -> dict[str, bytes]:
    root = nestopia_chunks(data, 0, len(data) - 8)
    top = root.get(b"NST\x1A")
    if top is None:
        raise ValueError("Nestopia state is missing its NST root chunk.")
    top_chunks = nestopia_chunks(top, 0, len(top))
    cpu = nestopia_chunks(top_chunks[b"CPU\0"], 0, len(top_chunks[b"CPU\0"]))
    ppu = nestopia_chunks(top_chunks[b"PPU\0"], 0, len(top_chunks[b"PPU\0"]))
    image = nestopia_chunks(top_chunks[b"IMG\0"], 0, len(top_chunks[b"IMG\0"]))
    mapper = nestopia_chunks(image[b"MPR\0"], 0, len(image[b"MPR\0"]))
    mapping = nestopia_chunks(mapper[b"NMT\0"], 0, len(mapper[b"NMT\0"]))
    expected_mapping = bytes((1, 0, 0, 1, 1, 0, 1, 2, 0, 1, 3, 0))
    if mapping.get(b"BNK\0") != expected_mapping:
        raise ValueError("Nestopia did not map four physical nametables to VRAM banks 0..3.")
    return {
        "RAM": nestopia_uncompressed(cpu[b"RAM\0"], 0x800),
        "NAMETABLES": nestopia_uncompressed(mapper[b"VRM\0"], 0x1000),
        "PALETTE": nestopia_uncompressed(ppu[b"PAL\0"], 0x20),
        "PPU_REGISTERS": ppu[b"REG\0"],
    }


def nametable_snapshot(raw: bytes) -> dict[str, object]:
    if len(raw) != 0x1000:
        raise ValueError("Four-screen nametable state must contain exactly 4096 bytes.")
    tables = [raw[offset : offset + 0x400] for offset in range(0, 0x1000, 0x400)]
    return {
        "raw": raw,
        "tables": [
            {
                "address": f"0x{0x2000 + index * 0x400:04X}",
                "tiles_sha256": hashlib.sha256(table[:0x3C0]).hexdigest(),
                "attributes_sha256": hashlib.sha256(table[0x3C0:]).hexdigest(),
                "raw_sha256": hashlib.sha256(table).hexdigest(),
            }
            for index, table in enumerate(tables)
        ],
    }


def visible_background_cells(
    nametables: bytes,
    camera_x: int,
    camera_y: int,
) -> list[tuple[int, int]]:
    cells: list[tuple[int, int]] = []
    for screen_row in range(29):
        tile_y = camera_y // 8 + screen_row
        table_y = tile_y // 30 & 1
        local_y = tile_y % 30
        for screen_column in range(33):
            tile_x = camera_x // 8 + screen_column
            table_x = tile_x // 32 & 1
            local_x = tile_x % 32
            table_index = table_y * 2 + table_x
            table_start = table_index * 0x400
            tile = nametables[table_start + local_y * 32 + local_x]
            attribute = nametables[
                table_start + 0x3C0 + (local_y // 4) * 8 + local_x // 4
            ]
            shift = (local_y % 4 // 2) * 4 + (local_x % 4 // 2) * 2
            cells.append((tile, attribute >> shift & 0x03))
    return cells


def authored_collision_evidence(state: dict[str, object]) -> dict[str, object]:
    map_path = ROOT / "samples" / "runner" / "assets" / "maps" / "stage1.tmj"
    tileset_path = map_path.with_name("stage1.tsx")
    authored = json.loads(map_path.read_text(encoding="utf-8"))
    tileset = ElementTree.parse(tileset_path).getroot()
    solid_ids = {
        int(tile.attrib["id"])
        for tile in tileset.findall("tile")
        if tile.find("objectgroup") is not None
    }
    layer = next(layer for layer in authored["layers"] if layer["name"] == "world")
    first_gid = int(authored["tilesets"][0]["firstgid"])
    player_x = int(state["player_x"])
    player_y = int(state["player_y"])
    foot_y = player_y + 31
    row = foot_y // int(authored["tileheight"])
    first_column = player_x // int(authored["tilewidth"])
    last_column = (player_x + 17) // int(authored["tilewidth"])
    cells = []
    for column in range(first_column, last_column + 1):
        gid = int(layer["data"][row * int(layer["width"]) + column])
        local_id = gid - first_gid if gid else -1
        cells.append({"column": column, "gid": gid, "solid": local_id in solid_ids})
    collision_top = row * int(authored["tileheight"])
    return {
        "player_x": player_x,
        "player_y": player_y,
        "foot_y": foot_y,
        "collision_row": row,
        "collision_top": collision_top,
        "expected_player_y": collision_top - 31,
        "cells": cells,
        "aligned": player_y == collision_top - 31 and any(cell["solid"] for cell in cells),
    }


def exercise_jump(read_state, advance_frame, jump_hold_frames: int) -> dict[str, object]:
    start = read_state()
    start_y = int(start["player_y"])
    minimum_y = start_y
    airborne_frames = 0
    became_airborne = False
    for _ in range(jump_hold_frames):
        state = advance_frame(True)
        y = int(state["player_y"])
        minimum_y = min(minimum_y, y)
        became_airborne |= y < start_y
        airborne_frames += y < start_y
    for _ in range(120):
        state = advance_frame(False)
        y = int(state["player_y"])
        minimum_y = min(minimum_y, y)
        became_airborne |= y < start_y
        airborne_frames += y < start_y
        if became_airborne and y == start_y:
            break
    else:
        raise AssertionError("Runner jump did not return to its authored collision floor.")
    if minimum_y >= start_y:
        raise AssertionError("Runner A input did not produce an airborne frame.")
    return {
        "start_player_x": int(start["player_x"]),
        "end_player_x": int(state["player_x"]),
        "start_y": start_y,
        "minimum_y": minimum_y,
        "landed_y": int(state["player_y"]),
        "airborne_frames": airborne_frames,
        "hold_frames": jump_hold_frames,
        "landed_collision": authored_collision_evidence(state),
    }


def free_udp_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def download_nestopia(url: str, work_directory: Path) -> tuple[Path, dict[str, object]]:
    work_directory.mkdir(parents=True, exist_ok=True)
    archive = work_directory / "nestopia_libretro.so.zip"
    urllib.request.urlretrieve(url, archive)
    with zipfile.ZipFile(archive) as package:
        members = [name for name in package.namelist() if name.endswith("nestopia_libretro.so")]
        if len(members) != 1:
            raise RuntimeError(f"Nestopia package had unexpected members: {package.namelist()}")
        package.extract(members[0], work_directory)
    core = work_directory / members[0]
    return core, {
        "url": url,
        "archive_sha256": sha256(archive),
        "core_sha256": sha256(core),
    }


def capture_aprnes(
    rom: Path,
    work_directory: Path,
    idle_frames: int,
    maximum_right_frames: int,
    target_camera_x: int,
    release_frames: int,
    continuation_frames: int,
    maximum_left_frames: int,
    return_camera_x: int,
    return_release_frames: int,
    jump_hold_frames: int,
    command: list[str],
) -> dict[str, object]:
    states: list[dict[str, object]] = []
    left_states: list[dict[str, object]] = []
    checkpoints: dict[str, dict[str, object]] = {}
    with McpClient(
        command,
        work_directory,
        {"NES_MCP_EMULATOR_BACKEND": "aprnes"},
    ) as mcp:
        load = mcp.call_json("load_rom", {"path": str(rom)})
        mcp.call_json("run_frame", {"count": idle_frames})

        def read(address: int, length: int) -> list[int]:
            return parse_bytes(
                mcp.call_json(
                    "read_memory",
                    {"address": f"0x{address:04X}", "length": length},
                )
            )

        initial_state = runtime_snapshot(read)
        checkpoints["initial"] = capture_aprnes_checkpoint(mcp, initial_state)
        mcp.call_json("set_controller", {"buttons": ["RIGHT"]})
        previous_commit = int(initial_state["lifecycle"]["commit"])
        for frame_index in range(maximum_right_frames):
            mcp.call_json("run_frame", {"count": 1})
            state = runtime_snapshot(read)
            states.append(state)
            commit = int(state["lifecycle"]["commit"])
            visible = int(state["visible_camera_x"])
            if commit != previous_commit and "first-commit" not in checkpoints:
                checkpoints["first-commit"] = capture_aprnes_checkpoint(mcp, state)
            if visible >= 256 and "camera-256" not in checkpoints:
                checkpoints["camera-256"] = capture_aprnes_checkpoint(mcp, state)
            if visible >= target_camera_x:
                mcp.call_json("set_controller", {"buttons": []})
                mcp.call_json("run_frame", {"count": release_frames})
                released_state = runtime_snapshot(read)
                checkpoints["camera-target"] = capture_aprnes_checkpoint(mcp, released_state)
                break
            previous_commit = commit
        else:
            raise AssertionError(
                f"AprNes did not reach camera X {target_camera_x} in {maximum_right_frames} RIGHT frames."
            )
        continuation_extra = continuation_frames - release_frames
        if continuation_extra < 0:
            raise ValueError("FCEUmm continuation preceded its serialized target checkpoint.")
        if continuation_extra:
            mcp.call_json("run_frame", {"count": continuation_extra})

        def advance_jump(pressed: bool) -> dict[str, object]:
            mcp.call_json("set_controller", {"buttons": ["A"] if pressed else []})
            mcp.call_json("run_frame", {"count": 1})
            return runtime_snapshot(read)

        jump = exercise_jump(lambda: runtime_snapshot(read), advance_jump, jump_hold_frames)
        mcp.call_json("set_controller", {"buttons": ["LEFT"]})
        for _ in range(maximum_left_frames):
            mcp.call_json("run_frame", {"count": 1})
            state = runtime_snapshot(read)
            left_states.append(state)
            visible = int(state["visible_camera_x"])
            if visible <= 255 and "left-256" not in checkpoints:
                checkpoints["left-256"] = capture_aprnes_checkpoint(mcp, state)
            if visible <= return_camera_x:
                mcp.call_json("set_controller", {"buttons": []})
                mcp.call_json("run_frame", {"count": return_release_frames})
                checkpoints["camera-return"] = capture_aprnes_checkpoint(
                    mcp,
                    runtime_snapshot(read),
                )
                break
        else:
            raise AssertionError(
                f"AprNes did not return through camera X {return_camera_x} in "
                f"{maximum_left_frames} LEFT frames."
            )
        return {
            "states": states,
            "left_states": left_states,
            "initial_state": initial_state,
            "checkpoints": checkpoints,
            "right_frames": len(states),
            "left_frames": len(left_states),
            "jump": jump,
            "load": load,
            "server": mcp.server_info,
        }


def capture_aprnes_checkpoint(
    mcp: McpClient,
    state: dict[str, object],
) -> dict[str, object]:
    physical_tables = bytearray()
    for base in (0x2000, 0x2400, 0x2800, 0x2C00):
        tilemap = mcp.call_json("dump_tilemap", {"address": f"0x{base:04X}"})
        # NesMcp returns 960 bytes per dump. Starting at +$40 makes the final
        # 64 bytes land exactly on this physical table's attribute block,
        # including the last table without crossing $2FFF.
        attributes = mcp.call_json("dump_tilemap", {"address": f"0x{base + 0x40:04X}"})
        tile_bytes = bytes(
            int(value, 16)
            for row in tilemap["rows"]
            for value in str(row).split()
        )
        attribute_bytes = bytes(
            int(value, 16)
            for row in attributes["rows"]
            for value in str(row).split()
        )[-64:]
        if len(tile_bytes) != 0x3C0 or len(attribute_bytes) != 0x40:
            raise ValueError(f"NesMcp returned an invalid physical nametable at 0x{base:04X}.")
        physical_tables.extend(tile_bytes)
        physical_tables.extend(attribute_bytes)
    palette_indices = mcp.call_json(
        "read_screen_region",
        {"x": 0, "y": 0, "width": 256, "height": 224, "format": "palette_indices"},
    )
    return {
        "state": state,
        "framebuffer": normalize_frame(mcp.capture_screen()),
        "nametables": bytes(physical_tables),
        "rendered_palette_indices": {
            "histogram": palette_indices["histogram"],
            "row_hashes": palette_indices["rowHashes"],
        },
        "ppu": mcp.call_json("read_ppu_state", {}),
        "ppu_writers": mcp.call_json("find_last_writers", {"address": "0x2000", "length": 8}),
    }


def capture_fceumm_checkpoint(
    session: RetroArchNetworkSession,
    live_state: dict[str, object],
) -> dict[str, object]:
    state_path = session.save_state()
    fields = parse_fceumm_state(state_path.read_bytes())
    ram = fields["RAM"]
    saved_state = runtime_snapshot(
        lambda address, length: list(ram[address : address + length])
    )
    thumbnail = state_path.with_name(state_path.name + ".png")
    framebuffer = (
        normalize_frame(Image.open(thumbnail).convert("RGB"))
        if thumbnail.is_file()
        else normalize_frame(session.capture_screen())
    )
    checkpoint = {
        "state": saved_state,
        "live_state_before_save": live_state,
        "framebuffer": framebuffer,
        "nametables": fields["NTAR"] + fields["EXNR"],
        "palette": fields["PRAM"],
        "ppu_registers": fields["PPUR"],
        "state_sha256": sha256(state_path),
        "release_frames": (
            int(saved_state["hardware_frame"]) - int(live_state["hardware_frame"])
        )
        & 0xFFFF,
    }
    live_state_after_save = runtime_snapshot(session.read)
    checkpoint["live_state_after_save"] = live_state_after_save
    checkpoint["continuation_frames"] = (
        int(live_state_after_save["hardware_frame"])
        - int(live_state["hardware_frame"])
    ) & 0xFFFF
    state_path.unlink()
    if thumbnail.is_file():
        thumbnail.unlink()
    return checkpoint


def capture_fceumm(
    rom: Path,
    core: Path,
    work_directory: Path,
    idle_frames: int,
    maximum_right_frames: int,
    target_camera_x: int,
    maximum_left_frames: int,
    return_camera_x: int,
    jump_hold_frames: int,
    launch_command: list[str],
    base_config: str,
) -> dict[str, object]:
    states: list[dict[str, object]] = []
    left_states: list[dict[str, object]] = []
    checkpoints: dict[str, dict[str, object]] = {}
    with RetroArchNetworkSession(
        launch_command,
        core,
        rom,
        work_directory,
        free_udp_port(),
        free_udp_port(),
        {
            "fceumm_ramstate": "fill $ff",
            "fceumm_overscan_v_top": "8",
            "fceumm_overscan_v_bottom": "8",
            "fceumm_overscan_h_left": "0",
            "fceumm_overscan_h_right": "0",
        },
        base_config,
    ) as session:
        session.set_paused(True)
        for _ in range(idle_frames):
            session.advance_frame()
        initial_state = runtime_snapshot(session.read)
        checkpoints["initial"] = {
            "state": initial_state,
            "framebuffer": normalize_frame(session.capture_screen()),
        }
        previous_commit = int(initial_state["lifecycle"]["commit"])
        for _ in range(maximum_right_frames):
            session.set_right(True)
            session.advance_frame()
            state = runtime_snapshot(session.read)
            states.append(state)
            commit = int(state["lifecycle"]["commit"])
            visible = int(state["visible_camera_x"])
            if commit != previous_commit and "first-commit" not in checkpoints:
                checkpoints["first-commit"] = {
                    "state": state,
                    "framebuffer": normalize_frame(session.capture_screen()),
                }
            if visible >= 256 and "camera-256" not in checkpoints:
                checkpoints["camera-256"] = {
                    "state": state,
                    "framebuffer": normalize_frame(session.capture_screen()),
                }
            if visible >= target_camera_x:
                session.set_right(False)
                checkpoints["camera-target"] = capture_fceumm_checkpoint(session, state)
                break
            previous_commit = commit
        else:
            raise AssertionError(
                f"FCEUmm did not reach camera X {target_camera_x} in {maximum_right_frames} RIGHT frames."
            )

        def advance_jump(pressed: bool) -> dict[str, object]:
            session.set_button(RETRO_DEVICE_ID_JOYPAD_A, pressed)
            session.advance_frame()
            return runtime_snapshot(session.read)

        jump = exercise_jump(
            lambda: runtime_snapshot(session.read),
            advance_jump,
            jump_hold_frames,
        )
        session.set_button(RETRO_DEVICE_ID_JOYPAD_A, False)
        session.set_button(RETRO_DEVICE_ID_JOYPAD_LEFT, True)
        for _ in range(maximum_left_frames):
            session.advance_frame()
            state = runtime_snapshot(session.read)
            left_states.append(state)
            visible = int(state["visible_camera_x"])
            if visible <= 255 and "left-256" not in checkpoints:
                checkpoints["left-256"] = {
                    "state": state,
                    "framebuffer": normalize_frame(session.capture_screen()),
                }
            if visible <= return_camera_x:
                session.set_button(RETRO_DEVICE_ID_JOYPAD_LEFT, False)
                checkpoints["camera-return"] = capture_fceumm_checkpoint(session, state)
                break
        else:
            raise AssertionError(
                f"FCEUmm did not return through camera X {return_camera_x} in "
                f"{maximum_left_frames} LEFT frames."
            )
    return {
        "states": states,
        "left_states": left_states,
        "initial_state": initial_state,
        "checkpoints": checkpoints,
        "right_frames": len(states),
        "release_frames": checkpoints["camera-target"]["release_frames"],
        "continuation_frames": checkpoints["camera-target"]["continuation_frames"],
        "return_release_frames": checkpoints["camera-return"]["release_frames"],
        "left_frames": len(left_states),
        "jump": jump,
    }


def capture_nestopia(
    rom: Path,
    core: Path,
    work_directory: Path,
    idle_frames: int,
    maximum_right_frames: int,
    target_camera_x: int,
    release_frames: int,
    continuation_frames: int,
    maximum_left_frames: int,
    return_camera_x: int,
    return_release_frames: int,
    jump_hold_frames: int,
) -> dict[str, object]:
    states: list[dict[str, object]] = []
    left_states: list[dict[str, object]] = []
    checkpoints: dict[str, dict[str, object]] = {}
    with LibretroSession(core, rom, work_directory) as session:
        initial_frame: Image.Image | None = None
        for _ in range(idle_frames):
            initial_frame = normalize_frame(session.run_frame([]))
        if initial_frame is None:
            raise RuntimeError("Nestopia idle capture produced no frame.")

        def read_ram_state() -> dict[str, object]:
            ram = parse_nestopia_state(session.serialize())["RAM"]
            return runtime_snapshot(lambda address, length: list(ram[address : address + length]))

        initial_state = read_ram_state()
        checkpoints["initial"] = {"state": initial_state, "framebuffer": initial_frame}
        previous_commit = int(initial_state["lifecycle"]["commit"])
        for _ in range(maximum_right_frames):
            framebuffer = normalize_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_RIGHT])
            )
            state = read_ram_state()
            states.append(state)
            commit = int(state["lifecycle"]["commit"])
            visible = int(state["visible_camera_x"])
            if commit != previous_commit and "first-commit" not in checkpoints:
                checkpoints["first-commit"] = {"state": state, "framebuffer": framebuffer}
            if visible >= 256 and "camera-256" not in checkpoints:
                checkpoints["camera-256"] = {"state": state, "framebuffer": framebuffer}
            if visible >= target_camera_x:
                for _ in range(release_frames):
                    framebuffer = normalize_frame(session.run_frame([]))
                state = read_ram_state()
                fields = parse_nestopia_state(session.serialize())
                checkpoints["camera-target"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "nametables": fields["NAMETABLES"],
                    "palette": fields["PALETTE"],
                    "ppu_registers": fields["PPU_REGISTERS"],
                }
                break
            previous_commit = commit
        else:
            raise AssertionError(
                f"Nestopia did not reach camera X {target_camera_x} in {maximum_right_frames} RIGHT frames."
            )
        continuation_extra = continuation_frames - release_frames
        if continuation_extra < 0:
            raise ValueError("FCEUmm continuation preceded its serialized target checkpoint.")
        for _ in range(continuation_extra):
            framebuffer = normalize_frame(session.run_frame([]))

        def advance_jump(pressed: bool) -> dict[str, object]:
            nonlocal framebuffer
            framebuffer = normalize_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_A] if pressed else [])
            )
            return read_ram_state()

        jump = exercise_jump(read_ram_state, advance_jump, jump_hold_frames)
        for _ in range(maximum_left_frames):
            framebuffer = normalize_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_LEFT])
            )
            state = read_ram_state()
            left_states.append(state)
            visible = int(state["visible_camera_x"])
            if visible <= 255 and "left-256" not in checkpoints:
                checkpoints["left-256"] = {"state": state, "framebuffer": framebuffer}
            if visible <= return_camera_x:
                for _ in range(return_release_frames):
                    framebuffer = normalize_frame(session.run_frame([]))
                state = read_ram_state()
                fields = parse_nestopia_state(session.serialize())
                checkpoints["camera-return"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "nametables": fields["NAMETABLES"],
                    "palette": fields["PALETTE"],
                    "ppu_registers": fields["PPU_REGISTERS"],
                }
                break
        else:
            raise AssertionError(
                f"Nestopia did not return through camera X {return_camera_x} in "
                f"{maximum_left_frames} LEFT frames."
            )
    return {
        "states": states,
        "left_states": left_states,
        "initial_state": initial_state,
        "checkpoints": checkpoints,
        "right_frames": len(states),
        "left_frames": len(left_states),
        "jump": jump,
    }


def state_signature(state: dict[str, object]) -> dict[str, object]:
    return {
        key: state[key]
        for key in (
            "player_x",
            "player_y",
            "requested_camera_x",
            "requested_camera_y",
            "visible_camera_x",
            "visible_camera_y",
            "lifecycle",
            "forbidden_commit_work",
            "last_commit_writes",
            "critical_section",
            "pending_axes",
            "collision_decodes",
        )
    }


def image_sha256(image: Image.Image) -> str:
    return hashlib.sha256(
        struct.pack("<II", image.width, image.height) + image.convert("RGB").tobytes()
    ).hexdigest()


def save_checkpoint_framebuffers(
    artifacts: Path,
    captures: dict[str, dict[str, object]],
) -> dict[str, dict[str, object]]:
    reports: dict[str, dict[str, object]] = {}
    for emulator, capture in captures.items():
        directory = artifacts / "frames" / emulator
        directory.mkdir(parents=True, exist_ok=True)
        checkpoint_reports: dict[str, object] = {}
        for name, checkpoint in capture["checkpoints"].items():
            framebuffer = checkpoint["framebuffer"]
            path = directory / f"{name}.png"
            framebuffer.save(path)
            checkpoint_reports[name] = {
                "framebuffer": str(path.relative_to(artifacts)),
                "framebuffer_sha256": image_sha256(framebuffer),
                "state": checkpoint["state"],
            }
        reports[emulator] = {
            "right_frames": capture["right_frames"],
            "left_frames": capture["left_frames"],
            "jump": capture["jump"],
            "checkpoints": checkpoint_reports,
        }
    return reports


def mismatch_report(reference: bytes, candidate: bytes) -> dict[str, object]:
    differences = [
        {
            "address": f"0x{0x2000 + index:04X}",
            "reference": expected,
            "candidate": actual,
            "kind": "attribute" if index % 0x400 >= 0x3C0 else "tile",
        }
        for index, (expected, actual) in enumerate(zip(reference, candidate))
        if expected != actual
    ]
    return {
        "count": len(differences),
        "tile_count": sum(item["kind"] == "tile" for item in differences),
        "attribute_count": sum(item["kind"] == "attribute" for item in differences),
        "first": differences[:100],
    }


def consensus_mismatch_report(
    reference_a: bytes,
    reference_b: bytes,
    candidate: bytes,
) -> dict[str, object]:
    consensus = sum(left == right for left, right in zip(reference_a, reference_b))
    mismatches = [
        {
            "address": f"0x{0x2000 + index:04X}",
            "consensus": left,
            "candidate": actual,
            "kind": "attribute" if index % 0x400 >= 0x3C0 else "tile",
        }
        for index, (left, right, actual) in enumerate(
            zip(reference_a, reference_b, candidate)
        )
        if left == right and actual != left
    ]
    return {
        "consensus_bytes": consensus,
        "count": len(mismatches),
        "tile_count": sum(item["kind"] == "tile" for item in mismatches),
        "attribute_count": sum(item["kind"] == "attribute" for item in mismatches),
        "first": mismatches[:100],
    }


def consensus_visible_mismatches(
    reference_a: list[tuple[int, int]],
    reference_b: list[tuple[int, int]],
    candidate: list[tuple[int, int]],
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    mismatches = []
    for index, (left, right, actual) in enumerate(zip(reference_a, reference_b, candidate)):
        if left == right and actual != left:
            mismatches.append(
                {
                    "cell": index,
                    "column": index % 33,
                    "row": index // 33,
                    "consensus_tile": left[0],
                    "consensus_palette": left[1],
                    "candidate_tile": actual[0],
                    "candidate_palette": actual[1],
                }
            )
    repeated_runs = []
    for column in range(33):
        rows = [
            item["row"]
            for item in mismatches
            if item["column"] == column
            and item["consensus_tile"] == 0
            and item["candidate_tile"] != 0
        ]
        start: int | None = None
        previous: int | None = None
        for row in rows:
            if start is None:
                start = previous = row
            elif previous is not None and row == previous + 1:
                previous = row
            else:
                if previous is not None and previous - start + 1 >= 4:
                    repeated_runs.append(
                        {"column": column, "start_row": start, "end_row": previous}
                    )
                start = previous = row
        if start is not None and previous is not None and previous - start + 1 >= 4:
            repeated_runs.append(
                {"column": column, "start_row": start, "end_row": previous}
            )
    return mismatches, repeated_runs


def main() -> int:
    args = parse_args()
    rom = args.rom.resolve()
    fceumm_core = args.fceumm_core.resolve()
    artifacts = args.artifacts.resolve()
    if not rom.is_file():
        raise FileNotFoundError(rom)
    if not fceumm_core.is_file():
        raise FileNotFoundError(fceumm_core)
    if b"(SVN) 3a84a6f" not in fceumm_core.read_bytes():
        raise RuntimeError("FCEUmm core is not the required (SVN) 3a84a6f build.")
    persistent_paths = [PERSISTENT_RETROARCH_CONFIG, PERSISTENT_FCEUMM_OPTIONS]
    for persistent_path in persistent_paths:
        if not persistent_path.is_file():
            raise FileNotFoundError(persistent_path)

    shutil.rmtree(artifacts, ignore_errors=True)
    artifacts.mkdir(parents=True)
    config_guard = ConfigIntegrityGuard(persistent_paths)
    base_config = subprocess.check_output(
        shlex.split(args.retroarch_default_config_command),
        text=True,
    )
    summary: dict[str, object] = {
        "rom": str(rom),
        "rom_sha256": sha256(rom),
        "fceumm_core": str(fceumm_core),
        "fceumm_core_sha256": sha256(fceumm_core),
        "fceumm_version": "(SVN) 3a84a6f",
        "persistent_files_before_sha256": {
            str(path): digest for path, digest in config_guard.before_sha256.items()
        },
        "idle_frames": args.idle_frames,
        "maximum_right_frames": args.right_frames,
        "target_camera_x": args.target_camera_x,
        "maximum_left_frames": args.left_frames,
        "return_camera_x": args.return_camera_x,
        "jump_hold_frames": args.jump_hold_frames,
    }

    try:
        with tempfile.TemporaryDirectory(prefix="issue-327-", dir=artifacts) as temporary:
            work = Path(temporary)
            if args.nestopia_core is None:
                nestopia_core, nestopia_source = download_nestopia(
                    args.nestopia_core_url,
                    work / "nestopia-download",
                )
            else:
                nestopia_core = args.nestopia_core.resolve()
                nestopia_source = {"core_sha256": sha256(nestopia_core)}
            summary["nestopia"] = {**nestopia_source, "core": str(nestopia_core)}
            fceumm = capture_fceumm(
                rom,
                fceumm_core,
                work / "fceumm",
                args.idle_frames,
                args.right_frames,
                args.target_camera_x,
                args.left_frames,
                args.return_camera_x,
                args.jump_hold_frames,
                shlex.split(args.retroarch_command),
                base_config,
            )
            aprnes = capture_aprnes(
                rom,
                work / "aprnes",
                args.idle_frames,
                args.right_frames,
                args.target_camera_x,
                int(fceumm["release_frames"]),
                int(fceumm["continuation_frames"]),
                args.left_frames,
                args.return_camera_x,
                int(fceumm["return_release_frames"]),
                args.jump_hold_frames,
                shlex.split(args.mcp_command),
            )
            nestopia = capture_nestopia(
                rom,
                nestopia_core,
                work / "nestopia",
                args.idle_frames,
                args.right_frames,
                args.target_camera_x,
                int(fceumm["release_frames"]),
                int(fceumm["continuation_frames"]),
                args.left_frames,
                args.return_camera_x,
                int(fceumm["return_release_frames"]),
                args.jump_hold_frames,
            )
            captures = {"aprnes": aprnes, "fceumm": fceumm, "nestopia": nestopia}
            summary["runs"] = save_checkpoint_framebuffers(artifacts, captures)

            final = {
                emulator: capture["checkpoints"]["camera-target"]
                for emulator, capture in captures.items()
            }
            nametables = {
                emulator: checkpoint["nametables"]
                for emulator, checkpoint in final.items()
            }
            states = {emulator: checkpoint["state"] for emulator, checkpoint in final.items()}
            visible_cells = {
                emulator: visible_background_cells(
                    nametables[emulator],
                    int(states[emulator]["visible_camera_x"]),
                    int(states[emulator]["visible_camera_y"]),
                )
                for emulator in final
            }
            collision = {
                emulator: authored_collision_evidence(state)
                for emulator, state in states.items()
            }
            reference_nametables_match = nametables["aprnes"] == nametables["nestopia"]
            reference_visible_match = visible_cells["aprnes"] == visible_cells["nestopia"]
            fceumm_nametable_mismatch = mismatch_report(
                nametables["aprnes"], nametables["fceumm"]
            )
            fceumm_consensus_nametable_mismatch = consensus_mismatch_report(
                nametables["aprnes"],
                nametables["nestopia"],
                nametables["fceumm"],
            )
            fceumm_visible_mismatches = [
                {
                    "cell": index,
                    "aprnes_tile": expected[0],
                    "aprnes_palette": expected[1],
                    "fceumm_tile": actual[0],
                    "fceumm_palette": actual[1],
                }
                for index, (expected, actual) in enumerate(
                    zip(visible_cells["aprnes"], visible_cells["fceumm"])
                )
                if expected != actual
            ]
            fceumm_consensus_visible_mismatches, repeated_tile_runs = (
                consensus_visible_mismatches(
                    visible_cells["aprnes"],
                    visible_cells["nestopia"],
                    visible_cells["fceumm"],
                )
            )
            state_signatures = {
                emulator: state_signature(state) for emulator, state in states.items()
            }
            state_parity = len(
                {json.dumps(signature, sort_keys=True) for signature in state_signatures.values()}
            ) == 1
            collision_aligned = all(evidence["aligned"] for evidence in collision.values())
            defect_reproduced = (
                state_parity
                and collision_aligned
                and (
                    not reference_nametables_match
                    or not reference_visible_match
                    or fceumm_consensus_nametable_mismatch["count"] > 0
                    or bool(fceumm_consensus_visible_mismatches)
                )
            )
            summary.update(
                {
                    "final_states": states,
                    "state_signatures": state_signatures,
                    "state_parity": state_parity,
                    "collision": collision,
                    "collision_aligned": collision_aligned,
                    "nametables": {
                        emulator: {
                            **nametable_snapshot(raw),
                            "raw": raw.hex(),
                        }
                        for emulator, raw in nametables.items()
                    },
                    "palettes": {
                        "aprnes_rendered_indices": final["aprnes"]["rendered_palette_indices"],
                        "fceumm_raw_hex": final["fceumm"]["palette"].hex(),
                        "nestopia_raw_hex": final["nestopia"]["palette"].hex(),
                        "fceumm_nestopia_match": final["fceumm"]["palette"]
                        == final["nestopia"]["palette"],
                    },
                    "ppu": {
                        "aprnes": final["aprnes"]["ppu"],
                        "aprnes_writers": final["aprnes"]["ppu_writers"],
                        "fceumm_registers_hex": final["fceumm"]["ppu_registers"].hex(),
                        "nestopia_registers_hex": final["nestopia"]["ppu_registers"].hex(),
                    },
                    "reference_nametables_match": reference_nametables_match,
                    "reference_visible_tile_palette_match": reference_visible_match,
                    "fceumm_nametable_mismatch": fceumm_nametable_mismatch,
                    "fceumm_consensus_nametable_mismatch": fceumm_consensus_nametable_mismatch,
                    "fceumm_visible_tile_palette_mismatch_count": len(fceumm_visible_mismatches),
                    "fceumm_visible_tile_palette_mismatches": fceumm_visible_mismatches[:100],
                    "fceumm_consensus_visible_mismatch_count": len(
                        fceumm_consensus_visible_mismatches
                    ),
                    "fceumm_consensus_visible_mismatches": fceumm_consensus_visible_mismatches[:100],
                    "fceumm_repeated_tile_runs": repeated_tile_runs,
                    "defect_reproduced": defect_reproduced,
                }
            )

            returned = {
                emulator: capture["checkpoints"]["camera-return"]
                for emulator, capture in captures.items()
            }
            returned_nametables = {
                emulator: checkpoint["nametables"]
                for emulator, checkpoint in returned.items()
            }
            returned_states = {
                emulator: checkpoint["state"] for emulator, checkpoint in returned.items()
            }
            returned_cells = {
                emulator: visible_background_cells(
                    returned_nametables[emulator],
                    int(returned_states[emulator]["visible_camera_x"]),
                    int(returned_states[emulator]["visible_camera_y"]),
                )
                for emulator in returned
            }
            returned_collision = {
                emulator: authored_collision_evidence(state)
                for emulator, state in returned_states.items()
            }
            returned_state_signatures = {
                emulator: state_signature(state)
                for emulator, state in returned_states.items()
            }
            returned_state_parity = len(
                {
                    json.dumps(signature, sort_keys=True)
                    for signature in returned_state_signatures.values()
                }
            ) == 1
            returned_collision_aligned = all(
                evidence["aligned"] for evidence in returned_collision.values()
            )
            returned_consensus_nametable_mismatch = consensus_mismatch_report(
                returned_nametables["aprnes"],
                returned_nametables["nestopia"],
                returned_nametables["fceumm"],
            )
            returned_consensus_visible_mismatches, returned_repeated_runs = (
                consensus_visible_mismatches(
                    returned_cells["aprnes"],
                    returned_cells["nestopia"],
                    returned_cells["fceumm"],
                )
            )
            returned_defect = (
                returned_state_parity
                and returned_collision_aligned
                and (
                    returned_nametables["aprnes"] != returned_nametables["nestopia"]
                    or returned_cells["aprnes"] != returned_cells["nestopia"]
                    or returned_consensus_nametable_mismatch["count"] > 0
                    or bool(returned_consensus_visible_mismatches)
                )
            )
            jump_signatures = {
                emulator: capture["jump"] for emulator, capture in captures.items()
            }
            jump_parity = len(
                {
                    json.dumps(signature, sort_keys=True)
                    for signature in jump_signatures.values()
                }
            ) == 1
            jump_collision_aligned = all(
                signature["minimum_y"] < signature["start_y"]
                and signature["landed_y"] == signature["start_y"]
                and signature["start_player_x"] == signature["end_player_x"]
                and signature["landed_collision"]["aligned"]
                for signature in jump_signatures.values()
            )
            right_frame_counts = {
                emulator: int(capture["right_frames"])
                for emulator, capture in captures.items()
            }
            left_frame_counts = {
                emulator: int(capture["left_frames"])
                for emulator, capture in captures.items()
            }
            input_frame_parity = (
                len(set(right_frame_counts.values())) == 1
                and len(set(left_frame_counts.values())) == 1
            )
            input_progress_bounded = (
                all(0 < count <= args.right_frames for count in right_frame_counts.values())
                and all(0 < count <= args.left_frames for count in left_frame_counts.values())
            )
            summary["return_path"] = {
                "final_states": returned_states,
                "state_signatures": returned_state_signatures,
                "state_parity": returned_state_parity,
                "collision": returned_collision,
                "collision_aligned": returned_collision_aligned,
                "nametables": {
                    emulator: {
                        **nametable_snapshot(raw),
                        "raw": raw.hex(),
                    }
                    for emulator, raw in returned_nametables.items()
                },
                "palettes": {
                    "aprnes_rendered_indices": returned["aprnes"][
                        "rendered_palette_indices"
                    ],
                    "fceumm_raw_hex": returned["fceumm"]["palette"].hex(),
                    "nestopia_raw_hex": returned["nestopia"]["palette"].hex(),
                },
                "ppu": {
                    "aprnes": returned["aprnes"]["ppu"],
                    "aprnes_writers": returned["aprnes"]["ppu_writers"],
                    "fceumm_registers_hex": returned["fceumm"]["ppu_registers"].hex(),
                    "nestopia_registers_hex": returned["nestopia"]["ppu_registers"].hex(),
                },
                "reference_nametables_match": returned_nametables["aprnes"]
                == returned_nametables["nestopia"],
                "reference_visible_tile_palette_match": returned_cells["aprnes"]
                == returned_cells["nestopia"],
                "fceumm_consensus_nametable_mismatch": returned_consensus_nametable_mismatch,
                "fceumm_consensus_visible_mismatch_count": len(
                    returned_consensus_visible_mismatches
                ),
                "fceumm_consensus_visible_mismatches": returned_consensus_visible_mismatches[
                    :100
                ],
                "fceumm_repeated_tile_runs": returned_repeated_runs,
                "defect_reproduced": returned_defect,
            }
            summary["jump"] = {
                "signatures": jump_signatures,
                "parity": jump_parity,
                "collision_aligned": jump_collision_aligned,
            }
            summary["input_frames"] = {
                "right": right_frame_counts,
                "left": left_frame_counts,
                "parity": input_frame_parity,
                "bounded": input_progress_bounded,
            }
            summary["defect_reproduced"] = defect_reproduced or returned_defect
            summary["acceptance_converged"] = (
                state_parity
                and collision_aligned
                and reference_nametables_match
                and reference_visible_match
                and fceumm_consensus_nametable_mismatch["count"] == 0
                and not fceumm_consensus_visible_mismatches
                and returned_state_parity
                and returned_collision_aligned
                and returned_nametables["aprnes"] == returned_nametables["nestopia"]
                and returned_cells["aprnes"] == returned_cells["nestopia"]
                and returned_consensus_nametable_mismatch["count"] == 0
                and not returned_consensus_visible_mismatches
                and jump_parity
                and jump_collision_aligned
                and input_progress_bounded
            )
    finally:
        config_guard.verify_unchanged()

    summary["persistent_files_after_sha256"] = {
        str(path): sha256(path) for path in persistent_paths
    }
    summary["verified"] = (
        not summary["defect_reproduced"] and summary["acceptance_converged"]
    )
    summary_path = artifacts / "summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(summary_path)
    if summary["defect_reproduced"]:
        print(
            "RED: FCEUmm diverged from the AprNes/Nestopia physical-nametable consensus "
            f"at {summary['fceumm_consensus_nametable_mismatch']['count']} consensus bytes and "
            f"{summary['fceumm_consensus_visible_mismatch_count']} consensus tile/palette cells; "
            f"repeated_runs={summary['fceumm_repeated_tile_runs']}."
        )
        return 1
    if not summary["acceptance_converged"]:
        print(
            "INVALID: bidirectional runtime state, jump trajectory, input frames, or "
            "authored collision did not converge."
        )
        return 2
    print(
        "GREEN: right/left physical nametables, visible tile/palette identities, "
        "jump, and collision matched."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
