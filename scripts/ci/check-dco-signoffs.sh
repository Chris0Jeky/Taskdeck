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
failure_count=0

while IFS= read -r commit_sha; do
  [[ -n "$commit_sha" ]] || continue

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
  # the actual terminal trailer block and preserves Git's `---` divider, so
  # arbitrary body/patch mentions do not count. pipefail makes either Git
  # command's error fatal.
  if ! parsed_trailers="$("$git_executable" show -s --format='%B' "$commit_sha" |
    "$git_executable" -c core.commentChar='#' interpret-trailers --parse)"; then
    fail "cannot parse trailers for commit $commit_sha"
  fi

  matching_signoff=false
  if [[ "$author_name" == 'dependabot[bot]' ]] &&
    [[ "$author_email" == '49699333+dependabot[bot]@users.noreply.github.com' ]]; then
    # GitHub's established Dependabot message puts its stable support@github.com
    # sign-off after a `---` dependency-metadata divider. Parse through that
    # divider only for this exact signed bot mapping; human/other-bot commits
    # retain Git's normal divider boundary and receive no exemption.
    if ! dependabot_trailers="$("$git_executable" show -s --format='%B' "$commit_sha" |
      "$git_executable" -c core.commentChar='#' interpret-trailers --parse --no-divider)"; then
      fail "cannot parse Dependabot trailers for commit $commit_sha"
    fi
    while IFS= read -r trailer_line; do
      if [[ "$trailer_line" =~ $signoff_pattern ]] &&
        [[ "$(trim_whitespace "${BASH_REMATCH[1]}")" == 'dependabot[bot]' ]] &&
        [[ "${BASH_REMATCH[2]}" == 'support@github.com' ]]; then
        matching_signoff=true
        break
      fi
    done <<<"$dependabot_trailers"
  fi

  if [[ "$matching_signoff" == false ]]; then
    while IFS= read -r trailer_line; do
      [[ -n "$trailer_line" ]] || continue
      if [[ "$trailer_line" =~ $signoff_pattern ]]; then
        signed_name="$(trim_whitespace "${BASH_REMATCH[1]}")"
        signed_email="${BASH_REMATCH[2]}"
        [[ -n "$signed_name" ]] || continue

        if { [[ "$signed_name" == "$author_name" ]] && [[ "$signed_email" == "$author_email" ]]; } ||
          { [[ "$signed_name" == "$committer_name" ]] && [[ "$signed_email" == "$committer_email" ]]; }; then
          matching_signoff=true
          break
        fi
      fi
    done <<<"$parsed_trailers"
  fi

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

printf 'DCO verification passed: checked=%d range=%s..%s\n' \
  "$checked_count" "$base_sha" "$head_sha"
