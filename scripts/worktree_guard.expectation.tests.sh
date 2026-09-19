#!/usr/bin/env bash
# Cross-shell regression contract for malformed or contradictory worktree HEAD
# expectations. These are caller setup errors, not conditions either guard may
# silently resolve, normalize differently, or treat as ordinary HEAD mismatch.

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

normalize_assertion_output() {
    printf '%s' "$1" |
        tr '\r\n\t' '   ' |
        sed 's/[[:space:]][[:space:]]*/ /g'
}

assert_setup_error() {
    local name="$1"
    local code="$2"
    local output="$3"
    local expected="$4"
    local normalized_output

    if [ "$code" -ne 2 ]; then
        printf '%s\n' "$output" >&2
        fail "$name must exit 2 for invalid expectations (got $code)"
    fi

    normalized_output="$(normalize_assertion_output "$output")"
    if ! printf '%s' "$normalized_output" | grep -qF -- "$expected"; then
        printf '%s\n' "$output" >&2
        fail "$name did not explain the invalid expectations (missing '$expected')"
    fi
    pass "$name rejects invalid expectations as a setup error"
}

assert_named_branch_mismatch() {
    local name="$1"
    local code="$2"
    local output="$3"
    local normalized_output

    if [ "$code" -ne 1 ]; then
        printf '%s\n' "$output" >&2
        fail "$name must treat the value as a named branch expectation (got $code)"
    fi

    normalized_output="$(normalize_assertion_output "$output")"
    if ! printf '%s' "$normalized_output" | grep -qF -- "detached but a branch was required"; then
        printf '%s\n' "$output" >&2
        fail "$name did not reach the ordinary named-branch mismatch"
    fi
    pass "$name treats the value as a named branch expectation"
}

assert_setup_error "formatter-wrapped setup error" \
    2 $'ERROR: cannot be\r\ncombined with -ExpectedBranch' "cannot be combined"

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
        bash -c 'source "$1"' bash "$SH_GUARD" 2>&1
)"
sh_code=$?
set -e
assert_setup_error "shell guard contradictory expectation" "$sh_code" "$sh_output" "cannot be combined"

set +e
sh_whitespace_output="$(
    cd -- "$FIXTURE_ROOT/detached"
    WT_EXPECT_HEAD=any \
    WT_EXPECT_BRANCH='   ' \
        bash -c 'source "$1"' bash "$SH_GUARD" 2>&1
)"
sh_whitespace_code=$?
set -e
assert_setup_error "shell guard whitespace-only branch" \
    "$sh_whitespace_code" "$sh_whitespace_output" "cannot be whitespace-only"

unicode_branch=$'\u00a0'
set +e
sh_unicode_output="$(
    cd -- "$FIXTURE_ROOT/detached"
    WT_EXPECT_HEAD=any \
    WT_EXPECT_BRANCH="$unicode_branch" \
        bash -c 'source "$1"' bash "$SH_GUARD" 2>&1
)"
sh_unicode_code=$?
set -e
assert_named_branch_mismatch "shell guard Unicode branch" \
    "$sh_unicode_code" "$sh_unicode_output"

# The space character alone cannot distinguish a correctly built ASCII-whitespace
# class from a mis-built one, so exercise a non-space member (tab) and a control
# made only of the letters that a literally-taken "\t\n\r\v\f" escape would
# contain. A guard whose class degraded to those letters rejects "fnrtv" as
# whitespace-only, which the two cases below catch in opposite directions.
tab_branch=$'\t'
literal_escape_branch="fnrtv"
set +e
sh_tab_output="$(
    cd -- "$FIXTURE_ROOT/detached"
    WT_EXPECT_HEAD=any \
    WT_EXPECT_BRANCH="$tab_branch" \
        bash -c 'source "$1"' bash "$SH_GUARD" 2>&1
)"
sh_tab_code=$?
set -e
assert_setup_error "shell guard tab-only branch" \
    "$sh_tab_code" "$sh_tab_output" "cannot be whitespace-only"

set +e
sh_literal_escape_output="$(
    cd -- "$FIXTURE_ROOT/detached"
    WT_EXPECT_HEAD=any \
    WT_EXPECT_BRANCH="$literal_escape_branch" \
        bash -c 'source "$1"' bash "$SH_GUARD" 2>&1
)"
sh_literal_escape_code=$?
set -e
assert_named_branch_mismatch "shell guard escape-letter branch" \
    "$sh_literal_escape_code" "$sh_literal_escape_output"

if [ -z "$PS_EXE" ]; then
    printf '  SKIP: PowerShell guard contract (no powershell/pwsh on PATH)\n'
else
    set +e
    ps_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Detached \
            -ExpectedBranch guard-test-branch 2>&1
    )"
    ps_code=$?
    set -e
    assert_setup_error "PowerShell guard contradictory expectation" \
        "$ps_code" "$ps_output" "cannot be combined"

    set +e
    ps_whitespace_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Any \
            -ExpectedBranch '   ' 2>&1
    )"
    ps_whitespace_code=$?
    set -e
    assert_setup_error "PowerShell guard whitespace-only branch" \
        "$ps_whitespace_code" "$ps_whitespace_output" "cannot be whitespace-only"

    set +e
    ps_unicode_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Any \
            -ExpectedBranch "$unicode_branch" 2>&1
    )"
    ps_unicode_code=$?
    set -e
    assert_named_branch_mismatch "PowerShell guard Unicode branch" \
        "$ps_unicode_code" "$ps_unicode_output"

    set +e
    ps_tab_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Any \
            -ExpectedBranch "$tab_branch" 2>&1
    )"
    ps_tab_code=$?
    set -e
    assert_setup_error "PowerShell guard tab-only branch" \
        "$ps_tab_code" "$ps_tab_output" "cannot be whitespace-only"

    set +e
    ps_literal_escape_output="$(
        cd -- "$FIXTURE_ROOT/detached"
        "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD_NATIVE" \
            -ExpectHead Any \
            -ExpectedBranch "$literal_escape_branch" 2>&1
    )"
    ps_literal_escape_code=$?
    set -e
    assert_named_branch_mismatch "PowerShell guard escape-letter branch" \
        "$ps_literal_escape_code" "$ps_literal_escape_output"
fi

printf 'worktree_guard expectation contract passed.\n'
