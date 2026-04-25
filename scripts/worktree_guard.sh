#!/usr/bin/env bash
# Validate that the current shell is inside an agent worktree (not the main
# checkout) and export canonical path variables for worker prompts.
#
# Usage: source scripts/worktree_guard.sh
#
# Exports on success:
#   WT_REPO_ROOT   - absolute path to the worktree's git toplevel
#   WT_PROJECT_DIR - same as WT_REPO_ROOT for Taskdeck's single-repo layout

set -euo pipefail

_wt_toplevel="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo "ERROR [worktree_guard]: not inside a git repository." >&2
    return 2 2>/dev/null || exit 2
}

# Claude Code worktrees live under .claude/worktrees/agent-<id>/.
# Codex issue worktrees should live under .worktrees/codex-<issue>-<slug>/.
# Check both forward-slash (Unix/MSYS) and backslash (native Windows) forms.
if [[ "$_wt_toplevel" != */.claude/worktrees/* ]] && \
   [[ "$_wt_toplevel" != *\\.claude\\worktrees\\* ]] && \
   [[ "$_wt_toplevel" != */.worktrees/* ]] && \
   [[ "$_wt_toplevel" != *\\.worktrees\\* ]]; then
    echo "=========================================================" >&2
    echo "FATAL [worktree_guard]: You are in the main checkout." >&2
    echo "  toplevel: $_wt_toplevel" >&2
    echo "" >&2
    echo "Worktree agents must not operate on the main checkout." >&2
    echo "All git operations here can collide with other parallel agents." >&2
    echo "=========================================================" >&2
    return 1 2>/dev/null || exit 1
fi

export WT_REPO_ROOT="$_wt_toplevel"
export WT_PROJECT_DIR="$_wt_toplevel"

echo "OK [worktree_guard]: Running in an isolated worktree."
echo "  WT_REPO_ROOT=$WT_REPO_ROOT"
echo "  WT_PROJECT_DIR=$WT_PROJECT_DIR"

unset _wt_toplevel
