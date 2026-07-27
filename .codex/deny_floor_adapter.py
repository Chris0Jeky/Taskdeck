#!/usr/bin/env python3
"""Taskdeck's attributed bridge to the reviewed shared Codex deny floor.

The shared dispatcher remains owned by agent-harness and installed under the
operating-system account home.  This bridge deliberately does not trust the
inherited HOME value because Taskdeck's Windows-focused Codex configuration can
be loaded in a POSIX session.  It also turns every adapter/dispatcher failure
that occurs after a Bash payload is identified into an attributed deny.
"""

from __future__ import annotations

import ctypes
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from typing import Callable, Mapping, Sequence


EXPECTED_DISPATCHER_SHA256 = (
    "4da65bb4d1fc84409db8fe6846a5b2961c408f2278d963485bf2fa886e4bf1a3"
)
EXPECTED_FLOOR_VERSION = "1.6.18 (2026-07-27)"
DISPATCHER_SUFFIX = ".claude/hooks/dispatch.py"
ATTRIBUTION = "[Taskdeck Codex deny-floor adapter]"
DISPATCH_TIMEOUT_SECONDS = 3.5


class AdapterFailure(RuntimeError):
    """A safe-to-report adapter contract failure."""


def deny_document(reason: str) -> str:
    """Return a Codex PreToolUse deny document with stable attribution."""

    return json.dumps(
        {
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "permissionDecision": "deny",
                "permissionDecisionReason": f"{ATTRIBUTION} {reason}",
            }
        },
        separators=(",", ":"),
    )


def normalized_text_sha256(path: Path) -> str:
    """Hash UTF-8 text after the producer's CRLF/CR-to-LF normalization."""

    text = path.read_text(encoding="utf-8").replace("\r\n", "\n").replace("\r", "\n")
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _posix_account_home() -> Path:
    import pwd

    return Path(pwd.getpwuid(os.getuid()).pw_dir)


def _windows_account_home() -> Path:
    # CSIDL_PROFILE resolves the Windows account profile through the shell API
    # instead of the process's mutable HOME/USERPROFILE variables.
    buffer = ctypes.create_unicode_buffer(32768)
    result = ctypes.windll.shell32.SHGetFolderPathW(None, 0x0028, None, 0, buffer)
    if result != 0 or not buffer.value:
        raise AdapterFailure("the operating-system account home could not be resolved")
    return Path(buffer.value)


def account_home(
    *,
    platform_name: str | None = None,
    posix_resolver: Callable[[], Path] = _posix_account_home,
    windows_resolver: Callable[[], Path] = _windows_account_home,
) -> Path:
    """Resolve the real account home without consulting inherited HOME."""

    platform_name = os.name if platform_name is None else platform_name
    resolved = windows_resolver() if platform_name == "nt" else posix_resolver()
    if not resolved.is_absolute():
        raise AdapterFailure("the operating-system account home was not absolute")
    return resolved


def dispatcher_path(
    suffix: str,
    *,
    home_resolver: Callable[[], Path] = account_home,
) -> Path:
    """Resolve the one accepted shared-dispatcher suffix under account home."""

    if suffix != DISPATCHER_SUFFIX:
        raise AdapterFailure("the shared-dispatcher suffix did not match the reviewed contract")
    return home_resolver() / Path(*DISPATCHER_SUFFIX.split("/"))


def validate_bash_payload(raw_payload: str) -> None:
    """Fail closed when the matched Bash hook receives unknown/malformed input."""

    try:
        payload = json.loads(
            raw_payload,
            parse_constant=lambda value: (_ for _ in ()).throw(
                ValueError(f"invalid JSON constant {value}")
            ),
        )
    except (TypeError, ValueError, json.JSONDecodeError) as exc:
        raise AdapterFailure("the Bash hook payload was not valid JSON") from exc

    if not isinstance(payload, dict) or payload.get("tool_name") != "Bash":
        raise AdapterFailure("the matched hook did not receive a Bash payload object")
    tool_input = payload.get("tool_input")
    if not isinstance(tool_input, dict):
        raise AdapterFailure("the Bash hook payload had no tool_input object")
    command = tool_input.get("command")
    if not isinstance(command, str) or not command.strip():
        raise AdapterFailure("the Bash hook payload had no non-empty command")
    cwd = payload.get("cwd", "")
    if not isinstance(cwd, str):
        raise AdapterFailure("the Bash hook payload had a non-string cwd")


def validate_dispatcher_identity(
    dispatcher: Path,
    *,
    expected_hash: str = EXPECTED_DISPATCHER_SHA256,
    expected_version: str = EXPECTED_FLOOR_VERSION,
) -> None:
    """Verify the installed dispatcher is exactly the reviewed producer text."""

    if not dispatcher.is_file():
        raise AdapterFailure("the shared dispatcher is missing")
    try:
        actual_hash = normalized_text_sha256(dispatcher)
        text = dispatcher.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as exc:
        raise AdapterFailure("the shared dispatcher could not be read as UTF-8") from exc
    if actual_hash != expected_hash:
        raise AdapterFailure(
            "the shared dispatcher identity differs from the reviewed 1.6.18 pin"
        )
    version_match = re.search(
        r'^FLOOR_VERSION\s*=\s*"([^"]*)"', text, flags=re.MULTILINE
    )
    if version_match is None or version_match.group(1) != expected_version:
        raise AdapterFailure("the shared dispatcher version differs from the reviewed 1.6.18 pin")


def attribute_dispatcher_output(stdout: str) -> str | None:
    """Accept canonical allow/deny output and attribute every deny to this adapter."""

    if stdout == "":
        return None
    try:
        document = json.loads(
            stdout,
            parse_constant=lambda value: (_ for _ in ()).throw(
                ValueError(f"invalid JSON constant {value}")
            ),
        )
    except (TypeError, ValueError, json.JSONDecodeError) as exc:
        raise AdapterFailure("the shared dispatcher returned malformed output") from exc
    if not isinstance(document, dict):
        raise AdapterFailure("the shared dispatcher returned a non-object result")
    output = document.get("hookSpecificOutput")
    if not isinstance(output, dict):
        raise AdapterFailure("the shared dispatcher returned no hookSpecificOutput object")
    if output.get("hookEventName") != "PreToolUse":
        raise AdapterFailure("the shared dispatcher returned an unknown hook event")
    if output.get("permissionDecision") != "deny":
        raise AdapterFailure("the shared dispatcher returned an unknown permission decision")
    reason = output.get("permissionDecisionReason")
    if not isinstance(reason, str) or not reason.strip():
        raise AdapterFailure("the shared dispatcher returned no denial reason")
    output["permissionDecisionReason"] = f"{ATTRIBUTION} {reason}"
    return json.dumps(document, separators=(",", ":"))


def invoke_dispatcher(
    raw_payload: str,
    dispatcher: Path,
    *,
    expected_hash: str = EXPECTED_DISPATCHER_SHA256,
    expected_version: str = EXPECTED_FLOOR_VERSION,
    environment: Mapping[str, str] | None = None,
    interpreter: str | Path | None = None,
    timeout_seconds: float = DISPATCH_TIMEOUT_SECONDS,
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> str | None:
    """Run the exact dispatcher and return attributed output, or None for allow."""

    validate_bash_payload(raw_payload)
    validate_dispatcher_identity(
        dispatcher,
        expected_hash=expected_hash,
        expected_version=expected_version,
    )
    executable = str(Path(sys.executable) if interpreter is None else interpreter)
    try:
        result = runner(
            [
                executable,
                "-B",
                str(dispatcher),
                "--event",
                "pre",
                "--runtime",
                "codex",
            ],
            input=raw_payload,
            text=True,
            capture_output=True,
            timeout=timeout_seconds,
            env=dict(os.environ if environment is None else environment),
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        raise AdapterFailure("the shared dispatcher timed out") from exc
    except OSError as exc:
        raise AdapterFailure("the shared dispatcher process could not start") from exc
    if result.returncode != 0:
        raise AdapterFailure("the shared dispatcher process failed")
    if result.stderr != "":
        raise AdapterFailure("the shared dispatcher wrote unexpected diagnostic output")
    return attribute_dispatcher_output(result.stdout)


def parse_arguments(arguments: Sequence[str]) -> str:
    """Accept only the exact reviewed wiring; unknown/malformed flags deny."""

    expected = [
        "--dispatcher-suffix",
        DISPATCHER_SUFFIX,
        "--event",
        "pre",
        "--runtime",
        "codex",
    ]
    if list(arguments) != expected:
        raise AdapterFailure("the hook arguments differed from the reviewed Codex wiring")
    return DISPATCHER_SUFFIX


def run(raw_payload: str, arguments: Sequence[str]) -> str | None:
    """Execute the production bridge, converting every bridge failure to deny."""

    try:
        suffix = parse_arguments(arguments)
        dispatcher = dispatcher_path(suffix)
        return invoke_dispatcher(raw_payload, dispatcher)
    except AdapterFailure as exc:
        return deny_document(str(exc))
    except Exception as exc:  # fail closed without exposing environment details
        return deny_document(f"unexpected adapter failure ({exc.__class__.__name__})")


def main() -> int:
    result = run(sys.stdin.read(), sys.argv[1:])
    if result is not None:
        print(result)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
