#!/usr/bin/env python3
"""Claude Code PreToolUse hook for dangerous shell commands."""
from __future__ import annotations

import json
import re
import sys

DENY_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"\brm\s+-rf\b", re.I), "Destructive recursive removal requires explicit human approval."),
    (re.compile(r"\bRemove-Item\b.*\b-Recurse\b.*\b-Force\b", re.I), "Destructive recursive removal requires explicit human approval."),
    (re.compile(r"\bgit\s+reset\s+--hard\b", re.I), "Hard reset would discard work; inspect state and ask first."),
    (re.compile(r"\bgit\s+clean\s+-f[dDxX]*\b", re.I), "Git clean can delete untracked work; ask first."),
    (re.compile(r"\bgit\s+checkout\s+--\b", re.I), "Path checkout can discard user edits; ask first."),
    (re.compile(r"\bgit\s+push\s+--force(?:-with-lease)?\b", re.I), "Force-push is blocked by project policy."),
    (re.compile(r"\bsudo\b", re.I), "sudo is outside normal Taskdeck repo workflow."),
    (re.compile(r"\bchmod\s+-R\s+777\b", re.I), "Recursive world-writable permissions are blocked."),
    (re.compile(r"\b(?:curl|wget|Invoke-WebRequest|iwr)\b.+\|\s*(?:sh|bash|pwsh|powershell)\b", re.I), "Piping remote scripts into a shell is blocked."),
    (re.compile(r"\bdotnet\s+ef\s+database\s+drop\b", re.I), "Database drop requires explicit human approval."),
    (re.compile(r"\bDROP\s+(?:TABLE|DATABASE)\b", re.I), "Destructive SQL requires explicit human approval."),
]

SECRET_PATH = re.compile(r"(^|[\s/\\])\.env(?:[.\s]|$)|secrets?\.(json|ya?ml|toml)$", re.I)
SECRET_MUTATORS = re.compile(
    r"\b(rm|del|erase|mv|move|cp|copy|Set-Content|Add-Content|Out-File|New-Item|Remove-Item|Move-Item|Copy-Item)\b",
    re.I,
)


def deny(reason: str) -> None:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        return 0

    tool_name = str(payload.get("tool_name", ""))
    if tool_name not in {"Bash", "Shell"}:
        return 0

    command = str(payload.get("tool_input", {}).get("command", ""))
    compact = " ".join(command.split())

    for pattern, reason in DENY_PATTERNS:
        if pattern.search(compact):
            deny(reason)
            return 0

    if SECRET_PATH.search(compact) and SECRET_MUTATORS.search(compact):
        deny("Command appears to modify or move secret/env files. Ask for explicit approval.")
        return 0

    return 0


if __name__ == "__main__":
    raise SystemExit(main())

