"""Differential NES runner visual-parity acceptance helpers."""

from __future__ import annotations

import base64
import ctypes
import hashlib
from io import BytesIO
import json
import os
from pathlib import Path
from collections.abc import Sequence
import re
import signal
import socket
import struct
import subprocess
import time

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
RETRO_DEVICE_JOYPAD = 1
RETRO_DEVICE_ID_JOYPAD_LEFT = 6
RETRO_DEVICE_ID_JOYPAD_RIGHT = 7
RETRO_DEVICE_ID_JOYPAD_A = 8
RETRO_DEVICE_ID_JOYPAD_MASK = 256

RETRO_ENVIRONMENT_GET_OVERSCAN = 2
RETRO_ENVIRONMENT_GET_CAN_DUPE = 3
RETRO_ENVIRONMENT_SHUTDOWN = 7
RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL = 8
RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9
RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10
RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS = 11
RETRO_ENVIRONMENT_GET_VARIABLE = 15
RETRO_ENVIRONMENT_SET_VARIABLES = 16
RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE = 17
RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME = 18
RETRO_ENVIRONMENT_GET_CONTENT_DIRECTORY = 30
RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY = 31
RETRO_ENVIRONMENT_SET_CONTROLLER_INFO = 35
RETRO_ENVIRONMENT_SET_MEMORY_MAPS = 36
RETRO_ENVIRONMENT_SET_GEOMETRY = 37
RETRO_ENVIRONMENT_GET_LANGUAGE = 39
RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS = 42
RETRO_ENVIRONMENT_GET_VFS_INTERFACE = 45
RETRO_ENVIRONMENT_GET_AUDIO_VIDEO_ENABLE = 47
RETRO_ENVIRONMENT_GET_FASTFORWARDING = 49
RETRO_ENVIRONMENT_GET_TARGET_REFRESH_RATE = 50
RETRO_ENVIRONMENT_GET_INPUT_BITMASKS = 51
RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION = 52
RETRO_ENVIRONMENT_SET_CORE_OPTIONS = 53
RETRO_ENVIRONMENT_SET_CORE_OPTIONS_INTL = 54
RETRO_ENVIRONMENT_SET_CONTENT_INFO_OVERRIDE = 65
RETRO_ENVIRONMENT_GET_GAME_INFO_EXT = 66
RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2 = 67
RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL = 68

RETRO_PIXEL_FORMAT_0RGB1555 = 0
RETRO_PIXEL_FORMAT_XRGB8888 = 1
RETRO_PIXEL_FORMAT_RGB565 = 2


class _RetroSystemInfo(ctypes.Structure):
    _fields_ = [
        ("library_name", ctypes.c_char_p),
        ("library_version", ctypes.c_char_p),
        ("valid_extensions", ctypes.c_char_p),
        ("need_fullpath", ctypes.c_bool),
        ("block_extract", ctypes.c_bool),
    ]


class _RetroGameInfo(ctypes.Structure):
    _fields_ = [
        ("path", ctypes.c_char_p),
        ("data", ctypes.c_void_p),
        ("size", ctypes.c_size_t),
        ("meta", ctypes.c_char_p),
    ]


class _RetroVariable(ctypes.Structure):
    _fields_ = [("key", ctypes.c_char_p), ("value", ctypes.c_char_p)]


class _RetroCoreOptionValue(ctypes.Structure):
    _fields_ = [("value", ctypes.c_char_p), ("label", ctypes.c_char_p)]


class _RetroCoreOptionV2Definition(ctypes.Structure):
    _fields_ = [
        ("key", ctypes.c_char_p),
        ("desc", ctypes.c_char_p),
        ("info", ctypes.c_char_p),
        ("desc_categorized", ctypes.c_char_p),
        ("info_categorized", ctypes.c_char_p),
        ("category_key", ctypes.c_char_p),
        ("values", _RetroCoreOptionValue * 128),
        ("default_value", ctypes.c_char_p),
    ]


class _RetroCoreOptionsV2(ctypes.Structure):
    _fields_ = [("categories", ctypes.c_void_p), ("definitions", ctypes.POINTER(_RetroCoreOptionV2Definition))]


class _RetroCoreOptionsV2Intl(ctypes.Structure):
    _fields_ = [("us", ctypes.POINTER(_RetroCoreOptionsV2)), ("local", ctypes.POINTER(_RetroCoreOptionsV2))]


def build_retroarch_command(
    launch_command: Sequence[str],
    config_path: Path,
    core_path: Path,
    rom_path: Path,
) -> list[str]:
    return [
        *launch_command,
        f"--config={config_path.resolve()}",
        "--verbose",
        "--max-frames=1000000",
        "-L",
        str(core_path.resolve()),
        str(rom_path.resolve()),
    ]


def snapshot_screenshot_files(directory: Path) -> dict[Path, tuple[int, int]]:
    return {
        path: (path.stat().st_mtime_ns, path.stat().st_size)
        for path in directory.iterdir()
        if path.suffix.lower() in (".png", ".bmp")
    }


def changed_screenshot_files(
    directory: Path,
    before: dict[Path, tuple[int, int]],
) -> list[Path]:
    current = snapshot_screenshot_files(directory)
    return [path for path, version in current.items() if before.get(path) != version]


def build_retroarch_config(
    work_directory: Path,
    command_port: int,
    remote_port: int,
    core_options_path: Path,
    base_config: str = "",
    frontend_options: dict[str, str] | None = None,
) -> str:
    work_directory = work_directory.resolve()
    settings = {
        "config_save_on_exit": "false",
        "network_cmd_enable": "true",
        "network_cmd_port": str(command_port),
        "network_remote_enable": "true",
        "network_remote_enable_user_p1": "true",
        "network_remote_base_port": str(remote_port),
        "core_options_path": str(core_options_path.resolve()),
        "global_core_options": "true",
        "game_specific_options": "false",
        "video_driver": "null",
        "audio_driver": "null",
        "input_driver": "null",
        "joypad_driver": "null",
        "menu_driver": "null",
        "screenshot_directory": str(work_directory / "screenshots"),
        "savestate_directory": str(work_directory / "states"),
        "savefile_directory": str(work_directory / "saves"),
        "system_directory": str(work_directory / "system"),
        "assets_directory": str(work_directory / "assets"),
        "cache_directory": str(work_directory / "cache"),
        "log_dir": str(work_directory / "logs"),
        "recording_output_directory": str(work_directory / "recordings"),
        "playlist_directory": str(work_directory / "playlists"),
        "runtime_log_directory": str(work_directory / "logs"),
        "input_remapping_directory": str(work_directory / "remaps"),
        "content_history_path": str(work_directory / "content_history.lpl"),
        "content_favorites_path": str(work_directory / "content_favorites.lpl"),
        "content_image_history_path": str(work_directory / "content_image_history.lpl"),
        "content_music_history_path": str(work_directory / "content_music_history.lpl"),
        "content_video_history_path": str(work_directory / "content_video_history.lpl"),
        "confirm_reset": "false",
        "confirm_quit": "false",
        "rewind_enable": "false",
        "run_ahead_enabled": "false",
        "pause_nonactive": "false",
        "video_vsync": "false",
        "video_threaded": "false",
        "autosave_interval": "0",
        "savestate_file_compression": "false",
        "savestate_thumbnail_enable": "false",
        "savestate_auto_save": "false",
        "savestate_auto_index": "false",
    }
    settings.update(frontend_options or {})
    base_lines = []
    for line in base_config.splitlines():
        match = re.match(r"\s*([A-Za-z0-9_]+)\s*=", line)
        if match is not None and match.group(1) in settings:
            continue
        base_lines.append(line)
    prefix = "\n".join(base_lines).rstrip() + "\n" if base_lines else ""
    return prefix + "\n".join(f'{name} = "{value}"' for name, value in settings.items()) + "\n"


class ConfigIntegrityGuard:
    def __init__(self, config_paths: Path | Sequence[Path]) -> None:
        paths = [config_paths] if isinstance(config_paths, Path) else list(config_paths)
        self.before_sha256 = {
            path.resolve(): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in paths
        }

    def verify_unchanged(self) -> None:
        changes = []
        for path, before in self.before_sha256.items():
            after = hashlib.sha256(path.read_bytes()).hexdigest()
            if after != before:
                changes.append(f"{path}: {before} -> {after}")
        if changes:
            raise AssertionError("persistent RetroArch config changed: " + "; ".join(changes))


class RetroArchNetworkSession:
    def __init__(
        self,
        launch_command: Sequence[str],
        core_path: Path,
        rom_path: Path,
        work_directory: Path,
        command_port: int,
        remote_port: int,
        core_options: dict[str, str] | None = None,
        base_config: str = "",
        frontend_options: dict[str, str] | None = None,
        frame_counter_addresses: tuple[int, int] | None = None,
    ) -> None:
        self.launch_command = list(launch_command)
        self.core_path = core_path.resolve()
        self.rom_path = rom_path.resolve()
        self.work_directory = work_directory.resolve()
        self.command_port = command_port
        self.remote_port = remote_port
        self.core_options = core_options or {}
        self.base_config = base_config
        self.frontend_options = frontend_options or {}
        self.frame_counter_addresses = frame_counter_addresses
        self.process: subprocess.Popen[str] | None = None
        self.log_file = None
        self.command_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.command_socket.settimeout(1.0)
        self.remote_socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def __enter__(self) -> "RetroArchNetworkSession":
        self.work_directory.mkdir(parents=True, exist_ok=True)
        for directory in (
            "screenshots",
            "states",
            "saves",
            "system",
            "assets",
            "cache",
            "logs",
            "recordings",
            "playlists",
            "remaps",
        ):
            (self.work_directory / directory).mkdir(exist_ok=True)
        core_options_path = self.work_directory / "core-options.cfg"
        core_options_path.write_text(
            "".join(f'{name} = "{value}"\n' for name, value in self.core_options.items()),
            encoding="utf-8",
        )
        config_path = self.work_directory / "retroarch.cfg"
        config_path.write_text(
            build_retroarch_config(
                self.work_directory,
                self.command_port,
                self.remote_port,
                core_options_path,
                self.base_config,
                self.frontend_options,
            ),
            encoding="utf-8",
        )
        log_path = self.work_directory / "retroarch.log"
        self.log_file = log_path.open("w", encoding="utf-8")
        self.process = subprocess.Popen(
            build_retroarch_command(
                self.launch_command,
                config_path,
                self.core_path,
                self.rom_path,
            ),
            cwd=ROOT,
            stdout=self.log_file,
            stderr=subprocess.STDOUT,
            text=True,
            start_new_session=True,
        )
        self.wait_until(
            lambda: self.status().startswith("GET_STATUS PLAYING"),
            timeout=20,
            description="RetroArch to start the isolated core",
        )
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        try:
            self.action("QUIT")
            if self.process is not None:
                self.process.wait(timeout=5)
        except (OSError, subprocess.TimeoutExpired):
            if self.process is not None and self.process.poll() is None:
                os.killpg(self.process.pid, signal.SIGTERM)
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    os.killpg(self.process.pid, signal.SIGKILL)
                    self.process.wait(timeout=3)
        finally:
            self.command_socket.close()
            self.remote_socket.close()
            if self.log_file is not None:
                self.log_file.close()

    def wait_until(
        self,
        predicate,
        timeout: float,
        description: str,
        interval: float = 0.01,
    ) -> None:
        deadline = time.monotonic() + timeout
        last_error: Exception | None = None
        while time.monotonic() < deadline:
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError(
                    f"RetroArch exited with code {self.process.returncode} while waiting for {description}."
                )
            try:
                if predicate():
                    return
            except (OSError, TimeoutError, ValueError) as error:
                last_error = error
            time.sleep(interval)
        suffix = f" Last error: {last_error}" if last_error is not None else ""
        raise TimeoutError(f"Timed out waiting for {description}.{suffix}")

    def query(self, command: str) -> str:
        self.command_socket.sendto(
            (command + "\n").encode("ascii"),
            ("127.0.0.1", self.command_port),
        )
        response, _ = self.command_socket.recvfrom(65535)
        return response.decode("ascii").strip()

    def action(self, command: str) -> None:
        self.command_socket.sendto(
            (command + "\n").encode("ascii"),
            ("127.0.0.1", self.command_port),
        )

    def status(self) -> str:
        return self.query("GET_STATUS")

    def set_paused(self, paused: bool) -> None:
        expected = "GET_STATUS PAUSED" if paused else "GET_STATUS PLAYING"
        if self.status().startswith(expected):
            return
        self.action("PAUSE_TOGGLE")
        self.wait_until(
            lambda: self.status().startswith(expected),
            timeout=3,
            description=f"RetroArch to become {'paused' if paused else 'playing'}",
        )

    def read(self, address: int, length: int) -> list[int]:
        response = self.query(f"READ_CORE_MEMORY {address:x} {length}")
        parts = response.split()
        if len(parts) != length + 2 or parts[0] != "READ_CORE_MEMORY":
            raise RuntimeError(f"Unexpected RetroArch memory response: {response}")
        return [int(value, 16) for value in parts[2:]]

    def write(self, address: int, values: Sequence[int]) -> None:
        encoded = " ".join(f"{value:02X}" for value in values)
        response = self.query(f"WRITE_CORE_MEMORY {address:x} {encoded}")
        parts = response.split()
        if parts[:2] != ["WRITE_CORE_MEMORY", f"{address:x}"] or parts[2:] != [str(len(values))]:
            raise RuntimeError(f"Unexpected RetroArch memory-write response: {response}")

    def fill_cpu_ram(self, pattern: bytes) -> None:
        if len(pattern) != 0x800:
            raise ValueError("CPU RAM pattern must contain exactly 2 KiB.")
        for start in range(0, len(pattern), 128):
            self.write(start, pattern[start : start + 128])

    def frame_counter(self) -> int:
        if self.frame_counter_addresses is None:
            raise RuntimeError("This core does not expose the runner frame counter through RetroArch.")
        low = self.read(self.frame_counter_addresses[0], 1)[0]
        high = self.read(self.frame_counter_addresses[1], 1)[0]
        return low | high << 8

    def advance_frame(self) -> None:
        if self.frame_counter_addresses is None:
            self.action("FRAMEADVANCE")
            self.query("GET_STATUS")
            time.sleep(0.001)
            return
        before = self.frame_counter()
        deadline = time.monotonic() + 2
        after = before
        while time.monotonic() < deadline and after == before:
            self.action("FRAMEADVANCE")
            retry_deadline = time.monotonic() + 0.05
            while time.monotonic() < retry_deadline:
                after = self.frame_counter()
                if after != before:
                    break
                time.sleep(0.001)
        if after == before:
            raise TimeoutError("Timed out waiting for one paused frontend frame.")
        if (after - before) & 0xFFFF != 1:
            raise RuntimeError(
                f"FRAMEADVANCE changed the NES hardware counter by {(after - before) & 0xFFFF}, expected 1."
            )
        time.sleep(0.003)

    def set_button(self, button_id: int, pressed: bool) -> None:
        packet = struct.pack(
            "<iiiiHxx",
            0,
            RETRO_DEVICE_JOYPAD,
            0,
            button_id,
            1 if pressed else 0,
        )
        self.remote_socket.sendto(packet, ("127.0.0.1", self.remote_port))

    def set_right(self, pressed: bool) -> None:
        self.set_button(RETRO_DEVICE_ID_JOYPAD_RIGHT, pressed)

    def capture_screen(self) -> Image.Image:
        screenshot_directory = self.work_directory / "screenshots"
        before = snapshot_screenshot_files(screenshot_directory)
        self.action("SCREENSHOT")

        created: list[Path] = []

        def screenshot_created() -> bool:
            nonlocal created
            created = changed_screenshot_files(screenshot_directory, before)
            if not created:
                return False
            newest = max(created, key=lambda path: path.stat().st_mtime_ns)
            try:
                with Image.open(newest) as image:
                    image.verify()
            except (OSError, SyntaxError):
                return False
            return True

        self.wait_until(
            screenshot_created,
            timeout=5,
            description="RetroArch to write an isolated screenshot",
        )
        with Image.open(max(created, key=lambda path: path.stat().st_mtime_ns)) as image:
            return image.convert("RGB").copy()

    def save_state(self) -> Path:
        state_directory = self.work_directory / "states"
        before = {path for path in state_directory.rglob("*.state")}
        created: list[Path] = []

        def state_created() -> bool:
            nonlocal created
            created = [
                path
                for path in state_directory.rglob("*.state")
                if path not in before and path.stat().st_size > 0
            ]
            return bool(created)

        # RetroArch 1.22.2 only consumes this hotkey command while its runloop
        # is playing. The caller measures the serialized runner frame counter
        # and reproduces the same release-frame count in both references.
        self.set_paused(False)
        try:
            self.action("SAVE_STATE")
            self.wait_until(
                state_created,
                timeout=10,
                description="RetroArch to save isolated core state",
                interval=0.001,
            )
        finally:
            self.set_paused(True)
        return max(created, key=lambda path: path.stat().st_mtime_ns)


class LibretroSession:
    """Minimal deterministic software-framebuffer frontend for a libretro core."""

    def __init__(
        self,
        core_path: Path,
        rom_path: Path,
        work_directory: Path,
        core_options: dict[str, str] | None = None,
    ) -> None:
        self.core_path = core_path.resolve()
        self.rom_path = rom_path.resolve()
        self.work_directory = work_directory.resolve()
        self.requested_core_options = core_options or {}
        self.option_values: dict[str, bytes] = {}
        self.option_buffers: dict[str, ctypes.Array] = {}
        self.pixel_format = RETRO_PIXEL_FORMAT_0RGB1555
        self.pressed_buttons: set[int] = set()
        self.shutdown_requested = False
        self.last_frame: Image.Image | None = None
        self._rom_buffer = None
        self._core = None
        self._callbacks: list[object] = []

    def __enter__(self) -> "LibretroSession":
        self.work_directory.mkdir(parents=True, exist_ok=True)
        system_directory = self.work_directory / "system"
        save_directory = self.work_directory / "saves"
        system_directory.mkdir(exist_ok=True)
        save_directory.mkdir(exist_ok=True)
        self._system_directory_buffer = ctypes.create_string_buffer(os.fsencode(system_directory))
        self._save_directory_buffer = ctypes.create_string_buffer(os.fsencode(save_directory))
        self._content_directory_buffer = ctypes.create_string_buffer(os.fsencode(self.rom_path.parent))

        core = ctypes.CDLL(str(self.core_path))
        self._core = core
        environment_type = ctypes.CFUNCTYPE(ctypes.c_bool, ctypes.c_uint, ctypes.c_void_p)
        video_type = ctypes.CFUNCTYPE(None, ctypes.c_void_p, ctypes.c_uint, ctypes.c_uint, ctypes.c_size_t)
        audio_type = ctypes.CFUNCTYPE(None, ctypes.c_int16, ctypes.c_int16)
        audio_batch_type = ctypes.CFUNCTYPE(ctypes.c_size_t, ctypes.POINTER(ctypes.c_int16), ctypes.c_size_t)
        input_poll_type = ctypes.CFUNCTYPE(None)
        input_state_type = ctypes.CFUNCTYPE(
            ctypes.c_int16,
            ctypes.c_uint,
            ctypes.c_uint,
            ctypes.c_uint,
            ctypes.c_uint,
        )
        self._environment_callback = environment_type(self._environment)
        self._video_callback = video_type(self._video_refresh)
        self._audio_callback = audio_type(lambda left, right: None)
        self._audio_batch_callback = audio_batch_type(lambda data, frames: frames)
        self._input_poll_callback = input_poll_type(lambda: None)
        self._input_state_callback = input_state_type(self._input_state)
        self._callbacks = [
            self._environment_callback,
            self._video_callback,
            self._audio_callback,
            self._audio_batch_callback,
            self._input_poll_callback,
            self._input_state_callback,
        ]

        core.retro_set_environment.argtypes = [environment_type]
        core.retro_set_video_refresh.argtypes = [video_type]
        core.retro_set_audio_sample.argtypes = [audio_type]
        core.retro_set_audio_sample_batch.argtypes = [audio_batch_type]
        core.retro_set_input_poll.argtypes = [input_poll_type]
        core.retro_set_input_state.argtypes = [input_state_type]
        core.retro_set_controller_port_device.argtypes = [ctypes.c_uint, ctypes.c_uint]
        core.retro_get_system_info.argtypes = [ctypes.POINTER(_RetroSystemInfo)]
        core.retro_load_game.argtypes = [ctypes.POINTER(_RetroGameInfo)]
        core.retro_load_game.restype = ctypes.c_bool
        core.retro_run.argtypes = []
        core.retro_serialize_size.argtypes = []
        core.retro_serialize_size.restype = ctypes.c_size_t
        core.retro_serialize.argtypes = [ctypes.c_void_p, ctypes.c_size_t]
        core.retro_serialize.restype = ctypes.c_bool
        core.retro_get_memory_data.argtypes = [ctypes.c_uint]
        core.retro_get_memory_data.restype = ctypes.c_void_p
        core.retro_get_memory_size.argtypes = [ctypes.c_uint]
        core.retro_get_memory_size.restype = ctypes.c_size_t
        core.retro_unload_game.argtypes = []
        core.retro_deinit.argtypes = []

        core.retro_set_environment(self._environment_callback)
        core.retro_set_video_refresh(self._video_callback)
        core.retro_set_audio_sample(self._audio_callback)
        core.retro_set_audio_sample_batch(self._audio_batch_callback)
        core.retro_set_input_poll(self._input_poll_callback)
        core.retro_set_input_state(self._input_state_callback)
        core.retro_init()
        info = _RetroSystemInfo()
        core.retro_get_system_info(ctypes.byref(info))
        rom_bytes = self.rom_path.read_bytes()
        self._rom_buffer = ctypes.create_string_buffer(rom_bytes)
        game_info = _RetroGameInfo(
            path=os.fsencode(self.rom_path),
            data=ctypes.cast(self._rom_buffer, ctypes.c_void_p),
            size=len(rom_bytes),
            meta=None,
        )
        if not core.retro_load_game(ctypes.byref(game_info)):
            core.retro_deinit()
            raise RuntimeError(f"Libretro core rejected ROM {self.rom_path}.")
        core.retro_set_controller_port_device(0, RETRO_DEVICE_JOYPAD)
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        if self._core is not None:
            self._core.retro_unload_game()
            self._core.retro_deinit()
            self._core = None

    def run_frame(self, pressed_buttons: Sequence[int] = ()) -> Image.Image:
        if self._core is None:
            raise RuntimeError("Libretro session is not active.")
        self.pressed_buttons = set(pressed_buttons)
        self._core.retro_run()
        if self.shutdown_requested:
            raise RuntimeError("Libretro core requested shutdown.")
        if self.last_frame is None:
            raise RuntimeError("Libretro core did not publish a software framebuffer.")
        return self.last_frame.copy()

    def serialize(self) -> bytes:
        if self._core is None:
            raise RuntimeError("Libretro session is not active.")
        size = int(self._core.retro_serialize_size())
        if size <= 0:
            raise RuntimeError("Libretro core does not expose a serializable state.")
        buffer = (ctypes.c_ubyte * size)()
        if not self._core.retro_serialize(buffer, size):
            raise RuntimeError("Libretro core failed to serialize its state.")
        return bytes(buffer)

    def read_system_ram(self) -> bytes:
        if self._core is None:
            raise RuntimeError("Libretro session is not active.")
        size = int(self._core.retro_get_memory_size(0))
        data = self._core.retro_get_memory_data(0)
        if size <= 0 or not data:
            raise RuntimeError("Libretro core does not expose RETRO_MEMORY_SYSTEM_RAM.")
        return ctypes.string_at(data, size)

    def _environment(self, command: int, data: int) -> bool:
        if command == RETRO_ENVIRONMENT_GET_OVERSCAN:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_bool))[0] = True
            return True
        if command == RETRO_ENVIRONMENT_GET_CAN_DUPE:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_bool))[0] = True
            return True
        if command == RETRO_ENVIRONMENT_SHUTDOWN:
            self.shutdown_requested = True
            return True
        if command in (
            RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL,
            RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS,
            RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME,
            RETRO_ENVIRONMENT_SET_CONTROLLER_INFO,
            RETRO_ENVIRONMENT_SET_MEMORY_MAPS,
            RETRO_ENVIRONMENT_SET_GEOMETRY,
            RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS,
            RETRO_ENVIRONMENT_SET_CONTENT_INFO_OVERRIDE,
        ):
            return True
        if command == RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_char_p))[0] = ctypes.cast(
                self._system_directory_buffer,
                ctypes.c_char_p,
            )
            return True
        if command == RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_char_p))[0] = ctypes.cast(
                self._save_directory_buffer,
                ctypes.c_char_p,
            )
            return True
        if command == RETRO_ENVIRONMENT_GET_CONTENT_DIRECTORY:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_char_p))[0] = ctypes.cast(
                self._content_directory_buffer,
                ctypes.c_char_p,
            )
            return True
        if command == RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
            requested = ctypes.cast(data, ctypes.POINTER(ctypes.c_int))[0]
            if requested not in (
                RETRO_PIXEL_FORMAT_0RGB1555,
                RETRO_PIXEL_FORMAT_XRGB8888,
                RETRO_PIXEL_FORMAT_RGB565,
            ):
                return False
            self.pixel_format = requested
            return True
        if command == RETRO_ENVIRONMENT_GET_VARIABLE:
            variable = ctypes.cast(data, ctypes.POINTER(_RetroVariable)).contents
            if not variable.key:
                return False
            key = variable.key.decode("utf-8")
            value = self.requested_core_options.get(key)
            if value is None:
                encoded = self.option_values.get(key)
            else:
                encoded = value.encode("utf-8")
            if encoded is None:
                return False
            buffer = self.option_buffers.setdefault(key, ctypes.create_string_buffer(encoded))
            variable.value = ctypes.cast(buffer, ctypes.c_char_p)
            return True
        if command == RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_bool))[0] = False
            return True
        if command == RETRO_ENVIRONMENT_SET_VARIABLES:
            self._remember_legacy_variables(data)
            return True
        if command in (RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2, RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL):
            self._remember_v2_options(command, data)
            return True
        if command in (RETRO_ENVIRONMENT_SET_CORE_OPTIONS, RETRO_ENVIRONMENT_SET_CORE_OPTIONS_INTL):
            return False
        if command == RETRO_ENVIRONMENT_GET_LANGUAGE:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_uint))[0] = 0
            return True
        if command == RETRO_ENVIRONMENT_GET_AUDIO_VIDEO_ENABLE:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_int))[0] = 3
            return True
        if command == RETRO_ENVIRONMENT_GET_FASTFORWARDING:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_bool))[0] = False
            return True
        if command == RETRO_ENVIRONMENT_GET_TARGET_REFRESH_RATE:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_float))[0] = 60.0
            return True
        if command == RETRO_ENVIRONMENT_GET_INPUT_BITMASKS:
            return True
        if command == RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION:
            ctypes.cast(data, ctypes.POINTER(ctypes.c_uint))[0] = 2
            return True
        if command in (RETRO_ENVIRONMENT_GET_VFS_INTERFACE, RETRO_ENVIRONMENT_GET_GAME_INFO_EXT):
            return False
        return False

    def _remember_legacy_variables(self, data: int) -> None:
        variables = ctypes.cast(data, ctypes.POINTER(_RetroVariable))
        index = 0
        while variables[index].key:
            key = variables[index].key.decode("utf-8")
            description = (variables[index].value or b"").decode("utf-8")
            choices = description.split(";", 1)[-1].strip().split("|")
            if choices and choices[0]:
                self.option_values[key] = choices[0].encode("utf-8")
            index += 1

    def _remember_v2_options(self, command: int, data: int) -> None:
        if command == RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL:
            international = ctypes.cast(data, ctypes.POINTER(_RetroCoreOptionsV2Intl)).contents
            options_pointer = international.us
        else:
            options_pointer = ctypes.cast(data, ctypes.POINTER(_RetroCoreOptionsV2))
        if not options_pointer:
            return
        definitions = options_pointer.contents.definitions
        index = 0
        while definitions and definitions[index].key:
            definition = definitions[index]
            key = definition.key.decode("utf-8")
            if definition.default_value:
                self.option_values[key] = bytes(definition.default_value)
            index += 1

    def _input_state(self, port: int, device: int, index: int, button_id: int) -> int:
        if port != 0 or device != RETRO_DEVICE_JOYPAD or index != 0:
            return 0
        if button_id == RETRO_DEVICE_ID_JOYPAD_MASK:
            return sum(1 << pressed for pressed in self.pressed_buttons)
        return 1 if button_id in self.pressed_buttons else 0

    def _video_refresh(self, data: int, width: int, height: int, pitch: int) -> None:
        if not data or data == ctypes.c_void_p(-1).value:
            return
        source = ctypes.string_at(data, pitch * height)
        if self.pixel_format == RETRO_PIXEL_FORMAT_XRGB8888:
            tight = b"".join(source[row * pitch : row * pitch + width * 4] for row in range(height))
            self.last_frame = Image.frombytes("RGB", (width, height), tight, "raw", "BGRX")
            return
        if self.pixel_format == RETRO_PIXEL_FORMAT_RGB565:
            decoder = "BGR;16"
        else:
            decoder = "BGR;15"
        tight = b"".join(source[row * pitch : row * pitch + width * 2] for row in range(height))
        self.last_frame = Image.frombytes("RGB", (width, height), tight, "raw", decoder)


class McpClient:
    def __init__(
        self,
        command: Sequence[str],
        work_directory: Path,
        environment: dict[str, str] | None = None,
    ) -> None:
        self.command = list(command)
        self.work_directory = work_directory.resolve()
        self.environment = environment or {}
        self.process: subprocess.Popen[str] | None = None
        self.request_id = 0
        self.stderr_file = None
        self.server_info: dict[str, object] = {}

    def __enter__(self) -> "McpClient":
        self.work_directory.mkdir(parents=True, exist_ok=True)
        self.stderr_file = (self.work_directory / "nes-mcp.log").open("w", encoding="utf-8")
        environment = os.environ.copy()
        environment.update(self.environment)
        self.process = subprocess.Popen(
            self.command,
            cwd=ROOT,
            env=environment,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=self.stderr_file,
            text=True,
            bufsize=1,
        )
        response = self.request(
            "initialize",
            {
                "protocolVersion": "2025-06-18",
                "capabilities": {},
                "clientInfo": {"name": "retrosharp-nes-visual-parity", "version": "1"},
            },
        )
        self.server_info = response["result"]["serverInfo"]
        self.notify("notifications/initialized", {})
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        if self.process is not None:
            if self.process.stdin is not None:
                self.process.stdin.close()
            try:
                self.process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                self.process.terminate()
                try:
                    self.process.wait(timeout=3)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=3)
        if self.stderr_file is not None:
            self.stderr_file.close()

    def notify(self, method: str, params: dict[str, object]) -> None:
        self._write({"jsonrpc": "2.0", "method": method, "params": params})

    def request(self, method: str, params: dict[str, object]) -> dict[str, object]:
        self.request_id += 1
        request_id = self.request_id
        self._write(
            {
                "jsonrpc": "2.0",
                "id": request_id,
                "method": method,
                "params": params,
            }
        )
        if self.process is None or self.process.stdout is None:
            raise RuntimeError("MCP server is not active.")
        while True:
            line = self.process.stdout.readline()
            if not line:
                code = self.process.poll()
                raise RuntimeError(f"Nes.Mcp exited before replying to {method}; exit code {code}.")
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                continue
            if message.get("id") != request_id:
                continue
            if "error" in message:
                raise RuntimeError(f"Nes.Mcp {method} failed: {message['error']}")
            return message

    def call_tool(self, name: str, arguments: dict[str, object]) -> list[dict[str, object]]:
        response = self.request(
            "tools/call",
            {"name": name, "arguments": arguments},
        )
        result = response["result"]
        if not isinstance(result, dict):
            raise RuntimeError(f"Unexpected Nes.Mcp result for {name}: {result}")
        if result.get("isError"):
            raise RuntimeError(f"Nes.Mcp tool {name} failed: {result.get('content')}")
        content = result.get("content", [])
        if not isinstance(content, list):
            raise RuntimeError(f"Unexpected Nes.Mcp content for {name}: {content}")
        return content

    def call_json(self, name: str, arguments: dict[str, object]) -> dict[str, object]:
        content = self.call_tool(name, arguments)
        for item in content:
            if item.get("type") == "text":
                return json.loads(str(item["text"]))
        raise RuntimeError(f"Nes.Mcp tool {name} returned no JSON text content.")

    def capture_screen(self) -> Image.Image:
        content = self.call_tool("capture_screen", {"includeMetadata": True})
        for item in content:
            if item.get("type") == "image":
                with Image.open(BytesIO(base64.b64decode(str(item["data"])))) as image:
                    return image.convert("RGB").copy()
        raise RuntimeError("Nes.Mcp capture_screen returned no image.")

    def _write(self, message: dict[str, object]) -> None:
        if self.process is None or self.process.stdin is None:
            raise RuntimeError("MCP server is not active.")
        self.process.stdin.write(json.dumps(message, separators=(",", ":")) + "\n")
        self.process.stdin.flush()
