#!/usr/bin/env python3
"""Differential visual acceptance for RetroSharp issue #327."""

from __future__ import annotations

import argparse
from collections import Counter, defaultdict
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
TRANSIENT_FRAME_RADIUS = 2
POST_TARGET_SETTLE_FRAMES = TRANSIENT_FRAME_RADIUS * 2
MCP_MIN_VERSION = (0, 0, 7, 0)
OBSERVATION_MAX_PPU_EVENTS = 2_000
PPU_COMMIT_BUDGET_CYCLES = 2_136
REQUIRED_FOCAL_TRACE_LABELS = frozenset(
    {
        "first-column",
        "right-before-256",
        "right-cross-256",
        "right-target",
        "left-cross-256",
    }
)
REQUIRED_NES_MCP_TOOLS = frozenset(
    {
        "observe_screen",
        "observe_execution",
        "trace_ppu_register_writes",
        "dump_nametables",
        "read_ppu_state",
        "read_screen_region",
        "dump_tilemap",
        "dump_oam",
        "save_state",
        "load_state",
    }
)

PLAYER_X_LOW = 0x0000
PLAYER_Y_LOW = 0x0002
REQUESTED_CAMERA_X_LOW = 0x00E0
REQUESTED_CAMERA_Y_LOW = 0x00E1
RUNTIME_BLOCK_START = 0x0318
RUNTIME_BLOCK_END = 0x03FB
OBSERVATION_MEMORY_PROBES = (
    (0x0000, 4),
    (0x00E0, 2),
    (0x0318, 2),
    (0x036E, 12),
    (0x0380, 12),
    (0x0390, 1),
    (0x03A0, 1),
    (0x03CA, 5),
    (0x03F8, 4),
)


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


def validate_nes_mcp_surface(
    server_info: dict[str, object],
    tool_names: set[str],
) -> dict[str, object]:
    name = str(server_info.get("name", ""))
    version = str(server_info.get("version", ""))
    try:
        parsed_version = tuple(int(part) for part in version.split("."))
    except ValueError as error:
        raise RuntimeError(f"Nes.Mcp returned an invalid server version: {version!r}.") from error
    parsed_version += (0,) * (len(MCP_MIN_VERSION) - len(parsed_version))
    missing = sorted(REQUIRED_NES_MCP_TOOLS - tool_names)
    if name != "Nes.Mcp" or parsed_version < MCP_MIN_VERSION:
        raise RuntimeError(
            f"Nes.Mcp 0.0.7 or newer is required; connected server is {name} {version}."
        )
    if missing:
        raise RuntimeError(
            "Nes.Mcp 0.0.7 tool surface is incomplete; missing: " + ", ".join(missing)
        )
    return {
        "name": name,
        "version": version,
        "tool_count": len(tool_names),
        "missing_tools": missing,
    }


def ppu_snapshot_in_physical_vblank(snapshot: dict[str, object]) -> bool:
    """Use the beam position, not the clear-on-read PPUSTATUS VBlank latch."""

    scanline = int(snapshot["scanline"])
    dot = int(snapshot["dot"])
    in_interval = (
        (scanline == 241 and dot >= 1)
        or 242 <= scanline <= 260
        or (scanline == 261 and dot == 0)
    )
    return in_interval and not bool(snapshot["renderingActive"])


def validate_execution_observation(observation: dict[str, object]) -> dict[str, object]:
    frames_requested = int(observation.get("framesRequested", -1))
    frames_run = int(observation.get("framesRun", -1))
    frames = list(observation.get("frames", []))
    event_count = int(observation.get("ppuEventCount", -1))
    events_observed = int(observation.get("ppuEventsObserved", -1))
    if bool(observation.get("ppuTraceTruncated")) or bool(
        observation.get("truncated")
    ):
        raise AssertionError(
            f"Nes.Mcp observation trace was truncated at {event_count}/{events_observed} events."
        )
    if frames_run != frames_requested or frames_run != len(frames):
        raise AssertionError(
            f"Nes.Mcp observation completed {frames_run}/{frames_requested} frames and returned {len(frames)}."
        )
    if event_count != events_observed:
        raise AssertionError(
            f"Nes.Mcp observation omitted PPU events ({event_count}/{events_observed})."
        )
    if observation.get("stopReason") != "framesComplete":
        raise AssertionError(
            f"Nes.Mcp observation stopped early: {observation.get('stopReason')}."
        )
    outside_vblank = [
        event
        for event in observation.get("ppuEvents", [])
        if int(str(event["address"]), 16) == 0x2007
        and (
            not ppu_snapshot_in_physical_vblank(event["before"])
            or not ppu_snapshot_in_physical_vblank(event["after"])
        )
    ]
    if outside_vblank:
        raise AssertionError(
            f"Nes.Mcp observed {len(outside_vblank)} PPUDATA writes outside VBlank."
        )
    return {
        "frames_requested": frames_requested,
        "frames_run": frames_run,
        "ppu_event_count": event_count,
        "ppu_events_observed": events_observed,
        "ppu_trace_truncated": False,
        "truncated": False,
        "ppudata_outside_vblank": 0,
        "timeline": observation.get("timeline"),
    }


def parse_bytes(result: dict[str, object]) -> list[int]:
    return [int(value, 16) for value in str(result["bytesHex"]).split()]


def runtime_snapshot(read) -> dict[str, object]:
    player = read(PLAYER_X_LOW, 4)
    requested_low = read(REQUESTED_CAMERA_X_LOW, 2)
    runtime = read(RUNTIME_BLOCK_START, RUNTIME_BLOCK_END - RUNTIME_BLOCK_START + 1)

    def byte(address: int) -> int:
        return runtime[address - RUNTIME_BLOCK_START]

    return runtime_snapshot_from_parts(player, requested_low, byte)


def runtime_snapshot_from_parts(player, requested_low, byte) -> dict[str, object]:
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
        "commit_descriptor": {
            "axis": byte(0x0382),
            "direction": byte(0x0383),
            "target": byte(0x0386),
            "payload_length": byte(0x0389),
            "target_start": byte(0x038B),
        },
        "slot_states": [byte(0x0390), byte(0x03A0)],
        "pending_axes": byte(0x03CA),
        "visible_camera_x": word(0x03CB),
        "visible_camera_y": word(0x03CD),
        "collision_decodes": word(0x03F8),
        "gameplay_ticks": byte(0x03FA),
        "audio_ticks": byte(0x03FB),
    }


def observed_runtime_frame(
    frame: dict[str, object],
    *,
    step: int,
    phase: str,
) -> dict[str, object]:
    probes = {
        int(str(probe["address"]), 16): parse_bytes(probe)
        for probe in frame["memory"]
    }

    def read(address: int, length: int) -> list[int]:
        for probe_address, values in probes.items():
            offset = address - probe_address
            if 0 <= offset and offset + length <= len(values):
                return values[offset : offset + length]
        raise KeyError(
            f"Nes.Mcp observation omitted RAM probe 0x{address:04X}+{length}."
        )

    def byte(address: int) -> int:
        return read(address, 1)[0]

    return {
        "step": step,
        "phase": phase,
        "state": runtime_snapshot_from_parts(
            read(PLAYER_X_LOW, 4),
            read(REQUESTED_CAMERA_X_LOW, 2),
            byte,
        ),
        "screen": frame["screen"],
        "ppu_state": frame.get("ppuState"),
    }


def normalize_frame(image: Image.Image) -> Image.Image:
    image = image.convert("RGB")
    if image.size == (256, 240):
        return image.crop((0, 8, 256, 232))
    if image.size != (256, 224):
        raise ValueError(f"Unexpected NES framebuffer size: {image.size}")
    return image


def split_raw_frame(image: Image.Image) -> tuple[Image.Image, Image.Image]:
    """Retain the frontend raster while exposing the normalized viewport."""

    raw = image.convert("RGB")
    if raw.size not in ((256, 240), (256, 224)):
        raise ValueError(f"Unexpected raw NES framebuffer size: {raw.size}")
    return raw, normalize_frame(raw)


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
    tile_flags: dict[int, set[str]] = {}
    for tile in tileset.findall("tile"):
        flags = tile_flags.setdefault(int(tile.attrib["id"]), set())
        if tile.find("objectgroup") is not None:
            flags.add("solid")
        for prop in tile.findall("./properties/property"):
            if prop.attrib.get("name", "").lower() not in {
                "retrosharpcollision",
                "retrosharpflags",
            }:
                continue
            value = prop.attrib.get("value", prop.text or "").lower()
            for token in value.replace("|", ",").split(","):
                token = token.strip()
                if token in {"solid", "hazard", "platform"}:
                    flags.add(token)
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
        flags = tile_flags.get(local_id, set())
        cells.append(
            {
                "column": column,
                "gid": gid,
                "solid": "solid" in flags,
                "platform": "platform" in flags,
                "landable": bool(flags & {"solid", "platform"}),
            }
        )
    collision_top = row * int(authored["tileheight"])
    return {
        "player_x": player_x,
        "player_y": player_y,
        "foot_y": foot_y,
        "collision_row": row,
        "collision_top": collision_top,
        "expected_player_y": collision_top - 31,
        "cells": cells,
        "aligned": player_y == collision_top - 31 and any(cell["landable"] for cell in cells),
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


def jump_semantics_match(signatures: dict[str, dict[str, object]]) -> bool:
    """Compare jump behavior while allowing one frame of observation phase skew."""

    if not signatures:
        return False
    semantic_signatures = {
        json.dumps(
            {
                key: value
                for key, value in signature.items()
                if key != "airborne_frames"
            },
            sort_keys=True,
        )
        for signature in signatures.values()
    }
    airborne_frames = [
        int(signature["airborne_frames"]) for signature in signatures.values()
    ]
    return (
        len(semantic_signatures) == 1
        and max(airborne_frames) - min(airborne_frames) <= 1
    )


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
    frames: list[dict[str, object]] = []
    observation_reports: list[dict[str, object]] = []
    checkpoints: dict[str, dict[str, object]] = {}
    with McpClient(
        command,
        work_directory,
        {"NES_MCP_EMULATOR_BACKEND": "aprnes"},
    ) as mcp:
        listed = mcp.request("tools/list", {})["result"]["tools"]
        mcp_surface = validate_nes_mcp_surface(
            mcp.server_info,
            {str(tool["name"]) for tool in listed},
        )
        load = mcp.call_json("load_rom", {"path": str(rom)})
        if int(load["mapper"]) != 4:
            raise AssertionError(f"Tracked runner loaded as mapper {load['mapper']}, not MMC3.")
        mcp.call_json("run_frame", {"count": idle_frames})

        def read(address: int, length: int) -> list[int]:
            return parse_bytes(
                mcp.call_json(
                    "read_memory",
                    {"address": f"0x{address:04X}", "length": length},
                )
            )

        def observe(
            phase: str,
            buttons: list[str],
            frame_count: int,
            *,
            replay_state: str | None = None,
            replay_offset: int = 0,
        ) -> list[dict[str, object]]:
            if frame_count == 0:
                return []
            if frame_count > 1:
                observed_frames: list[dict[str, object]] = []
                for offset in range(frame_count):
                    observed_frames.extend(
                        observe(
                            phase,
                            buttons,
                            1,
                            replay_state=replay_state,
                            replay_offset=replay_offset + offset,
                        )
                    )
                return observed_frames
            result = mcp.call_json(
                "observe_execution",
                {
                    "buttons": buttons,
                    "frameCount": frame_count,
                    "memoryProbes": [
                        {"address": f"0x{address:04X}", "length": length}
                        for address, length in OBSERVATION_MEMORY_PROBES
                    ],
                    "includePpuState": True,
                    "tracePpuWrites": True,
                    "maxPpuEvents": OBSERVATION_MAX_PPU_EVENTS,
                    "ppuRegisters": [
                        "PPUCTRL",
                        "PPUSCROLL",
                        "PPUADDR",
                        "PPUDATA",
                    ],
                },
            )
            report = validate_execution_observation(result)
            report.update(
                {
                    "phase": phase,
                    "start_step": len(frames),
                    "end_step": len(frames) + frame_count - 1,
                    "initial_nametables": result["initialNametables"],
                    "final_nametables": result["finalNametables"],
                    "limits": result["limits"],
                }
            )
            observation_reports.append(report)
            observed: list[dict[str, object]] = []
            for index, frame in enumerate(result["frames"], start=1):
                item = observed_runtime_frame(
                    frame,
                    step=len(frames) + len(observed),
                    phase=phase,
                )
                anomaly = compact_screen_anomaly_report(item["screen"])
                item["screen_anomaly"] = anomaly
                regions = background_regions(item["state"])
                observed_regions = []
                for region in regions:
                    observed_region = mcp.call_json(
                        "read_screen_region",
                        {
                            "x": region["x"],
                            "y": region["y"] + 8,
                            "width": region["width"],
                            "height": region["height"],
                            "format": "palette_indices",
                        },
                    )
                    observed_regions.append(
                        {**region, "rowHashes": observed_region["rowHashes"]}
                    )
                item["screen"]["backgroundRegions"] = regions
                item["screen"]["backgroundRegionDigest"] = (
                    background_region_digest(observed_regions)
                )
                if replay_state is not None:
                    item["replay_state"] = replay_state
                    item["replay_offset"] = replay_offset + index
                    item["replay_buttons"] = buttons
                observed.append(item)
            frames.extend(observed)
            return observed

        initial_state = runtime_snapshot(read)
        checkpoints["initial"] = capture_aprnes_checkpoint(mcp, initial_state)
        state_paths = {
            "right": work_directory / "trace-states" / "right-start.nesstate",
            "left": work_directory / "trace-states" / "left-start.nesstate",
        }
        state_paths["right"].parent.mkdir(parents=True, exist_ok=True)
        mcp.call_json("save_state", {"path": str(state_paths["right"])})

        right_frames = 0
        right_state = initial_state
        while right_frames < maximum_right_frames:
            remaining = maximum_right_frames - right_frames
            far_from_target = int(right_state["visible_camera_x"]) < target_camera_x - 64
            count = min(120, remaining) if far_from_target else 1
            observed = observe(
                "right",
                ["RIGHT"],
                count,
                replay_state="right",
                replay_offset=right_frames,
            )
            right_frames += count
            right_state = observed[-1]["state"]
            if int(right_state["visible_camera_x"]) >= target_camera_x:
                break
        else:
            raise AssertionError(
                f"AprNes did not reach camera X {target_camera_x} in {maximum_right_frames} RIGHT frames."
            )

        observe("right-post", [], POST_TARGET_SETTLE_FRAMES)
        observe("right-release", [], release_frames)
        released_state = runtime_snapshot(read)
        checkpoints["camera-target"] = capture_aprnes_checkpoint(mcp, released_state)
        continuation_extra = continuation_frames - release_frames
        if continuation_extra < 0:
            raise ValueError("FCEUmm continuation preceded its serialized target checkpoint.")
        observe("right-continuation", [], continuation_extra)

        def advance_jump(pressed: bool) -> dict[str, object]:
            return observe("jump", ["A"] if pressed else [], 1)[-1]["state"]

        jump = exercise_jump(lambda: runtime_snapshot(read), advance_jump, jump_hold_frames)
        mcp.call_json("save_state", {"path": str(state_paths["left"])})
        left_frames = 0
        left_state = runtime_snapshot(read)
        while left_frames < maximum_left_frames:
            observed = observe(
                "left",
                ["LEFT"],
                1,
                replay_state="left",
                replay_offset=left_frames,
            )
            left_frames += 1
            left_state = observed[-1]["state"]
            if int(left_state["visible_camera_x"]) <= return_camera_x:
                break
        else:
            raise AssertionError(
                f"AprNes did not return through camera X {return_camera_x} in "
                f"{maximum_left_frames} LEFT frames."
            )
        observe("left-post", [], POST_TARGET_SETTLE_FRAMES)
        observe("left-release", [], return_release_frames)
        checkpoints["camera-return"] = capture_aprnes_checkpoint(
            mcp,
            runtime_snapshot(read),
        )

        windows = commit_frame_windows(frames, TRANSIENT_FRAME_RADIUS)
        focal_windows = select_focal_commit_windows(
            windows,
            target_camera_x=target_camera_x,
        )
        ppu_commit_traces = capture_aprnes_ppu_commit_traces(
            mcp,
            read,
            focal_windows,
            state_paths,
        )
        return {
            "states": [frame["state"] for frame in frames if frame["phase"] == "right"],
            "left_states": [frame["state"] for frame in frames if frame["phase"] == "left"],
            "frames": frames,
            "initial_state": initial_state,
            "checkpoints": checkpoints,
            "right_frames": right_frames,
            "left_frames": left_frames,
            "jump": jump,
            "load": load,
            "server": {**mcp_surface, "backend": "AprNes", "mapper": load["mapper"]},
            "observation_reports": observation_reports,
            "commit_windows": windows,
            "focal_windows": focal_windows,
            "ppu_commit_traces": ppu_commit_traces,
        }


def capture_aprnes_checkpoint(
    mcp: McpClient,
    state: dict[str, object],
) -> dict[str, object]:
    nametable_dump = mcp.call_json("dump_nametables", {"includeDetails": True})
    physical_tables = nametable_dump_bytes(nametable_dump)
    palette_indices = mcp.call_json(
        "read_screen_region",
        {"x": 0, "y": 8, "width": 256, "height": 224, "format": "palette_indices_raw"},
    )
    return {
        "state": state,
        "framebuffer": normalize_frame(mcp.capture_screen()),
        "nametables": physical_tables,
        "nametable_hashes": compact_nametable_hashes(nametable_dump),
        "rendered_palette_indices": {
            "histogram": palette_indices["histogram"],
            "row_hashes": palette_indices["rowHashes"],
        },
        "_palette_indices": palette_indices["values"],
        "ppu": mcp.call_json("read_ppu_state", {}),
        "ppu_writers": mcp.call_json("find_last_writers", {"address": "0x2000", "length": 8}),
    }


def compact_nametable_hashes(dump: dict[str, object]) -> list[dict[str, object]]:
    return [
        {
            "address": table["address"],
            "hash": table["hash"],
            "tile_hash": table["tileHash"],
            "attribute_hash": table["attributeHash"],
        }
        for table in dump["nametables"]
    ]


def nametable_dump_bytes(dump: dict[str, object]) -> bytes:
    physical_tables = bytearray()
    for table in dump["nametables"]:
        detail = table.get("detail")
        if not isinstance(detail, dict):
            raise ValueError("Nes.Mcp nametable details were not requested.")
        tile_bytes = bytes(
            int(value, 16)
            for row in detail["rows"]
            for value in str(row).split()
        )
        attribute_bytes = bytes(
            int(value, 16)
            for row in detail["attributeRows"]
            for value in str(row).split()
        )
        if len(tile_bytes) != 0x3C0 or len(attribute_bytes) != 0x40:
            raise ValueError(
                f"Nes.Mcp returned an invalid physical nametable at {table['address']}."
            )
        physical_tables.extend(tile_bytes)
        physical_tables.extend(attribute_bytes)
    if len(physical_tables) != 0x1000:
        raise ValueError("Nes.Mcp did not return four complete physical nametables.")
    return bytes(physical_tables)


def capture_aprnes_ppu_commit_traces(
    mcp: McpClient,
    read,
    focal_windows: dict[str, dict[str, object]],
    state_paths: dict[str, Path],
) -> dict[str, dict[str, object]]:
    reports: dict[str, dict[str, object]] = {}
    for label, window in focal_windows.items():
        center = next(
            frame
            for frame in window["frames"]
            if int(frame["step"]) == int(window["center_step"])
        )
        replay_state = str(center["replay_state"])
        replay_offset = int(center["replay_offset"])
        buttons = list(center["replay_buttons"])
        mcp.call_json("load_state", {"path": str(state_paths[replay_state])})
        mcp.call_json("set_controller", {"buttons": buttons})
        if replay_offset > 1:
            mcp.call_json("run_frame", {"count": replay_offset - 1})
        initial_state = runtime_snapshot(read)
        before_nametables = mcp.call_json(
            "dump_nametables", {"includeDetails": False}
        )
        trace = mcp.call_json(
            "trace_ppu_register_writes",
            {
                "buttons": buttons,
                "frameCount": 1,
                "maxEvents": 10_000,
                "registers": ["PPUCTRL", "PPUSCROLL", "PPUADDR", "PPUDATA"],
            },
        )
        current_state = runtime_snapshot(read)
        after_nametables = mcp.call_json(
            "dump_nametables", {"includeDetails": False}
        )
        ppu_state = mcp.call_json("read_ppu_state", {})
        selected_table = str(ppu_state["control"]["nametableAddress"])
        validation = validate_ppu_commit_trace(
            trace,
            before_state=initial_state,
            after_state=current_state,
            max_cpu_cycles=PPU_COMMIT_BUDGET_CYCLES,
        )
        reports[label] = {
            "commit": int(window["commit"]),
            "trajectory_step": int(window["center_step"]),
            "replay_state": replay_state,
            "replay_offset": replay_offset,
            "initial_state": initial_state,
            "final_state": current_state,
            "trace": trace,
            "validation": validation,
            "nametables_before": compact_nametable_hashes(before_nametables),
            "nametables_after": compact_nametable_hashes(after_nametables),
            "ppu_state": ppu_state,
            "screen_region": mcp.call_json(
                "read_screen_region",
                {
                    "x": 0,
                    "y": 8,
                    "width": 256,
                    "height": 224,
                    "format": "palette_indices",
                },
            ),
            "tilemap": mcp.call_json("dump_tilemap", {"address": selected_table}),
            "oam": mcp.call_json("dump_oam", {}),
            "framebuffer": normalize_frame(mcp.capture_screen()),
        }
    return reports


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
    raw_framebuffer, framebuffer = split_raw_frame(
        Image.open(thumbnail).convert("RGB")
        if thumbnail.is_file()
        else session.capture_screen()
    )
    checkpoint = {
        "state": saved_state,
        "live_state_before_save": live_state,
        "framebuffer": framebuffer,
        "_raw_framebuffer": raw_framebuffer,
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
    frames: list[dict[str, object]] = []
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
        live_initial_state = runtime_snapshot(session.read)
        checkpoints["initial"] = capture_fceumm_checkpoint(session, live_initial_state)
        initial_state = checkpoints["initial"]["state"]
        frames.append(
            {
                "step": len(frames),
                "phase": "initial",
                "state": initial_state,
                "framebuffer": checkpoints["initial"]["framebuffer"],
                "_raw_framebuffer": checkpoints["initial"]["_raw_framebuffer"],
            }
        )
        previous_commit = int(
            checkpoints["initial"]["live_state_after_save"]["lifecycle"]["commit"]
        )
        for _ in range(maximum_right_frames):
            session.set_right(True)
            session.advance_frame()
            state = runtime_snapshot(session.read)
            states.append(state)
            raw_framebuffer, framebuffer = split_raw_frame(session.capture_screen())
            frames.append(
                {
                    "step": len(frames),
                    "phase": "right",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            commit = int(state["lifecycle"]["commit"])
            visible = int(state["visible_camera_x"])
            if commit != previous_commit and "first-commit" not in checkpoints:
                checkpoints["first-commit"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible >= 256 and "camera-256" not in checkpoints:
                checkpoints["camera-256"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible >= target_camera_x:
                session.set_right(False)
                for _ in range(POST_TARGET_SETTLE_FRAMES):
                    session.advance_frame()
                    settled_state = runtime_snapshot(session.read)
                    raw_framebuffer, framebuffer = split_raw_frame(session.capture_screen())
                    frames.append(
                        {
                            "step": len(frames),
                            "phase": "right-post",
                            "state": settled_state,
                            "framebuffer": framebuffer,
                            "_raw_framebuffer": raw_framebuffer,
                        }
                    )
                state = runtime_snapshot(session.read)
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
            state = runtime_snapshot(session.read)
            raw_framebuffer, framebuffer = split_raw_frame(session.capture_screen())
            frames.append(
                {
                    "step": len(frames),
                    "phase": "jump",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            return state

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
            raw_framebuffer, framebuffer = split_raw_frame(session.capture_screen())
            frames.append(
                {
                    "step": len(frames),
                    "phase": "left",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            visible = int(state["visible_camera_x"])
            if visible <= 255 and "left-256" not in checkpoints:
                checkpoints["left-256"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible <= return_camera_x:
                session.set_button(RETRO_DEVICE_ID_JOYPAD_LEFT, False)
                for _ in range(POST_TARGET_SETTLE_FRAMES):
                    session.advance_frame()
                    settled_state = runtime_snapshot(session.read)
                    raw_framebuffer, framebuffer = split_raw_frame(session.capture_screen())
                    frames.append(
                        {
                            "step": len(frames),
                            "phase": "left-post",
                            "state": settled_state,
                            "framebuffer": framebuffer,
                            "_raw_framebuffer": raw_framebuffer,
                        }
                    )
                state = runtime_snapshot(session.read)
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
        "frames": frames,
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
    frames: list[dict[str, object]] = []
    checkpoints: dict[str, dict[str, object]] = {}
    with LibretroSession(core, rom, work_directory) as session:
        initial_raw_frame: Image.Image | None = None
        initial_frame: Image.Image | None = None
        for _ in range(idle_frames):
            initial_raw_frame, initial_frame = split_raw_frame(session.run_frame([]))
        if initial_frame is None:
            raise RuntimeError("Nestopia idle capture produced no frame.")
        assert initial_raw_frame is not None

        def read_ram_state() -> dict[str, object]:
            ram = parse_nestopia_state(session.serialize())["RAM"]
            return runtime_snapshot(lambda address, length: list(ram[address : address + length]))

        initial_fields = parse_nestopia_state(session.serialize())
        initial_state = runtime_snapshot(
            lambda address, length: list(
                initial_fields["RAM"][address : address + length]
            )
        )
        checkpoints["initial"] = {
            "state": initial_state,
            "framebuffer": initial_frame,
            "_raw_framebuffer": initial_raw_frame,
            "nametables": initial_fields["NAMETABLES"],
            "palette": initial_fields["PALETTE"],
            "ppu_registers": initial_fields["PPU_REGISTERS"],
        }
        frames.append(
            {
                "step": len(frames),
                "phase": "initial",
                "state": initial_state,
                "framebuffer": initial_frame,
                "_raw_framebuffer": initial_raw_frame,
            }
        )
        previous_commit = int(initial_state["lifecycle"]["commit"])
        for _ in range(maximum_right_frames):
            raw_framebuffer, framebuffer = split_raw_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_RIGHT])
            )
            state = read_ram_state()
            states.append(state)
            frames.append(
                {
                    "step": len(frames),
                    "phase": "right",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            commit = int(state["lifecycle"]["commit"])
            visible = int(state["visible_camera_x"])
            if commit != previous_commit and "first-commit" not in checkpoints:
                checkpoints["first-commit"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible >= 256 and "camera-256" not in checkpoints:
                checkpoints["camera-256"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible >= target_camera_x:
                for _ in range(POST_TARGET_SETTLE_FRAMES):
                    raw_framebuffer, framebuffer = split_raw_frame(session.run_frame([]))
                    frames.append(
                        {
                            "step": len(frames),
                            "phase": "right-post",
                            "state": read_ram_state(),
                            "framebuffer": framebuffer,
                            "_raw_framebuffer": raw_framebuffer,
                        }
                    )
                for _ in range(release_frames):
                    raw_framebuffer, framebuffer = split_raw_frame(session.run_frame([]))
                state = read_ram_state()
                fields = parse_nestopia_state(session.serialize())
                checkpoints["camera-target"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
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
            raw_framebuffer, framebuffer = split_raw_frame(session.run_frame([]))

        def advance_jump(pressed: bool) -> dict[str, object]:
            nonlocal framebuffer
            raw_framebuffer, framebuffer = split_raw_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_A] if pressed else [])
            )
            state = read_ram_state()
            frames.append(
                {
                    "step": len(frames),
                    "phase": "jump",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            return state

        jump = exercise_jump(read_ram_state, advance_jump, jump_hold_frames)
        for _ in range(maximum_left_frames):
            raw_framebuffer, framebuffer = split_raw_frame(
                session.run_frame([RETRO_DEVICE_ID_JOYPAD_LEFT])
            )
            state = read_ram_state()
            left_states.append(state)
            frames.append(
                {
                    "step": len(frames),
                    "phase": "left",
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            )
            visible = int(state["visible_camera_x"])
            if visible <= 255 and "left-256" not in checkpoints:
                checkpoints["left-256"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
                }
            if visible <= return_camera_x:
                for _ in range(POST_TARGET_SETTLE_FRAMES):
                    raw_framebuffer, framebuffer = split_raw_frame(session.run_frame([]))
                    frames.append(
                        {
                            "step": len(frames),
                            "phase": "left-post",
                            "state": read_ram_state(),
                            "framebuffer": framebuffer,
                            "_raw_framebuffer": raw_framebuffer,
                        }
                    )
                for _ in range(return_release_frames):
                    raw_framebuffer, framebuffer = split_raw_frame(session.run_frame([]))
                state = read_ram_state()
                fields = parse_nestopia_state(session.serialize())
                checkpoints["camera-return"] = {
                    "state": state,
                    "framebuffer": framebuffer,
                    "_raw_framebuffer": raw_framebuffer,
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
        "frames": frames,
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


def commit_frame_windows(
    frames: list[dict[str, object]],
    radius: int = 2,
) -> list[dict[str, object]]:
    if radius < 1:
        raise ValueError("Commit-frame radius must be positive.")
    windows: list[dict[str, object]] = []
    if not frames:
        return windows
    previous_commit = int(frames[0]["state"]["lifecycle"]["commit"])
    for index, frame in enumerate(frames[1:], start=1):
        commit = int(frame["state"]["lifecycle"]["commit"])
        if commit == previous_commit:
            continue
        start = index - radius
        end = index + radius + 1
        if start < 0 or end > len(frames):
            raise AssertionError(
                f"Commit {commit} at frame {index} lacks a complete +/-{radius} frame window."
            )
        windows.append(
            {
                "commit": commit,
                "center_step": int(frame["step"]),
                "frames": frames[start:end],
            }
        )
        previous_commit = commit
    return windows


def select_focal_commit_windows(
    windows: list[dict[str, object]],
    *,
    target_camera_x: int,
) -> dict[str, dict[str, object]]:
    def center(window: dict[str, object]) -> dict[str, object]:
        center_step = int(window["center_step"])
        return next(
            frame for frame in window["frames"] if int(frame["step"]) == center_step
        )

    right = [window for window in windows if center(window)["phase"] == "right"]
    left = [window for window in windows if center(window)["phase"] == "left"]
    if not right or not left:
        raise AssertionError("Commit evidence must include both RIGHT and LEFT windows.")
    right_cross_index = next(
        (
            index
            for index, window in enumerate(right)
            if int(center(window)["state"]["visible_camera_x"]) >= 256
        ),
        None,
    )
    if right_cross_index is None or right_cross_index == 0:
        raise AssertionError("RIGHT commit evidence did not approach and cross camera X 256.")
    right_target = next(
        (
            window
            for window in right
            if int(center(window)["state"]["visible_camera_x"]) >= target_camera_x
        ),
        None,
    )
    left_cross = next(
        (
            window
            for window in left
            if int(center(window)["state"]["visible_camera_x"]) <= 255
        ),
        None,
    )
    if right_target is None:
        raise AssertionError(
            f"RIGHT commit evidence did not reach camera X {target_camera_x}."
        )
    if left_cross is None:
        raise AssertionError("LEFT commit evidence did not cross camera X 256/255.")
    return {
        "first-column": right[0],
        "right-before-256": right[right_cross_index - 1],
        "right-cross-256": right[right_cross_index],
        "right-target": right_target,
        "left-cross-256": left_cross,
    }


def calibrate_framebuffer_palette(
    reference: Image.Image,
    candidate: Image.Image,
) -> dict[tuple[int, int, int], tuple[int, int, int]]:
    reference = reference.convert("RGB")
    candidate = candidate.convert("RGB")
    if reference.size != candidate.size:
        raise ValueError(
            f"Framebuffer calibration sizes differ: {reference.size} != {candidate.size}."
        )
    cooccurrences: dict[
        tuple[int, int, int],
        dict[tuple[int, int, int], int],
    ] = {}
    for reference_pixel, candidate_pixel in zip(
        reference.getdata(),
        candidate.getdata(),
    ):
        counts = cooccurrences.setdefault(candidate_pixel, {})
        counts[reference_pixel] = counts.get(reference_pixel, 0) + 1

    ranked_pairs = sorted(
        (
            (count, candidate_pixel, reference_pixel)
            for candidate_pixel, counts in cooccurrences.items()
            for reference_pixel, count in counts.items()
        ),
        reverse=True,
    )
    mapping: dict[tuple[int, int, int], tuple[int, int, int]] = {}
    assigned_reference: set[tuple[int, int, int]] = set()
    for _, candidate_pixel, reference_pixel in ranked_pairs:
        if candidate_pixel in mapping or reference_pixel in assigned_reference:
            continue
        mapping[candidate_pixel] = reference_pixel
        assigned_reference.add(reference_pixel)
    for candidate_pixel, counts in cooccurrences.items():
        if candidate_pixel not in mapping:
            mapping[candidate_pixel] = max(counts, key=counts.get)
    return mapping


def framebuffer_corruption_report(
    reference: Image.Image,
    candidate: Image.Image,
    palette_mapping: dict[tuple[int, int, int], tuple[int, int, int]],
    ignored_rectangles: list[tuple[int, int, int, int]] | None = None,
) -> dict[str, object]:
    reference = reference.convert("RGB")
    candidate = candidate.convert("RGB")
    if reference.size != candidate.size:
        raise ValueError(
            f"Framebuffer comparison sizes differ: {reference.size} != {candidate.size}."
        )
    width, height = reference.size
    ignored = bytearray(width * height)
    for left, top, right, bottom in ignored_rectangles or []:
        for y in range(max(0, top), min(height, bottom)):
            for x in range(max(0, left), min(width, right)):
                ignored[y * width + x] = 1

    mismatches = bytearray(width * height)
    for index, (reference_pixel, candidate_pixel) in enumerate(
        zip(reference.getdata(), candidate.getdata())
    ):
        if ignored[index]:
            continue
        if palette_mapping.get(candidate_pixel) != reference_pixel:
            mismatches[index] = 1

    components: list[dict[str, int]] = []
    pending = {index for index, mismatch in enumerate(mismatches) if mismatch}
    while pending:
        first = pending.pop()
        stack = [first]
        xs: list[int] = []
        ys: list[int] = []
        while stack:
            index = stack.pop()
            x = index % width
            y = index // width
            xs.append(x)
            ys.append(y)
            for neighbor in (index - 1, index + 1, index - width, index + width):
                if neighbor not in pending:
                    continue
                neighbor_x = neighbor % width
                neighbor_y = neighbor // width
                if abs(neighbor_x - x) + abs(neighbor_y - y) != 1:
                    continue
                pending.remove(neighbor)
                stack.append(neighbor)
        components.append(
            {
                "x": min(xs),
                "y": min(ys),
                "width": max(xs) - min(xs) + 1,
                "height": max(ys) - min(ys) + 1,
                "pixels": len(xs),
            }
        )

    horizontal_lines = [
        component
        for component in components
        if component["width"] >= max(8, width // 2) and component["height"] <= 4
    ]
    vertical_columns = [
        component
        for component in components
        if component["height"] >= max(8, height // 2) and component["width"] <= 16
    ]
    mismatch_pixels = sum(mismatches)
    return {
        "corrupt": mismatch_pixels != 0,
        "mismatch_pixels": mismatch_pixels,
        "components": components,
        "horizontal_lines": horizontal_lines,
        "vertical_columns": vertical_columns,
    }


def compact_screen_anomaly_report(screen: dict[str, object]) -> dict[str, object]:
    """Classify compact Nes.Mcp screen diffs without expanding a framebuffer."""

    bounds = screen.get("changedBounds")
    if not isinstance(bounds, dict):
        return {
            "corrupt": False,
            "horizontal_lines": [],
            "vertical_columns": [],
        }
    width = int(bounds["width"])
    height = int(bounds["height"])
    horizontal_lines = []
    if width >= 128 and height <= 8:
        horizontal_lines = [
            int(row["row"])
            for row in screen.get("changedTileRows", [])
            if int(str(row["mask"]), 16).bit_count() >= 16
        ]
    vertical_columns = []
    if width <= 16 and height >= 64:
        masks = [
            int(str(row["mask"]), 16)
            for row in screen.get("changedTileRows", [])
        ]
        for column in range(32):
            rows = sum(bool(mask & (1 << column)) for mask in masks)
            if rows >= 8:
                vertical_columns.append({"column": column, "rows": rows})
    return {
        "corrupt": bool(horizontal_lines or vertical_columns),
        "horizontal_lines": horizontal_lines,
        "vertical_columns": vertical_columns,
    }


def aprnes_oam_rectangles(oam: dict[str, object]) -> list[tuple[int, int, int, int]]:
    rectangles: list[tuple[int, int, int, int]] = []
    for sprite in oam.get("sprites", []):
        if not sprite.get("visible"):
            continue
        x = int(sprite["x"])
        y = int(sprite["y"])
        # NES OAM stores top Y minus one. normalize_frame removes eight
        # overscan scanlines; one extra pixel on every side covers frontend
        # raster-phase differences without masking a complete background tile.
        top = y + 1 - 8
        rectangles.append((x - 1, top - 1, x + 9, top + 9))
    return rectangles


def visual_state_key(state: dict[str, object]) -> tuple[object, ...]:
    lifecycle = state["lifecycle"]
    writes = state["last_commit_writes"]
    return (
        int(state["player_x"]),
        int(state["player_y"]),
        int(state["requested_camera_x"]),
        int(state["requested_camera_y"]),
        int(state["visible_camera_x"]),
        int(state["visible_camera_y"]),
        int(lifecycle["request"]),
        int(lifecycle["prepare"]),
        int(lifecycle["resident"]),
        int(lifecycle["commit"]),
        int(lifecycle["release"]),
        int(writes["tiles"]),
        int(writes["attributes"]),
    )


def runner_sprite_rectangles(state: dict[str, object]) -> list[tuple[int, int, int, int]]:
    """Mask the runner's 2x3-sprite actor, not any background tile."""

    screen_x = int(state["player_x"]) - int(state["visible_camera_x"])
    screen_y = int(state["player_y"]) - int(state["visible_camera_y"])
    return [(screen_x - 2, screen_y - 4, screen_x + 18, screen_y + 28)]


def calibrate_palette_index_mapping(
    aprnes_checkpoints: dict[str, dict[str, object]],
    external_checkpoints: dict[str, dict[str, object]],
    aprnes_index: dict[tuple[object, ...], list[dict[str, object]]] | None = None,
    external_index: dict[tuple[object, ...], list[dict[str, object]]] | None = None,
) -> tuple[dict[tuple[int, int, int], int], dict[str, object]]:
    """Map one frontend's stable RGB palette back to AprNes palette indices."""

    votes: dict[tuple[int, int, int], Counter[int]] = defaultdict(Counter)
    used = []
    for name in ("initial", "camera-target", "camera-return"):
        aprnes = aprnes_checkpoints.get(name)
        external = external_checkpoints.get(name)
        if aprnes is None or external is None:
            continue
        indices = list(aprnes.get("_palette_indices", []))
        image = external.get("framebuffer")
        if not indices or not isinstance(image, Image.Image):
            continue
        pixels = list(image.convert("RGB").getdata())
        if len(indices) != len(pixels):
            raise AssertionError(
                f"Palette calibration checkpoint {name} has {len(indices)} AprNes "
                f"pixels and {len(pixels)} external pixels."
            )
        used.append(name)
        for color, index in zip(pixels, indices):
            votes[color][int(index) & 0x3F] += 1
    sampled_frames = 0
    if aprnes_index is not None and external_index is not None:
        for key, references in aprnes_index.items():
            candidates = external_index.get(key, [])
            if not candidates:
                continue
            candidate = candidates[0]
            image = candidate.get("framebuffer")
            if not isinstance(image, Image.Image):
                continue
            for reference in references:
                sample = reference.get("_palette_sample")
                if not isinstance(sample, dict):
                    continue
                box = (
                    int(sample["x"]),
                    int(sample["y"]),
                    int(sample["x"]) + int(sample["width"]),
                    int(sample["y"]) + int(sample["height"]),
                )
                pixels = list(image.convert("RGB").crop(box).getdata())
                indices = list(sample["values"])
                if len(indices) != len(pixels):
                    raise AssertionError("Actor palette sample dimensions do not match.")
                sampled_frames += 1
                for color, index in zip(pixels, indices):
                    votes[color][int(index) & 0x3F] += 1
    if not used:
        raise AssertionError("Compact AprNes comparison lacks raw checkpoint calibration evidence.")

    mapping: dict[tuple[int, int, int], int] = {}
    ambiguous: dict[str, dict[str, int]] = {}
    for color, counts in votes.items():
        index, count = counts.most_common(1)[0]
        mapping[color] = index
        if len(counts) > 1:
            ambiguous[str(color)] = {
                str(value): occurrences for value, occurrences in sorted(counts.items())
            }
    return mapping, {
        "checkpoints": used,
        "actor_sampled_frames": sampled_frames,
        "mapped_rgb_colors": len(mapping),
        "ambiguous_rgb_colors": ambiguous,
    }


def palette_index_row_digest(row_hashes: list[str]) -> str:
    encoded = "\n".join(row_hashes).encode()
    return f"sha256:{hashlib.sha256(encoded).hexdigest()}"


def background_regions(
    state: dict[str, object],
    *,
    width: int = 256,
    height: int = 224,
) -> list[dict[str, int]]:
    actor_x = max(
        0,
        min(width - 20, int(state["player_x"]) - int(state["visible_camera_x"]) - 2),
    )
    actor_y = max(
        0,
        min(height - 32, int(state["player_y"]) - int(state["visible_camera_y"]) - 4),
    )
    candidates = [
        {"x": 0, "y": 0, "width": width, "height": actor_y},
        {
            "x": 0,
            "y": actor_y + 32,
            "width": width,
            "height": height - actor_y - 32,
        },
        {"x": 0, "y": actor_y, "width": actor_x, "height": 32},
        {
            "x": actor_x + 20,
            "y": actor_y,
            "width": width - actor_x - 20,
            "height": 32,
        },
    ]
    return [region for region in candidates if region["width"] and region["height"]]


def background_region_digest(regions: list[dict[str, object]]) -> str:
    compact = [
        {
            "x": int(region["x"]),
            "y": int(region["y"]),
            "width": int(region["width"]),
            "height": int(region["height"]),
            "rowHashes": list(region["rowHashes"]),
        }
        for region in regions
    ]
    encoded = json.dumps(compact, sort_keys=True, separators=(",", ":")).encode()
    return f"sha256:{hashlib.sha256(encoded).hexdigest()}"


def canonical_palette_index_hash(
    image: Image.Image,
    mapping: dict[tuple[int, int, int], int],
) -> tuple[str | None, list[tuple[int, int, int]]]:
    unknown = sorted(set(image.convert("RGB").getdata()) - set(mapping))
    if unknown:
        return None, unknown
    rows = []
    rgb = image.convert("RGB")
    for y in range(rgb.height):
        row_hash = 2166136261
        for x in range(rgb.width):
            row_hash ^= mapping[rgb.getpixel((x, y))]
            row_hash = row_hash * 16777619 & 0xFFFFFFFF
        rows.append(f"0x{row_hash:08X}")
    return palette_index_row_digest(rows), []


def canonical_background_region_hash(
    image: Image.Image,
    mapping: dict[tuple[int, int, int], int],
    regions: list[dict[str, int]],
) -> tuple[str | None, list[tuple[int, int, int]]]:
    rgb = image.convert("RGB")
    unknown: set[tuple[int, int, int]] = set()
    summaries: list[dict[str, object]] = []
    for region in regions:
        row_hashes = []
        for y in range(region["y"], region["y"] + region["height"]):
            row_hash = 2166136261
            for x in range(region["x"], region["x"] + region["width"]):
                color = rgb.getpixel((x, y))
                index = mapping.get(color)
                if index is None:
                    unknown.add(color)
                    index = 0
                row_hash ^= index
                row_hash = row_hash * 16777619 & 0xFFFFFFFF
            row_hashes.append(f"0x{row_hash:08X}")
        summaries.append({**region, "rowHashes": row_hashes})
    if unknown:
        return None, sorted(unknown)
    return background_region_digest(summaries), []


def compare_transient_frame_runs(
    captures: dict[str, dict[str, object]],
) -> dict[str, object]:
    aprnes_frames = list(captures["aprnes"]["frames"])
    if not aprnes_frames:
        raise AssertionError("AprNes produced no per-frame visual evidence.")
    comparable_phases = {"right", "right-post", "left", "left-post", "settle"}
    aprnes_index: dict[tuple[object, ...], list[dict[str, object]]] = {}
    for frame in aprnes_frames:
        if frame["phase"] not in comparable_phases:
            continue
        key = (frame["phase"], *visual_state_key(frame["state"]))
        aprnes_index.setdefault(key, []).append(frame)

    if "framebuffer" not in aprnes_frames[0]:
        # Nes.Mcp 0.0.7 intentionally retains palette-index hashes and compact
        # per-frame diffs atomically rather than 61,440 raw bytes per frame.
        # Calibrate each external RGB palette from three raw checkpoints, then
        # compare every aligned frontend raster as the exact AprNes index hash.
        # Compact bounds remain diagnostics; they are not the acceptance oracle.
        candidate_indexes: dict[
            str, dict[tuple[object, ...], list[dict[str, object]]]
        ] = {}
        canonical_indexes: dict[
            str, dict[tuple[object, ...], list[dict[str, object]]]
        ] = {}
        reports: dict[str, object] = {
            "aprnes": {
                "aligned_frames": len(aprnes_index),
                "compact_palette_index_frames": len(aprnes_index),
                "corrupt_frames": [],
                "unmatched_candidate_frames": [],
                "unmatched_reference_frames": [],
            }
        }
        independent_expectations: dict[
            str, tuple[list[dict[str, int]], list[dict[str, object]]]
        ] = {}
        for references in aprnes_index.values():
            for reference in references:
                regions = list(reference["screen"]["backgroundRegions"])
                key = json.dumps(regions, sort_keys=True, separators=(",", ":"))
                if key not in independent_expectations:
                    independent_expectations[key] = (regions, [])
                independent_expectations[key][1].append(reference)
        for emulator in ("fceumm", "nestopia"):
            frames = list(captures[emulator]["frames"])
            index: dict[tuple[object, ...], list[dict[str, object]]] = {}
            canonical_index: dict[tuple[object, ...], list[dict[str, object]]] = {}
            unmatched = []
            for frame in frames:
                if frame["phase"] not in comparable_phases:
                    continue
                key = (frame["phase"], *visual_state_key(frame["state"]))
                index.setdefault(key, []).append(frame)
            palette_mapping, calibration = calibrate_palette_index_mapping(
                captures["aprnes"]["checkpoints"],
                captures[emulator]["checkpoints"],
                aprnes_index,
                index,
            )
            aligned = 0
            corrupt = []
            independently_aligned = []
            for frame in frames:
                if frame["phase"] not in comparable_phases:
                    continue
                key = (frame["phase"], *visual_state_key(frame["state"]))
                references = aprnes_index.get(key, [])
                if not references:
                    image = frame.get("framebuffer")
                    if not isinstance(image, Image.Image):
                        raise AssertionError(f"{emulator} frame lacks its 256x224 raster.")
                    independent_match = None
                    for regions, possible_references in independent_expectations.values():
                        candidate_hash, unknown = canonical_background_region_hash(
                            image,
                            palette_mapping,
                            regions,
                        )
                        matching_references = [
                            reference
                            for reference in possible_references
                            if reference["screen"]["backgroundRegionDigest"]
                            == candidate_hash
                        ]
                        if matching_references:
                            independent_match = {
                                "step": int(frame["step"]),
                                "phase": frame["phase"],
                                "hash": candidate_hash,
                                "reference_steps": sorted(
                                    {int(reference["step"]) for reference in matching_references}
                                ),
                            }
                            break
                    if independent_match is not None:
                        aligned += 1
                        independently_aligned.append(independent_match)
                        continue
                    unmatched.append(
                        {"step": int(frame["step"]), "phase": frame["phase"]}
                    )
                    continue
                aligned += 1
                raw = frame.get("framebuffer")
                if not isinstance(raw, Image.Image):
                    raise AssertionError(f"{emulator} frame lacks its 256x224 raster.")
                canonical_hash, unknown = canonical_background_region_hash(
                    raw,
                    palette_mapping,
                    list(references[0]["screen"]["backgroundRegions"]),
                )
                evidence = {
                    "step": int(frame["step"]),
                    "phase": frame["phase"],
                    "hash": canonical_hash,
                }
                canonical_index.setdefault(key, []).append(evidence)
                expected = {
                    str(reference["screen"]["backgroundRegionDigest"])
                    for reference in references
                }
                if canonical_hash not in expected:
                    corrupt.append(
                        {
                            **evidence,
                            "expected_aprnes_hashes": sorted(expected),
                            "unknown_rgb_colors": [str(color) for color in unknown],
                        }
                    )
            candidate_indexes[emulator] = index
            canonical_indexes[emulator] = canonical_index
            reports[emulator] = {
                "aligned_frames": aligned,
                "canonical_palette_index_frames": sum(map(len, canonical_index.values())),
                "palette_calibration": calibration,
                "independently_aligned_frames": independently_aligned,
                "corrupt_frames": corrupt,
                "unmatched_candidate_frames": unmatched,
                "unmatched_reference_frames": [
                    {"step": int(frame["step"]), "phase": frame["phase"]}
                    for key, reference_frames in aprnes_index.items()
                    if key not in index
                    for frame in reference_frames
                ],
            }

        for key, references in aprnes_index.items():
            for reference in references:
                expected_hash = str(reference["screen"]["backgroundRegionDigest"])
                matching = {
                    emulator: any(
                        candidate["hash"] == expected_hash
                        for candidate in canonical_indexes[emulator].get(key, [])
                    )
                    for emulator in ("fceumm", "nestopia")
                }
                if not all(matching.values()):
                    reports["aprnes"]["corrupt_frames"].append(
                        {
                            "step": int(reference["step"]),
                            "phase": reference["phase"],
                            "hash": expected_hash,
                            "matching_external_rasters": matching,
                            "screen_anomaly": reference["screen_anomaly"],
                        }
                    )

        return {
            "verified": all(
                not report["corrupt_frames"]
                and not report["unmatched_candidate_frames"]
                and not report["unmatched_reference_frames"]
                for report in reports.values()
            ),
            "comparison": "exact AprNes palette-index hashes reconstructed from calibrated FCEUmm/Nestopia RGB",
            "emulators": reports,
        }

    reports: dict[str, object] = {}
    for emulator in ("fceumm", "nestopia"):
        candidate_frames = list(captures[emulator]["frames"])
        if not candidate_frames:
            raise AssertionError(f"{emulator} produced no per-frame visual evidence.")
        palette_mapping = calibrate_framebuffer_palette(
            aprnes_frames[0]["framebuffer"],
            candidate_frames[0]["framebuffer"],
        )
        candidate_index: dict[tuple[object, ...], list[dict[str, object]]] = {}
        corrupt_frames: list[dict[str, object]] = []
        unmatched_candidate: list[dict[str, object]] = []
        aligned_frames = 0
        for frame in candidate_frames:
            if frame["phase"] not in comparable_phases:
                continue
            key = (frame["phase"], *visual_state_key(frame["state"]))
            candidate_index.setdefault(key, []).append(frame)
            references = aprnes_index.get(key, [])
            if not references:
                unmatched_candidate.append(
                    {"step": int(frame["step"]), "phase": frame["phase"]}
                )
                continue
            comparisons = [
                (
                    reference,
                    framebuffer_corruption_report(
                        reference["framebuffer"],
                        frame["framebuffer"],
                        palette_mapping,
                        list(reference.get("oam_rectangles", [])),
                    ),
                )
                for reference in references
            ]
            reference, comparison = min(
                comparisons,
                key=lambda item: int(item[1]["mismatch_pixels"]),
            )
            aligned_frames += 1
            if comparison["corrupt"]:
                corrupt_frames.append(
                    {
                        "candidate_step": int(frame["step"]),
                        "reference_step": int(reference["step"]),
                        "phase": frame["phase"],
                        **comparison,
                    }
                )

        unmatched_reference = [
            {"step": int(frame["step"]), "phase": frame["phase"]}
            for key, frames in aprnes_index.items()
            if key not in candidate_index
            for frame in frames
        ]
        reports[emulator] = {
            "aligned_frames": aligned_frames,
            "corrupt_frames": corrupt_frames,
            "unmatched_candidate_frames": unmatched_candidate,
            "unmatched_reference_frames": unmatched_reference,
        }

    return {
        "verified": all(
            not report["corrupt_frames"]
            and not report["unmatched_candidate_frames"]
            and not report["unmatched_reference_frames"]
            for report in reports.values()
        ),
        "emulators": reports,
    }


def validate_ppu_commit_trace(
    trace: dict[str, object],
    *,
    before_state: dict[str, object],
    after_state: dict[str, object],
    max_cpu_cycles: int,
) -> dict[str, object]:
    """Validate one complete AprNes column commit from an MCP 0.0.7 trace."""

    violations: list[str] = []

    events = list(trace.get("events", []))
    event_count = int(trace.get("eventCount", -1))
    events_observed = int(trace.get("eventsObserved", -1))
    if bool(trace.get("truncated")) or event_count != events_observed:
        violations.append(
            "PPU trace was truncated; every observed register write is required."
        )
    if event_count != len(events):
        violations.append(
            f"PPU trace declared {event_count} events but returned {len(events)}."
        )
    if int(trace.get("framesRun", 0)) != 1 or trace.get("stopReason") != "framesComplete":
        violations.append("PPU trace did not complete exactly one focal frame.")

    def integer(value: object) -> int:
        return int(value, 16) if isinstance(value, str) else int(value)

    def address(event: dict[str, object]) -> int:
        return integer(event["address"])

    def value(event: dict[str, object]) -> int:
        return integer(event["value"])

    def snapshot_value(event: dict[str, object], side: str, name: str) -> int:
        return integer(event[side][name])

    required_event_fields = {
        "frameOffset",
        "frame",
        "cpuCycle",
        "instructionCounter",
        "pc",
        "address",
        "register",
        "value",
        "before",
        "after",
    }
    required_snapshot_fields = {
        "scanline",
        "dot",
        "vblank",
        "renderingActive",
        "v",
        "t",
        "x",
        "w",
    }
    metadata_complete = True
    for event in events:
        if not required_event_fields <= set(event):
            metadata_complete = False
            continue
        if any(
            not required_snapshot_fields <= set(event[side])
            for side in ("before", "after")
        ):
            metadata_complete = False
    if not metadata_complete:
        violations.append(
            "PPU events lack PC/cycle/instruction/frame or before/after v/t/x/w metadata."
        )

    vertical_control = next(
        (
            index
            for index, event in enumerate(events)
            if address(event) == 0x2000 and value(event) == 0x84
        ),
        None,
    )
    horizontal_control = next(
        (
            index
            for index, event in enumerate(events)
            if address(event) == 0x2000
            and value(event) == 0x80
            and (vertical_control is None or index > vertical_control)
        ),
        None,
    )
    if vertical_control is None:
        violations.append("PPUCTRL=$84 was not written before vertical tile streaming.")
    if horizontal_control is None:
        violations.append("PPUCTRL=$80 was not restored before attribute streaming.")

    display_controls = [
        index
        for index, event in enumerate(events)
        if address(event) == 0x2000 and value(event) & 0xFC == 0x80
    ]
    display_control = (
        next(
            (
                index
                for index in display_controls
                if horizontal_control is not None and index > horizontal_control
            ),
            None,
        )
        if horizontal_control is not None
        else None
    )
    scroll_writes = [
        (index, event)
        for index, event in enumerate(events)
        if address(event) == 0x2005
    ]
    expected_display_control: int | None = None
    expected_scroll_values: list[int] = []
    if display_control is None:
        violations.append("Display PPUCTRL was not republished before the final PPUSCROLL pair.")
    if len(scroll_writes) != 2:
        violations.append(
            f"Camera restore wrote {len(scroll_writes)} PPUSCROLL values instead of 2."
        )
    elif (
        display_control is None
        or [index for index, _ in scroll_writes]
        != [display_control + 1, display_control + 2]
        or display_control + 2 != len(events) - 1
    ):
        violations.append(
            "The final display PPUCTRL/PPUSCROLL/PPUSCROLL sequence was not ordered after the commit."
        )
    else:
        first_scroll = scroll_writes[0][1]
        second_scroll = scroll_writes[1][1]
        if not (
            not bool(first_scroll["before"]["w"])
            and bool(second_scroll["before"]["w"])
            and not bool(trace["finalPpuState"]["w"])
        ):
            violations.append("The PPUSCROLL pair did not complete the w=false -> true -> false latch sequence.")

        # A focal column commit publishes the requested horizontal position and
        # preserves the already-visible vertical position before restoring the
        # display registers. Validate the exact runtime-derived viewport, not
        # merely a self-consistent PPU latch sequence.
        expected_camera_x = int(before_state["requested_camera_x"])
        expected_camera_y = int(before_state["visible_camera_y"])
        expected_display_control = (
            0x80
            | ((expected_camera_x // 256) & 0x01)
            | (((expected_camera_y // 240) & 0x01) << 1)
        )
        expected_scroll_values = [expected_camera_x & 0xFF, expected_camera_y % 240]
        actual_scroll_values = [value(first_scroll), value(second_scroll)]
        if (
            value(events[display_control]) != expected_display_control
            or actual_scroll_values != expected_scroll_values
        ):
            violations.append(
                "The final PPUCTRL/PPUSCROLL values do not match the focal runtime camera."
            )

    tile_writes = 0
    attribute_writes = 0
    tile_events: list[dict[str, object]] = []
    attribute_events: list[dict[str, object]] = []
    if vertical_control is not None and horizontal_control is not None:
        tile_events = [
            event
            for event in events[vertical_control + 1 : horizontal_control]
            if address(event) == 0x2007
        ]
        attribute_events = [
            event
            for event in events[horizontal_control + 1 :]
            if address(event) == 0x2007
        ]
        tile_writes = len(tile_events)
        attribute_writes = len(attribute_events)
    if tile_writes != 32:
        violations.append(f"Column commit wrote {tile_writes} tiles instead of 32.")
    if attribute_writes != 8:
        violations.append(
            f"Column commit wrote {attribute_writes} attributes instead of 8."
        )

    outside_vblank = [
        event
        for event in events
        if address(event) == 0x2007
        and (
            not ppu_snapshot_in_physical_vblank(event["before"])
            or not ppu_snapshot_in_physical_vblank(event["after"])
        )
    ]
    if outside_vblank:
        violations.append(
            f"{len(outside_vblank)} PPUDATA writes occurred outside VBlank."
        )

    ppuaddr_pairs: list[dict[str, object]] = []
    if vertical_control is not None and horizontal_control is not None:
        tile_phase = [
            event
            for event in events[vertical_control + 1 : horizontal_control]
            if address(event) in (0x2006, 0x2007)
        ]
        cursor = 0
        while cursor < len(tile_phase):
            if (
                cursor + 2 >= len(tile_phase)
                or [address(event) for event in tile_phase[cursor : cursor + 2]]
                != [0x2006, 0x2006]
            ):
                violations.append("Tile stream has a malformed PPUADDR segment boundary.")
                break
            pair = tile_phase[cursor : cursor + 2]
            cursor += 2
            data_start = cursor
            while cursor < len(tile_phase) and address(tile_phase[cursor]) == 0x2007:
                cursor += 1
            data_events = tile_phase[data_start:cursor]
            if not data_events:
                violations.append("Tile PPUADDR pair was not followed by PPUDATA.")
                break
            ppuaddr_pairs.append(
                {
                    "kind": "tiles",
                    "events": pair,
                    "data": data_events[0],
                    "data_events": data_events,
                }
            )

        attribute_phase_end = display_control if display_control is not None else len(events)
        attribute_phase = [
            event
            for event in events[horizontal_control + 1 : attribute_phase_end]
            if address(event) in (0x2006, 0x2007)
        ]
        if len(attribute_phase) != attribute_writes * 3:
            violations.append(
                "Attribute stream did not use one PPUADDR pair per PPUDATA write."
            )
        for index in range(0, len(attribute_phase) - 2, 3):
            group = attribute_phase[index : index + 3]
            if [address(event) for event in group] != [0x2006, 0x2006, 0x2007]:
                violations.append(
                    f"Attribute write group {index // 3} has malformed PPUADDR/PPUDATA order."
                )
                continue
            ppuaddr_pairs.append(
                {
                    "kind": "attribute",
                    "events": group[:2],
                    "data": group[2],
                    "data_events": [group[2]],
                }
            )

    decoded_pairs: list[dict[str, object]] = []
    for pair in ppuaddr_pairs:
        high, low = pair["events"]
        target = ((value(high) & 0x3F) << 8) | value(low)
        valid_latch = (
            not bool(high["before"]["w"])
            and bool(high["after"]["w"])
            and bool(low["before"]["w"])
            and not bool(low["after"]["w"])
        )
        valid_target = (
            snapshot_value(low, "after", "t") & 0x3FFF == target
            and pair["data"] is not None
            and snapshot_value(pair["data"], "before", "v") & 0x3FFF == target
        )
        kind = str(pair["kind"])
        if not valid_latch or not valid_target:
            violations.append(
                f"{kind} PPUADDR pair did not publish the encoded address/latch state."
            )
        if kind == "attribute" and target & 0x03C0 != 0x03C0:
            violations.append(
                f"Attribute PPUADDR target 0x{target:04X} is outside an attribute table."
            )
        decoded_pairs.append({"kind": kind, "address": f"0x{target:04X}"})

    for pair, decoded in zip(ppuaddr_pairs, decoded_pairs):
        if pair["kind"] != "tiles":
            continue
        tile_start = integer(decoded["address"])
        for index, event in enumerate(pair["data_events"]):
            expected = (tile_start + index * 32) & 0x7FFF
            actual_before = snapshot_value(event, "before", "v") & 0x7FFF
            if actual_before != expected:
                violations.append(
                    f"Tile PPUDATA {index} did not follow the PPUCTRL=$84 vertical address sequence."
                )
                break

    expected_tile_addresses: list[int] = []
    expected_attribute_addresses: list[int] = []
    expected_commit_target: int | None = None
    descriptor = before_state.get("commit_descriptor")
    if not isinstance(descriptor, dict):
        violations.append("Focal runtime state omitted the commit descriptor.")
    else:
        commit_axis = int(descriptor["axis"])
        commit_direction = int(descriptor["direction"])
        commit_target = int(descriptor["target"])
        commit_payload_length = int(descriptor["payload_length"])
        commit_target_start = int(descriptor["target_start"])
        if (
            commit_axis != 1
            or commit_direction not in (1, 2)
            or not 0 <= commit_target < 64
            or commit_payload_length != 32
            or not 0 <= commit_target_start < 60
        ):
            violations.append(
                "Focal runtime commit descriptor is not one valid 32-row column."
            )
        else:
            requested_tile_column = int(before_state["requested_camera_x"]) // 8
            expected_commit_target = (
                requested_tile_column + (32 if commit_direction == 2 else 0)
            ) % 64
            if commit_target != expected_commit_target:
                violations.append(
                    "Focal runtime commit target does not match its direction/requested camera."
                )
            horizontal_table = expected_commit_target // 32
            tile_column = expected_commit_target % 32
            for index in range(commit_payload_length):
                tile_row = (commit_target_start + index) % 60
                vertical_table = tile_row // 30
                local_row = tile_row % 30
                expected_tile_addresses.append(
                    0x2000
                    + vertical_table * 0x800
                    + horizontal_table * 0x400
                    + local_row * 32
                    + tile_column
                )
            for index in range(8):
                tile_row = (commit_target_start + index * 4) % 60
                vertical_table = tile_row // 30
                local_row = tile_row % 30
                expected_attribute_addresses.append(
                    0x2000
                    + vertical_table * 0x800
                    + horizontal_table * 0x400
                    + 0x03C0
                    + (local_row // 4) * 8
                    + tile_column // 4
                )

    if tile_events:
        actual_tile_addresses = [
            snapshot_value(event, "before", "v") & 0x3FFF
            for event in tile_events
        ]
        if actual_tile_addresses != expected_tile_addresses:
            violations.append(
                "Tile PPUDATA targets do not match the focal runtime commit target."
            )

        if attribute_events:
            actual_attribute_addresses = [
                snapshot_value(event, "before", "v") & 0x3FFF
                for event in attribute_events
            ]
            if actual_attribute_addresses != expected_attribute_addresses:
                violations.append(
                    "Attribute PPUDATA targets do not follow the semantic attribute address sequence."
                )

    vertical_controls = [
        index
        for index, event in enumerate(events)
        if address(event) == 0x2000 and value(event) == 0x84
    ]
    if vertical_control is not None and horizontal_control is not None and any(
        index < vertical_control or index >= horizontal_control
        for index in vertical_controls
    ):
        violations.append("PPUCTRL=$84 was active outside vertical tile streaming.")

    write_span_cycles: int | None = None
    if vertical_control is not None and (tile_events or attribute_events):
        last_data = (attribute_events or tile_events)[-1]
        write_span_cycles = int(last_data["cpuCycle"]) - int(
            events[vertical_control]["cpuCycle"]
        )
        if write_span_cycles > max_cpu_cycles:
            violations.append(
                f"Commit write span {write_span_cycles} cycles exceeded the {max_cpu_cycles}-cycle VBlank budget."
            )

    before_commit = int(before_state["lifecycle"]["commit"])
    after_commit = int(after_state["lifecycle"]["commit"])
    if after_commit != (before_commit + 1) & 0xFF:
        violations.append("Focal frame did not publish exactly one commit counter advance.")
    writes = after_state["last_commit_writes"]
    if int(writes["tiles"]) != 32 or int(writes["attributes"]) != 8:
        violations.append("Runtime commit counters do not report exactly 32 tiles and 8 attributes.")
    forbidden = {
        key: (int(before_state["forbidden_commit_work"][key]), int(after_state["forbidden_commit_work"][key]))
        for key in ("bank", "directory", "decode")
    }
    if any(before or after for before, after in forbidden.values()):
        violations.append("WorldPack bank/directory/decode work occurred inside a commit.")
    if int(after_state["critical_section"]) != 0:
        violations.append("Commit critical section remained active after the focal frame.")

    return {
        "valid": not violations,
        "tile_writes": tile_writes,
        "attribute_writes": attribute_writes,
        "scroll_writes": len(scroll_writes),
        "expected_display_control": expected_display_control,
        "expected_scroll_values": expected_scroll_values,
        "expected_commit_target": expected_commit_target,
        "expected_tile_addresses": [
            f"0x{target:04X}" for target in expected_tile_addresses
        ],
        "expected_attribute_addresses": [
            f"0x{target:04X}" for target in expected_attribute_addresses
        ],
        "outside_vblank_writes": outside_vblank,
        "ppuaddr_pairs": decoded_pairs,
        "write_span_cycles": write_span_cycles,
        "max_cpu_cycles": max_cpu_cycles,
        "metadata_complete": metadata_complete,
        "events_observed": events_observed,
        "trace_truncated": bool(trace.get("truncated")),
        "forbidden_commit_work": forbidden,
        "violations": violations,
    }


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


def save_commit_frame_windows(
    artifacts: Path,
    captures: dict[str, dict[str, object]],
) -> dict[str, dict[str, object]]:
    reports: dict[str, dict[str, object]] = {}
    for emulator, capture in captures.items():
        directory = artifacts / "commit-windows" / emulator
        directory.mkdir(parents=True, exist_ok=True)
        windows = commit_frame_windows(capture["frames"], TRANSIENT_FRAME_RADIUS)
        serialized_windows: list[dict[str, object]] = []
        for window_index, window in enumerate(windows):
            serialized_frames: list[dict[str, object]] = []
            for frame in window["frames"]:
                serialized = {
                    "step": int(frame["step"]),
                    "phase": frame["phase"],
                    "state": frame["state"],
                }
                if "framebuffer" in frame:
                    path = directory / (
                        f"commit-{int(window['commit']):03d}-window-{window_index:03d}-"
                        f"step-{int(frame['step']):04d}.png"
                    )
                    frame["framebuffer"].save(path)
                    serialized.update(
                        {
                            "framebuffer": str(path.relative_to(artifacts)),
                            "framebuffer_sha256": image_sha256(frame["framebuffer"]),
                        }
                    )
                else:
                    serialized.update(
                        {
                            "screen": frame["screen"],
                            "screen_anomaly": frame["screen_anomaly"],
                        }
                    )
                serialized_frames.append(serialized)
            serialized_windows.append(
                {"commit": int(window["commit"]), "frames": serialized_frames}
            )
        reports[emulator] = {
            "radius": TRANSIENT_FRAME_RADIUS,
            "count": len(serialized_windows),
            "complete": bool(serialized_windows)
            and all(
                len(window["frames"]) == TRANSIENT_FRAME_RADIUS * 2 + 1
                for window in serialized_windows
            ),
            "windows": serialized_windows,
        }
    return reports


def save_ppu_commit_trace_artifacts(
    artifacts: Path,
    reports: dict[str, dict[str, object]],
) -> dict[str, dict[str, object]]:
    directory = artifacts / "ppu-commit-traces"
    directory.mkdir(parents=True, exist_ok=True)
    serialized: dict[str, dict[str, object]] = {}
    for label, report in reports.items():
        framebuffer = report["framebuffer"]
        path = directory / f"{label}.png"
        framebuffer.save(path)
        serialized[label] = {
            **{key: value for key, value in report.items() if key != "framebuffer"},
            "framebuffer": str(path.relative_to(artifacts)),
            "framebuffer_sha256": image_sha256(framebuffer),
        }
    return serialized


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
            summary["aprnes_server"] = aprnes["server"]
            summary["aprnes_observation_reports"] = aprnes["observation_reports"]
            summary["runs"] = save_checkpoint_framebuffers(artifacts, captures)
            commit_windows = save_commit_frame_windows(artifacts, captures)
            transient_frames = compare_transient_frame_runs(captures)
            ppu_commit_traces = aprnes["ppu_commit_traces"]
            serialized_ppu_commit_traces = save_ppu_commit_trace_artifacts(
                artifacts,
                ppu_commit_traces,
            )
            ppu_traces_complete = REQUIRED_FOCAL_TRACE_LABELS <= set(ppu_commit_traces)
            ppu_traces_valid = ppu_traces_complete and all(
                trace["validation"]["valid"] for trace in ppu_commit_traces.values()
            )
            commit_window_counts = {
                emulator: int(report["count"])
                for emulator, report in commit_windows.items()
            }
            commit_windows_complete = (
                len(set(commit_window_counts.values())) == 1
                and all(report["complete"] for report in commit_windows.values())
            )
            summary["commit_frame_windows"] = commit_windows
            summary["transient_frames"] = transient_frames
            summary["ppu_commit_traces"] = serialized_ppu_commit_traces
            summary["ppu_traces_complete"] = ppu_traces_complete
            summary["ppu_traces_valid"] = ppu_traces_valid

            initial = {
                emulator: capture["checkpoints"]["initial"]
                for emulator, capture in captures.items()
            }
            initial_nametables = {
                emulator: checkpoint["nametables"]
                for emulator, checkpoint in initial.items()
            }
            initial_states = {
                emulator: checkpoint["state"] for emulator, checkpoint in initial.items()
            }
            initial_cells = {
                emulator: visible_background_cells(
                    initial_nametables[emulator],
                    int(initial_states[emulator]["visible_camera_x"]),
                    int(initial_states[emulator]["visible_camera_y"]),
                )
                for emulator in initial
            }
            initial_physical_match = len(set(initial_nametables.values())) == 1
            initial_visible_match = len(
                {tuple(cells) for cells in initial_cells.values()}
            ) == 1
            summary["initial_physical_state"] = {
                "nametables_match": initial_physical_match,
                "visible_tile_palette_match": initial_visible_match,
                "nametables": {
                    emulator: {**nametable_snapshot(raw), "raw": raw.hex()}
                    for emulator, raw in initial_nametables.items()
                },
            }

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
            jump_parity = jump_semantics_match(jump_signatures)
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
                "maximum_observation_phase_skew_frames": 1,
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
                and initial_physical_match
                and initial_visible_match
                and commit_windows_complete
                and transient_frames["verified"]
                and ppu_traces_valid
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
            "INVALID: physical state, transient commit frames, real PPU traces, "
            "bidirectional runtime state, jump, input, or collision did not converge."
        )
        return 2
    print(
        "GREEN: every retained commit window had zero corrupt frames; initial/right/left "
        "physical nametables, exact tile/palette identities, real PPU traces, jump, and "
        "collision matched."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
