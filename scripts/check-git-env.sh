#!/usr/bin/env bash
# scripts/check-git-env.sh
# Validates the local Git environment on Windows for safe agent/local workflows.
# Checks:
#   1. git resolves to Git for Windows (not Cygwin/MSYS)
#   2. No stale .git/index.lock without an active git process
#
# Usage:
#   bash scripts/check-git-env.sh            # from repo root
#   bash scripts/check-git-env.sh /path/to   # check a specific repo

set -euo pipefail

REPO_DIR="${1:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
EXIT_CODE=0

# ---------------------------------------------------------------------------
# 1. Git executable resolution
# ---------------------------------------------------------------------------
GIT_PATH="$(command -v git 2>/dev/null || true)"

if [ -z "$GIT_PATH" ]; then
    echo "[ERROR] git is not on PATH. Install Git for Windows from https://git-scm.com"
    EXIT_CODE=1
else
    GIT_VERSION="$(git --version 2>/dev/null || true)"
    echo "[INFO]  git path:    $GIT_PATH"
    echo "[INFO]  git version: $GIT_VERSION"

    # Detect Cygwin or MSYS2 (non-MinGW) git — path typically contains
    # /cygdrive/ or /usr/bin/. Git for Windows (MinGW) resolves to /mingw64/bin/git.
    case "$GIT_PATH" in
        /cygdrive/*|/usr/bin/git)
            echo "[WARN]  git appears to be Cygwin or MSYS2 (non-MinGW) git ($GIT_PATH)."
            echo "        This can cause signal errors and path translation issues."
            echo "        Fix: add 'C:\\Program Files\\Git\\cmd' to the FRONT of your PATH,"
            echo "        or set alias git='\"C:\\Program Files\\Git\\cmd\\git.exe\"' in your shell profile."
            EXIT_CODE=1
            ;;
    esac

    if echo "$GIT_VERSION" | grep -qi "cygwin"; then
        echo "[WARN]  git --version reports Cygwin: $GIT_VERSION"
        echo "        Use Git for Windows instead. See guidance above."
        EXIT_CODE=1
    fi

    if [ $EXIT_CODE -eq 0 ]; then
        echo "[OK]    git resolves to a non-Cygwin executable."
    fi
fi

# ---------------------------------------------------------------------------
# 2. Stale .git/index.lock detection
# ---------------------------------------------------------------------------
# In worktrees, .git is a file pointing to the real git dir.
# Use git rev-parse to find the actual git directory.
# --absolute-git-dir (Git 2.13+) returns an absolute path, avoiding the bug
# where a relative ".git" would resolve against CWD instead of REPO_DIR.
GIT_DIR="$(git -C "$REPO_DIR" rev-parse --absolute-git-dir 2>/dev/null || echo "$REPO_DIR/.git")"
LOCK_FILE="$GIT_DIR/index.lock"

if [ -f "$LOCK_FILE" ]; then
    echo ""
    echo "[WARN]  Stale lock file found: $LOCK_FILE"

    # Check for active git processes (Windows: tasklist; Unix: pgrep)
    ACTIVE_GIT=""
    if command -v tasklist &>/dev/null; then
        ACTIVE_GIT="$(tasklist //FI "IMAGENAME eq git.exe" 2>/dev/null | grep -i "git.exe" || true)"
    elif command -v pgrep &>/dev/null; then
        ACTIVE_GIT="$(pgrep -a git 2>/dev/null || true)"
    fi

    if [ -n "$ACTIVE_GIT" ]; then
        echo "[WARN]  Active git process(es) detected — lock may be legitimate:"
        echo "$ACTIVE_GIT"
        echo "        Wait for the process to finish, or kill it if stuck, then remove the lock."
    else
        echo "[WARN]  No active git processes found. The lock file is likely stale."
        echo "        Safe to remove: rm \"$LOCK_FILE\""
    fi
    EXIT_CODE=1
else
    echo "[OK]    No .git/index.lock present."
fi

echo ""
if [ $EXIT_CODE -eq 0 ]; then
    echo "All checks passed."
else
    echo "One or more issues detected. See warnings above."
fi

exit $EXIT_CODE
