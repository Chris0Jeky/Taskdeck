#!/usr/bin/env bash
# worktree_guard.sh — Validate that the current shell is inside a Claude Code
# git worktree (not the main checkout) and export canonical path variables.
#
# Usage:  source scripts/worktree_guard.sh
#
# Exports on success:
#   WT_REPO_ROOT   — absolute path to the worktree's git toplevel
#   WT_PROJECT_DIR — same as WT_REPO_ROOT (override below for sub-repo layouts)
#
# Returns 1 if running in the main checkout, 2 if not in a git repo.
#
# Why this exists: see docs/WORKTREE_AGENT_PROTOCOL.md

set -euo pipefail

_wt_toplevel="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo "ERROR [worktree_guard]: not inside a git repository." >&2
    return 2 2>/dev/null || exit 2
}

# Claude Code worktrees always live under .claude/worktrees/agent-<id>/
# Check both forward-slash (Unix/MSYS) and backslash (native Windows) forms.
if [[ "$_wt_toplevel" != */.claude/worktrees/* ]] && \
   [[ "$_wt_toplevel" != *\\.claude\\worktrees\\* ]]; then
    echo "=========================================================" >&2
    echo "FATAL [worktree_guard]: You are in the MAIN CHECKOUT." >&2
    echo "  toplevel: $_wt_toplevel" >&2
    echo "" >&2
    echo "Worktree agents must NOT operate on the main checkout." >&2
    echo "All git operations (checkout, commit, push) here will" >&2
    echo "collide with other parallel agents and corrupt state." >&2
    echo "=========================================================" >&2
    return 1 2>/dev/null || exit 1
fi

export WT_REPO_ROOT="$_wt_toplevel"

# ── Adjust this line for sub-repo layouts ─────────────────────────────
# If the project lives in a subdirectory of the git root, set it here:
#   export WT_PROJECT_DIR="$_wt_toplevel/my_subdir"
# Taskdeck is a single-repo layout, so project dir = repo root:
export WT_PROJECT_DIR="$_wt_toplevel"

echo "OK [worktree_guard]: Running in worktree."
echo "  WT_REPO_ROOT=$WT_REPO_ROOT"
echo "  WT_PROJECT_DIR=$WT_PROJECT_DIR"

unset _wt_toplevel
