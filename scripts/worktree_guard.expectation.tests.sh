#!/usr/bin/env bash
# Cross-shell regression contract for contradictory worktree HEAD expectations.
# Supplying a branch name while explicitly requiring a detached HEAD is a caller
# setup error, not a condition either guard may silently resolve or ignore.

set -euo pipefail

_tests_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
SH_GUARD="${WT_GUARD_SH:-$_tests_dir/worktree_guard.sh}"
PS_GUARD="${WT_GUARD_PS:-$_tests_dir/worktree_guard.ps1}"
PS_GUARD_NATIVE="$PS_GUARD"
if command -v cygpath >/dev/null 2>&1; then
    PS_GUARD_NATIVE="$(cygpath -w "$PS_GUARD" 2>/dev/null || printf '%s' "$PS_GUARD")"
fi

PS_EXE=""
if command -v powershell >/dev/null 2>&1; then
    PS_EXE="powershell"
elif command -v pwsh >/dev/null 2>&1; then
    PS_EXE="pwsh"
fi

FIXTURE_ROOT="$(mktemp -d)"
cleanup() {
    if [ -d "$FIXTURE_ROOT/primary" ]; then
        git -C "$FIXTURE_ROOT/primary" worktree remove "$FIXTURE_ROOT/detached" >/dev/null 2>&1 || true
    fi
    rm -rf -- "$FIXTURE_ROOT" 2>/dev/null || true
}
trap cleanup EXIT

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

pass() {
    printf '  PASS: %s\n' "$1"
}

assert_setup_error() {
    local name="$1"
    local code="$2"
    local output="$3"

    if [ "$code" -ne 2 ]; then
        printf '%s\n' "$output" >&2
        fail "$name must exit 2 for contradictory expectations (got $code)"
    fi
    if ! printf '%s' "$output" | grep -qF -- "cannot be combined"; then
        printf '%s\n' "$output" >&2
        fail "$name did not explain the contradictory expectations"
    fi
    pass "$name rejects contradictory expectations as a setup error"
}

if [ ! -f "$SH_GUARD" ] || [ ! -f "$PS_GUARD" ]; then
    fail "guard scripts were not found beside the contract"
fi

git init -q -b main "$FIXTURE_ROOT/primary"
git -C "$FIXTURE_ROOT/primary" \
    -c user.email=t@example.com \
    -c user.name=t \
    commit -q --allow-empty -m "seed" --no-gpg-sign
git -C "$FIXTURE_ROOT/primary" worktree add -q --detach "$FIXTURE_ROOT/detached" HEAD

set +e
sh_output="$(
    cd -- "$FIXTURE_ROOT/detached"
    WT_EXPECT_HEAD=detached \
    WT_EXPECT_BRANCH=guard-test-branch \
        bash -c 'source "$1"' bash "$SH_GUARD"
 2>&1)"
sh_code=$?
set -e
assert_setup_error "shell guard" "$sh_code" "$sh_output"

if [ -z "$PS_EXE" ]; then
    printf '  SKIP: PowerShell guard contract (no powershell/pwsh on PATH)\n'
else
    set +e
    ps_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Detached \
            -ExpectedBranch guard-test-branch
     2>&1)"
    ps_code=$?
    set -e
    assert_setup_error "PowerShell guard" "$ps_code" "$ps_output"
fi

printf 'worktree_guard expectation contract passed.\n'
