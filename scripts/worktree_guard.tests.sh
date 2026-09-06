#!/usr/bin/env bash
# Self-tests for scripts/worktree_guard.sh and scripts/worktree_guard.ps1.
#
# Both guards must accept ANY linked worktree (including a short out-of-repo
# root chosen to stay under the Windows MAX_PATH limit) and must still reject a
# primary checkout, a standalone clone that merely sits under a .worktrees
# path, a bare repository, and a non-repository directory.
#
# Usage:
#   bash scripts/worktree_guard.tests.sh
#
# The PowerShell guard is exercised only when a `powershell` or `pwsh`
# executable is on PATH; otherwise those cases are reported as skipped.
#
# WT_GUARD_SH / WT_GUARD_PS override which guard scripts are exercised, so a
# mutated or historical copy can be run against the same expectations.

set -uo pipefail

_tests_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
SH_GUARD="${WT_GUARD_SH:-$_tests_dir/worktree_guard.sh}"
PS_GUARD="${WT_GUARD_PS:-$_tests_dir/worktree_guard.ps1}"
# PowerShell resolves an argument path itself, but a path embedded in a
# -Command string must already be native (MSYS /c/... is not resolvable).
PS_GUARD_NATIVE="$PS_GUARD"
if command -v cygpath >/dev/null 2>&1; then
    PS_GUARD_NATIVE="$(cygpath -w "$PS_GUARD" 2>/dev/null || printf '%s' "$PS_GUARD")"
fi
_MKTEMP_COMMAND="${WT_GUARD_MKTEMP:-mktemp}"

PASS=0
FAIL=0
SKIP=0

PS_EXE=""
if command -v powershell >/dev/null 2>&1; then
    PS_EXE="powershell"
elif command -v pwsh >/dev/null 2>&1; then
    PS_EXE="pwsh"
fi

pass() { PASS=$((PASS + 1)); echo "  PASS: $1"; }
fail() { FAIL=$((FAIL + 1)); echo "  FAIL: $1"; }
skip() { SKIP=$((SKIP + 1)); echo "  SKIP: $1"; }

# Leading NAME=VALUE arguments become environment for the guard process; the
# rest are passed through as guard arguments (PowerShell guard only).
_split_env() {
    GUARD_ENV=()
    GUARD_ARGS=()
    local arg
    local still_env=1
    for arg in "$@"; do
        if [ "$still_env" -eq 1 ] && [[ "$arg" == [A-Za-z_]*=* ]]; then
            GUARD_ENV+=("$arg")
        else
            still_env=0
            GUARD_ARGS+=("$arg")
        fi
    done
}

# run_sh_guard <dir> [ENV=VAL ...] -> prints output, returns guard exit code
run_sh_guard() {
    local dir="$1"
    shift
    _split_env "$@"
    local out
    out="$(cd -- "$dir" && env ${GUARD_ENV[@]+"${GUARD_ENV[@]}"} bash -c "source '$SH_GUARD'" 2>&1)"
    local code=$?
    printf '%s\n' "$out"
    return $code
}

# run_ps_guard <dir> [ENV=VAL ...] [-Param Value ...]
run_ps_guard() {
    local dir="$1"
    shift
    _split_env "$@"
    local out
    out="$(cd -- "$dir" && env ${GUARD_ENV[@]+"${GUARD_ENV[@]}"} "$PS_EXE" -NoLogo -NoProfile -NonInteractive -File "$PS_GUARD" ${GUARD_ARGS[@]+"${GUARD_ARGS[@]}"} 2>&1)"
    local code=$?
    printf '%s\n' "$out"
    return $code
}

# expect_sh <name> <expected-code> <dir> [ENV=VAL ...]
expect_sh() {
    local name="$1" expected="$2" dir="$3"
    shift 3
    local output
    output="$(run_sh_guard "$dir" "$@")"
    local code=$?
    if [ "$code" -eq "$expected" ]; then
        pass "sh: $name (exit $code)"
    else
        fail "sh: $name (expected exit $expected, got $code)"
        printf '%s\n' "$output" | sed 's/^/        /'
    fi
}

# expect_ps <name> <expected-code> <dir> [args...]
expect_ps() {
    local name="$1" expected="$2" dir="$3"
    shift 3
    if [ -z "$PS_EXE" ]; then
        skip "ps: $name (no PowerShell on PATH)"
        return 0
    fi
    local output
    output="$(run_ps_guard "$dir" "$@")"
    local code=$?
    if [ "$code" -eq "$expected" ]; then
        pass "ps: $name (exit $code)"
    else
        fail "ps: $name (expected exit $expected, got $code)"
        printf '%s\n' "$output" | sed 's/^/        /'
    fi
}

# The printed worker handoff calls the PowerShell guard IN PROCESS and gates on
# `$?` plus `$LASTEXITCODE`, so success must leave both clean even though the
# guard runs git commands that legitimately exit non-zero (symbolic-ref on a
# detached HEAD). expect_ps_inprocess <name> <dir>
expect_ps_inprocess() {
    local name="$1" dir="$2"
    if [ -z "$PS_EXE" ]; then
        skip "ps: $name (no PowerShell on PATH)"
        return 0
    fi
    local script="& '$PS_GUARD_NATIVE'; if (-not \$?) { exit 91 }; if (\$LASTEXITCODE -ne 0) { exit 92 }; exit 0"
    local output
    output="$(cd -- "$dir" && "$PS_EXE" -NoLogo -NoProfile -NonInteractive -Command "$script" 2>&1)"
    local code=$?
    if [ "$code" -eq 0 ]; then
        pass "ps: $name (in-process, exit $code)"
    else
        fail "ps: $name (in-process guard left \$?/\$LASTEXITCODE dirty, exit $code)"
        printf '%s\n' "$output" | sed 's/^/        /'
    fi
}

# expect_sh_output <name> <needle> <dir>
expect_sh_output() {
    local name="$1" needle="$2" dir="$3"
    local output
    output="$(run_sh_guard "$dir")"
    local code=$?
    if [ "$code" -eq 0 ] && printf '%s' "$output" | grep -qF -- "$needle"; then
        pass "sh: $name"
        return 0
    else
        fail "sh: $name (expected exit 0 and output containing '$needle', got exit $code)"
        printf '%s\n' "$output" | sed 's/^/        /'
        return 1
    fi
}

allocate_fixture_root() {
    local candidate=""
    if candidate="$("$_MKTEMP_COMMAND" -d 2>/dev/null)" &&
        [ -n "$candidate" ] && [ -d "$candidate" ]; then
        printf '%s\n' "$candidate"
        return 0
    fi

    if candidate="$("$_MKTEMP_COMMAND" -d -t wtguard 2>/dev/null)" &&
        [ -n "$candidate" ] && [ -d "$candidate" ]; then
        printf '%s\n' "$candidate"
        return 0
    fi

    return 2
}

FIXTURE_ROOT=""
cleanup() {
    # Plain `git worktree remove` only; never --force.
    if [ -z "${FIXTURE_ROOT:-}" ] || [ ! -d "$FIXTURE_ROOT" ]; then
        return 0
    fi
    git -C "$FIXTURE_ROOT/primary" worktree remove "$FIXTURE_ROOT/primary/.worktrees/inrepo" >/dev/null 2>&1 || true
    git -C "$FIXTURE_ROOT/primary" worktree remove "$FIXTURE_ROOT/short" >/dev/null 2>&1 || true
    rm -rf -- "$FIXTURE_ROOT" 2>/dev/null || true
}
trap cleanup EXIT

if [ "${WT_GUARD_ALLOCATE_ONLY:-0}" -eq 1 ]; then
    if FIXTURE_ROOT="$(allocate_fixture_root)"; then
        if [ -z "$FIXTURE_ROOT" ] || [ ! -d "$FIXTURE_ROOT" ]; then
            echo "worktree_guard self-tests: mktemp returned an unusable fixture root" >&2
            exit 2
        fi
        rm -rf -- "$FIXTURE_ROOT"
        FIXTURE_ROOT=""
        exit 0
    fi
    FIXTURE_ROOT=""
    echo "worktree_guard self-tests: unable to allocate fixture root" >&2
    exit 2
fi

if ! FIXTURE_ROOT="$(allocate_fixture_root)"; then
    echo "worktree_guard self-tests: unable to allocate fixture root" >&2
    exit 2
fi
if [ -z "$FIXTURE_ROOT" ] || [ ! -d "$FIXTURE_ROOT" ]; then
    echo "worktree_guard self-tests: mktemp returned an unusable fixture root" >&2
    exit 2
fi

echo "worktree_guard self-tests (fixture: $FIXTURE_ROOT)"

# --- fixtures -------------------------------------------------------------
git init -q -b main "$FIXTURE_ROOT/primary"
git -C "$FIXTURE_ROOT/primary" -c user.email=t@example.com -c user.name=t \
    commit -q --allow-empty -m "seed" --no-gpg-sign
git -C "$FIXTURE_ROOT/primary" worktree add -q --detach "$FIXTURE_ROOT/primary/.worktrees/inrepo" HEAD
git -C "$FIXTURE_ROOT/primary" worktree add -q --detach "$FIXTURE_ROOT/short" HEAD
mkdir -p "$FIXTURE_ROOT/plain"
# Mutation control for expect_sh_output: an advisory NOTE must not turn a
# failing guard into a passing case merely because the text is present.
cat > "$FIXTURE_ROOT/note-then-fail.sh" <<'EOF'
echo "NOTE [worktree_guard]: synthetic failure"
return 1 2>/dev/null || exit 1
EOF
git init -q --bare "$FIXTURE_ROOT/bare.git"
# A standalone clone whose path merely LOOKS like a worktree root.
mkdir -p "$FIXTURE_ROOT/.worktrees"
git clone -q "$FIXTURE_ROOT/primary" "$FIXTURE_ROOT/.worktrees/decoy-clone"

echo "fixture-root allocation"
fake_mktemp="$FIXTURE_ROOT/fake-mktemp.sh"
cat > "$fake_mktemp" <<'EOF'
#!/usr/bin/env bash
set -u
if [ -n "${WT_GUARD_FAKE_LOG:-}" ]; then
    printf '%s\n' "$*" >> "$WT_GUARD_FAKE_LOG"
fi
if [ "${WT_GUARD_MKTEMP_MODE:-}" = "first-fails" ] && [ "$#" -eq 3 ]; then
    mkdir -p "$WT_GUARD_FAKE_ROOT/fallback"
    printf '%s\n' "$WT_GUARD_FAKE_ROOT/fallback"
    exit 0
fi
exit 1
EOF
chmod +x "$fake_mktemp"

fallback_root="$FIXTURE_ROOT/fallback-probe"
fallback_log="$fallback_root/mktemp.log"
mkdir -p "$fallback_root"
if output="$(
    WT_GUARD_ALLOCATE_ONLY=1 \
    WT_GUARD_MKTEMP_MODE=first-fails \
    WT_GUARD_MKTEMP="$fake_mktemp" \
    WT_GUARD_FAKE_ROOT="$fallback_root" \
    WT_GUARD_FAKE_LOG="$fallback_log" \
    bash "$BASH_SOURCE" 2>&1
)" &&
    [ "$(wc -l < "$fallback_log")" -eq 2 ] &&
    [ ! -d "$fallback_root/fallback" ]; then
    pass "fixture-root allocation falls back after first mktemp form fails"
else
    fail "fixture-root allocation falls back after first mktemp form fails"
    printf '%s\n' "$output"
fi

failure_root="$FIXTURE_ROOT/failure-probe"
failure_log="$failure_root/mktemp.log"
mkdir -p "$failure_root"
if output="$(
    WT_GUARD_ALLOCATE_ONLY=1 \
    WT_GUARD_MKTEMP_MODE=both-fail \
    WT_GUARD_MKTEMP="$fake_mktemp" \
    WT_GUARD_FAKE_ROOT="$failure_root" \
    WT_GUARD_FAKE_LOG="$failure_log" \
    bash "$BASH_SOURCE" 2>&1
)"; then
    fail "fixture-root allocation fails before fixture commands when both mktemp forms fail"
    printf '%s\n' "$output"
else
    code=$?
    if [ "$code" -eq 2 ] &&
        [ "$(wc -l < "$failure_log")" -eq 2 ] &&
        [ ! -d "$failure_root/primary" ] &&
        [ ! -d "$failure_root/bare.git" ] &&
        ! printf '%s' "$output" | grep -qE '/primary|/bare\.git|git -C|rm -rf'; then
        pass "fixture-root allocation fails before fixture commands when both mktemp forms fail"
    else
        fail "fixture-root allocation fails before fixture commands when both mktemp forms fail (exit $code)"
        printf '%s\n' "$output"
    fi
fi

# --- accepted: real linked worktrees -------------------------------------
echo "accepted linked worktrees"
expect_sh "in-repo .worktrees/ root accepted" 0 "$FIXTURE_ROOT/primary/.worktrees/inrepo"
expect_ps "in-repo .worktrees/ root accepted" 0 "$FIXTURE_ROOT/primary/.worktrees/inrepo"
expect_sh "short out-of-repo root accepted" 0 "$FIXTURE_ROOT/short"
expect_ps "short out-of-repo root accepted" 0 "$FIXTURE_ROOT/short"
expect_sh_output "out-of-repo root reports the advisory NOTE" \
    "NOTE [worktree_guard]:" "$FIXTURE_ROOT/short"
if output="$({
    SH_GUARD="$FIXTURE_ROOT/note-then-fail.sh"
    expect_sh_output "NOTE-only failure is rejected" \
        "NOTE [worktree_guard]:" "$FIXTURE_ROOT/plain"
})"; then
    fail "sh: NOTE-only failure was accepted"
else
    pass "sh: NOTE-only failure is rejected"
fi
expect_ps_inprocess "detached worktree leaves a clean in-process exit code" "$FIXTURE_ROOT/short"
expect_ps_inprocess "in-repo worktree leaves a clean in-process exit code" "$FIXTURE_ROOT/primary/.worktrees/inrepo"

# --- rejected: non-worktree roots ----------------------------------------
echo "rejected roots"
expect_sh "primary checkout rejected" 1 "$FIXTURE_ROOT/primary"
expect_ps "primary checkout rejected" 1 "$FIXTURE_ROOT/primary"
expect_sh "standalone clone under .worktrees rejected" 1 "$FIXTURE_ROOT/.worktrees/decoy-clone"
expect_ps "standalone clone under .worktrees rejected" 1 "$FIXTURE_ROOT/.worktrees/decoy-clone"
expect_sh "non-repository directory rejected" 2 "$FIXTURE_ROOT/plain"
expect_ps "non-repository directory rejected" 2 "$FIXTURE_ROOT/plain"
expect_sh "bare repository rejected" 2 "$FIXTURE_ROOT/bare.git"
expect_ps "bare repository rejected" 2 "$FIXTURE_ROOT/bare.git"

# --- HEAD state expectations ---------------------------------------------
echo "HEAD state expectations"
expect_sh "detached HEAD satisfies WT_EXPECT_HEAD=detached" 0 "$FIXTURE_ROOT/short" WT_EXPECT_HEAD=detached
expect_ps "detached HEAD satisfies -ExpectHead Detached" 0 "$FIXTURE_ROOT/short" -ExpectHead Detached
expect_sh "detached HEAD fails WT_EXPECT_HEAD=branch" 1 "$FIXTURE_ROOT/short" WT_EXPECT_HEAD=branch
expect_ps "detached HEAD fails -ExpectHead Branch" 1 "$FIXTURE_ROOT/short" -ExpectHead Branch
expect_sh "invalid WT_EXPECT_HEAD is a setup error" 2 "$FIXTURE_ROOT/short" WT_EXPECT_HEAD=nonsense

git -C "$FIXTURE_ROOT/short" switch -q -c guard-test-branch
expect_sh "branch HEAD accepted by default" 0 "$FIXTURE_ROOT/short"
expect_ps "branch HEAD accepted by default" 0 "$FIXTURE_ROOT/short"
expect_sh "matching WT_EXPECT_BRANCH accepted" 0 "$FIXTURE_ROOT/short" WT_EXPECT_BRANCH=guard-test-branch
expect_ps "matching -ExpectedBranch accepted" 0 "$FIXTURE_ROOT/short" -ExpectedBranch guard-test-branch
expect_sh "wrong WT_EXPECT_BRANCH rejected" 1 "$FIXTURE_ROOT/short" WT_EXPECT_BRANCH=some-other-branch
expect_ps "wrong -ExpectedBranch rejected" 1 "$FIXTURE_ROOT/short" -ExpectedBranch some-other-branch
expect_sh "branch HEAD fails WT_EXPECT_HEAD=detached" 1 "$FIXTURE_ROOT/short" WT_EXPECT_HEAD=detached
expect_ps "branch HEAD fails -ExpectHead Detached" 1 "$FIXTURE_ROOT/short" -ExpectHead Detached

# --- environment confusion ------------------------------------------------
# GIT_DIR/GIT_WORK_TREE can make git report a LINKED git dir while the work
# tree is the MAIN checkout. Only the .git pointer-file check catches this.
echo "environment confusion"
expect_sh "linked git dir aimed at the primary work tree rejected" 1 "$FIXTURE_ROOT/primary" \
    "GIT_DIR=$FIXTURE_ROOT/primary/.git/worktrees/inrepo" "GIT_WORK_TREE=$FIXTURE_ROOT/primary"
expect_ps "linked git dir aimed at the primary work tree rejected" 1 "$FIXTURE_ROOT/primary" \
    "GIT_DIR=$FIXTURE_ROOT/primary/.git/worktrees/inrepo" "GIT_WORK_TREE=$FIXTURE_ROOT/primary"

# --- sabotaged .git pointer ----------------------------------------------
# A pointer to the common git dir identifies an unrecognized/main checkout, so
# both guards reject it with the documented fatal exit 1 rather than accepting
# the root. A malformed pointer cannot be resolved and is a layout error (2).
echo "sabotaged pointer"
cp "$FIXTURE_ROOT/short/.git" "$FIXTURE_ROOT/short-gitfile.bak"
printf 'gitdir: %s\n' "$FIXTURE_ROOT/primary/.git" > "$FIXTURE_ROOT/short/.git"
expect_sh "gitdir pointer to the common dir rejected" 1 "$FIXTURE_ROOT/short"
expect_ps "gitdir pointer to the common dir rejected" 1 "$FIXTURE_ROOT/short"
printf 'not a pointer\n' > "$FIXTURE_ROOT/short/.git"
expect_sh "non-pointer .git file rejected" 2 "$FIXTURE_ROOT/short"
expect_ps "non-pointer .git file rejected" 2 "$FIXTURE_ROOT/short"
cp "$FIXTURE_ROOT/short-gitfile.bak" "$FIXTURE_ROOT/short/.git"
expect_sh "restored pointer accepted again" 0 "$FIXTURE_ROOT/short"

echo ""
echo "worktree_guard self-tests: $PASS passed, $FAIL failed, $SKIP skipped"
if [ "$FAIL" -ne 0 ]; then
    exit 1
fi
exit 0
