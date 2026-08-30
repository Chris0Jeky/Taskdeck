#!/usr/bin/env python3
"""Windows post-ZIP acceptance for the marked Taskdeck desktop package.

The harness keeps raw application output in a pipe only long enough to recognize stable desktop
markers. It persists a strict whitelist of synthetic evidence and never writes configuration,
credentials, provider payloads, or application logs.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import queue
import re
import shutil
import signal
import socket
import sqlite3
import stat
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Mapping


READY_PATTERN = re.compile(r"^TASKDECK_DESKTOP_READY url=(http://127\.0\.0\.1:([1-9]\d{0,4}))$")
BOOTSTRAP_IDENTITY_PREFIX = "TASKDECK_DESKTOP_BOOTSTRAP"
BOOTSTRAP_IDENTITY_PATTERN = re.compile(
    r"^TASKDECK_DESKTOP_BOOTSTRAP jwt_created=(true|false) connector_created=(true|false)$"
)
RETIRED_PROVIDER_FATAL_MARKER = (
    "TASKDECK_DESKTOP_FATAL code=retired_provider_configuration"
)
RETIRED_PROVIDER_FATAL_GUIDANCE = (
    "Taskdeck could not start because retired Gemini provider configuration is still active. "
    "Choose OpenAI, OpenAICompatible, Ollama, or Mock, then remove the retired Gemini selector, "
    "child settings, and Docker Compose variable. Restart Taskdeck after updating them. "
    "No settings were printed."
)
SYNTHETIC_RETIRED_PROVIDER_VALUE = "synthetic-secret-never-print"
LOOPBACK_URL_TEMPLATE = "http://127.0.0.1:{port}"
MAX_MONITORED_OUTPUT_LINES = 400
MAX_MONITORED_OUTPUT_BYTES = 65_536
RETIRED_PROVIDER_IGNORED_WARNING_MARKER = (
    "TASKDECK_DESKTOP_WARNING code=retired_provider_configuration_ignored"
)
SAFE_ROOT_PREFIX = "taskdeck-desktop-acceptance-"
PHASE_EVIDENCE_SCHEMA_VERSION = 3
FINAL_EVIDENCE_SCHEMA_VERSION = 7
MAX_PROBE_LATENCY_MS = 300_000
MCP_PROTOCOL_VERSION = "2025-11-25"
MCP_INITIALIZE_ID = 1
MCP_STDIO_TIMEOUT_SECONDS = 120
MAX_MCP_STDOUT_BYTES = 1_048_576
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
    "GEMINI_API_KEY",
    "LLM__OPENAI__APIKEY",
    "OPENAI_API_KEY",
    "TASKDECK_DEMO_GEMINI_API_KEY",
    "TASKDECK_DEMO_OPENAI_API_KEY",
    "TASKDECK_LLM_GEMINI_API_KEY",
    "TASKDECK_LLM_OPENAI_API_KEY",
    "TASKDECK_LLM_OPENAI_COMPATIBLE_API_KEY",
    "TASKDECK_RELEASE_OPENAI_API_KEY",
}

FORBIDDEN_EVIDENCE_KEYS = re.compile(
    r"(?:api.?key|authorization|config|credential|email|environment|error|filesystem.?path|header|"
    r"log|password|prompt|raw.?error|response|screenshot|secret|token|tool.?arg|trace|transcript|"
    r"user.?id|username)$",
    re.IGNORECASE,
)


class AcceptanceFailure(RuntimeError):
    """Sanitized acceptance failure; messages never include raw process/provider data."""


@dataclass(frozen=True)
class LiveOpenAiResolution:
    mode: str
    enabled: bool
    skip_reason: str
    operator_key: str | None


def validate_bootstrap_identity_markers(markers: list[str]) -> dict[str, bool]:
    if not markers:
        raise AcceptanceFailure("The packaged process did not report bootstrap identity lifecycle.")
    if len(markers) != 1:
        raise AcceptanceFailure("The packaged process reported duplicate bootstrap identity lifecycle.")

    match = BOOTSTRAP_IDENTITY_PATTERN.fullmatch(markers[0])
    if match is None:
        raise AcceptanceFailure("The packaged process reported malformed bootstrap identity lifecycle.")
    return {
        "jwtCreated": match.group(1) == "true",
        "connectorCreated": match.group(2) == "true",
    }


def require_bootstrap_identity(
    actual: Mapping[str, bool],
    expected: Mapping[str, bool],
) -> None:
    if actual != expected:
        raise AcceptanceFailure("The packaged bootstrap identity lifecycle did not match the clean-run gate.")


class ProcessMonitor:
    def __init__(self, process: subprocess.Popen[str]) -> None:
        self.process = process
        self.markers: queue.Queue[str] = queue.Queue()
        self.seen_markers: set[str] = set()
        self.bootstrap_identity_markers: list[str] = []
        self.warning_markers: list[str] = []
        # Bounded capture of EVERY line, marker or not: a marker's human-readable companion line
        # carries the guidance, and the value-blind assertion has to be able to read it.
        self.output_lines: list[str] = []
        self.output_truncated = False
        self._output_bytes = 0
        self._thread = threading.Thread(target=self._drain, daemon=True)
        self._thread.start()

    def _drain(self) -> None:
        assert self.process.stdout is not None
        for raw_line in self.process.stdout:
            line = raw_line.rstrip("\r\n")
            if (
                len(self.output_lines) >= MAX_MONITORED_OUTPUT_LINES
                or self._output_bytes + len(line) > MAX_MONITORED_OUTPUT_BYTES
            ):
                self.output_truncated = True
            else:
                self.output_lines.append(line)
                self._output_bytes += len(line)
            if line.startswith("TASKDECK_DESKTOP_"):
                marker_name = line.split(maxsplit=1)[0]
                self.seen_markers.add(marker_name)
                if line.startswith(BOOTSTRAP_IDENTITY_PREFIX):
                    self.bootstrap_identity_markers.append(line)
                if marker_name == "TASKDECK_DESKTOP_WARNING":
                    self.warning_markers.append(line)
                self.markers.put(line)

    def wait_for_ready(self, timeout_seconds: float = 120.0) -> tuple[str, int, dict[str, bool]]:
        deadline = time.monotonic() + timeout_seconds
        bootstrap_identity_markers: list[str] = []
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise AcceptanceFailure("The packaged process exited before readiness.")
            try:
                marker = self.markers.get(timeout=min(0.25, deadline - time.monotonic()))
            except queue.Empty:
                continue
            if marker.startswith("TASKDECK_DESKTOP_FATAL"):
                raise AcceptanceFailure("The packaged process reported a redacted startup failure.")
            if marker.startswith(BOOTSTRAP_IDENTITY_PREFIX):
                bootstrap_identity_markers.append(marker)
                continue
            match = READY_PATTERN.fullmatch(marker)
            if match:
                port = int(match.group(2))
                if not 1 <= port <= 65535:
                    raise AcceptanceFailure("The packaged process reported an invalid loopback port.")
                bootstrap_identity = validate_bootstrap_identity_markers(
                    bootstrap_identity_markers
                )
                return match.group(1), port, bootstrap_identity
        raise AcceptanceFailure("The packaged process did not report readiness within the bounded wait.")

    def wait_for_output_completion(self, timeout_seconds: float = 2.0) -> None:
        self._thread.join(timeout=timeout_seconds)
        if self._thread.is_alive():
            raise AcceptanceFailure("The packaged process output did not close after shutdown.")


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


def resolve_live_openai_mode(
    *,
    required: bool,
    optional: bool,
    environment: Mapping[str, str],
) -> LiveOpenAiResolution:
    if required and optional:
        raise AcceptanceFailure("Hosted acceptance mode is ambiguous.")

    if not required and not optional:
        return LiveOpenAiResolution("off", False, "not_requested", None)

    operator_key = resolve_operator_key(environment)
    if required:
        if operator_key is None:
            raise AcceptanceFailure("Required hosted acceptance is unavailable.")
        return LiveOpenAiResolution("required", True, "none", operator_key)

    if operator_key is None:
        return LiveOpenAiResolution("optional", False, "credential_unavailable", None)
    return LiveOpenAiResolution("optional", True, "none", operator_key)


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
            "GEMINI_API_KEY",
            "LLM__",
            "OPENAI_API_KEY",
            "TASKDECK_DEMO_GEMINI_API_KEY",
            "TASKDECK_DEMO_OPENAI_API_KEY",
            "TASKDECK_LLM_GEMINI_API_KEY",
            "TASKDECK_LLM_OPENAI_API_KEY",
            "TASKDECK_LLM_OPENAI_COMPATIBLE_API_KEY",
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


def build_mcp_initialize_request() -> str:
    request = {
        "jsonrpc": "2.0",
        "id": MCP_INITIALIZE_ID,
        "method": "initialize",
        "params": {
            "protocolVersion": MCP_PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {
                "name": "taskdeck-desktop-archive-smoke",
                "version": "1.0",
            },
        },
    }
    return json.dumps(request, separators=(",", ":")) + "\n"


def validate_mcp_initialize_stdout(output: str) -> dict[str, bool]:
    if len(output.encode("utf-8")) > MAX_MCP_STDOUT_BYTES:
        raise AcceptanceFailure("The packaged MCP stdout exceeded its bounded protocol response.")

    lines = output.splitlines()
    if len(lines) != 1 or not lines[0].strip():
        raise AcceptanceFailure(
            "The packaged MCP stdout contained non-protocol output or multiple responses."
        )

    try:
        response = json.loads(lines[0])
    except json.JSONDecodeError:
        raise AcceptanceFailure("The packaged MCP stdout was not one JSON-RPC response.") from None

    if not isinstance(response, dict) or set(response) != {"jsonrpc", "id", "result"}:
        raise AcceptanceFailure("The packaged MCP initialize response shape was invalid.")
    if response["jsonrpc"] != "2.0" or response["id"] != MCP_INITIALIZE_ID:
        raise AcceptanceFailure("The packaged MCP initialize response identity was invalid.")

    result = response["result"]
    if not isinstance(result, dict):
        raise AcceptanceFailure("The packaged MCP initialize result was invalid.")
    server_info = result.get("serverInfo")
    if not isinstance(server_info, dict):
        raise AcceptanceFailure("The packaged MCP initialize result omitted serverInfo.")
    for field in ("name", "version"):
        value = server_info.get(field)
        if not isinstance(value, str) or not value.strip() or len(value) > 256:
            raise AcceptanceFailure("The packaged MCP initialize serverInfo was invalid.")

    return {
        "initialized": True,
        "serverInfoValid": True,
        "stdoutClean": True,
    }


def verify_packaged_mcp_stdio(
    executable: Path,
    cwd: Path,
    environment: Mapping[str, str],
) -> dict[str, bool]:
    if not executable.is_absolute() or not executable.is_file():
        raise AcceptanceFailure("The packaged MCP executable path is not an absolute file.")

    # The web journey sets CI=true to suppress browser launch, and a hosted runner can also set
    # GITHUB_ACTIONS or TF_BUILD. A packaged desktop deliberately ignores those flags for its
    # durable bootstrap path, while the Generic Host used by --mcp treats them as headless. Remove
    # only those runner flags so this probe exercises the same per-user profile an ordinary desktop
    # MCP client receives.
    mcp_environment = {
        key: value
        for key, value in environment.items()
        if key.upper() not in {"CI", "GITHUB_ACTIONS", "TF_BUILD"}
    }
    creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    process = subprocess.Popen(
        [str(executable), "--mcp"],
        cwd=str(cwd),
        env=mcp_environment,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=creation_flags,
    )

    response_available: queue.Queue[bool] = queue.Queue(maxsize=1)
    stdout_chunks: list[str] = []
    stdout_bytes = 0
    stdout_overflow = False

    def drain_stdout() -> None:
        nonlocal stdout_bytes, stdout_overflow
        saw_line = False
        try:
            assert process.stdout is not None
            for raw_line in process.stdout:
                encoded_size = len(raw_line.encode("utf-8"))
                stdout_bytes += encoded_size
                if stdout_bytes <= MAX_MCP_STDOUT_BYTES:
                    stdout_chunks.append(raw_line)
                else:
                    stdout_overflow = True
                if not saw_line:
                    saw_line = True
                    response_available.put(True)
        finally:
            if not saw_line:
                response_available.put(False)

    stdout_thread = threading.Thread(target=drain_stdout, daemon=True)
    stdout_thread.start()
    try:
        assert process.stdin is not None
        process.stdin.write(build_mcp_initialize_request())
        process.stdin.flush()
        try:
            has_response = response_available.get(timeout=MCP_STDIO_TIMEOUT_SECONDS)
        except queue.Empty:
            _terminate_tracked_process_tree(process)
            raise AcceptanceFailure(
                "The packaged MCP initialize probe did not respond within the bounded wait."
            ) from None

        if not has_response:
            process.wait(timeout=MCP_STDIO_TIMEOUT_SECONDS)
            if process.returncode != 0:
                raise AcceptanceFailure("The packaged MCP initialize probe exited non-zero.")
            raise AcceptanceFailure("The packaged MCP initialize probe returned no response.")

        # Keep stdin open until the response arrives. Closing it with communicate(input=...) can
        # end the SDK transport before its initialize response is flushed.
        process.stdin.close()
        process.wait(timeout=MCP_STDIO_TIMEOUT_SECONDS)
    except (BrokenPipeError, OSError):
        _terminate_tracked_process_tree(process)
        raise AcceptanceFailure(
            "The packaged MCP initialize probe closed its transport before responding."
        ) from None
    except subprocess.TimeoutExpired:
        _terminate_tracked_process_tree(process)
        raise AcceptanceFailure(
            "The packaged MCP initialize probe did not complete within the bounded wait."
        ) from None

    stdout_thread.join(timeout=2)
    if stdout_thread.is_alive():
        _terminate_tracked_process_tree(process)
        raise AcceptanceFailure("The packaged MCP stdout did not close after shutdown.")
    if process.returncode != 0:
        raise AcceptanceFailure("The packaged MCP initialize probe exited non-zero.")
    if stdout_overflow:
        raise AcceptanceFailure("The packaged MCP stdout exceeded its bounded protocol response.")
    return validate_mcp_initialize_stdout("".join(stdout_chunks))


def validate_retired_provider_failure_output(output: str) -> None:
    lines = output.splitlines()
    if len(output) > 4096 or len(lines) > 20 or any(len(line) > 512 for line in lines):
        raise AcceptanceFailure("The retired-provider failure output exceeded its bounded contract.")
    if lines.count(RETIRED_PROVIDER_FATAL_MARKER) != 1:
        raise AcceptanceFailure("The packaged process did not report the retired-provider failure code.")
    if lines.count(RETIRED_PROVIDER_FATAL_GUIDANCE) != 1:
        raise AcceptanceFailure("The packaged process did not report fixed retired-provider guidance.")
    fatal_markers = [line for line in lines if line.startswith("TASKDECK_DESKTOP_FATAL")]
    if fatal_markers != [RETIRED_PROVIDER_FATAL_MARKER]:
        raise AcceptanceFailure("The packaged process reported an unexpected fatal marker.")
    if "TASKDECK_DESKTOP_READY" in output or "http://" in output or "https://" in output:
        raise AcceptanceFailure("The retired-provider failure path reported a listener or URL.")
    if (
        SYNTHETIC_RETIRED_PROVIDER_VALUE in output
        or "RetiredLlmProviderConfigurationException" in output
        or " at Taskdeck." in output
    ):
        raise AcceptanceFailure("The retired-provider failure path exposed raw diagnostics.")


def validate_retired_provider_ignored_warning(
    warning_markers: list[str],
    observed_output: list[str],
    forbidden_value: str,
    output_truncated: bool = False,
) -> None:
    """The warning is announced exactly once, and NOTHING printed carries a configured value.

    ``observed_output`` is the monitor's bounded capture of every line, not just the marker
    lines, because the marker's human-readable companion line is where a value would leak.
    """
    if warning_markers != [RETIRED_PROVIDER_IGNORED_WARNING_MARKER]:
        raise AcceptanceFailure(
            "The packaged process did not announce the ignored retired configuration exactly once."
        )
    if output_truncated:
        raise AcceptanceFailure(
            "The packaged start exceeded its bounded console output, so it could not be scanned."
        )
    if not forbidden_value:
        return
    for line in observed_output:
        if forbidden_value in line:
            raise AcceptanceFailure(
                "The packaged start printed a configured value alongside the ignored-configuration warning."
            )


def verify_inherited_retired_provider_configuration_starts(
    executable: Path,
    cwd: Path,
    local_app_data: Path,
) -> None:
    """#2233: a profile carrying leftover retired provider variables must still start.

    Case A is a retired selector plus retired children; case B is retired children with no
    selector at all, which is the state of an upgraded Windows profile. Both are inherited from
    the PROCESS ENVIRONMENT, so both are ignored for selection, announced once, and the app
    starts on the packaged default. Each case gets its own data directory so the bootstrap
    identity gate still sees a first run.
    """
    cases = (
        (
            "selector-and-children",
            {
                "Llm__Provider": "Gemini",
                "Llm__Gemini__ApiKey": SYNTHETIC_RETIRED_PROVIDER_VALUE,
                "Llm__Gemini__Model": "retired-model",
                "Llm__Gemini__BaseUrl": "https://retired.example.invalid",
            },
        ),
        (
            "children-only",
            {
                "Llm__Gemini__ApiKey": SYNTHETIC_RETIRED_PROVIDER_VALUE,
                "Llm__Gemini__Model": "retired-model",
                "Llm__Gemini__BaseUrl": "https://retired.example.invalid",
            },
        ),
    )
    for case_name, retired_variables in cases:
        case_data = local_app_data / case_name
        case_data.mkdir(parents=True, exist_ok=False)
        environment = build_app_environment(os.environ, case_data, None)
        environment.update(retired_variables)
        monitor = start_packaged_process(executable, cwd, environment)
        try:
            url, _, bootstrap_identity = monitor.wait_for_ready()
            require_bootstrap_identity(
                bootstrap_identity,
                {"jwtCreated": True, "connectorCreated": True},
            )
            request_health_and_spa(url)
            validate_retired_provider_ignored_warning(
                list(monitor.warning_markers),
                list(monitor.output_lines),
                SYNTHETIC_RETIRED_PROVIDER_VALUE,
                monitor.output_truncated,
            )
            if "TASKDECK_DESKTOP_FATAL" in monitor.seen_markers:
                raise AcceptanceFailure(
                    "The inherited retired-provider start reported a fatal marker."
                )
        finally:
            stop_packaged_process(monitor)


def verify_ambient_openai_pins_do_not_block_start(
    executable: Path,
    cwd: Path,
    local_app_data: Path,
) -> None:
    """#2233: a stale ambient OpenAI pin is not retired configuration and starts silently."""
    environment = build_app_environment(os.environ, local_app_data, None)
    environment.update({"Llm__OpenAi__Model": "stale-pinned-model"})
    monitor = start_packaged_process(executable, cwd, environment)
    try:
        url, _, bootstrap_identity = monitor.wait_for_ready()
        require_bootstrap_identity(
            bootstrap_identity,
            {"jwtCreated": True, "connectorCreated": True},
        )
        request_health_and_spa(url)
        if monitor.warning_markers:
            raise AcceptanceFailure(
                "An ambient OpenAI pin was reported as ignored retired configuration."
            )
    finally:
        stop_packaged_process(monitor)


def verify_retired_provider_configuration_failure(
    executable: Path,
    cwd: Path,
    local_app_data: Path,
) -> None:
    """Retired configuration in Taskdeck's OWN durable settings file stays fatal (#2233).

    The user wrote it there deliberately, so the fail-closed contract PR #2016 established is
    unchanged for that source; only inherited environment variables are ignored.
    """
    if not executable.is_absolute() or not executable.is_file():
        raise AcceptanceFailure("The packaged executable path is not an absolute file.")

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as port_probe:
        port_probe.bind(("127.0.0.1", 0))
        expected_port = int(port_probe.getsockname()[1])

    durable_directory = local_app_data / "Taskdeck"
    durable_directory.mkdir(parents=True, exist_ok=True)
    (durable_directory / "appsettings.local.json").write_text(
        json.dumps({"Llm": {"Gemini": {"ApiKey": SYNTHETIC_RETIRED_PROVIDER_VALUE}}}),
        encoding="utf-8",
    )

    environment = build_app_environment(os.environ, local_app_data, None)
    environment.update({"ASPNETCORE_URLS": LOOPBACK_URL_TEMPLATE.format(port=expected_port)})
    creation_flags = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    process = subprocess.Popen(
        [str(executable)],
        cwd=str(cwd),
        env=environment,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=creation_flags,
    )
    try:
        output, _ = process.communicate(timeout=120)
    except subprocess.TimeoutExpired:
        _terminate_tracked_process_tree(process)
        raise AcceptanceFailure(
            "The retired-provider packaged regression did not exit within the bounded wait."
        ) from None

    if process.returncode != 1:
        raise AcceptanceFailure("The retired-provider packaged regression did not fail closed.")
    validate_retired_provider_failure_output(output)

    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener_probe:
        listener_probe.settimeout(1)
        if listener_probe.connect_ex(("127.0.0.1", expected_port)) == 0:
            raise AcceptanceFailure("The retired-provider failure path created a listener.")


def verify_supported_provider_ignores_inert_retired_child_settings(
    executable: Path,
    cwd: Path,
    local_app_data: Path,
) -> None:
    environment = build_app_environment(os.environ, local_app_data, None)
    environment.update(
        {
            "Llm__Provider": "Mock",
            "Llm__Gemini__ApiKey": SYNTHETIC_RETIRED_PROVIDER_VALUE,
        }
    )
    monitor = start_packaged_process(executable, cwd, environment)
    try:
        url, port, bootstrap_identity = monitor.wait_for_ready()
        if port != 5000:
            raise AcceptanceFailure("The supported-provider regression did not use the available default port.")
        require_bootstrap_identity(
            bootstrap_identity,
            {"jwtCreated": True, "connectorCreated": True},
        )
        request_health_and_spa(url)
    finally:
        stop_packaged_process(monitor)


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
        monitor.wait_for_output_completion()
        validate_bootstrap_identity_markers(list(monitor.bootstrap_identity_markers))
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


def seed_legacy_v01_state(legacy_path: Path, local_app_data: Path) -> bytes:
    """Create synthetic v0.1 adjacent identity and durable SQLite state without logging either."""
    taskdeck_data = (local_app_data / "Taskdeck").resolve()
    taskdeck_data.mkdir(parents=True, exist_ok=True)
    database = taskdeck_data / "taskdeck.db"
    connection = sqlite3.connect(database)
    try:
        connection.execute(
            "CREATE TABLE taskdeck_acceptance_legacy_state (marker TEXT NOT NULL)"
        )
        connection.execute(
            "INSERT INTO taskdeck_acceptance_legacy_state (marker) VALUES (?)",
            ("synthetic-legacy-state",),
        )
        connection.commit()
    finally:
        connection.close()

    payload = json.dumps(
        {
            "ConnectionStrings": {"DefaultConnection": f"Data Source={database}"},
            "Jwt": {"SecretKey": base64.b64encode(os.urandom(48)).decode("ascii")},
            "Connectors": {"EncryptionKey": base64.b64encode(os.urandom(32)).decode("ascii")},
            "ArchiveAcceptance": {"Sentinel": "synthetic-non-identity-setting"},
        },
        separators=(",", ":"),
    ).encode("utf-8")
    legacy_path.write_bytes(payload)
    return payload


def require_absent_legacy_fixture_path(legacy_path: Path) -> None:
    if legacy_path.exists():
        raise AcceptanceFailure(
            "The untouched package contained an adjacent local config and cannot be safely seeded."
        )


def assert_legacy_state_reused(database: Path) -> None:
    try:
        connection = sqlite3.connect(database)
        try:
            row = connection.execute(
                "SELECT marker FROM taskdeck_acceptance_legacy_state"
            ).fetchone()
        finally:
            connection.close()
    except sqlite3.Error as exc:
        raise AcceptanceFailure("The synthetic legacy app-data state was not reusable.") from exc
    if row != ("synthetic-legacy-state",):
        raise AcceptanceFailure("The synthetic legacy app-data state did not survive packaged startup.")


def assert_legacy_identity_imported_and_retained(
    legacy_path: Path,
    durable_path: Path,
    expected_payload: bytes,
) -> None:
    try:
        legacy_payload = legacy_path.read_bytes()
        durable_payload = durable_path.read_bytes()
    except OSError as exc:
        raise AcceptanceFailure("The packaged legacy identity was not retained in the expected locations.") from exc
    if legacy_payload != expected_payload:
        raise AcceptanceFailure("The packaged legacy identity source was not retained byte-for-byte.")
    try:
        expected = json.loads(expected_payload)
        durable = json.loads(durable_payload)
        expected_identity = (
            expected["ConnectionStrings"]["DefaultConnection"],
            expected["Jwt"]["SecretKey"],
            expected["Connectors"]["EncryptionKey"],
        )
        durable_identity = (
            durable["ConnectionStrings"]["DefaultConnection"],
            durable["Jwt"]["SecretKey"],
            durable["Connectors"]["EncryptionKey"],
        )
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("The durable packaged identity could not be safely verified.") from exc
    if durable != expected:
        raise AcceptanceFailure(
            "The complete packaged legacy config payload was not imported into durable app data."
        )
    if durable_identity != expected_identity:
        raise AcceptanceFailure("The packaged legacy identity was not imported into durable app data.")


def assert_data_isolated(
    temp_root: Path,
    local_app_data: Path,
    retained_legacy_path: Path | None = None,
) -> None:
    taskdeck_data = (local_app_data / "Taskdeck").resolve()
    local_config = taskdeck_data / "appsettings.local.json"
    database = taskdeck_data / "taskdeck.db"
    if not local_config.is_file() or not database.is_file():
        raise AcceptanceFailure("Durable packaged configuration and database files were not created.")

    sensitive_names = {"appsettings.local.json", "taskdeck.db", "taskdeck.db-wal", "taskdeck.db-shm"}
    allowed_retained_legacy_path = (
        retained_legacy_path.resolve() if retained_legacy_path is not None else None
    )
    for path in temp_root.rglob("*"):
        if not path.is_file() or path.name not in sensitive_names:
            continue
        resolved = path.resolve()
        if resolved == allowed_retained_legacy_path:
            continue
        if taskdeck_data not in resolved.parents:
            raise AcceptanceFailure("Packaged configuration or database state escaped isolated LOCALAPPDATA.")


def validate_phase_evidence(
    value: Any,
    expected_phase: str,
    journey_id: str,
    *,
    require_live_openai: bool = False,
) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise AcceptanceFailure("Packaged Playwright evidence is not an object.")
    allowed_top = {"schemaVersion", "phase", "journeyId", "board", "persistence", "http", "liveOpenAi"}
    if set(value) != allowed_top or value.get("schemaVersion") != PHASE_EVIDENCE_SCHEMA_VERSION:
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
    if require_live_openai and live["outcome"] != "passed":
        raise AcceptanceFailure("Required hosted acceptance did not produce live evidence.")
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
            {"outcome", "cardCountAfterRestart"},
            "live-provider restart",
        )
        if live["cardCountAfterRestart"] != 1:
            raise AcceptanceFailure("Packaged live-provider restart evidence is invalid.")
        return

    _require_exact_keys(
        live,
        {
            "outcome",
            "provider",
            "model",
            "promptVersion",
            "isMock",
            "isProbed",
            "verificationStatus",
            "probeLatencyMs",
            "proposal",
            "cardCounts",
        },
        "live-provider create",
    )
    if (
        live["provider"] != "OpenAI"
        or live["model"] != "gpt-5.6-luna"
        or live["promptVersion"] != "llm-triage.v2"
        or live["isMock"] is not False
        or live["isProbed"] is not True
        or live["verificationStatus"] != "verified"
        or type(live["probeLatencyMs"]) is not int
        or not 1 <= live["probeLatencyMs"] <= MAX_PROBE_LATENCY_MS
    ):
        raise AcceptanceFailure("Packaged live-provider identity evidence is invalid.")
    _require_exact_keys(
        live["proposal"],
        {"statusBeforeApproval", "statusAfterApproval", "statusAfterApply", "operationCount"},
        "proposal",
    )
    if (
        live["proposal"]["statusBeforeApproval"] not in {"PendingReview", "0"}
        or live["proposal"]["statusAfterApproval"] not in {"Approved", "1"}
        or live["proposal"]["statusAfterApply"] not in {"Applied", "3"}
        or type(live["proposal"]["operationCount"]) is not int
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


def validate_migration_evidence(value: Any) -> dict[str, dict[str, str]]:
    expected = {
        "legacy": {"location": "adjacent", "state": "retained"},
        "durable": {"location": "app-data", "state": "imported"},
        "database": {"location": "app-data", "state": "reused"},
        "board": {"location": "app-data", "state": "created"},
    }
    if value != expected:
        raise AcceptanceFailure("Packaged migration evidence did not match the retention contract.")
    return expected


def build_final_evidence(
    archive_name: str,
    archive_hash: str,
    first_http: dict[str, Any],
    second_http: dict[str, Any],
    first_bootstrap_identity: dict[str, bool],
    second_bootstrap_identity: dict[str, bool],
    create_evidence: dict[str, Any],
    restart_evidence: dict[str, Any],
    mcp_stdio_evidence: Mapping[str, bool],
    migration_evidence: Any,
    migration_create_evidence: dict[str, Any],
    migration_restart_evidence: dict[str, Any],
) -> dict[str, Any]:
    for identity in (first_bootstrap_identity, second_bootstrap_identity):
        _require_exact_keys(identity, {"jwtCreated", "connectorCreated"}, "bootstrap identity")
        if any(type(identity[key]) is not bool for key in ("jwtCreated", "connectorCreated")):
            raise AcceptanceFailure("Packaged bootstrap identity evidence is invalid.")

    expected_mcp_stdio_evidence = {
        "initialized": True,
        "serverInfoValid": True,
        "stdoutClean": True,
    }
    if dict(mcp_stdio_evidence) != expected_mcp_stdio_evidence:
        raise AcceptanceFailure("Packaged MCP stdio evidence did not match the initialize contract.")

    final_evidence = {
        "schemaVersion": FINAL_EVIDENCE_SCHEMA_VERSION,
        "release": {"archive": archive_name, "sha256": archive_hash, "archiveUnchanged": True},
        "cleanInstall": {
            "launches": [
                {
                    "extraction": 1,
                    "heldDefaultPort": True,
                    "usedFallbackPort": True,
                    "bootstrapIdentity": dict(first_bootstrap_identity),
                    "http": first_http,
                },
                {
                    "extraction": 2,
                    "heldDefaultPort": False,
                    "usedDefaultPort": True,
                    "bootstrapIdentity": dict(second_bootstrap_identity),
                    "http": second_http,
                },
            ],
            "create": create_evidence,
            "restart": restart_evidence,
            "mcpStdio": expected_mcp_stdio_evidence,
        },
        "migration": {
            **validate_migration_evidence(migration_evidence),
            "create": migration_create_evidence,
            "restart": migration_restart_evidence,
        },
    }
    _reject_forbidden_evidence_keys(final_evidence)
    return final_evidence


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
    hosted_mode = parser.add_mutually_exclusive_group()
    hosted_mode.add_argument("--live-openai", action="store_true")
    hosted_mode.add_argument("--live-openai-if-configured", action="store_true")
    return parser.parse_args(argv)


def run_packaged_journey(
    *,
    journey_root: Path,
    first_extract: Path,
    second_extract: Path,
    local_app_data: Path,
    unrelated_cwd: Path,
    frontend_directory: Path,
    journey_id: str,
    live_openai: bool,
    live_skip_reason: str,
    require_live_openai: bool,
    operator_key: str | None,
    expected_first_identity: Mapping[str, bool],
    expected_second_identity: Mapping[str, bool],
    hold_default_port: bool,
    retained_legacy_path: Path | None = None,
    expected_legacy_payload: bytes | None = None,
) -> dict[str, Any]:
    first_snapshot = snapshot_tree(first_extract)
    second_snapshot = snapshot_tree(second_extract)
    cwd_snapshot = snapshot_tree(unrelated_cwd)
    app_environment = build_app_environment(
        os.environ,
        local_app_data,
        operator_key if live_openai else None,
    )
    monitors: list[ProcessMonitor] = []
    port_guard: socket.socket | None = None
    try:
        if hold_default_port:
            port_guard = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            port_guard.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 0)
            try:
                port_guard.bind(("127.0.0.1", 5000))
                port_guard.listen(1)
            except OSError:
                raise AcceptanceFailure(
                    "The harness could not take exclusive ownership of port 5000."
                ) from None

        first_monitor = start_packaged_process(
            (first_extract / "Taskdeck.Api.exe").resolve(),
            unrelated_cwd,
            app_environment,
        )
        monitors.append(first_monitor)
        first_url, first_port, first_bootstrap_identity = first_monitor.wait_for_ready()
        require_bootstrap_identity(first_bootstrap_identity, expected_first_identity)
        if hold_default_port:
            if first_port == 5000:
                raise AcceptanceFailure(
                    "The packaged process did not fall back while port 5000 was held."
                )
            port_guard.close()
            port_guard = None
        elif first_port != 5000:
            raise AcceptanceFailure("The packaged process did not use the available default port.")
        first_http = request_health_and_spa(first_url)

        create_evidence_path = journey_root / "create-evidence.json"
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
        mcp_stdio_evidence = verify_packaged_mcp_stdio(
            (first_extract / "Taskdeck.Api.exe").resolve(),
            unrelated_cwd,
            app_environment,
        )
        assert_data_isolated(journey_root, local_app_data, retained_legacy_path)
        if expected_legacy_payload is not None and retained_legacy_path is not None:
            assert_legacy_identity_imported_and_retained(
                retained_legacy_path,
                local_app_data / "Taskdeck" / "appsettings.local.json",
                expected_legacy_payload,
            )
            assert_legacy_state_reused(local_app_data / "Taskdeck" / "taskdeck.db")
        assert_tree_unchanged(first_snapshot, first_extract, "first extracted archive")
        assert_tree_unchanged(cwd_snapshot, unrelated_cwd, "unrelated working directory")

        second_monitor = start_packaged_process(
            (second_extract / "Taskdeck.Api.exe").resolve(),
            unrelated_cwd,
            app_environment,
        )
        monitors.append(second_monitor)
        second_url, second_port, second_bootstrap_identity = second_monitor.wait_for_ready()
        require_bootstrap_identity(second_bootstrap_identity, expected_second_identity)
        if second_port != 5000:
            raise AcceptanceFailure("The packaged process did not prefer port 5000 after restart.")
        second_http = request_health_and_spa(second_url)

        restart_evidence_path = journey_root / "restart-evidence.json"
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
        assert_data_isolated(journey_root, local_app_data, retained_legacy_path)
        if expected_legacy_payload is not None and retained_legacy_path is not None:
            assert_legacy_identity_imported_and_retained(
                retained_legacy_path,
                local_app_data / "Taskdeck" / "appsettings.local.json",
                expected_legacy_payload,
            )
            assert_legacy_state_reused(local_app_data / "Taskdeck" / "taskdeck.db")

        return {
            "firstHttp": first_http,
            "secondHttp": second_http,
            "firstBootstrapIdentity": first_bootstrap_identity,
            "secondBootstrapIdentity": second_bootstrap_identity,
            "create": validate_phase_evidence(
                json.loads(create_evidence_path.read_text(encoding="utf-8")),
                "create",
                journey_id,
                require_live_openai=require_live_openai,
            ),
            "restart": validate_phase_evidence(
                json.loads(restart_evidence_path.read_text(encoding="utf-8")),
                "restart",
                journey_id,
                require_live_openai=require_live_openai,
            ),
            "mcpStdio": mcp_stdio_evidence,
        }
    finally:
        if port_guard is not None:
            port_guard.close()
        for monitor in monitors:
            try:
                stop_packaged_process(monitor, require_clean=False)
            except Exception:
                _terminate_tracked_process_tree(monitor.process)


def run(argv: list[str]) -> int:
    args = parse_args(argv)
    if os.name != "nt":
        raise AcceptanceFailure("The packaged desktop acceptance harness requires Windows.")

    live_resolution = resolve_live_openai_mode(
        required=args.live_openai,
        optional=args.live_openai_if_configured,
        environment=os.environ,
    )

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
    try:
        journey_stamp = f"{int(time.time())}-{os.getpid()}"
        clean_root = temp_root / "clean-install"
        migration_root = temp_root / "migration"
        retired_provider_root = temp_root / "retired-provider"
        supported_provider_root = temp_root / "supported-provider"
        clean_root.mkdir()
        migration_root.mkdir()
        retired_provider_root.mkdir()
        supported_provider_root.mkdir()
        for root in (clean_root, migration_root):
            (root / "unrelated-cwd").mkdir()
            (root / "local-app-data").mkdir()

        retired_provider_extract = retired_provider_root / "extract"
        retired_provider_cwd = retired_provider_root / "unrelated-cwd"
        retired_provider_local_app_data = retired_provider_root / "local-app-data"
        retired_provider_cwd.mkdir()
        retired_provider_local_app_data.mkdir()
        (retired_provider_local_app_data / "inherited").mkdir()
        (retired_provider_local_app_data / "durable-settings-file").mkdir()
        safe_extract_archive(archive, retired_provider_extract)
        retired_provider_extract_snapshot = snapshot_tree(retired_provider_extract)
        retired_provider_cwd_snapshot = snapshot_tree(retired_provider_cwd)
        retired_provider_executable = (
            retired_provider_extract / "Taskdeck.Api.exe"
        ).resolve()
        # #2233 cases A and B: leftover retired variables inherited from the profile must start.
        verify_inherited_retired_provider_configuration_starts(
            retired_provider_executable,
            retired_provider_cwd,
            retired_provider_local_app_data / "inherited",
        )
        ambient_pin_local_app_data = retired_provider_local_app_data / "ambient-openai-pins"
        ambient_pin_local_app_data.mkdir()
        verify_ambient_openai_pins_do_not_block_start(
            retired_provider_executable,
            retired_provider_cwd,
            ambient_pin_local_app_data,
        )
        # The same configuration written into Taskdeck's own durable file is still fatal.
        verify_retired_provider_configuration_failure(
            retired_provider_executable,
            retired_provider_cwd,
            retired_provider_local_app_data / "durable-settings-file",
        )
        assert_tree_unchanged(
            retired_provider_extract_snapshot,
            retired_provider_extract,
            "retired-provider extracted archive",
        )
        assert_tree_unchanged(
            retired_provider_cwd_snapshot,
            retired_provider_cwd,
            "retired-provider unrelated working directory",
        )

        supported_provider_extract = supported_provider_root / "extract"
        supported_provider_cwd = supported_provider_root / "unrelated-cwd"
        supported_provider_local_app_data = supported_provider_root / "local-app-data"
        supported_provider_cwd.mkdir()
        supported_provider_local_app_data.mkdir()
        safe_extract_archive(archive, supported_provider_extract)
        supported_provider_extract_snapshot = snapshot_tree(supported_provider_extract)
        supported_provider_cwd_snapshot = snapshot_tree(supported_provider_cwd)
        verify_supported_provider_ignores_inert_retired_child_settings(
            (supported_provider_extract / "Taskdeck.Api.exe").resolve(),
            supported_provider_cwd,
            supported_provider_local_app_data,
        )
        assert_tree_unchanged(
            supported_provider_extract_snapshot,
            supported_provider_extract,
            "supported-provider extracted archive",
        )
        assert_tree_unchanged(
            supported_provider_cwd_snapshot,
            supported_provider_cwd,
            "supported-provider unrelated working directory",
        )

        clean_first_extract = clean_root / "extract-one"
        clean_second_extract = clean_root / "extract-two"
        safe_extract_archive(archive, clean_first_extract)
        safe_extract_archive(archive, clean_second_extract)
        require_absent_legacy_fixture_path(clean_first_extract / "appsettings.local.json")
        require_absent_legacy_fixture_path(clean_second_extract / "appsettings.local.json")
        clean_journey = run_packaged_journey(
            journey_root=clean_root,
            first_extract=clean_first_extract,
            second_extract=clean_second_extract,
            local_app_data=clean_root / "local-app-data",
            unrelated_cwd=clean_root / "unrelated-cwd",
            frontend_directory=frontend_directory,
            journey_id=f"release-clean-{journey_stamp}",
            live_openai=live_resolution.enabled,
            live_skip_reason=live_resolution.skip_reason,
            require_live_openai=live_resolution.mode == "required",
            operator_key=live_resolution.operator_key,
            expected_first_identity={"jwtCreated": True, "connectorCreated": True},
            expected_second_identity={"jwtCreated": False, "connectorCreated": False},
            hold_default_port=True,
        )

        migration_first_extract = migration_root / "extract-one"
        migration_second_extract = migration_root / "extract-two"
        migration_local_app_data = migration_root / "local-app-data"
        safe_extract_archive(archive, migration_first_extract)
        safe_extract_archive(archive, migration_second_extract)
        migration_legacy_config = migration_first_extract / "appsettings.local.json"
        require_absent_legacy_fixture_path(migration_legacy_config)
        migration_payload = seed_legacy_v01_state(migration_legacy_config, migration_local_app_data)
        migration_journey = run_packaged_journey(
            journey_root=migration_root,
            first_extract=migration_first_extract,
            second_extract=migration_second_extract,
            local_app_data=migration_local_app_data,
            unrelated_cwd=migration_root / "unrelated-cwd",
            frontend_directory=frontend_directory,
            journey_id=f"release-migration-{journey_stamp}",
            live_openai=live_resolution.enabled,
            live_skip_reason=live_resolution.skip_reason,
            require_live_openai=live_resolution.mode == "required",
            operator_key=live_resolution.operator_key,
            expected_first_identity={"jwtCreated": False, "connectorCreated": False},
            expected_second_identity={"jwtCreated": False, "connectorCreated": False},
            hold_default_port=False,
            retained_legacy_path=migration_legacy_config,
            expected_legacy_payload=migration_payload,
        )

        if sha256_file(archive) != archive_hash:
            raise AcceptanceFailure("The archive changed during post-ZIP acceptance.")

        final_evidence = build_final_evidence(
            archive.name,
            archive_hash,
            clean_journey["firstHttp"],
            clean_journey["secondHttp"],
            clean_journey["firstBootstrapIdentity"],
            clean_journey["secondBootstrapIdentity"],
            clean_journey["create"],
            clean_journey["restart"],
            clean_journey["mcpStdio"],
            {
                "legacy": {"location": "adjacent", "state": "retained"},
                "durable": {"location": "app-data", "state": "imported"},
                "database": {"location": "app-data", "state": "reused"},
                "board": {"location": "app-data", "state": "created"},
            },
            migration_journey["create"],
            migration_journey["restart"],
        )
        evidence_path.write_text(json.dumps(final_evidence, indent=2) + "\n", encoding="utf-8")
        live_result = clean_journey["create"]["liveOpenAi"]
        outcome = live_result["outcome"]
        outcome_label = outcome if outcome == "passed" else f"skipped:{live_result['reason']}"
        print(
            "Packaged desktop acceptance passed "
            f"(MCP stdio initialize: passed; live OpenAI: {outcome_label})."
        )
        return 0
    finally:
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
