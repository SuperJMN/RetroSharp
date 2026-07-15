"""Load and validate the compiler-generated NES runtime ABI sidecar."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
from typing import Any


CONTRACT = "retrosharp.nes.runtime-abi"
VERSION = 1

REQUIRED_ADDRESSES = frozenset(
    {
        "camera.X",
        "camera.Y",
        "camera.XHigh",
        "camera.YHigh",
        "packed camera.FrameCounterLow",
        "packed camera.FrameCounterHigh",
        "packed camera.RequestCount",
        "packed camera.PrepareCount",
        "packed camera.ResidentCount",
        "packed camera.CommitCount",
        "packed camera.ReleaseCount",
        "packed camera.BankWorkInCommit",
        "packed camera.DirectoryWorkInCommit",
        "packed camera.DecodeWorkInCommit",
        "packed camera.LastTileWrites",
        "packed camera.LastAttributeWrites",
        "packed camera.CriticalSection",
        "packed camera.SelectedSlot",
        "packed camera.CommitAxis",
        "packed camera.CommitDirection",
        "packed camera.CommitTarget",
        "packed camera.CommitPayloadLength",
        "packed camera.CommitTargetStart",
        "packed camera.FramePending",
        "packed camera.Slot0",
        "packed camera.Slot1",
        "packed camera.Status",
        "packed camera.PendingAxes",
        "packed camera.VisibleCameraXLow",
        "packed camera.VisibleCameraXHigh",
        "packed camera.VisibleCameraYLow",
        "packed camera.VisibleCameraYHigh",
        "WorldPack.ValidationState",
        "WorldPack.VisualCache0Valid",
        "WorldPack.BulkReadActive",
        "WorldPack.BulkReadCurrentBank",
        "WorldPack.CollisionCache0Valid",
        "WorldPack.CollisionDecodeCountLow",
        "WorldPack.CollisionDecodeCountHigh",
        "WorldPack.GameplayTickCount",
        "WorldPack.AudioTickCount",
        "WorldPack.CollisionCellResult",
    }
)
REQUIRED_RANGES = frozenset(
    {
        "WorldPackScalarState",
        "PackedCameraAndWorldPackAuxiliaryState",
        "WorldPackStaging",
    }
)
REQUIRED_REGIONS = frozenset(
    f"WorldPack.VisualSlot{index}"
    for index in range(6)
)
REQUIRED_CONSTANTS = frozenset(
    {
        "packed camera.Empty",
        "packed camera.Released",
        "packed camera.NoSlot",
        "packed camera.SlotMetadataBytes",
        "WorldPack.MaximumStagingBytes",
    }
)
REQUIRED_VARIABLES = frozenset({"player.x", "player.y"})


class NesRuntimeAbiError(RuntimeError):
    """The ABI sidecar cannot safely describe the selected ROM."""


@dataclass(frozen=True)
class NesRuntimeAbiRange:
    id: str
    name: str
    start: int
    length: int

    @property
    def end_exclusive(self) -> int:
        return self.start + self.length


@dataclass(frozen=True)
class NesRuntimeAbiVariable:
    name: str
    type: str
    address: int
    size: int

    @property
    def end_exclusive(self) -> int:
        return self.address + self.size


@dataclass(frozen=True)
class NesRuntimeAbiRegion:
    name: str
    start: int
    length: int
    owner: str

    @property
    def end_exclusive(self) -> int:
        return self.start + self.length


@dataclass(frozen=True)
class NesRuntimeAbi:
    path: Path
    rom_path: Path
    abi_fingerprint: str
    rom_sha256: str
    _addresses: dict[str, int]
    _ranges: dict[str, NesRuntimeAbiRange]
    _constants: dict[str, int]
    _regions: dict[str, NesRuntimeAbiRegion]
    _variables: dict[str, NesRuntimeAbiVariable]

    @classmethod
    def load(cls, path: Path, rom_path: Path) -> "NesRuntimeAbi":
        path = path.resolve()
        rom_path = rom_path.resolve()
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
        except FileNotFoundError as error:
            raise NesRuntimeAbiError(f"NES runtime ABI projection was not found: {path}") from error
        except json.JSONDecodeError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI projection '{path}' is not valid JSON: {error}"
            ) from error
        if not isinstance(raw, dict):
            raise NesRuntimeAbiError(f"NES runtime ABI projection '{path}' must be a JSON object.")

        if raw.get("contract") != CONTRACT:
            raise NesRuntimeAbiError(
                f"unsupported NES runtime ABI contract {raw.get('contract')!r}; expected {CONTRACT!r}."
            )
        version = raw.get("version")
        if version != VERSION:
            raise NesRuntimeAbiError(
                f"unsupported NES runtime ABI version {version}; expected {VERSION}."
            )
        if raw.get("target") != "nes":
            raise NesRuntimeAbiError(
                f"NES runtime ABI projection targets {raw.get('target')!r}; expected 'nes'."
            )

        fingerprint = cls._sha256_field(raw, "abiFingerprint")
        layout_payload = {
            "ranges": raw.get("ranges"),
            "addresses": raw.get("addresses"),
            "rangeAliases": raw.get("rangeAliases"),
            "addressAliases": raw.get("addressAliases"),
            "constants": raw.get("constants"),
            "runtimeRegions": raw.get("runtimeRegions"),
            "userVariables": raw.get("userVariables"),
        }
        actual_fingerprint = hashlib.sha256(
            json.dumps(
                layout_payload,
                separators=(",", ":"),
                ensure_ascii=True,
            ).encode("utf-8")
        ).hexdigest()
        if actual_fingerprint != fingerprint:
            raise NesRuntimeAbiError(
                f"NES runtime ABI fingerprint does not match its projected layout: "
                f"expected {fingerprint}, actual {actual_fingerprint}."
            )
        expected_rom_hash = cls._sha256_field(raw, "romSha256")
        try:
            actual_rom_hash = hashlib.sha256(rom_path.read_bytes()).hexdigest()
        except FileNotFoundError as error:
            raise NesRuntimeAbiError(f"NES ROM was not found: {rom_path}") from error
        if actual_rom_hash != expected_rom_hash:
            raise NesRuntimeAbiError(
                f"NES runtime ABI projection '{path}' does not match ROM '{rom_path}': "
                f"expected {expected_rom_hash}, actual {actual_rom_hash}."
            )

        ranges = cls._load_ranges(raw.get("ranges"))
        addresses = cls._load_addresses(raw.get("addresses"), ranges)
        constants = cls._load_constants(raw.get("constants"))
        regions = cls._load_regions(raw.get("runtimeRegions"), ranges)
        variables = cls._load_variables(raw.get("userVariables"), ranges)
        cls._require(REQUIRED_RANGES, ranges, "range")
        cls._require(REQUIRED_ADDRESSES, addresses, "address")
        cls._require(REQUIRED_CONSTANTS, constants, "constant")
        cls._require(REQUIRED_REGIONS, regions, "runtime region")
        cls._require(REQUIRED_VARIABLES, variables, "user variable")
        for name in REQUIRED_VARIABLES:
            if variables[name].size != 2:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI user variable '{name}' must occupy exactly two bytes."
                )
        return cls(
            path,
            rom_path,
            fingerprint,
            actual_rom_hash,
            addresses,
            ranges,
            constants,
            regions,
            variables,
        )

    def address(self, qualified_name: str) -> int:
        try:
            return self._addresses[qualified_name]
        except KeyError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required address '{qualified_name}'."
            ) from error

    def range(self, range_id: str) -> NesRuntimeAbiRange:
        try:
            return self._ranges[range_id]
        except KeyError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required range '{range_id}'."
            ) from error

    def constant(self, name: str) -> int:
        try:
            return self._constants[name]
        except KeyError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required constant '{name}'."
            ) from error

    def region(self, name: str) -> NesRuntimeAbiRegion:
        try:
            return self._regions[name]
        except KeyError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required runtime region '{name}'."
            ) from error

    def variable(self, name: str) -> NesRuntimeAbiVariable:
        try:
            return self._variables[name]
        except KeyError as error:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required user variable '{name}'."
            ) from error

    @staticmethod
    def _sha256_field(raw: dict[str, Any], name: str) -> str:
        value = raw.get(name)
        if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
            raise NesRuntimeAbiError(
                f"NES runtime ABI field '{name}' must be a lowercase SHA-256 digest."
            )
        return value

    @staticmethod
    def _array(value: Any, name: str) -> list[dict[str, Any]]:
        if not isinstance(value, list) or not all(isinstance(item, dict) for item in value):
            raise NesRuntimeAbiError(f"NES runtime ABI field '{name}' must be an array of objects.")
        return value

    @classmethod
    def _load_ranges(cls, value: Any) -> dict[str, NesRuntimeAbiRange]:
        result: dict[str, NesRuntimeAbiRange] = {}
        for item in cls._array(value, "ranges"):
            range_id = item.get("id")
            name = item.get("name")
            start = item.get("start")
            length = item.get("length")
            if not isinstance(range_id, str) or not isinstance(name, str):
                raise NesRuntimeAbiError("NES runtime ABI range id/name must be strings.")
            if range_id in result:
                raise NesRuntimeAbiError(f"NES runtime ABI has duplicate range '{range_id}'.")
            cls._validate_span(f"range '{range_id}'", start, length)
            result[range_id] = NesRuntimeAbiRange(range_id, name, start, length)
        return result

    @classmethod
    def _load_addresses(
        cls,
        value: Any,
        ranges: dict[str, NesRuntimeAbiRange],
    ) -> dict[str, int]:
        result: dict[str, int] = {}
        for item in cls._array(value, "addresses"):
            domain = item.get("domain")
            name = item.get("name")
            address = item.get("address")
            owner = item.get("owner")
            if not isinstance(domain, str) or not isinstance(name, str):
                raise NesRuntimeAbiError("NES runtime ABI address domain/name must be strings.")
            key = f"{domain}.{name}"
            if key in result:
                raise NesRuntimeAbiError(f"NES runtime ABI has duplicate address field '{key}'.")
            cls._validate_address(f"address '{key}'", address)
            if owner not in ranges:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI address '{key}' names unknown owner range {owner!r}."
                )
            if not ranges[owner].start <= address < ranges[owner].end_exclusive:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI address '{key}' is outside owner range '{owner}'."
                )
            result[key] = address
        return result

    @classmethod
    def _load_constants(cls, value: Any) -> dict[str, int]:
        result: dict[str, int] = {}
        for item in cls._array(value, "constants"):
            name = item.get("name")
            number = item.get("value")
            if not isinstance(name, str) or not isinstance(number, int):
                raise NesRuntimeAbiError("NES runtime ABI constant name/value is invalid.")
            if name in result:
                raise NesRuntimeAbiError(f"NES runtime ABI has duplicate constant '{name}'.")
            result[name] = number
        return result

    @classmethod
    def _load_regions(
        cls,
        value: Any,
        ranges: dict[str, NesRuntimeAbiRange],
    ) -> dict[str, NesRuntimeAbiRegion]:
        result: dict[str, NesRuntimeAbiRegion] = {}
        for item in cls._array(value, "runtimeRegions"):
            name = item.get("name")
            start = item.get("start")
            length = item.get("length")
            owner = item.get("owner")
            if not isinstance(name, str) or not isinstance(owner, str):
                raise NesRuntimeAbiError(
                    "NES runtime ABI region name/owner must be strings."
                )
            if name in result:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI has duplicate runtime region '{name}'."
                )
            cls._validate_span(f"runtime region '{name}'", start, length)
            if owner not in ranges:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI region '{name}' names unknown owner range {owner!r}."
                )
            if start < ranges[owner].start or start + length > ranges[owner].end_exclusive:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI region '{name}' is outside owner range '{owner}'."
                )
            result[name] = NesRuntimeAbiRegion(name, start, length, owner)
        return result

    @classmethod
    def _load_variables(
        cls,
        value: Any,
        ranges: dict[str, NesRuntimeAbiRange],
    ) -> dict[str, NesRuntimeAbiVariable]:
        result: dict[str, NesRuntimeAbiVariable] = {}
        user_locals = ranges.get("UserLocals")
        if user_locals is None:
            raise NesRuntimeAbiError("NES runtime ABI is missing required range 'UserLocals'.")
        for item in cls._array(value, "userVariables"):
            name = item.get("name")
            variable_type = item.get("type")
            address = item.get("address")
            size = item.get("size")
            if not isinstance(name, str) or not isinstance(variable_type, str):
                raise NesRuntimeAbiError("NES runtime ABI user variable name/type must be strings.")
            if name in result:
                raise NesRuntimeAbiError(f"NES runtime ABI has duplicate user variable '{name}'.")
            cls._validate_span(f"user variable '{name}'", address, size)
            if address < user_locals.start or address + size > user_locals.end_exclusive:
                raise NesRuntimeAbiError(
                    f"NES runtime ABI user variable '{name}' is outside range 'UserLocals'."
                )
            result[name] = NesRuntimeAbiVariable(name, variable_type, address, size)
        return result

    @staticmethod
    def _validate_address(label: str, address: Any) -> None:
        if not isinstance(address, int) or isinstance(address, bool) or not 0 <= address <= 0xFFFF:
            raise NesRuntimeAbiError(f"NES runtime ABI {label} is outside 0..65535.")

    @classmethod
    def _validate_span(cls, label: str, start: Any, length: Any) -> None:
        cls._validate_address(f"{label} start", start)
        if not isinstance(length, int) or isinstance(length, bool) or length <= 0:
            raise NesRuntimeAbiError(f"NES runtime ABI {label} length must be positive.")
        if start + length > 0x10000:
            raise NesRuntimeAbiError(f"NES runtime ABI {label} is outside 0..65535.")

    @staticmethod
    def _require(required: frozenset[str], values: dict[str, Any], kind: str) -> None:
        missing = sorted(required - values.keys())
        if missing:
            raise NesRuntimeAbiError(
                f"NES runtime ABI is missing required {kind} '{missing[0]}'."
            )
