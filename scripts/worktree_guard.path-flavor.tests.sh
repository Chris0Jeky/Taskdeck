#!/usr/bin/env bash
# Cross-platform contract for the shell guard's exported path variables.
# Internal validation may use an MSYS/POSIX physical path, but the public
# WT_REPO_ROOT and WT_GIT_DIR values must share one flavour so a caller can hand
# either value to the same native tool without a platform-specific conversion.

set -euo pipefail

_tests_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
GUARD="${WT_GUARD_SH:-$_tests_dir/worktree_guard.sh}"
FIXTURE_ROOT="$(mktemp -d)"

cleanup() {
    if [ -d "$FIXTURE_ROOT/primary" ]; then
        git -C "$FIXTURE_ROOT/primary" worktree remove "$FIXTURE_ROOT/linked" >/dev/null 2>&1 || true
    fi
    rm -rf -- "$FIXTURE_ROOT" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

path_flavor() {
    case "$1" in
        [A-Za-z]:[\\/]*) printf 'windows\n' ;;
        /*) printf 'posix\n' ;;
        *) printf 'unknown\n' ;;
    esac
}

if [ ! -f "$GUARD" ]; then
    fail "guard script not found: $GUARD"
fi

git init -q -b main "$FIXTURE_ROOT/primary"
git -C "$FIXTURE_ROOT/primary" \
    -c user.email=t@example.com \
    -c user.name=t \
    commit -q --allow-empty -m "seed" --no-gpg-sign
git -C "$FIXTURE_ROOT/primary" worktree add -q --detach "$FIXTURE_ROOT/linked" HEAD

mapfile -t exported < <(
    cd -- "$FIXTURE_ROOT/linked"
    bash -c '
        source "$1" >/dev/null
        printf "%s\n%s\n" "$WT_REPO_ROOT" "$WT_GIT_DIR"
    ' bash "$GUARD"
)

if [ "${#exported[@]}" -ne 2 ]; then
    fail "guard did not emit exactly the two exported paths"
fi

repo_root="${exported[0]}"
git_dir="${exported[1]}"
repo_flavor="$(path_flavor "$repo_root")"
git_flavor="$(path_flavor "$git_dir")"

if [ "$repo_flavor" = "unknown" ] || [ "$git_flavor" = "unknown" ]; then
    printf 'WT_REPO_ROOT=%s\nWT_GIT_DIR=%s\n' "$repo_root" "$git_dir" >&2
    fail "guard exported an unrecognized path flavour"
fi
if [ "$repo_flavor" != "$git_flavor" ]; then
    printf 'WT_REPO_ROOT=%s (%s)\nWT_GIT_DIR=%s (%s)\n' \
        "$repo_root" "$repo_flavor" "$git_dir" "$git_flavor" >&2
    fail "WT_REPO_ROOT and WT_GIT_DIR use different path flavours"
fi

printf 'worktree_guard path-flavour contract passed: %s.\n' "$repo_flavor"
