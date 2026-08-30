#!/usr/bin/env bash
# Validate that the current shell is inside a linked agent worktree (not the
# main checkout) and export canonical path variables for worker prompts.
#
# Validation is by SUBSTANCE, not by path shape: a worktree root outside the
# repository (chosen deliberately on Windows to stay under MAX_PATH) is just as
# valid as .worktrees/ or .claude/worktrees/. The guard therefore checks that
#   1. the toplevel has a .git FILE holding a `gitdir:` pointer,
#   2. that pointer resolves to this repository's real git dir, which lives
#      under <main-repo>/.git/worktrees/<name> (so it is a linked worktree and
#      not the primary checkout, a plain clone, or a bare repository), and
#   3. HEAD resolves and matches the requested state (detached or a branch).
#
# Usage: source scripts/worktree_guard.sh
#
# Optional expectations (environment, default: accept detached or any branch):
#   WT_EXPECT_HEAD   - any | detached | branch
#   WT_EXPECT_BRANCH - branch name HEAD must be on (implies WT_EXPECT_HEAD=branch)
#
# Exit/return codes (unchanged contract):
#   0 - inside a valid linked worktree
#   1 - FATAL: main checkout / not a linked worktree / HEAD expectation unmet
#   2 - ERROR: not inside a git repository, or the layout could not be read
#
# Exports on success:
#   WT_REPO_ROOT    - absolute path to the worktree's git toplevel
#   WT_PROJECT_DIR  - same as WT_REPO_ROOT for Taskdeck's single-repo layout
#   WT_GIT_DIR      - this worktree's git dir (<main-repo>/.git/worktrees/<name>)
#   WT_HEAD_STATE   - "detached" or "branch"
#   WT_HEAD_BRANCH  - branch name when WT_HEAD_STATE=branch, otherwise empty

set -euo pipefail

# Print the canonical absolute form of an existing directory.
_wt_realdir() {
    (cd -- "$1" 2>/dev/null && pwd -P) || return 1
}

_wt_cleanup() {
    unset -v _wt_toplevel _wt_gitdir _wt_common _wt_worktrees_dir _wt_pointer \
        _wt_pointer_line _wt_pointer_dir _wt_head_branch _wt_head_state \
        _wt_expect_head _wt_expect_branch _wt_conventional 2>/dev/null || true
    unset -f _wt_realdir _wt_fatal 2>/dev/null || true
    unset -f _wt_cleanup 2>/dev/null || true
}

# _wt_fatal <headline> [detail lines...]
_wt_fatal() {
    local _wt_headline="$1"
    shift
    local _wt_line
    {
        echo "========================================================="
        echo "FATAL [worktree_guard]: $_wt_headline"
        for _wt_line in "$@"; do
            if [ -z "$_wt_line" ]; then
                echo ""
            else
                echo "  $_wt_line"
            fi
        done
        echo ""
        echo "Worktree agents must not operate on the main checkout."
        echo "All git operations here can collide with other parallel agents."
        echo "========================================================="
    } >&2
}

_wt_toplevel="$(git rev-parse --show-toplevel 2>/dev/null)" || {
    echo "ERROR [worktree_guard]: not inside a git repository." >&2
    _wt_cleanup
    return 2 2>/dev/null || exit 2
}

if [ -z "$_wt_toplevel" ]; then
    # Bare repositories and --git-dir-only contexts have no work tree at all.
    echo "ERROR [worktree_guard]: not inside a git work tree." >&2
    _wt_cleanup
    return 2 2>/dev/null || exit 2
fi

_wt_gitdir="$(git rev-parse --absolute-git-dir 2>/dev/null)" || _wt_gitdir=""
_wt_common="$(git rev-parse --git-common-dir 2>/dev/null)" || _wt_common=""
if [ -z "$_wt_gitdir" ] || [ -z "$_wt_common" ]; then
    echo "ERROR [worktree_guard]: repository layout could not be verified." >&2
    _wt_cleanup
    return 2 2>/dev/null || exit 2
fi

# --git-common-dir may be relative to the current directory; normalize both so
# they are comparable regardless of separator style or symlinks.
_wt_gitdir="$(_wt_realdir "$_wt_gitdir")" || _wt_gitdir=""
_wt_common="$(_wt_realdir "$_wt_common")" || _wt_common=""
if [ -z "$_wt_gitdir" ] || [ -z "$_wt_common" ]; then
    echo "ERROR [worktree_guard]: repository layout could not be resolved on disk." >&2
    _wt_cleanup
    return 2 2>/dev/null || exit 2
fi

# Substance check 1: this must be a LINKED worktree, i.e. its git dir lives
# under <common-git-dir>/worktrees/<name> and is not the common dir itself.
_wt_worktrees_dir="$_wt_common/worktrees"
case "$_wt_gitdir" in
    "$_wt_worktrees_dir"/*) ;;
    *)
        _wt_fatal "You are in the main checkout or an unrecognized worktree." \
            "toplevel:   $_wt_toplevel" \
            "git dir:    $_wt_gitdir" \
            "common dir: $_wt_common" \
            "" \
            "A linked worktree's git dir must live under <main-repo>/.git/worktrees/<name>."
        _wt_cleanup
        return 1 2>/dev/null || exit 1
        ;;
esac

# Substance check 2: the worktree root's .git must be a FILE whose gitdir
# pointer resolves to exactly that linked git dir.
if [ ! -f "$_wt_toplevel/.git" ]; then
    _wt_fatal "Worktree root has no linked-worktree .git pointer file." \
        "toplevel: $_wt_toplevel" \
        "" \
        "A linked worktree stores '.git' as a file containing 'gitdir: <path>'."
    _wt_cleanup
    return 1 2>/dev/null || exit 1
fi

_wt_pointer_line=""
IFS= read -r _wt_pointer_line < "$_wt_toplevel/.git" || true
_wt_pointer_line="${_wt_pointer_line%$'\r'}"
case "$_wt_pointer_line" in
    gitdir:*) _wt_pointer="${_wt_pointer_line#gitdir:}" ;;
    *)
        _wt_fatal "Worktree root .git file is not a gitdir pointer." \
            "toplevel:     $_wt_toplevel" \
            "first line:   $_wt_pointer_line"
        _wt_cleanup
        return 1 2>/dev/null || exit 1
        ;;
esac

# Trim surrounding whitespace from the pointer target (pure bash, no deps).
_wt_pointer="${_wt_pointer#"${_wt_pointer%%[![:space:]]*}"}"
_wt_pointer="${_wt_pointer%"${_wt_pointer##*[![:space:]]}"}"

# Git may store the pointer relative to the worktree root.
case "$_wt_pointer" in
    /*) ;;
    [A-Za-z]:[\\/]*) ;;
    *) _wt_pointer="$_wt_toplevel/$_wt_pointer" ;;
esac

_wt_pointer_dir="$(_wt_realdir "$_wt_pointer")" || _wt_pointer_dir=""
if [ -z "$_wt_pointer_dir" ] || [ "$_wt_pointer_dir" != "$_wt_gitdir" ]; then
    _wt_fatal "Worktree .git pointer does not resolve to this repository's linked git dir." \
        "toplevel: $_wt_toplevel" \
        "pointer:  ${_wt_pointer_dir:-$_wt_pointer}" \
        "git dir:  $_wt_gitdir"
    _wt_cleanup
    return 1 2>/dev/null || exit 1
fi

# Substance check 3: HEAD must resolve, and match the requested state.
if ! git rev-parse --verify --quiet HEAD >/dev/null 2>&1; then
    _wt_fatal "Worktree HEAD does not resolve to a commit." \
        "toplevel: $_wt_toplevel"
    _wt_cleanup
    return 1 2>/dev/null || exit 1
fi

_wt_head_branch="$(git symbolic-ref --quiet --short HEAD 2>/dev/null)" || _wt_head_branch=""
if [ -n "$_wt_head_branch" ]; then
    _wt_head_state="branch"
else
    _wt_head_state="detached"
fi

_wt_expect_head="${WT_EXPECT_HEAD:-any}"
_wt_expect_branch="${WT_EXPECT_BRANCH:-}"
if [ -n "$_wt_expect_branch" ] && [ "$_wt_expect_head" = "any" ]; then
    _wt_expect_head="branch"
fi

case "$_wt_expect_head" in
    any) ;;
    detached)
        if [ "$_wt_head_state" != "detached" ]; then
            _wt_fatal "Worktree HEAD is not detached as required." \
                "toplevel: $_wt_toplevel" \
                "HEAD:     branch $_wt_head_branch"
            _wt_cleanup
            return 1 2>/dev/null || exit 1
        fi
        ;;
    branch)
        if [ "$_wt_head_state" != "branch" ]; then
            _wt_fatal "Worktree HEAD is detached but a branch was required." \
                "toplevel: $_wt_toplevel" \
                "expected: ${_wt_expect_branch:-<any branch>}"
            _wt_cleanup
            return 1 2>/dev/null || exit 1
        fi
        if [ -n "$_wt_expect_branch" ] && [ "$_wt_head_branch" != "$_wt_expect_branch" ]; then
            _wt_fatal "Worktree HEAD is on the wrong branch." \
                "toplevel: $_wt_toplevel" \
                "expected: $_wt_expect_branch" \
                "actual:   $_wt_head_branch"
            _wt_cleanup
            return 1 2>/dev/null || exit 1
        fi
        ;;
    *)
        echo "ERROR [worktree_guard]: WT_EXPECT_HEAD must be any, detached, or branch (got '$_wt_expect_head')." >&2
        _wt_cleanup
        return 2 2>/dev/null || exit 2
        ;;
esac

export WT_REPO_ROOT="$_wt_toplevel"
export WT_PROJECT_DIR="$_wt_toplevel"
export WT_GIT_DIR="$_wt_gitdir"
export WT_HEAD_STATE="$_wt_head_state"
export WT_HEAD_BRANCH="$_wt_head_branch"

echo "OK [worktree_guard]: Running in an isolated worktree."
echo "  WT_REPO_ROOT=$WT_REPO_ROOT"
echo "  WT_PROJECT_DIR=$WT_PROJECT_DIR"
echo "  WT_HEAD_STATE=$WT_HEAD_STATE${WT_HEAD_BRANCH:+ ($WT_HEAD_BRANCH)}"

# Conventional roots are advisory only; an out-of-repo root is still valid.
_wt_conventional=0
case "$_wt_toplevel" in
    */.claude/worktrees/*|*\\.claude\\worktrees\\*) _wt_conventional=1 ;;
    */.codex/worktrees/*|*\\.codex\\worktrees\\*) _wt_conventional=1 ;;
    */.worktrees/*|*\\.worktrees\\*) _wt_conventional=1 ;;
esac
if [ "$_wt_conventional" -eq 0 ]; then
    echo "NOTE [worktree_guard]: root is outside the conventional worktree directories; accepted on linked-worktree substance."
fi

_wt_cleanup
