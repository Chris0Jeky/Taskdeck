#!/usr/bin/env bash
# Isolating contract for worktree_guard.sh's linked-worktree containment check.
#
# A Git submodule is the load-bearing fixture: its work tree has a real .git
# pointer file that resolves exactly to its own git dir, so it satisfies the
# guard's pointer check. It is still not a linked worktree because that git dir
# does not live under <common-git-dir>/worktrees/<name>. Removing only the
# containment block must therefore turn this fixture from rejected to accepted.

set -euo pipefail

_tests_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
GUARD="${WT_GUARD_SH:-$_tests_dir/worktree_guard.sh}"
FIXTURE_ROOT="$(mktemp -d)"

cleanup() {
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

run_guard() {
    local guard="$1"
    (
        cd -- "$SUBMODULE_ROOT"
        bash -c 'source "$1"' bash "$guard"
    ) 2>&1
}

if [ ! -f "$GUARD" ]; then
    fail "guard script not found: $GUARD"
fi

# A committed source repository is required because submodule add checks out
# HEAD into the nested work tree.
git init -q -b main "$FIXTURE_ROOT/module-source"
git -C "$FIXTURE_ROOT/module-source" \
    -c user.email=t@example.com \
    -c user.name=t \
    commit -q --allow-empty -m "seed module" --no-gpg-sign

git init -q -b main "$FIXTURE_ROOT/superproject"
git -C "$FIXTURE_ROOT/superproject" \
    -c user.email=t@example.com \
    -c user.name=t \
    commit -q --allow-empty -m "seed superproject" --no-gpg-sign

# Git disables local file transport for submodules by default. Scope the
# exception to this fixture command; no global or repository configuration is
# changed.
git -C "$FIXTURE_ROOT/superproject" \
    -c protocol.file.allow=always \
    submodule add -q "$FIXTURE_ROOT/module-source" vendor/module
git -C "$FIXTURE_ROOT/superproject" \
    -c user.email=t@example.com \
    -c user.name=t \
    commit -q -m "add module" --no-gpg-sign

SUBMODULE_ROOT="$FIXTURE_ROOT/superproject/vendor/module"
if [ ! -f "$SUBMODULE_ROOT/.git" ]; then
    fail "fixture is not a submodule work tree with a .git pointer file"
fi

set +e
output="$(run_guard "$GUARD")"
code=$?
set -e

if [ "$code" -ne 1 ]; then
    printf '%s\n' "$output" >&2
    fail "submodule must be rejected with exit 1 (got $code)"
fi
if ! printf '%s' "$output" | grep -qF -- "main checkout or an unrecognized worktree"; then
    printf '%s\n' "$output" >&2
    fail "submodule rejection did not come from linked-worktree containment"
fi
pass "submodule is rejected by linked-worktree containment"

# Mutation control: remove exactly substance check 1 while leaving the pointer
# and HEAD checks byte-for-byte. The same submodule must then reach success;
# otherwise the fixture does not isolate the containment invariant.
MUTATED_GUARD="$FIXTURE_ROOT/worktree_guard-without-containment.sh"
awk '
    /^# Substance check 1:/ { skipping = 1; next }
    /^# Substance check 2:/ { skipping = 0 }
    !skipping { print }
' "$GUARD" > "$MUTATED_GUARD"

if grep -qF -- "# Substance check 1:" "$MUTATED_GUARD"; then
    fail "mutation control did not remove substance check 1"
fi
if ! grep -qF -- "# Substance check 2:" "$MUTATED_GUARD"; then
    fail "mutation control removed the pointer check as well"
fi

set +e
mutated_output="$(run_guard "$MUTATED_GUARD")"
mutated_code=$?
set -e

if [ "$mutated_code" -ne 0 ]; then
    printf '%s\n' "$mutated_output" >&2
    fail "submodule must pass when only linked-worktree containment is removed (got $mutated_code)"
fi
if ! printf '%s' "$mutated_output" | grep -qF -- "OK [worktree_guard]: Running in an isolated worktree."; then
    printf '%s\n' "$mutated_output" >&2
    fail "mutated guard did not reach its success path"
fi
pass "removing only containment makes the isolating submodule pass"

printf 'worktree_guard submodule contract passed: 2 checks.\n'
