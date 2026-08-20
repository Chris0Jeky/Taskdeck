#!/usr/bin/env python3
"""Collect bounded, content-free runner context around one test phase."""

from __future__ import annotations

import argparse
import ctypes
import json
import math
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
MAX_JSON_BYTES = 16_384
MAX_BYTES = 1 << 63
MAX_CPU_COUNT = 1_000_000
MAX_PHASE_SECONDS = 172_800.0
MAX_DOTNET_VERSION_LENGTH = 64
_MATRIX_OS_PATTERN = re.compile(r"^[A-Za-z0-9._-]{1,64}$")
_DOTNET_VERSION_PATTERN = re.compile(
    r"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$"
)
_SNAPSHOT_FIELDS = {
    "logicalCpuCount",
    "totalPhysicalMemoryBytes",
    "availablePhysicalMemoryBytes",
    "workspaceVolumeFreeBytes",
}
_CONTEXT_FIELDS = {
    "schemaVersion",
    "matrixOs",
    "dotnetSdkVersion",
    "before",
    "after",
    "testPhaseWallSeconds",
}
_STATE_FIELDS = {
    "schemaVersion",
    "matrixOs",
    "dotnetSdkVersion",
    "before",
    "startedMonotonicSeconds",
}


def _positive_int(value: object, maximum: int) -> int | None:
    if isinstance(value, bool) or not isinstance(value, int):
        return None
    if value < 0 or value > maximum:
        return None
    return value


def _windows_memory() -> tuple[int | None, int | None]:
    class MemoryStatus(ctypes.Structure):
        _fields_ = [
            ("dwLength", ctypes.c_ulong),
            ("dwMemoryLoad", ctypes.c_ulong),
            ("ullTotalPhys", ctypes.c_ulonglong),
            ("ullAvailPhys", ctypes.c_ulonglong),
            ("ullTotalPageFile", ctypes.c_ulonglong),
            ("ullAvailPageFile", ctypes.c_ulonglong),
            ("ullTotalVirtual", ctypes.c_ulonglong),
            ("ullAvailVirtual", ctypes.c_ulonglong),
            ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
        ]

    try:
        status = MemoryStatus()
        status.dwLength = ctypes.sizeof(status)
        if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
            return None, None
    except (AttributeError, OSError):
        return None, None
    return _positive_int(status.ullTotalPhys, MAX_BYTES), _positive_int(
        status.ullAvailPhys, MAX_BYTES
    )


def _linux_memory() -> tuple[int | None, int | None]:
    try:
        contents = Path("/proc/meminfo").read_text(encoding="ascii", errors="ignore")[:65536]
    except OSError:
        return None, None

    values: dict[str, int] = {}
    for line in contents.splitlines():
        match = re.fullmatch(r"(MemTotal|MemAvailable):\s+(\d+)\s+kB", line)
        if match:
            values[match.group(1)] = int(match.group(2)) * 1024
    return _positive_int(values.get("MemTotal"), MAX_BYTES), _positive_int(
        values.get("MemAvailable"), MAX_BYTES
    )


def _memory() -> tuple[int | None, int | None]:
    if os.name == "nt":
        return _windows_memory()
    if sys.platform.startswith("linux"):
        return _linux_memory()
    return None, None


def _dotnet_sdk_version() -> str | None:
    try:
        completed = subprocess.run(
            ["dotnet", "--version"],
            check=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            timeout=5,
        )
    except (OSError, subprocess.SubprocessError):
        return None

    lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
    if completed.returncode != 0 or len(lines) != 1:
        return None
    value = lines[0]
    if len(value) > MAX_DOTNET_VERSION_LENGTH or not _DOTNET_VERSION_PATTERN.fullmatch(value):
        return None
    return value


def collect_snapshot(workspace: Path) -> dict[str, int]:
    """Collect only allowed scalar runner metrics, omitting unavailable values."""

    snapshot: dict[str, int] = {}
    cpu_count = _positive_int(os.cpu_count(), MAX_CPU_COUNT)
    if cpu_count is not None:
        snapshot["logicalCpuCount"] = cpu_count
    total_memory, available_memory = _memory()
    if total_memory is not None:
        snapshot["totalPhysicalMemoryBytes"] = total_memory
    if available_memory is not None:
        snapshot["availablePhysicalMemoryBytes"] = available_memory
    try:
        free_space = _positive_int(shutil.disk_usage(workspace).free, MAX_BYTES)
    except OSError:
        free_space = None
    if free_space is not None:
        snapshot["workspaceVolumeFreeBytes"] = free_space
    validate_snapshot(snapshot)
    return snapshot


def validate_snapshot(snapshot: object) -> None:
    if not isinstance(snapshot, dict) or set(snapshot) - _SNAPSHOT_FIELDS:
        raise ValueError("runner snapshot schema is invalid")
    for value in snapshot.values():
        if _positive_int(value, MAX_BYTES) is None:
            raise ValueError("runner snapshot value is invalid")


def _validate_matrix_os(value: object) -> str:
    if not isinstance(value, str) or not _MATRIX_OS_PATTERN.fullmatch(value):
        raise ValueError("matrix OS is invalid")
    return value


def _validate_dotnet_sdk_version(value: object) -> str | None:
    if value is None:
        return None
    if (
        not isinstance(value, str)
        or len(value) > MAX_DOTNET_VERSION_LENGTH
        or not _DOTNET_VERSION_PATTERN.fullmatch(value)
    ):
        raise ValueError("dotnet SDK version is invalid")
    return value


def _validate_phase_seconds(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError("test phase wall seconds are invalid")
    result = float(value)
    if not math.isfinite(result) or result < 0 or result > MAX_PHASE_SECONDS:
        raise ValueError("test phase wall seconds are invalid")
    return round(result, 6)


def _validate_monotonic_seconds(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError("runner context monotonic timestamp is invalid")
    result = float(value)
    if not math.isfinite(result) or result < 0 or result > 1_000_000_000_000.0:
        raise ValueError("runner context monotonic timestamp is invalid")
    return result


def validate_context(context: object) -> None:
    if not isinstance(context, dict) or set(context) != _CONTEXT_FIELDS:
        raise ValueError("runner context schema is invalid")
    if context.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("runner context schema version is invalid")
    _validate_matrix_os(context.get("matrixOs"))
    _validate_dotnet_sdk_version(context.get("dotnetSdkVersion"))
    validate_snapshot(context.get("before"))
    validate_snapshot(context.get("after"))
    _validate_phase_seconds(context.get("testPhaseWallSeconds"))


def _write_json(path: Path, value: dict[str, Any]) -> None:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n"
    if len(encoded.encode("utf-8")) > MAX_JSON_BYTES:
        raise ValueError("runner context exceeds the bounded JSON size")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(encoded, encoding="utf-8")


def _read_state(path: Path) -> dict[str, Any]:
    raw = path.read_bytes()
    if len(raw) > MAX_JSON_BYTES:
        raise ValueError("runner context state exceeds the bounded JSON size")
    try:
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("runner context state is invalid") from error
    if not isinstance(value, dict) or set(value) != _STATE_FIELDS:
        raise ValueError("runner context state schema is invalid")
    if value.get("schemaVersion") != SCHEMA_VERSION:
        raise ValueError("runner context state schema version is invalid")
    _validate_matrix_os(value.get("matrixOs"))
    _validate_dotnet_sdk_version(value.get("dotnetSdkVersion"))
    validate_snapshot(value.get("before"))
    _validate_monotonic_seconds(value.get("startedMonotonicSeconds"))
    return value


def begin_context(*, state_path: Path, matrix_os: str, workspace: Path) -> None:
    state = {
        "schemaVersion": SCHEMA_VERSION,
        "matrixOs": _validate_matrix_os(matrix_os),
        "dotnetSdkVersion": _dotnet_sdk_version(),
        "before": collect_snapshot(workspace),
        "startedMonotonicSeconds": _validate_monotonic_seconds(time.monotonic()),
    }
    _write_json(state_path, state)


def finalize_context(*, state_path: Path, output_path: Path, workspace: Path) -> None:
    state = _read_state(state_path)
    context = {
        "schemaVersion": SCHEMA_VERSION,
        "matrixOs": state["matrixOs"],
        "dotnetSdkVersion": state["dotnetSdkVersion"],
        "before": state["before"],
        "after": collect_snapshot(workspace),
        "testPhaseWallSeconds": _validate_phase_seconds(
            time.monotonic() - state["startedMonotonicSeconds"]
        ),
    }
    validate_context(context)
    _write_json(output_path, context)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    begin = commands.add_parser("begin")
    begin.add_argument("--state", required=True, type=Path)
    begin.add_argument("--matrix-os", required=True)
    begin.add_argument("--workspace", required=True, type=Path)
    finalize = commands.add_parser("finalize")
    finalize.add_argument("--state", required=True, type=Path)
    finalize.add_argument("--output", required=True, type=Path)
    finalize.add_argument("--workspace", required=True, type=Path)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if args.command == "begin":
            begin_context(
                state_path=args.state, matrix_os=args.matrix_os, workspace=args.workspace
            )
        else:
            finalize_context(
                state_path=args.state, output_path=args.output, workspace=args.workspace
            )
    except (OSError, ValueError):
        print("Runner context collection failed.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
