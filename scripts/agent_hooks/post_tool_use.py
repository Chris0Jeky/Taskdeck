#!/usr/bin/env python3
"""Claude Code PostToolUse hook reminders for Taskdeck workflows."""
from __future__ import annotations

import json
import re
import sys

FRONTEND_PATH = "frontend/taskdeck-web"
FRONTEND_EXTENSIONS = (".ts", ".vue")
PR_CREATE_RE = re.compile(r"\bgh\s+pr\s+create\b", re.I)


def emit_context(message: str) -> None:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PostToolUse",
            "additionalContext": message,
        }
    }))


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0

    tool_name = str(payload.get("tool_name", ""))
    tool_input = payload.get("tool_input", {}) or {}

    if tool_name in {"Edit", "MultiEdit", "Write"}:
        file_path = str(tool_input.get("file_path", "")).replace("\\", "/")
        if FRONTEND_PATH in file_path and file_path.endswith(FRONTEND_EXTENSIONS):
            emit_context(
                f"Vue/TS file edited: {file_path} -- consider running typecheck if making multiple related edits."
            )
        return 0

    if tool_name == "Bash":
        command = " ".join(str(tool_input.get("command", "")).split())
        if PR_CREATE_RE.search(command):
            emit_context("PR created. Run the global review-and-ship skill now.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
