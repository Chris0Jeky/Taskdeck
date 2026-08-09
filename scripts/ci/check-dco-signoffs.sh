#!/usr/bin/env bash

set -euo pipefail

fail() {
  printf 'DCO verification error: %s\n' "$*" >&2
  exit 1
}

trim_whitespace() {
  local value="$1"

  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

is_valid_email() {
  local email="$1"
  local mailbox
  local domain
  local label
  local local_part_pattern="^[A-Za-z0-9.!#\$%&'*+/=?^_\`{|}~-]+$"
  local domain_pattern='^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$'
  local -a labels

  [[ ${#email} -le 254 && "$email" == *@* ]] || return 1
  mailbox="${email%%@*}"
  domain="${email#*@}"
  [[ -n "$mailbox" && ${#mailbox} -le 64 && -n "$domain" && "$domain" != *@* ]] || return 1
  [[ "$mailbox" != .* && "$mailbox" != *. && "$mailbox" != *..* ]] || return 1
  [[ "$mailbox" =~ $local_part_pattern && "$domain" =~ $domain_pattern ]] || return 1

  IFS='.' read -r -a labels <<<"$domain"
  for label in "${labels[@]}"; do
    [[ -n "$label" && ${#label} -le 63 && "$label" != -* && "$label" != *- ]] || return 1
  done
}

if [[ $# -ne 2 ]]; then
  fail 'usage: check-dco-signoffs.sh <base-sha> <head-sha>'
fi

base_sha="$1"
head_sha="$2"
git_executable="${DCO_GIT_EXECUTABLE:-git}"

# Accept only full object IDs supplied by the pull_request event. Besides making
# the checked range explicit, this prevents revision-option or symbolic-ref input.
sha_pattern='^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$'
[[ "$base_sha" =~ $sha_pattern ]] || fail 'base SHA must be a full hexadecimal object ID'
[[ "$head_sha" =~ $sha_pattern ]] || fail 'head SHA must be a full hexadecimal object ID'

command -v "$git_executable" >/dev/null 2>&1 || fail "Git executable not found: $git_executable"
"$git_executable" --version >/dev/null 2>&1 || fail 'Git is not executable'

"$git_executable" rev-parse --verify --quiet "${base_sha}^{commit}" >/dev/null ||
  fail "base object is missing or is not a commit: $base_sha"
"$git_executable" rev-parse --verify --quiet "${head_sha}^{commit}" >/dev/null ||
  fail "head object is missing or is not a commit: $head_sha"

if ! commit_range="$("$git_executable" rev-list --reverse "${base_sha}..${head_sha}")"; then
  fail "cannot enumerate explicit pull-request range ${base_sha}..${head_sha}"
fi

shopt -s nocasematch
signoff_pattern='^Signed-off-by:[[:space:]]*([^<>]+)[[:space:]]+<([^<>[:space:]]+)>[[:space:]]*$'
checked_count=0
skipped_merge_count=0
failure_count=0

while IFS= read -r commit_sha; do
  [[ -n "$commit_sha" ]] || continue

  if ! parent_line="$("$git_executable" rev-list --parents -n 1 "$commit_sha")"; then
    fail "cannot inspect parents for commit $commit_sha"
  fi
  read -r -a parent_fields <<<"$parent_line"
  if [[ ${#parent_fields[@]} -lt 1 || "${parent_fields[0]}" != "$commit_sha" ]]; then
    fail "unexpected parent metadata for commit $commit_sha"
  fi

  # A commit followed by two or more parent IDs is a merge. DCO applies to all
  # ordinary commits, including root commits, but merge commits are skipped.
  if (( ${#parent_fields[@]} > 2 )); then
    skipped_merge_count=$((skipped_merge_count + 1))
    printf 'DCO SKIP %s (multi-parent merge commit)\n' "${commit_sha:0:12}"
    continue
  fi

  if ! identity_block="$("$git_executable" show -s --format='%an%n%ae%n%cn%n%ce' "$commit_sha")"; then
    fail "cannot read author and committer identities for commit $commit_sha"
  fi
  mapfile -t identities <<<"$identity_block"
  if [[ ${#identities[@]} -ne 4 ]]; then
    fail "unexpected identity metadata for commit $commit_sha"
  fi
  author_name="${identities[0]}"
  author_email="${identities[1]}"
  committer_name="${identities[2]}"
  committer_email="${identities[3]}"

  # core.commentChar=# makes native Git ignore the conflict-help comments that
  # `git commit` appends after a valid trailer. --parse restricts the result to
  # the actual terminal trailer block; arbitrary mentions in the body do not
  # count. pipefail makes either Git command's error fatal.
  if ! parsed_trailers="$("$git_executable" show -s --format='%B' "$commit_sha" |
    "$git_executable" -c core.commentChar='#' interpret-trailers --parse --no-divider)"; then
    fail "cannot parse trailers for commit $commit_sha"
  fi

  matching_signoff=false
  while IFS= read -r trailer_line; do
    [[ -n "$trailer_line" ]] || continue
    if [[ "$trailer_line" =~ $signoff_pattern ]]; then
      signed_name="$(trim_whitespace "${BASH_REMATCH[1]}")"
      signed_email="${BASH_REMATCH[2]}"
      [[ -n "$signed_name" ]] || continue
      is_valid_email "$signed_email" || continue

      if { [[ "$signed_name" == "$author_name" ]] && [[ "$signed_email" == "$author_email" ]]; } ||
        { [[ "$signed_name" == "$committer_name" ]] && [[ "$signed_email" == "$committer_email" ]]; }; then
        matching_signoff=true
        break
      fi
    fi
  done <<<"$parsed_trailers"

  checked_count=$((checked_count + 1))
  if [[ "$matching_signoff" == true ]]; then
    printf 'DCO OK   %s\n' "${commit_sha:0:12}"
  else
    printf 'DCO FAIL %s (no parsed Signed-off-by identity matches author or committer)\n' \
      "${commit_sha:0:12}" >&2
    failure_count=$((failure_count + 1))
  fi
done <<<"$commit_range"

if (( failure_count > 0 )); then
  fail "$failure_count ordinary commit(s) failed DCO verification"
fi

printf 'DCO verification passed: checked=%d skipped_merges=%d range=%s..%s\n' \
  "$checked_count" "$skipped_merge_count" "$base_sha" "$head_sha"
