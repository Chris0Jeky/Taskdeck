#!/usr/bin/env bash
# Validate the local Git environment for safe agent/local workflows.
#
# Checks:
#   1. git resolves to Git for Windows or another non-Cygwin executable
#   2. no stale .git/index.lock exists without an active git process
#
# Usage:
#   bash scripts/check-git-env.sh            # from repo root
#   bash scripts/check-git-env.sh /path/to   # check a specific repo

set -euo pipefail

REPO_DIR="${1:-$(git rev-parse --show-toplevel 2>/dev/null || pwd)}"
EXIT_CODE=0

GIT_PATH="$(command -v git 2>/dev/null || true)"

if [ -z "$GIT_PATH" ]; then
    echo "[ERROR] git is not on PATH. Install Git for Windows from https://git-scm.com"
    EXIT_CODE=1
else
    GIT_VERSION="$(git --version 2>/dev/null || true)"
    echo "[INFO]  git path:    $GIT_PATH"
    echo "[INFO]  git version: $GIT_VERSION"

    case "$GIT_PATH" in
        /cygdrive/*|/usr/bin/git)
            echo "[WARN]  git appears to be Cygwin or MSYS2 (non-MinGW) git ($GIT_PATH)."
            echo "        This can cause signal errors and path translation issues."
            echo "        Fix: add 'C:\\Program Files\\Git\\cmd' to the front of PATH."
            EXIT_CODE=1
            ;;
    esac

    if echo "$GIT_VERSION" | grep -qi "cygwin"; then
        echo "[WARN]  git --version reports Cygwin: $GIT_VERSION"
        EXIT_CODE=1
    fi

    if [ $EXIT_CODE -eq 0 ]; then
        echo "[OK]    git resolves to a non-Cygwin executable."
    fi
fi

GIT_DIR="$(git -C "$REPO_DIR" rev-parse --absolute-git-dir 2>/dev/null || echo "$REPO_DIR/.git")"
LOCK_FILE="$GIT_DIR/index.lock"

if [ -f "$LOCK_FILE" ]; then
    echo ""
    echo "[WARN]  Git index lock found: $LOCK_FILE"

    ACTIVE_GIT=""
    if command -v tasklist >/dev/null 2>&1; then
        ACTIVE_GIT="$(tasklist //FI "IMAGENAME eq git.exe" 2>/dev/null | grep -i "git.exe" || true)"
    elif command -v pgrep >/dev/null 2>&1; then
        ACTIVE_GIT="$(pgrep -a git 2>/dev/null || true)"
    fi

    if [ -n "$ACTIVE_GIT" ]; then
        echo "[WARN]  Active git process(es) detected; do not remove the lock yet:"
        echo "$ACTIVE_GIT"
    else
        echo "[WARN]  No active git processes found. The lock file is likely stale."
        echo "        Safe to remove after review: rm \"$LOCK_FILE\""
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
