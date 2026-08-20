#!/usr/bin/env python3
"""Windows post-ZIP acceptance for the marked Taskdeck desktop package.

The harness keeps raw application output in a pipe only long enough to recognize stable desktop
markers. It persists a strict whitelist of synthetic evidence and never writes configuration,
credentials, provider payloads, or application logs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import queue
import re
import shutil
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
from typing import Any, Mapping


READY_PATTERN = re.compile(r"^TASKDECK_DESKTOP_READY url=(http://127\.0\.0\.1:([1-9]\d{0,4}))$")
SAFE_ROOT_PREFIX = "taskdeck-desktop-acceptance-"
EVIDENCE_SCHEMA_VERSION = 2
MAX_PROBE_LATENCY_MS = 300_000
OPERATOR_KEY_ENV_NAMES = (
    "Llm__OpenAi__ApiKey",
    "TASKDECK_RELEASE_OPENAI_API_KEY",
)

APP_ENV_PREFIXES_TO_REMOVE = (
    "ASPNETCORE_URLS",
    "ASPNETCORE_HTTP_PORTS",
    "ASPNETCORE_HTTPS_PORTS",
    "CONNECTIONSTRINGS__",
    "CONNECTORS__",
    "DOTNET_ENVIRONMENT",
    "DOTNET_HTTP_PORTS",
    "DOTNET_HTTPS_PORTS",
    "DOTNET_RUNNING_IN_CONTAINER",
    "DOTNET_RUNNING_IN_CONTAINERS",
    "FIRSTRUN__",
    "JWT__",
    "KESTREL__",
    "LLM__",
    "OPENAI_API_KEY",
    "GEMINI_API_KEY",
    "TASKDECK_",
)

PLAYWRIGHT_SECRET_ENV_NAMES = {
    "LLM__OPENAI__APIKEY",
    "OPENAI_API_KEY",
    "TASKDECK_DEMO_OPENAI_API_KEY",
    "TASKDECK_LLM_GEMINI_API_KEY",
    "TASKDECK_RELEASE_OPENAI_API_KEY",
}

FORBIDDEN_EVIDENCE_KEYS = re.compile(
    r"(?:api.?key|authorization|config|credential|email|error|header|log|password|prompt|response|"
    r"screenshot|secret|token|tool.?arg|trace|username)$",
    re.IGNORECASE,
)


class AcceptanceFailure(RuntimeError):
    """Sanitized acceptance failure; messages never include raw process/provider data."""


class ProcessMonitor:
    def __init__(self, process: subprocess.Popen[str]) -> None:
        self.process = process
        self.markers: queue.Queue[str] = queue.Queue()
        self.seen_markers: set[str] = set()
        self._thread = threading.Thread(target=self._drain, daemon=True)
        self._thread.start()

    def _drain(self) -> None:
        assert self.process.stdout is not None
        for raw_line in self.process.stdout:
            line = raw_line.rstrip("\r\n")
            if line.startswith("TASKDECK_DESKTOP_"):
                marker_name = line.split(maxsplit=1)[0]
                self.seen_markers.add(marker_name)
                self.markers.put(line)

    def wait_for_ready(self, timeout_seconds: float = 120.0) -> tuple[str, int]:
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise AcceptanceFailure("The packaged process exited before readiness.")
            try:
                marker = self.markers.get(timeout=min(0.25, deadline - time.monotonic()))
            except queue.Empty:
                continue
            if marker.startswith("TASKDECK_DESKTOP_FATAL"):
                raise AcceptanceFailure("The packaged process reported a redacted startup failure.")
            match = READY_PATTERN.fullmatch(marker)
            if match:
                port = int(match.group(2))
                if not 1 <= port <= 65535:
                    raise AcceptanceFailure("The packaged process reported an invalid loopback port.")
                return match.group(1), port
        raise AcceptanceFailure("The packaged process did not report readiness within the bounded wait.")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def verify_checksum(archive: Path, checksum_path: Path) -> str:
    lines = [line.strip() for line in checksum_path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if len(lines) != 1:
        raise AcceptanceFailure("The checksum file must contain exactly one non-empty record.")
    match = re.fullmatch(r"([0-9a-fA-F]{64})\s+\*?(.+)", lines[0])
    if not match or Path(match.group(2)).name != archive.name:
        raise AcceptanceFailure("The checksum record does not identify the exact archive.")
    expected = match.group(1).lower()
    actual = sha256_file(archive)
    if actual != expected:
        raise AcceptanceFailure("The archive SHA256 does not match its checksum record.")
    return actual


def safe_extract_archive(archive: Path, destination: Path) -> None:
    destination.mkdir(parents=True, exist_ok=False)
    destination_root = destination.resolve()
    with zipfile.ZipFile(archive) as package:
        for member in package.infolist():
            normalized = member.filename.replace("\\", "/")
            relative = Path(normalized)
            if (
                relative.is_absolute()
                or relative.drive
                or any(part in {"", ".", ".."} for part in relative.parts)
            ):
                raise AcceptanceFailure("The archive contains an unsafe path.")
            unix_mode = member.external_attr >> 16
            if stat.S_ISLNK(unix_mode):
                raise AcceptanceFailure("The archive contains a symbolic link.")
            target = (destination_root / relative).resolve()
            if destination_root not in target.parents and target != destination_root:
                raise AcceptanceFailure("The archive entry escapes the extraction root.")
        package.extractall(destination_root)


def snapshot_tree(root: Path) -> dict[str, str]:
    if not root.exists():
        return {}
    return {
        path.relative_to(root).as_posix(): sha256_file(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def assert_tree_unchanged(before: Mapping[str, str], root: Path, description: str) -> None:
    if dict(before) != snapshot_tree(root):
        raise AcceptanceFailure(f"The {description} was mutated by packaged execution.")


def _remove_environment_prefixes(
    environment: Mapping[str, str],
    prefixes: tuple[str, ...],
) -> dict[str, str]:
    result: dict[str, str] = {}
    for key, value in environment.items():
        upper = key.upper()
        if any(upper == prefix or upper.startswith(prefix) for prefix in prefixes):
            continue
        result[key] = value
    return result


def resolve_operator_key(environment: Mapping[str, str]) -> str | None:
    case_insensitive = {key.upper(): value for key, value in environment.items()}
    for name in OPERATOR_KEY_ENV_NAMES:
        candidate = case_insensitive.get(name.upper())
        if candidate and candidate.strip():
            return candidate.strip()
    return None


def build_app_environment(
    source: Mapping[str, str],
    local_app_data: Path,
    operator_key: str | None,
) -> dict[str, str]:
    if not local_app_data.is_absolute():
        raise AcceptanceFailure("LOCALAPPDATA isolation must be absolute.")
    environment = _remove_environment_prefixes(source, APP_ENV_PREFIXES_TO_REMOVE)
    environment = {key: value for key, value in environment.items() if key.upper() != "CI"}
    environment["CI"] = "true"
    environment["LOCALAPPDATA"] = str(local_app_data)
    if operator_key:
        environment["Llm__EnableLiveProviders"] = "true"
        environment["Llm__Provider"] = "OpenAI"
        environment["Llm__OpenAi__ApiKey"] = operator_key
    return environment


def build_playwright_environment(
    source: Mapping[str, str],
    *,
    base_url: str,
    evidence_path: Path,
    journey_id: str,
    phase: str,
    live_openai: bool,
    live_skip_reason: str,
) -> dict[str, str]:
    environment = _remove_environment_prefixes(
        source,
        (
            "LLM__",
            "OPENAI_API_KEY",
            "TASKDECK_DEMO_OPENAI_API_KEY",
            "TASKDECK_LLM_GEMINI_API_KEY",
            "TASKDECK_RELEASE_OPENAI_API_KEY",
            "TASKDECK_PACKAGED_",
        ),
    )
    environment.update(
        {
            "TASKDECK_PACKAGED_BASE_URL": base_url,
            "TASKDECK_E2E_API_BASE_URL": f"{base_url}/api",
            "TASKDECK_PACKAGED_EVIDENCE_PATH": str(evidence_path),
            "TASKDECK_PACKAGED_FAILURE_PATH": str(evidence_path.with_suffix(".failure.json")),
            "TASKDECK_PACKAGED_JOURNEY_ID": journey_id,
            "TASKDECK_PACKAGED_JOURNEY_PHASE": phase,
            "TASKDECK_PACKAGED_LIVE_OPENAI": "1" if live_openai else "0",
            "TASKDECK_PACKAGED_LIVE_OPENAI_SKIP_REASON": live_skip_reason,
            "TASKDECK_PACKAGED_OPENAI_MODEL": "gpt-5.6-luna",
        }
    )
    assert_playwright_environment_is_key_free(environment)
    return environment


def assert_playwright_environment_is_key_free(environment: Mapping[str, str]) -> None:
    for key, value in environment.items():
        if key.upper() in PLAYWRIGHT_SECRET_ENV_NAMES and value.strip():
            raise AcceptanceFailure("The Playwright child environment contains a provider key.")


def start_packaged_process(executable: Path, cwd: Path, environment: Mapping[str, str]) -> ProcessMonitor:
    if not executable.is_absolute() or not executable.is_file():
        raise AcceptanceFailure("The packaged executable path is not an absolute file.")
    creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    process = subprocess.Popen(
        [str(executable)],
        cwd=str(cwd),
        env=dict(environment),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=creation_flags,
    )
    return ProcessMonitor(process)


def stop_packaged_process(monitor: ProcessMonitor, require_clean: bool = True) -> None:
    process = monitor.process
    if process.poll() is None:
        try:
            process.send_signal(signal.CTRL_BREAK_EVENT)
            process.wait(timeout=30)
        except (AttributeError, OSError, subprocess.TimeoutExpired):
            _terminate_tracked_process_tree(process)
            if require_clean:
                raise AcceptanceFailure("The packaged process did not stop cleanly after Ctrl+Break.")
    if require_clean:
        if process.returncode not in (0, None):
            raise AcceptanceFailure("The packaged process returned a non-zero code after Ctrl+Break.")
        required_markers = {"TASKDECK_DESKTOP_SHUTTING_DOWN", "TASKDECK_DESKTOP_STOPPED"}
        deadline = time.monotonic() + 2
        while time.monotonic() < deadline and not required_markers.issubset(monitor.seen_markers):
            time.sleep(0.05)
        if not required_markers.issubset(monitor.seen_markers):
            raise AcceptanceFailure("The packaged process did not report its clean shutdown markers.")


def _terminate_tracked_process_tree(process: subprocess.Popen[str]) -> None:
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill.exe", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=15,
        )
    else:
        process.kill()
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        pass


def request_health_and_spa(base_url: str) -> list[dict[str, Any]]:
    evidence: list[dict[str, Any]] = []
    for path, expect_html in (("/health/ready", False), ("/", True)):
        request = urllib.request.Request(f"{base_url}{path}", method="GET")
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                status_code = response.status
                body = response.read(65536) if expect_html else b""
        except (urllib.error.URLError, TimeoutError):
            raise AcceptanceFailure(f"GET {path} failed for the packaged process.") from None
        evidence.append({"method": "GET", "path": path, "status": status_code})
        if status_code != 200:
            raise AcceptanceFailure(f"GET {path} did not return HTTP 200.")
        if expect_html and b"<div id=\"app\"" not in body and b"<!doctype html" not in body.lower():
            raise AcceptanceFailure("GET / did not return the packaged SPA shell.")
        if expect_html:
            script_match = re.search(rb'<script[^>]+src="(/assets/[^"?]+\.js)"', body)
            if not script_match:
                raise AcceptanceFailure("The packaged SPA shell did not identify its application bundle.")
            bundle_path = script_match.group(1).decode("ascii")
            try:
                with urllib.request.urlopen(f"{base_url}{bundle_path}", timeout=15) as bundle_response:
                    bundle_status = bundle_response.status
                    bundle_prefix = bundle_response.read(32)
            except (urllib.error.URLError, TimeoutError):
                raise AcceptanceFailure("The packaged SPA application bundle could not be loaded.") from None
            if bundle_status != 200 or not bundle_prefix:
                raise AcceptanceFailure("The packaged SPA application bundle was invalid.")
            evidence.append({"method": "GET", "path": "/assets/application.js", "status": bundle_status})
    return evidence


def run_playwright(frontend_directory: Path, environment: Mapping[str, str]) -> None:
    command = [
        "npx.cmd" if os.name == "nt" else "npx",
        "playwright",
        "test",
        "--config",
        "playwright.packaged-desktop.config.ts",
    ]
    try:
        result = subprocess.run(
            command,
            cwd=str(frontend_directory),
            env=dict(environment),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=180,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        raise AcceptanceFailure("The sanitized packaged Playwright journey could not complete.") from None
    if result.returncode != 0:
        checkpoint = "unknown"
        http_status: int | None = None
        try:
            failure_path = Path(environment["TASKDECK_PACKAGED_FAILURE_PATH"])
            failure = json.loads(failure_path.read_text(encoding="utf-8"))
            candidate = failure.get("checkpoint") if isinstance(failure, dict) else None
            if isinstance(candidate, str) and re.fullmatch(r"[a-z_]{3,40}", candidate):
                checkpoint = candidate
            status_candidate = failure.get("httpStatus") if isinstance(failure, dict) else None
            if isinstance(status_candidate, int) and 100 <= status_candidate <= 599:
                http_status = status_candidate
        except Exception:
            pass
        status_label = f" (HTTP {http_status})" if http_status is not None else ""
        raise AcceptanceFailure(
            f"The sanitized packaged Playwright journey failed at checkpoint {checkpoint}{status_label}.")


def assert_data_isolated(temp_root: Path, local_app_data: Path) -> None:
    taskdeck_data = (local_app_data / "Taskdeck").resolve()
    local_config = taskdeck_data / "appsettings.local.json"
    database = taskdeck_data / "taskdeck.db"
    if not local_config.is_file() or not database.is_file():
        raise AcceptanceFailure("Durable packaged configuration and database files were not created.")

    sensitive_names = {"appsettings.local.json", "taskdeck.db", "taskdeck.db-wal", "taskdeck.db-shm"}
    for path in temp_root.rglob("*"):
        if not path.is_file() or path.name not in sensitive_names:
            continue
        resolved = path.resolve()
        if taskdeck_data not in resolved.parents:
            raise AcceptanceFailure("Packaged configuration or database state escaped isolated LOCALAPPDATA.")


def validate_phase_evidence(value: Any, expected_phase: str, journey_id: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AcceptanceFailure("Packaged Playwright evidence is not an object.")
    allowed_top = {"schemaVersion", "phase", "journeyId", "board", "persistence", "http", "liveOpenAi"}
    if set(value) != allowed_top or value.get("schemaVersion") != EVIDENCE_SCHEMA_VERSION:
        raise AcceptanceFailure("Packaged Playwright evidence has an unexpected schema.")
    if value.get("phase") != expected_phase or value.get("journeyId") != journey_id:
        raise AcceptanceFailure("Packaged Playwright evidence identity does not match the harness journey.")
    _reject_forbidden_evidence_keys(value)
    _require_exact_keys(value.get("board"), {"id", "title"}, "board")
    board = value["board"]
    if not all(isinstance(board[field], str) and board[field] for field in ("id", "title")):
        raise AcceptanceFailure("Packaged board evidence is invalid.")

    expected_persistence_keys = (
        {"registered", "boardCreated"}
        if expected_phase == "create"
        else {"signedIn", "boardFound"}
    )
    _require_exact_keys(value.get("persistence"), expected_persistence_keys, "persistence")
    if any(value["persistence"][key] is not True for key in expected_persistence_keys):
        raise AcceptanceFailure("Packaged persistence evidence did not pass.")

    http = value.get("http")
    if not isinstance(http, list) or not http:
        raise AcceptanceFailure("Packaged HTTP evidence is invalid.")
    for record in http:
        _require_exact_keys(record, {"method", "path", "status"}, "HTTP")
        if (
            record["method"] not in {"GET", "POST"}
            or not isinstance(record["path"], str)
            or not record["path"].startswith("/api/")
            or not isinstance(record["status"], int)
            or not 200 <= record["status"] < 300
        ):
            raise AcceptanceFailure("Packaged HTTP evidence contained an invalid record.")

    live = value.get("liveOpenAi")
    if not isinstance(live, dict) or live.get("outcome") not in {"passed", "skipped"}:
        raise AcceptanceFailure("Packaged live-provider evidence has an invalid outcome.")
    _validate_live_evidence(live, expected_phase)
    return value


def _validate_live_evidence(live: dict[str, Any], phase: str) -> None:
    if live["outcome"] == "skipped":
        _require_exact_keys(live, {"outcome", "reason"}, "live-provider skip")
        if live["reason"] not in {"not_requested", "credential_unavailable"}:
            raise AcceptanceFailure("Packaged live-provider skip reason is invalid.")
        return

    if phase == "restart":
        _require_exact_keys(
            live,
            {"outcome", "cardTitle", "cardCountAfterRestart"},
            "live-provider restart",
        )
        if not isinstance(live["cardTitle"], str) or live["cardCountAfterRestart"] != 1:
            raise AcceptanceFailure("Packaged live-provider restart evidence is invalid.")
        return

    _require_exact_keys(
        live,
        {
            "outcome",
            "provider",
            "model",
            "isMock",
            "isProbed",
            "verificationStatus",
            "probeLatencyMs",
            "cardTitle",
            "proposal",
            "cardCounts",
        },
        "live-provider create",
    )
    if (
        live["provider"] != "OpenAI"
        or not isinstance(live["model"], str)
        or not live["model"]
        or live["isMock"] is not False
        or live["isProbed"] is not True
        or live["verificationStatus"] != "verified"
        or type(live["probeLatencyMs"]) is not int
        or not 1 <= live["probeLatencyMs"] <= MAX_PROBE_LATENCY_MS
        or not isinstance(live["cardTitle"], str)
    ):
        raise AcceptanceFailure("Packaged live-provider identity evidence is invalid.")
    _require_exact_keys(
        live["proposal"],
        {"id", "statusBeforeApproval", "statusAfterApproval", "statusAfterApply", "operationCount"},
        "proposal",
    )
    if (
        not isinstance(live["proposal"]["id"], str)
        or not live["proposal"]["id"]
        or not isinstance(live["proposal"]["operationCount"], int)
        or live["proposal"]["operationCount"] < 1
    ):
        raise AcceptanceFailure("Packaged proposal evidence is invalid.")
    _require_exact_keys(live["cardCounts"], {"beforeProposal", "afterApproval", "afterApply"}, "card count")
    if live["cardCounts"] != {"beforeProposal": 0, "afterApproval": 0, "afterApply": 1}:
        raise AcceptanceFailure("Packaged review-first card-count evidence is invalid.")


def _require_exact_keys(value: Any, allowed: set[str], description: str) -> None:
    if not isinstance(value, dict) or set(value) != allowed:
        raise AcceptanceFailure(f"Packaged {description} evidence has an unexpected schema.")


def _reject_forbidden_evidence_keys(value: Any) -> None:
    if isinstance(value, dict):
        for key, nested in value.items():
            if FORBIDDEN_EVIDENCE_KEYS.search(str(key)):
                raise AcceptanceFailure("Packaged evidence attempted to retain a forbidden field.")
            _reject_forbidden_evidence_keys(nested)
    elif isinstance(value, list):
        for nested in value:
            _reject_forbidden_evidence_keys(nested)


def create_temp_root(parent: Path) -> Path:
    parent = parent.resolve()
    if not parent.is_absolute() or not parent.is_dir():
        raise AcceptanceFailure("The acceptance temp parent must be an existing absolute directory.")
    return Path(tempfile.mkdtemp(prefix=SAFE_ROOT_PREFIX, dir=parent)).resolve()


def remove_temp_root(root: Path, parent: Path) -> None:
    resolved_root = root.resolve()
    resolved_parent = parent.resolve()
    if (
        resolved_root.parent != resolved_parent
        or not resolved_root.name.startswith(SAFE_ROOT_PREFIX)
        or resolved_root == resolved_parent
    ):
        raise AcceptanceFailure("Refusing to remove a temp root outside the validated acceptance boundary.")
    shutil.rmtree(resolved_root)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Test an untouched Taskdeck Windows desktop ZIP.")
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--checksum", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--frontend-directory", required=True, type=Path)
    parser.add_argument("--live-openai", action="store_true")
    return parser.parse_args(argv)


def run(argv: list[str]) -> int:
    args = parse_args(argv)
    if os.name != "nt":
        raise AcceptanceFailure("The packaged desktop acceptance harness requires Windows.")

    archive = args.archive.resolve(strict=True)
    checksum_path = args.checksum.resolve(strict=True)
    evidence_path = args.evidence.resolve()
    frontend_directory = args.frontend_directory.resolve(strict=True)
    evidence_path.parent.mkdir(parents=True, exist_ok=True)
    if evidence_path.exists():
        raise AcceptanceFailure("The evidence destination must not already exist.")

    archive_hash = verify_checksum(archive, checksum_path)
    temp_parent = Path(os.environ.get("RUNNER_TEMP") or tempfile.gettempdir()).resolve()
    temp_root = create_temp_root(temp_parent)
    monitors: list[ProcessMonitor] = []
    port_guard: socket.socket | None = None
    try:
        first_extract = temp_root / "extract-one"
        second_extract = temp_root / "extract-two"
        unrelated_cwd = temp_root / "unrelated-cwd"
        local_app_data = temp_root / "local-app-data"
        unrelated_cwd.mkdir()
        local_app_data.mkdir()
        safe_extract_archive(archive, first_extract)
        safe_extract_archive(archive, second_extract)
        first_snapshot = snapshot_tree(first_extract)
        second_snapshot = snapshot_tree(second_extract)
        cwd_snapshot = snapshot_tree(unrelated_cwd)

        executable_one = (first_extract / "Taskdeck.Api.exe").resolve()
        executable_two = (second_extract / "Taskdeck.Api.exe").resolve()
        operator_key = resolve_operator_key(os.environ)
        live_openai = args.live_openai and operator_key is not None
        live_skip_reason = (
            "none"
            if live_openai
            else "credential_unavailable"
            if args.live_openai
            else "not_requested"
        )
        journey_id = f"release-{int(time.time())}-{os.getpid()}"
        app_environment = build_app_environment(
            os.environ,
            local_app_data,
            operator_key if live_openai else None,
        )

        port_guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        port_guard.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 0)
        try:
            port_guard.bind(("127.0.0.1", 5000))
            port_guard.listen(1)
        except OSError:
            raise AcceptanceFailure("The harness could not take exclusive ownership of port 5000.") from None

        first_monitor = start_packaged_process(executable_one, unrelated_cwd, app_environment)
        monitors.append(first_monitor)
        first_url, first_port = first_monitor.wait_for_ready()
        if first_port == 5000:
            raise AcceptanceFailure("The packaged process did not fall back while port 5000 was held.")
        port_guard.close()
        port_guard = None
        first_http = request_health_and_spa(first_url)

        create_evidence_path = temp_root / "create-evidence.json"
        run_playwright(
            frontend_directory,
            build_playwright_environment(
                os.environ,
                base_url=first_url,
                evidence_path=create_evidence_path,
                journey_id=journey_id,
                phase="create",
                live_openai=live_openai,
                live_skip_reason=live_skip_reason,
            ),
        )
        stop_packaged_process(first_monitor)
        monitors.remove(first_monitor)
        assert_data_isolated(temp_root, local_app_data)
        assert_tree_unchanged(first_snapshot, first_extract, "first extracted archive")
        assert_tree_unchanged(cwd_snapshot, unrelated_cwd, "unrelated working directory")

        second_monitor = start_packaged_process(executable_two, unrelated_cwd, app_environment)
        monitors.append(second_monitor)
        second_url, second_port = second_monitor.wait_for_ready()
        if second_port != 5000:
            raise AcceptanceFailure("The packaged process did not prefer port 5000 after it became free.")
        second_http = request_health_and_spa(second_url)

        restart_evidence_path = temp_root / "restart-evidence.json"
        run_playwright(
            frontend_directory,
            build_playwright_environment(
                os.environ,
                base_url=second_url,
                evidence_path=restart_evidence_path,
                journey_id=journey_id,
                phase="restart",
                live_openai=live_openai,
                live_skip_reason=live_skip_reason,
            ),
        )
        stop_packaged_process(second_monitor)
        monitors.remove(second_monitor)
        assert_tree_unchanged(second_snapshot, second_extract, "second extracted archive")
        assert_tree_unchanged(cwd_snapshot, unrelated_cwd, "unrelated working directory")
        assert_data_isolated(temp_root, local_app_data)

        if sha256_file(archive) != archive_hash:
            raise AcceptanceFailure("The archive changed during post-ZIP acceptance.")

        create_evidence = validate_phase_evidence(
            json.loads(create_evidence_path.read_text(encoding="utf-8")),
            "create",
            journey_id,
        )
        restart_evidence = validate_phase_evidence(
            json.loads(restart_evidence_path.read_text(encoding="utf-8")),
            "restart",
            journey_id,
        )
        final_evidence = {
            "schemaVersion": EVIDENCE_SCHEMA_VERSION,
            "release": {"archive": archive.name, "sha256": archive_hash, "archiveUnchanged": True},
            "launches": [
                {"extraction": 1, "heldDefaultPort": True, "usedFallbackPort": True, "http": first_http},
                {"extraction": 2, "heldDefaultPort": False, "usedDefaultPort": True, "http": second_http},
            ],
            "create": create_evidence,
            "restart": restart_evidence,
        }
        _reject_forbidden_evidence_keys(final_evidence)
        evidence_path.write_text(json.dumps(final_evidence, indent=2) + "\n", encoding="utf-8")
        live_result = create_evidence["liveOpenAi"]
        outcome = live_result["outcome"]
        outcome_label = outcome if outcome == "passed" else f"skipped:{live_result['reason']}"
        print(f"Packaged desktop acceptance passed (live OpenAI: {outcome_label}).")
        return 0
    finally:
        if port_guard is not None:
            port_guard.close()
        for monitor in monitors:
            try:
                stop_packaged_process(monitor, require_clean=False)
            except Exception:
                _terminate_tracked_process_tree(monitor.process)
        remove_temp_root(temp_root, temp_parent)


def main() -> int:
    try:
        return run(sys.argv[1:])
    except (AcceptanceFailure, FileNotFoundError, json.JSONDecodeError, zipfile.BadZipFile) as exc:
        message = str(exc) if isinstance(exc, AcceptanceFailure) else "The packaged archive input was invalid."
        print(f"ERROR: {message}", file=sys.stderr)
        return 1
    except Exception:
        print("ERROR: Packaged desktop acceptance failed without retaining raw diagnostics.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
