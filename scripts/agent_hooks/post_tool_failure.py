#!/usr/bin/env python3
"""Record sanitized Claude Code tool failures for later review."""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
import re
import sys
from pathlib import Path

WINDOWS_ABSOLUTE = re.compile(r"^[A-Za-z]:[\\/]")


def resolve_root() -> Path:
    raw = os.environ.get("CLAUDE_PROJECT_DIR", ".")
    if os.name != "nt" and WINDOWS_ABSOLUTE.match(raw):
        drive = raw[0].lower()
        rest = raw[2:].replace("\\", "/").lstrip("/")
        for prefix in (Path("/mnt") / drive, Path("/") / drive):
            candidate = prefix / rest
            if candidate.exists():
                return candidate.resolve()
        return (Path("/mnt") / drive / rest).resolve()
    return Path(raw).resolve()


ROOT = resolve_root()
LEDGER = ROOT / "docs" / "agentic" / "failure_ledger.jsonl"
SECRET_RE = re.compile(
    r"""(?ix)
    \b(token|secret|password|api[_-]?key|authorization)\b["']?\s*[:=]\s*["']?(?:bearer\s+)?[^,\s}"'\]]+
    |\bbearer\s+[A-Za-z0-9._~+/=-]+
    """
)


def redact_secret(match: re.Match[str]) -> str:
    key = match.group(1)
    return f"{key}=<redacted>" if key else "Bearer <redacted>"


def scrub(value: object, limit: int = 800) -> str:
    text = str(value or "")
    text = SECRET_RE.sub(redact_secret, text)
    text = text.replace(str(ROOT), ".")
    if len(text) > limit:
        digest = hashlib.sha256(text.encode("utf-8", "ignore")).hexdigest()[:12]
        text = f"{text[:limit]}... <truncated sha256:{digest}>"
    return text


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0

    tool_name = payload.get("tool_name", "unknown")
    tool_input = payload.get("tool_input", {}) or {}
    entry = {
        "ts": dt.datetime.now(dt.timezone.utc).isoformat(),
        "class": "unclassified",
        "surface": scrub(tool_name, 80),
        "command_or_target": scrub(tool_input.get("command") or tool_input.get("file_path") or tool_input, 500),
        "failure": scrub(payload.get("error") or payload.get("stderr") or payload.get("message") or payload, 1000),
        "workaround": "",
        "future_fix": "classify and promote if recurring",
        "status": "open",
    }

    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    with LEDGER.open("a", encoding="utf-8") as file:
        file.write(json.dumps(entry, ensure_ascii=True) + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
