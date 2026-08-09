#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
verifier="$script_dir/check-dco-signoffs.sh"
git_executable="${DCO_TEST_GIT_EXECUTABLE:-git}"
bash_executable="${BASH:-bash}"
temp_root="$(mktemp -d)"
fixture_repo="$temp_root/repo"
tests_run=0

cleanup() {
  rm -rf -- "$temp_root"
}
trap cleanup EXIT

fail_test() {
  printf 'not ok %d - %s\n' "$tests_run" "$1" >&2
  if [[ -n "${2:-}" ]]; then
    printf '%s\n' "$2" >&2
  fi
  exit 1
}

expect_pass() {
  local name="$1"
  local base_sha="$2"
  local head_sha="$3"
  local output

  tests_run=$((tests_run + 1))
  if ! output="$(cd "$fixture_repo" &&
    DCO_GIT_EXECUTABLE="$git_executable" "$bash_executable" "$verifier" "$base_sha" "$head_sha" 2>&1)"; then
    fail_test "$name" "$output"
  fi
  printf 'ok %d - %s\n' "$tests_run" "$name"
}

expect_fail() {
  local name="$1"
  local base_sha="$2"
  local head_sha="$3"
  local output

  tests_run=$((tests_run + 1))
  if output="$(cd "$fixture_repo" &&
    DCO_GIT_EXECUTABLE="$git_executable" "$bash_executable" "$verifier" "$base_sha" "$head_sha" 2>&1)"; then
    fail_test "$name" "unexpected success:\n$output"
  fi
  printf 'ok %d - %s\n' "$tests_run" "$name"
}

expect_missing_tool_failure() {
  local name="$1"
  local base_sha="$2"
  local head_sha="$3"
  local output

  tests_run=$((tests_run + 1))
  if output="$(cd "$fixture_repo" &&
    DCO_GIT_EXECUTABLE="$temp_root/missing-git" "$bash_executable" "$verifier" "$base_sha" "$head_sha" 2>&1)"; then
    fail_test "$name" "unexpected success:\n$output"
  fi
  printf 'ok %d - %s\n' "$tests_run" "$name"
}

create_commit() {
  local author_name="$1"
  local author_email="$2"
  local committer_name="$3"
  local committer_email="$4"
  local message="$5"

  printf '%s' "$message" |
    GIT_AUTHOR_NAME="$author_name" \
      GIT_AUTHOR_EMAIL="$author_email" \
      GIT_AUTHOR_DATE='2000-01-01T00:00:00Z' \
      GIT_COMMITTER_NAME="$committer_name" \
      GIT_COMMITTER_EMAIL="$committer_email" \
      GIT_COMMITTER_DATE='2000-01-01T00:00:00Z' \
      "$git_executable" -C "$fixture_repo" commit --allow-empty --quiet --cleanup=verbatim --file=-
  "$git_executable" -C "$fixture_repo" rev-parse HEAD
}

create_case_commit() {
  "$git_executable" -C "$fixture_repo" checkout --detach --quiet "$base_sha"
  create_commit "$@"
}

command -v "$git_executable" >/dev/null 2>&1 || {
  printf 'test setup error: Git executable not found: %s\n' "$git_executable" >&2
  exit 1
}
command -v "$bash_executable" >/dev/null 2>&1 || {
  printf 'test setup error: Bash executable not found: %s\n' "$bash_executable" >&2
  exit 1
}

"$git_executable" init --quiet "$fixture_repo"
base_sha="$(create_commit \
  'Fixture Owner' 'owner@example.com' \
  'Fixture Owner' 'owner@example.com' \
  $'Create fixture base\n\nSigned-off-by: Fixture Owner <owner@example.com>\n')"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Accept a terminal trailer\n\nSigned-off-by: Alice Example <alice@example.com>\n')"
expect_pass 'ordinary terminal sign-off' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Accept Git conflict comments\n\nSigned-off-by: Alice Example <alice@example.com>\n\n# Conflicts:\n#\tbackend/src/Example.cs\n#\tdocs/EXAMPLE.md\n')"
expect_pass 'sign-off followed by Git conflict comments' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a non-terminal trailer\n\nSigned-off-by: Alice Example <alice@example.com>\n\nThis ordinary body paragraph follows the trailer.\n')"
expect_fail 'ordinary body text after sign-off' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a body mention\n\nThis body merely mentions Signed-off-by: Alice Example <alice@example.com>.\n')"
expect_fail 'arbitrary Signed-off-by body mention' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a missing trailer\n')"
expect_fail 'missing Signed-off-by trailer' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a missing signatory name\n\nSigned-off-by: <alice@example.com>\n')"
expect_fail 'malformed sign-off name' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a malformed email shape\n\nSigned-off-by: Alice Example alice@example.com\n')"
expect_fail 'malformed sign-off email structure' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'not-an-email' \
  'Build Bot' 'bot@example.com' \
  $'Reject a malformed matching email\n\nSigned-off-by: Alice Example <not-an-email>\n')"
expect_fail 'malformed bracketed email cannot match the author' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a mismatched name\n\nSigned-off-by: Mallory Example <alice@example.com>\n')"
expect_fail 'mismatched sign-off name' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject a mismatched email\n\nSigned-off-by: Alice Example <mallory@example.com>\n')"
expect_fail 'mismatched sign-off email' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'ALICE@EXAMPLE.COM' \
  'Build Bot' 'bot@example.com' \
  $'Match the author case-insensitively\n\nsigned-off-by: alice example <alice@example.com>\n')"
expect_pass 'author identity match is case-insensitive' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'BOT@EXAMPLE.COM' \
  $'Match the committer case-insensitively\n\nSigned-off-by: build bot <bot@example.com>\n')"
expect_pass 'committer identity match is case-insensitive' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject an unrelated identity\n\nSigned-off-by: Carol Example <carol@example.com>\n')"
expect_fail 'sign-off matches neither author nor committer' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Accept CRLF trailers\r\n\r\nSigned-off-by: Alice Example <alice@example.com>\r\n')"
expect_pass 'CRLF commit message' "$base_sha" "$head_sha"

head_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Reject uncommented conflict body text\n\nSigned-off-by: Alice Example <alice@example.com>\n\n# Conflicts:\nbackend/src/Example.cs\n')"
expect_fail 'mutating a conflict comment into body text invalidates the trailer' "$base_sha" "$head_sha"

side_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Create the merge side\n\nSigned-off-by: Alice Example <alice@example.com>\n')"
main_sha="$(create_case_commit \
  'Alice Example' 'alice@example.com' \
  'Build Bot' 'bot@example.com' \
  $'Create the merge mainline\n\nSigned-off-by: Build Bot <bot@example.com>\n')"
tree_sha="$("$git_executable" -C "$fixture_repo" rev-parse "${main_sha}^{tree}")"
merge_sha="$(printf '%s' $'Merge fixture branches without a sign-off\n' |
  GIT_AUTHOR_NAME='Merge Author' \
    GIT_AUTHOR_EMAIL='merge-author@example.com' \
    GIT_AUTHOR_DATE='2000-01-01T00:00:00Z' \
    GIT_COMMITTER_NAME='Merge Committer' \
    GIT_COMMITTER_EMAIL='merge-committer@example.com' \
    GIT_COMMITTER_DATE='2000-01-01T00:00:00Z' \
    "$git_executable" -C "$fixture_repo" commit-tree "$tree_sha" -p "$main_sha" -p "$side_sha")"
expect_pass 'multi-parent merge commit is skipped' "$base_sha" "$merge_sha"

missing_sha='1111111111111111111111111111111111111111'
expect_fail 'missing base object fails closed' "$missing_sha" "$base_sha"
expect_fail 'missing head object fails closed' "$base_sha" "$missing_sha"

blob_sha="$(printf 'not a commit' | "$git_executable" -C "$fixture_repo" hash-object -w --stdin)"
expect_fail 'non-commit range object fails closed' "$base_sha" "$blob_sha"
expect_missing_tool_failure 'missing Git tool fails closed' "$base_sha" "$head_sha"

printf 'DCO verifier synthetic tests passed: %d\n' "$tests_run"
