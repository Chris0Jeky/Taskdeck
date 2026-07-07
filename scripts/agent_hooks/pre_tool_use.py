#!/usr/bin/env python3
"""Claude Code PreToolUse hook — Taskdeck-specific overlay to the GLOBAL deny floor.

This is a THIN complement to ``~/.claude/hooks/dispatch.py`` (the estate harness deny
floor, wired PreToolUse/Bash in ``~/.claude/settings.json``, currently v1.3.0). It
enforces ONLY what the global floor does NOT hard-deny for Taskdeck at T3 — never a
duplicate of a rule the global floor already owns. See issue #1293 for the analysis.

WHY THIS HOOK STILL EXISTS UNDER bypassPermissions
---------------------------------------------------
Taskdeck runs ``defaultMode=bypassPermissions`` (``.claude/settings.local.json``).
Under bypass, native ``permissions.deny`` is SKIPPED but HOOKS STILL FIRE. And the
global floor only *asks* on work-loss guards at T3 — and "ask" under bypass
auto-allows. So the ONLY bypass-proof enforcer of Taskdeck's stricter, post-incident
work-loss HARD-DENY is a hook. That is this file (2026-05/06 worktree main-leak class).

DIVISION OF LABOR (proven end-to-end against dispatch.py @ T3 — see PR body)
---------------------------------------------------------------------------
GLOBAL floor owns (this overlay stays SILENT — no duplicate):
  * force-push ``--force`` / ``-f`` / ``+refspec``
  * ``rm -rf`` / ``Remove-Item -Recurse`` on ABSOLUTE dangerous paths, roots, ``*``
  * ``sudo``; pipe-to-shell (``curl|wget|iwr|irm | sh/iex``)
  * ``.env`` / ``.pem`` / ``credential`` secret mutation
  In-project recursive deletes of a NAMED subdirectory are intentionally allowed by
  the global model, so this overlay allows them too (the flagged #1293 relaxation).

THIS overlay owns (global floor only asks / allows / misses at T3):
  * work-loss HARD-DENY: ``git reset --hard``, ``clean -f*``, ``checkout -- <path>``,
    ``restore --worktree/-W``
  * repo-destructive: ``rmdir /s``, ``npm publish``, ``dotnet ef database drop``,
    ``DROP TABLE/DATABASE`` (SQL-client-gated), ``chmod -R 777``
  * broad secret-file mutation (``*token* / *password* / *api_key* / ...`` filenames
    the global floor's narrower ``.env``/``.pem`` set misses)
  * ``git push --force-with-lease`` (global allows it below T4; Taskdeck keeps
    no-force-at-all as its post-incident posture)
  * recursive delete of the project root (``.``), the parent (``..``), or any
    ``..``-escaping path — the worktree-leak vector; the global floor only blocks
    ABSOLUTE dangerous paths, so relative ``..`` traversal escapes it.

Sanitization (``strip_quotes`` + segment split) is ported from the global floor so a
quoted commit message / PR body ("fix reset --hard", "docs: DROP TABLE") can never
false-positive-deny. Keep the two sanitizers semantically aligned.

Deny-floor changes are T4-class work (top model + review + smoke tests). Keep the
contract green:  python scripts/agent_hooks/smoke_test.py
"""
from __future__ import annotations

import json
import re
import sys

# --- sanitization (ported from ~/.claude/hooks/dispatch.py — keep semantics aligned) --

_SINGLE_Q = re.compile(r"'[^']*'")
_DOUBLE_Q = re.compile(r'"(?:\\.|[^"\\])*"')


def strip_quotes(text: str) -> str:
    """Remove INERT quoted substrings so message/body text can never trip a rule.

    Single-quoted text never expands -> always stripped. Double-quoted text is
    stripped only when it holds no unescaped $ or backtick; if it does, the shell
    EXECUTES the substitution, so the text must stay visible for scanning.
    """
    text = _SINGLE_Q.sub(" ", text)

    def _dq(m: "re.Match[str]") -> str:
        return m.group(0) if re.search(r"(?<!\\)[$`]", m.group(0)) else " "

    return _DOUBLE_Q.sub(_dq, text)


def segments(text: str) -> list[str]:
    """Split a command line into per-command segments on chains and substitutions."""
    return [s.strip() for s in re.split(r"[;\n()`|]|&&", text) if s.strip()]


_GIT_VALUE_OPTS = {"-C", "-c", "--git-dir", "--work-tree", "--namespace",
                   "--super-prefix", "--config-env"}
_WRAPPERS = {"env", "command", "builtin", "nice", "nohup", "time", "stdbuf", "xargs"}
_ASSIGN = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")
_EXE_SUFFIX = re.compile(r"\.(exe|cmd|bat|com|ps1)$", re.IGNORECASE)


def command_head(toks: list[str]) -> tuple[str, list[str]]:
    """Strip leading VAR=val assignments and known wrappers; drop the head's directory
    and .exe/.cmd suffix. ``env FOO=bar /usr/bin/git.exe push`` and ``git push`` both
    resolve to head='git'."""
    i = 0
    while i < len(toks):
        t = toks[i]
        if _ASSIGN.match(t):
            i += 1
            continue
        base = _EXE_SUFFIX.sub("", t.replace("\\", "/").split("/")[-1]).lower()
        if base in _WRAPPERS:
            i += 1
            while i < len(toks) and _ASSIGN.match(toks[i]):
                i += 1
            continue
        return base, toks[i:]
    return "", []


def git_subcommand(toks: list[str]) -> tuple[str, int]:
    """Return (subcommand_lowercased, index_in_toks), skipping global options AND their
    value tokens. index is -1 when no subcommand is present. Returning the index avoids a
    fragile toks.index(sub) rescan that could mis-align when a global-option value equals
    the subcommand name (e.g. ``git -C push push``)."""
    i = 1
    while i < len(toks):
        t = toks[i]
        if t in _GIT_VALUE_OPTS:
            i += 2
            continue
        if t.startswith("-"):
            i += 1
            continue
        return t.lower(), i
    return "", -1


def _rm_recursive_force(toks: list[str]) -> tuple[bool, bool]:
    """(is_recursive, is_force) for an ``rm`` invocation. Short flags are scanned per
    character (``-rf``/``-fr``); long flags are matched whole so ``--force`` is NOT
    misread as recursive+force just because the word "force" contains 'r' and 'f'."""
    recursive = force = False
    for t in toks[1:]:
        if t.startswith("--"):
            opt = t.lower()
            if opt == "--recursive":
                recursive = True
            elif opt == "--force":
                force = True
        elif t.startswith("-"):
            chars = t[1:]
            if "r" in chars or "R" in chars:
                recursive = True
            if "f" in chars:
                force = True
    return recursive, force


# --- Taskdeck-specific patterns ---------------------------------------------------

# SQL clients whose ``-c "DROP TABLE ..."`` argument EXECUTES (unlike an inert commit
# body). Gating DROP detection on the command head lets us keep the mid-command
# substring rule while never false-denying a git/gh/echo body that mentions the phrase.
SQL_CLIENTS = {"psql", "sqlite3", "mysql", "mariadb", "sqlcmd", "sqlplus", "mysqlsh",
               "usql", "cockroach"}
DROP_RX = re.compile(r"\bDROP\s+(?:TABLE|DATABASE)\b", re.IGNORECASE)

# Broad secret-file coverage the global floor's narrower .env/.pem set misses
# (e.g. api_key.txt, password.env, *_token.json). Ported from the prior overlay.
SECRET_PATH = re.compile(
    r"""(?ix)
    (^|[\s/\\'"])\.env(?:[.\s'"]|$)
    |(^|[\s/\\'"])[^\s'"]*(?:token|password|api[_-]?key|authorization|secret)[^\s'"]*\.(?:json|ya?ml|toml|txt|env|config)
    |secrets?\.(json|ya?ml|toml)$
    """
)
SECRET_MUTATORS = re.compile(
    r"\b(rm|del|erase|mv|move|cp|copy|Set-Content|Add-Content|Out-File|New-Item|"
    r"Remove-Item|Move-Item|Copy-Item|echo|printf)\b|>>?",
    re.IGNORECASE,
)

# A relative ``..`` path-traversal token (``../``, ``..\``, bare ``..``). Used to keep
# recursive deletes from ESCAPING the project — the exact worktree-leak vector.
TRAVERSAL_RX = re.compile(r"(^|[\s/\\'\"=])\.\.(?:[\\/]|$)")


def _drop_table_deny(command: str) -> tuple[str, str] | None:
    """DROP TABLE/DATABASE executed by a SQL client. Checked on the RAW command so it
    survives inside a ``-c "..."`` argument (the SQL executes; the quotes are not
    inert). SQL-client gating avoids false-denying commit/PR bodies."""
    for seg in segments(command):
        head, _ = command_head(seg.split())
        if head in SQL_CLIENTS and DROP_RX.search(seg):
            return "deny", "Destructive SQL (DROP TABLE/DATABASE) requires explicit human approval."
    return None


def check(command: str) -> tuple[str, str]:
    """Return (decision, reason). decision in {'allow', 'deny'}."""
    # DROP is matched on the RAW command (see above); everything else on the sanitized
    # form so quoted bodies never trip a rule.
    drop = _drop_table_deny(command)
    if drop:
        return drop

    sanitized = strip_quotes(command)
    for seg in segments(sanitized):
        toks_raw = seg.split()
        if not toks_raw:
            continue
        head, toks = command_head(toks_raw)
        if not toks:
            continue

        # ---- git work-loss HARD-DENY (global floor only ASKS at T3) ----
        if head == "git":
            sub, sub_idx = git_subcommand(toks)
            args = toks[sub_idx + 1:] if sub_idx != -1 else []

            if sub == "reset" and "--hard" in args:
                return "deny", "Hard reset discards uncommitted work; inspect state and ask first."
            if sub == "clean" and any(re.match(r"^-[A-Za-z]*f", t) or t == "--force" for t in args):
                return "deny", "git clean -f deletes untracked work; ask first."
            if sub == "checkout" and "--" in args:
                return "deny", "git checkout -- <path> discards working-tree edits; ask first."
            if sub == "restore" and (
                "--worktree" in args
                or any(re.match(r"^-[A-Za-z]*W[A-Za-z]*$", t) for t in args)
            ):
                return "deny", "git restore --worktree/-W discards working-tree edits; ask first."
            if sub == "push":
                for t in args:
                    if (t in ("--force", "--force-with-lease")
                            or t.startswith("--force=")
                            or t.startswith("--force-with-lease=")):
                        return "deny", (
                            "Force-push (incl. --force-with-lease) is blocked by Taskdeck "
                            "policy; use merge-from-main + push HEAD:branch."
                        )

        # ---- repo-destructive (the global floor has none of these) ----
        if head == "rmdir" and any(re.match(r"^/[sS]$", t) for t in toks[1:]):
            return "deny", "Recursive directory removal (rmdir /s) requires explicit human approval."
        if head == "npm" and len(toks) >= 2 and toks[1].lower() == "publish":
            return "deny", "Publishing packages (npm publish) is outside normal Taskdeck workflow."
        if head == "dotnet" and [t.lower() for t in toks[1:4]] == ["ef", "database", "drop"]:
            return "deny", "Database drop (dotnet ef database drop) requires explicit human approval."
        if head == "chmod" and any(
            re.match(r"^-[A-Za-z]*R", t) or t.lower() == "--recursive" for t in toks[1:]
        ) and any("777" in t for t in toks[1:]):
            return "deny", "Recursive world-writable permissions (chmod -R 777) are blocked."

        # ---- recursive delete escaping the project (global blocks only ABSOLUTE) ----
        rm_recursive, rm_force = _rm_recursive_force(toks)
        is_rm_rf = head == "rm" and rm_recursive and rm_force
        is_recurse_del = head in ("remove-item", "ri") and any(
            re.match(r"^-recurse", t, re.IGNORECASE) for t in toks[1:]
        )
        if is_rm_rf or is_recurse_del:
            for tgt in (t for t in toks[1:] if not t.startswith("-")):
                bare = tgt.strip("'\"")
                if bare in (".", "..") or TRAVERSAL_RX.search(tgt):
                    return "deny", (
                        "Recursive delete of the project root/parent or an escaping '..' "
                        "path is blocked (worktree-leak guard). Delete a named in-project "
                        "target instead."
                    )

        # ---- broad secret-file mutation (global floor misses these filenames) ----
        if SECRET_PATH.search(seg) and SECRET_MUTATORS.search(seg):
            return "deny", (
                "Command appears to modify or move a secret/credential file; ask for "
                "explicit approval."
            )

    return "allow", ""


def _git_push_present(command: str) -> bool:
    for seg in segments(strip_quotes(command)):
        head, toks = command_head(seg.split())
        if head == "git" and toks and git_subcommand(toks)[0] == "push":
            return True
    return False


# --- entry ------------------------------------------------------------------------

def deny(reason: str) -> None:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))


def emit_context(message: str) -> None:
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "additionalContext": message,
        }
    }))


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        # Cannot even identify the command — allowing here matches the global floor's
        # "unparseable stdin -> allow" (denying would brick every session).
        return 0

    if str(payload.get("tool_name", "")) not in {"Bash", "Shell"}:
        return 0

    command = str((payload.get("tool_input") or {}).get("command", ""))
    if not command.strip():
        return 0

    try:
        decision, reason = check(command)
    except Exception as exc:  # fail CLOSED on rule-evaluation errors
        # This overlay is the bypass-proof enforcer of Taskdeck's stricter work-loss
        # denies; a crash must not silently open the gate. (The global floor still runs
        # as its own hook.)
        deny(f"pre_tool_use overlay error ({exc.__class__.__name__}); refusing to fail open.")
        return 0

    if decision == "deny":
        deny(reason)
        return 0

    if _git_push_present(command):
        emit_context(
            "[PRE-PUSH] Verify: tests passed, build clean, no secrets staged, "
            "commit messages are descriptive."
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
