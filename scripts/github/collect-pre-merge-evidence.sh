#!/usr/bin/env bash
set -euo pipefail

die() {
  printf 'BLOCKED: %s\n' "$*" >&2
  exit 1
}

# Git replacement refs are disabled for every Git invocation this collector makes. The
# boundary checks in assert_clean_exact_checkout reject persistent refs/replace/ refs, but a
# PR-controlled background process can install one transiently between those boundaries and
# redirect ordinary object reads so changed scan definitions compare equal to their opening
# base. Disabling replacement resolution process-wide removes that window entirely.
export GIT_NO_REPLACE_OBJECTS=1

# ---------------------------------------------------------------------------------------
# Phase 1 -- checkout discovery, bash only.
#
# Nothing in this phase executes an external program. It uses builtins only: string
# operations, file tests, `cd`, `pwd`, and `read`. That restriction is the point. An earlier
# revision asked Git for the checkout roots and only afterwards rejected evidence tools found
# inside them, so a forged `git` on PATH ran first and then named a decoy root that contained
# neither itself nor the checkout it was hiding, and the rejection could never fire. Roots
# discovered here are therefore derived from the collector's own location and the working
# directory, never from a program that a checkout could have supplied.
# ---------------------------------------------------------------------------------------

# Absolute physical directory containing $1, resolved without trusting any external tool.
absolute_directory() {
  [[ $# -eq 1 ]] || die "directory resolver received an invalid argument count"
  local target="$1"
  local directory="${target%/*}"
  if [[ "$directory" == "$target" ]]; then
    directory="."
  elif [[ -z "$directory" ]]; then
    directory="/"
  fi
  (cd -- "$directory" >/dev/null 2>&1 && pwd -P) || return 1
}

# True when $1 is $2 or lives beneath it. Ancestors are compared with the same-file test so
# Windows/MSYS path spellings (C:/x versus /c/x), symlinks, and case differences cannot hide
# a checkout-local executable behind a differently spelled path.
path_is_inside_tree() {
  [[ $# -eq 2 ]] || die "checkout containment test received an invalid argument count"
  local current="$1"
  local tree_root="$2"
  local parent
  [[ -n "$current" && -n "$tree_root" && -d "$tree_root" ]] || return 1
  while :; do
    [[ -e "$current" ]] || return 1
    if [[ "$current" -ef "$tree_root" ]]; then
      return 0
    fi
    parent="${current%/*}"
    [[ -n "$parent" ]] || parent="/"
    [[ "$parent" != "$current" ]] || return 1
    current="$parent"
  done
}

# True when $1 is absolute in either POSIX (/x) or Windows (C:/x, C:\x) spelling. A relative
# or empty path is treated as untrusted everywhere below, because it resolves against the
# working directory, which can itself sit inside a checkout.
path_is_absolute() {
  [[ $# -eq 1 ]] || die "absolute-path test received an invalid argument count"
  case "$1" in
    /*) return 0 ;;
    [A-Za-z]:/* | [A-Za-z]:\\*) return 0 ;;
    *) return 1 ;;
  esac
}

# Textual normalization for paths read out of Git's own plain-text files: backslashes become
# forward slashes and redundant trailing slashes are dropped. The result is published in a
# global rather than printed, so no subshell is required.
normalized_path=""
normalize_path_text() {
  [[ $# -eq 1 ]] || die "path normalizer received an invalid argument count"
  local text="${1//\\//}"
  while [[ "${#text}" -gt 1 && "$text" == */ && "$text" != *:/ ]]; do
    text="${text%/}"
  done
  normalized_path="$text"
}

# Trim ASCII whitespace, including the CR of a Windows-written Git file, from both ends of $1.
trimmed_text=""
trim_text() {
  [[ $# -eq 1 ]] || die "text trimmer received an invalid argument count"
  local text="$1"
  text="${text#"${text%%[![:space:]]*}"}"
  text="${text%"${text##*[![:space:]]}"}"
  trimmed_text="$text"
}

# Resolve $2, absolute or relative, against directory $1 to an absolute physical directory
# using only `cd` and `pwd`. The result is empty when the directory does not exist.
resolved_directory=""
resolve_directory_against() {
  [[ $# -eq 2 ]] || die "directory joiner received an invalid argument count"
  local base="$1"
  local target="$2"
  if path_is_absolute "$target"; then
    resolved_directory="$(cd -- "$target" >/dev/null 2>&1 && pwd -P)" || resolved_directory=""
  else
    resolved_directory="$(cd -- "$base" >/dev/null 2>&1 &&
      cd -- "$target" >/dev/null 2>&1 && pwd -P)" || resolved_directory=""
  fi
}

# Every tree that may never supply an evidence tool: the collector's own checkout, the
# checkout containing the working directory, the primary checkout owning either of them when
# the collector runs from a linked worktree, and the shared Git directories themselves.
repository_roots=()

remember_repository_root() {
  [[ $# -eq 1 ]] || die "checkout registrar received an invalid argument count"
  local candidate="$1"
  local existing
  [[ -n "$candidate" && -d "$candidate" ]] || return 0
  for existing in ${repository_roots[@]+"${repository_roots[@]}"}; do
    if [[ "$existing" -ef "$candidate" ]]; then
      return 0
    fi
  done
  repository_roots+=("$candidate")
}

# Walk up from $1 until a `.git` entry appears and register the checkout it marks. When `.git`
# is a FILE the tree is a linked worktree: its `gitdir:` line is parsed textually and the
# primary checkout owning the shared Git directory is registered too, because the ignored,
# writable `.runtime-codex/bin` PATH entry lives in that primary tree while the collector runs
# from the linked one. Git is never consulted here -- a forged git is exactly what this
# discovery exists to keep from executing.
discover_repository_roots() {
  [[ $# -eq 1 ]] || die "checkout discovery received an invalid argument count"
  local current="$1"
  local parent
  local marker
  local line
  local git_directory=""
  local common_directory=""

  [[ -n "$current" && -d "$current" ]] || return 0
  while :; do
    marker="$current/.git"
    if [[ -d "$marker" ]]; then
      remember_repository_root "$current"
      remember_repository_root "$marker"
      return 0
    fi
    if [[ -f "$marker" ]]; then
      remember_repository_root "$current"
      while IFS= read -r line || [[ -n "$line" ]]; do
        if [[ "$line" == gitdir:* ]]; then
          trim_text "${line#gitdir:}"
          normalize_path_text "$trimmed_text"
          resolve_directory_against "$current" "$normalized_path"
          git_directory="$resolved_directory"
          break
        fi
      done <"$marker"
      if [[ -n "$git_directory" ]]; then
        remember_repository_root "$git_directory"
        if [[ -f "$git_directory/commondir" ]]; then
          IFS= read -r line <"$git_directory/commondir" || line=""
          trim_text "$line"
          normalize_path_text "$trimmed_text"
          if [[ -n "$normalized_path" ]]; then
            resolve_directory_against "$git_directory" "$normalized_path"
            common_directory="$resolved_directory"
          fi
        fi
        if [[ -z "$common_directory" && "$git_directory" == */worktrees/* ]]; then
          # Standard linked-worktree layout: <shared Git directory>/worktrees/<name>.
          resolve_directory_against "$git_directory" "${git_directory%/worktrees/*}"
          common_directory="$resolved_directory"
        fi
        if [[ -n "$common_directory" ]]; then
          remember_repository_root "$common_directory"
          resolve_directory_against "$common_directory" ".."
          remember_repository_root "$resolved_directory"
        fi
      fi
      return 0
    fi
    parent="${current%/*}"
    [[ -n "$parent" ]] || parent="/"
    [[ "$parent" != "$current" ]] || return 0
    current="$parent"
  done
}

# A `.runtime-codex` directory is, by construction, gitignored and writable by PR-controlled
# checks: `.codex/config.toml` creates `.runtime-codex/bin` and prepends it to PATH. It is
# never a legitimate source of evidence tools, wherever it sits. Containment against a
# discovered checkout is the primary control; this name rule is defence in depth for the case
# where such a directory cannot be tied back to any checkout on disk -- a sibling tree with no
# `.git` marker, say -- and it is deliberately not the only thing standing in the way.
path_is_untrusted_tool_directory() {
  [[ $# -eq 1 ]] || die "tool-directory test received an invalid argument count"
  local path="/${1//\\//}/"
  [[ "$path" == */.runtime-codex/* ]]
}

# Drop every PATH entry inside a discovered checkout, plus every relative or empty entry, and
# re-export the result before the first external program is resolved. Rejecting a forged tool
# after resolution is not enough on its own: the tool that supplies the roots a rejection is
# measured against must never be a resolution candidate in the first place. Containment is
# tested both textually and with the same-file walk, so a PATH entry naming a directory that
# does not exist yet is still dropped.
sanitize_path_against_repository_roots() {
  local -a entries=()
  local entry
  local root
  local rejected
  local sanitized=""

  IFS=':' read -r -a entries <<<"$PATH"
  for entry in ${entries[@]+"${entries[@]}"}; do
    if [[ -z "$entry" ]] || ! path_is_absolute "$entry"; then
      continue
    fi
    if path_is_untrusted_tool_directory "$entry"; then
      continue
    fi
    rejected=false
    for root in ${repository_roots[@]+"${repository_roots[@]}"}; do
      if [[ "$entry" == "$root" || "$entry" == "$root"/* ]] ||
        path_is_inside_tree "$entry" "$root"; then
        rejected=true
        break
      fi
    done
    if [[ "$rejected" == true ]]; then
      continue
    fi
    if [[ -z "$sanitized" ]]; then
      sanitized="$entry"
    else
      sanitized="$sanitized:$entry"
    fi
  done
  [[ -n "$sanitized" ]] ||
    die "every PATH directory lies inside a checkout; no evidence tool can be trusted"
  PATH="$sanitized"
  export PATH
  hash -r
}

collector_script_directory="$(absolute_directory "${BASH_SOURCE[0]}")" ||
  die "cannot resolve the collector script directory"
collector_repo_root="$(cd -- "$collector_script_directory/../.." >/dev/null 2>&1 && pwd -P)" ||
  die "cannot resolve the collector checkout root"
collector_working_directory="$(pwd -P)" || die "cannot resolve the working directory"

remember_repository_root "$collector_repo_root"
discover_repository_roots "$collector_script_directory"
discover_repository_roots "$collector_working_directory"
sanitize_path_against_repository_roots

# ---------------------------------------------------------------------------------------
# Phase 2 -- evidence-tool resolution. External programs may run from here on, but only ones
# resolved through the sanitized PATH and proven to sit outside every discovered checkout.
# ---------------------------------------------------------------------------------------

# One entry per checked directory. A tool reached through a symlink contributes two entries,
# the link's directory and the target's, so both are re-checked against a checkout discovered
# later. Labels and paths are parallel arrays purely so a rejection can name the tool.
trusted_executable_labels=()
trusted_executable_paths=()
trusted_executable_directories=()

resolved_executable_path=""
resolved_executable_directory=""
resolved_executable_target_directory=""
readlink_executable=""

# Follow the final component of $1 through any symlink chain so a link planted in a trusted
# directory cannot point at a checkout-local program. `absolute_directory` dereferences only
# the directory component, which is what left a file-level symlink accepted. Bounded to 40
# hops; a cycle or an unreadable link fails closed. While `readlink` is not yet resolved this
# is a no-op, which is why `readlink` is registered first and re-validated straight after.
dereferenced_program_path=""
dereference_program_path() {
  [[ $# -eq 1 ]] || die "program dereferencer received an invalid argument count"
  local current="$1"
  local target
  local directory
  local hops=0
  while [[ -n "$readlink_executable" && -L "$current" ]]; do
    hops=$((hops + 1))
    [[ "$hops" -le 40 ]] ||
      die "refusing an evidence tool behind an unbounded symlink chain: $1"
    target="$("$readlink_executable" -- "$current")" ||
      die "cannot dereference the required executable: $1"
    [[ -n "$target" ]] || die "cannot dereference the required executable: $1"
    normalize_path_text "$target"
    target="$normalized_path"
    if path_is_absolute "$target"; then
      current="$target"
    else
      directory="$(absolute_directory "$current")" ||
        die "cannot dereference the required executable: $1"
      current="${directory%/}/$target"
    fi
  done
  dereferenced_program_path="$current"
}

# Resolve one evidence tool to an absolute program path and reject any resolution that lives
# inside a discovered checkout, whether it is reached directly or through a symlink.
# `.codex/config.toml` prepends the gitignored, writable `.runtime-codex/bin` directory to
# PATH, so without this a PR-controlled check can plant a forged gh, git, jq, awk, or any
# other tool the collector shells out to, leave the worktree clean, and have the collector
# fabricate a COMPLETE/CLEAN packet without ever querying GitHub. The TASKDECK_*_EXECUTABLE
# overrides remain supported and are held to the same rejection.
# (#1625 tracks the broader configuration hazard; this covers the collector only.)
resolve_trusted_executable() {
  [[ $# -eq 2 ]] || die "executable resolver received an invalid argument count"
  local label="$1"
  local candidate="$2"
  local candidate_path
  local candidate_directory
  local target_directory
  local resolved
  local root

  if [[ "$candidate" == */* ]]; then
    candidate_path="$candidate"
  else
    candidate_path="$(command -v -- "$candidate" 2>/dev/null)" || candidate_path=""
    [[ "$candidate_path" == */* ]] ||
      die "required executable is unavailable or is not a program file: $label ($candidate)"
  fi
  [[ -x "$candidate_path" ]] || die "required executable is unavailable: $label ($candidate_path)"

  candidate_directory="$(absolute_directory "$candidate_path")" ||
    die "cannot resolve the directory of the required executable: $label ($candidate_path)"
  resolved="${candidate_directory%/}/${candidate_path##*/}"
  [[ -x "$resolved" ]] ||
    die "required executable is unavailable after absolute resolution: $label ($resolved)"
  # The link path is what gets executed, so argv[0]-sensitive multi-call binaries keep
  # working; the target's directory is held to the same rejection separately.
  dereference_program_path "$candidate_path"
  target_directory="$(absolute_directory "$dereferenced_program_path")" ||
    die "cannot resolve the directory of the required executable: $label ($candidate_path)"
  # Containment is tested first so a checkout-local tool is always reported as such; the name
  # rule below is the fallback for a runtime directory no discovered checkout can claim.
  for root in ${repository_roots[@]+"${repository_roots[@]}"}; do
    if path_is_inside_tree "$candidate_directory" "$root" ||
      path_is_inside_tree "$target_directory" "$root"; then
      die "refusing an evidence tool resolved inside the collector checkout: $label ($resolved)"
    fi
  done
  if path_is_untrusted_tool_directory "$candidate_directory" ||
    path_is_untrusted_tool_directory "$target_directory"; then
    die "refusing an evidence tool resolved inside a PR-writable runtime directory: $label ($resolved)"
  fi
  resolved_executable_path="$resolved"
  resolved_executable_directory="$candidate_directory"
  resolved_executable_target_directory="$target_directory"
}

register_trusted_executable() {
  [[ $# -eq 2 ]] || die "executable registrar received an invalid argument count"
  resolve_trusted_executable "$1" "$2"
  trusted_executable_labels+=("$1")
  trusted_executable_paths+=("$resolved_executable_path")
  trusted_executable_directories+=("$resolved_executable_directory")
  if [[ "$resolved_executable_target_directory" != "$resolved_executable_directory" ]]; then
    trusted_executable_labels+=("$1")
    trusted_executable_paths+=("$resolved_executable_path")
    trusted_executable_directories+=("$resolved_executable_target_directory")
  fi
}

# Re-reject the already absolute evidence tools against a checkout discovered later, so a
# linked worktree cannot be measured with tools taken from its own tree or from the primary
# checkout that shares its Git directory. Every external the collector shells out to is in
# this set, not only the ones that talk to GitHub: `awk` used to sit outside it while forming
# the back half of the only hashing primitive, so a forged awk could echo recorded digests
# back and authenticate an arbitrarily rewritten opening state.
assert_trusted_executables_outside() {
  [[ $# -eq 1 ]] || die "executable trust check received an invalid argument count"
  local tree_root="$1"
  local index
  [[ -n "$tree_root" ]] || die "executable trust check received an empty checkout root"
  for index in "${!trusted_executable_directories[@]}"; do
    if path_is_inside_tree "${trusted_executable_directories[$index]}" "$tree_root"; then
      die "refusing an evidence tool resolved inside the measured checkout: ${trusted_executable_labels[$index]} (${trusted_executable_paths[$index]})"
    fi
  done
}

register_trusted_executable readlink "${TASKDECK_READLINK_EXECUTABLE:-readlink}"
readlink_executable="$resolved_executable_path"
# Re-run the now fully armed rule against readlink itself. PATH is already sanitized, so a
# checkout-local readlink was never a candidate; this closes the symlinked-readlink case.
resolve_trusted_executable readlink "$readlink_executable"

register_trusted_executable gh "${TASKDECK_GH_EXECUTABLE:-gh}"
gh_executable="$resolved_executable_path"
register_trusted_executable jq "${TASKDECK_JQ_EXECUTABLE:-jq}"
jq_executable="$resolved_executable_path"
register_trusted_executable cmp "${TASKDECK_CMP_EXECUTABLE:-cmp}"
cmp_executable="$resolved_executable_path"
register_trusted_executable openssl "${TASKDECK_OPENSSL_EXECUTABLE:-openssl}"
openssl_executable="$resolved_executable_path"
register_trusted_executable sha256sum "${TASKDECK_SHA256SUM_EXECUTABLE:-sha256sum}"
sha256sum_executable="$resolved_executable_path"
register_trusted_executable awk "${TASKDECK_AWK_EXECUTABLE:-awk}"
awk_executable="$resolved_executable_path"
register_trusted_executable cp cp
cp_executable="$resolved_executable_path"
register_trusted_executable mv mv
mv_executable="$resolved_executable_path"
register_trusted_executable rm rm
rm_executable="$resolved_executable_path"
register_trusted_executable ln ln
ln_executable="$resolved_executable_path"
register_trusted_executable mkdir mkdir
mkdir_executable="$resolved_executable_path"
register_trusted_executable mktemp mktemp
mktemp_executable="$resolved_executable_path"
register_trusted_executable tr tr
tr_executable="$resolved_executable_path"
if [[ -n "${TASKDECK_GIT_EXECUTABLE:-}" ]]; then
  register_trusted_executable git "$TASKDECK_GIT_EXECUTABLE"
elif command -v git.exe >/dev/null 2>&1; then
  register_trusted_executable git git.exe
else
  register_trusted_executable git git
fi
git_executable="$resolved_executable_path"

# Printed with the `printf` builtin rather than a here-document through `cat`: usage is one
# fewer external program the collector has to trust to state its own contract.
usage() {
  printf '%s\n' >&2 \
    'Usage:' \
    '  collect-pre-merge-evidence.sh start [PR_NUMBER]' \
    '  collect-pre-merge-evidence.sh abort < SESSION_TOKEN_FILE' \
    '  collect-pre-merge-evidence.sh finish < SESSION_TOKEN_FILE' \
    '' \
    "The start phase binds the current clean checkout to an explicit PR number or," \
    "when PR_NUMBER is omitted/empty, only to the current branch's pull request." \
    "It records one checkout-local, single-use state file under that worktree's Git" \
    'directory. Run the change-specific checks between phases. The abort phase' \
    'explicitly discards only a token-authenticated state belonging to the current checkout.' \
    'The finish phase captures every feedback surface and current check, then fails' \
    'if the opening state, checkout identity, scan definitions, or closing PR' \
    'identity differs.'
  exit 2
}

[[ $# -ge 1 ]] || usage
mode="$1"

resolve_state_path() {
  local resolved_top_level
  local resolved_git_dir
  local resolved_common_git_dir
  local resolved_primary_worktree
  local resolved_state_dir

  resolved_top_level="$("$git_executable" rev-parse --show-toplevel)" ||
    die "cannot resolve the checkout root"
  resolved_git_dir="$("$git_executable" rev-parse --absolute-git-dir)" ||
    die "cannot resolve the checkout Git directory"
  resolved_common_git_dir="$("$git_executable" rev-parse --git-common-dir)" ||
    die "cannot resolve the shared checkout Git directory"
  [[ -n "$resolved_top_level" && -n "$resolved_git_dir" && -n "$resolved_common_git_dir" ]] ||
    die "checkout identity is incomplete"
  resolved_primary_worktree="$(cd -- "$resolved_common_git_dir/.." >/dev/null 2>&1 && pwd -P)" ||
    resolved_primary_worktree=""
  if [[ -z "$resolved_primary_worktree" ]]; then
    # The shared Git directory is not present on disk, so nothing can be resolved inside the
    # primary checkout; keep a textual root for the containment test rather than failing.
    resolved_primary_worktree="${resolved_common_git_dir%/*}"
    [[ -n "$resolved_primary_worktree" &&
       "$resolved_primary_worktree" != "$resolved_common_git_dir" ]] ||
      resolved_primary_worktree="$resolved_top_level"
  fi
  # The measured checkout can differ from the collector's own, so the roots Git reports are
  # re-checked here. This is the SECOND containment layer, not the first: the roots that
  # decided which directories could supply Git itself were discovered with bash alone before
  # any external program ran, so the Git that produced the values above is already proven to
  # live outside the collector checkout, its primary checkout, and the working directory's
  # checkout. A linked worktree shares the primary checkout's Git directory, and the ignored
  # `.runtime-codex/bin` PATH entry lives in that primary checkout, so both trees are
  # untrusted sources of evidence tools.
  assert_trusted_executables_outside "$resolved_top_level"
  assert_trusted_executables_outside "$resolved_primary_worktree"
  resolved_state_dir="$resolved_git_dir/taskdeck-pre-merge-evidence"

  state_directory="$resolved_state_dir"
  state_file=""
  state_worktree="$resolved_top_level"
  state_git_dir="$resolved_git_dir"
}

# The digest field is split with bash string operations, never a pipeline through awk. awk is
# a trusted tool like every other one now, but keeping it out of the single primitive that
# authenticates the opening state means no external program at all stands between sha256sum
# and the comparison -- defence in depth for the one place a forgery pays for itself.
sha256_text() {
  [[ $# -eq 1 ]] || die "sha256 helper received an invalid argument count"
  local digest_line
  local digest
  digest_line="$(printf '%s' "$1" | "$sha256sum_executable")" ||
    die "cannot hash the supplied text"
  digest="${digest_line%% *}"
  [[ "$digest" =~ ^[0-9a-f]{64}$ ]] || die "sha256 helper produced a malformed digest"
  printf '%s\n' "$digest"
}

# The binding covers the exact document text supplied by the caller, never a path, so every
# probe below observes one immutable in-memory buffer.
state_binding() {
  [[ $# -eq 2 ]] || die "state-binding helper received an invalid argument count"
  local session_token="$1"
  local state_document="$2"
  local canonical_state

  canonical_state="$(printf '%s' "$state_document" |
    "$jq_executable" -S -c 'del(.openingStateBinding)')" ||
    die "cannot canonicalize opening evidence state"
  sha256_text "${session_token}:${canonical_state}"
}

# Authenticate the bytes of one opening-state document. The file is read exactly once into a
# variable: re-opening it for each probe let a PR-controlled background process satisfy the
# digest check with one document and then supply different fields to the binding check.
validate_session_token() {
  [[ $# -eq 3 ]] || die "session-token validator received an invalid argument count"
  local session_token="$1"
  local allow_stale_binding="$2"
  local state_document="$3"
  local token_digest
  local recorded_digest
  local actual_binding
  local expected_binding

  [[ "$session_token" =~ ^[0-9a-f]{64}$ ]] ||
    die "session token must be a 64-character lowercase hexadecimal value"
  [[ -n "$state_document" ]] || die "opening evidence state is empty"
  token_digest="$(sha256_text "$session_token")" || die "cannot hash the session token"
  recorded_digest="$(printf '%s' "$state_document" |
    "$jq_executable" -r '.sessionTokenDigest')" ||
    die "cannot read the recorded session-token digest"
  [[ "$token_digest" == "$recorded_digest" ]] ||
    die "session token does not authenticate the opening evidence state"
  actual_binding="$(state_binding "$session_token" "$state_document")" ||
    die "cannot bind the opening evidence state to the session token"
  expected_binding="$(printf '%s' "$state_document" |
    "$jq_executable" -r '.openingStateBinding')" ||
    die "cannot read the recorded opening-state binding"
  if [[ "$actual_binding" != "$expected_binding" && "$allow_stale_binding" != true ]]; then
    die "opening evidence state was rewritten after start"
  fi
}

load_opening_state() {
  [[ $# -eq 2 ]] || die "opening-state loader received an invalid argument count"
  local session_token="$1"
  local allow_stale_binding="$2"
  resolve_state_path
  local -a state_candidates=()
  shopt -s nullglob
  state_candidates=("$state_directory"/opening.*.json)
  shopt -u nullglob
  [[ "${#state_candidates[@]}" -eq 1 ]] ||
    die "opening evidence state is missing, ambiguous, or already consumed: $state_directory"
  state_file="${state_candidates[0]}"
  opening_state_document="$(<"$state_file")" ||
    die "cannot read the opening evidence state: $state_file"
  assert_authenticated_state_document \
    "$session_token" "$allow_stale_binding" "$state_file" "$opening_state_document"
}

# Structure, checkout ownership, and token authentication for one already read state
# document. Every probe consumes the same buffer, never the path.
assert_authenticated_state_document() {
  [[ $# -eq 4 ]] || die "state-document validator received an invalid argument count"
  local session_token="$1"
  local allow_stale_binding="$2"
  local declared_state_path="$3"
  local state_document="$4"

  printf '%s' "$state_document" | "$jq_executable" -e '
    .schemaVersion == 2 and
    (.session | type == "string" and length > 0) and
    (.sessionTokenDigest | type == "string" and test("^[0-9a-f]{64}$")) and
    (.openingStateBinding | type == "string" and test("^[0-9a-f]{64}$")) and
    (.repository | type == "string") and
    (.localHeadOid | type == "string") and
    (.statePath | type == "string") and
    (.worktree | type == "string") and
    (.gitDirectory | type == "string") and
    (.opening | type == "object")
  ' >/dev/null || die "opening evidence state is invalid"
  printf '%s' "$state_document" | "$jq_executable" -e \
    --arg statePath "$declared_state_path" \
    --arg worktree "$state_worktree" \
    --arg gitDirectory "$state_git_dir" '
      .statePath == $statePath and
      .worktree == $worktree and
      .gitDirectory == $gitDirectory and
      .statePath == ($gitDirectory + "/taskdeck-pre-merge-evidence/opening." +
        (.opening.number | tostring) + "." + .opening.headRefOid + ".json")
    ' >/dev/null ||
    die "opening evidence state does not belong to this checkout"
  validate_session_token "$session_token" "$allow_stale_binding" "$state_document"
}

validate_pr_snapshot() {
  local snapshot_file="$1"
  "$jq_executable" -e '
    (.number | type == "number") and
    (.headRefName | type == "string" and length > 0) and
    (.headRefOid | type == "string" and test("^[0-9a-f]{40}$")) and
    (.baseRefName | type == "string" and length > 0) and
    (.baseRefOid | type == "string" and test("^[0-9a-f]{40}$")) and
    (.mergeable | type == "string" and length > 0) and
    (.updatedAt | type == "string" and length > 0) and
    (.url | type == "string" and length > 0)
  ' "$snapshot_file" >/dev/null || die "PR lookup returned an incomplete identity snapshot"
}

assert_clean_exact_checkout() {
  local expected_head="$1"
  local local_head
  local worktree_status
  local hidden_index_entries
  local replacement_refs

  local_head="$("$git_executable" rev-parse HEAD)" || die "cannot resolve local HEAD"
  [[ "$local_head" == "$expected_head" ]] ||
    die "local HEAD $local_head is not PR head $expected_head"

  if ! hidden_index_entries="$("$git_executable" ls-files -v)"; then
    die "cannot inspect tracked index flags"
  fi
  hidden_index_entries="$(printf '%s\n' "$hidden_index_entries" | "$awk_executable" '$1 ~ /^[a-zS]$/ {print; found=1} END {exit found ? 0 : 1}')" || true
  [[ -z "$hidden_index_entries" ]] ||
    die "exact-head evidence rejects assume-unchanged or skip-worktree index flags"
  if ! replacement_refs="$("$git_executable" for-each-ref --format='%(refname)' refs/replace/)"; then
    die "cannot inspect Git replacement refs"
  fi
  [[ -z "$replacement_refs" ]] || die "exact-head evidence rejects Git replacement refs"

  worktree_status="$("$git_executable" status --porcelain=v1 --untracked-files=all)" ||
    die "cannot inspect worktree status"
  [[ -z "$worktree_status" ]] || die "exact-head evidence requires a clean worktree"
}

start_collection() {
  [[ $# -le 2 ]] || usage
  local requested_pr="${2-}"
  local repo_full_name
  local selected_pr
  local selected_head
  local selected_base
  local fetched_base
  local merge_base
  local state_tmp
  local state_session
  local session_token
  local session_token_digest
  local opening_state_binding
  local -a pr_args=()
  local snapshot_tmp

  if [[ -n "$requested_pr" ]]; then
    [[ "$requested_pr" =~ ^[1-9][0-9]*$ ]] || die "PR number must be a positive integer"
    pr_args=("$requested_pr")
  fi

  # Resolve the checkout first: this is where the evidence tools are re-checked against the
  # measured worktree, and no external evidence tool may run before that rejection except the
  # Git that reports the measured checkout's own identity -- which the bash-only discovery and
  # PATH sanitization at the top of this file have already proven is not checkout-local.
  resolve_state_path
  repo_full_name="$("$gh_executable" repo view --json nameWithOwner --jq .nameWithOwner)" ||
    die "cannot identify the current GitHub repository"
  [[ "$repo_full_name" =~ ^[^/]+/[^/]+$ ]] || die "repository identity is invalid"

  "$mkdir_executable" -p "$state_directory" || die "cannot create the checkout evidence directory"
  if compgen -G "$state_directory/opening.*.json" >/dev/null; then
    die "an unfinished pre-merge evidence session already exists for this checkout; investigate it, then run abort before restarting: $state_directory"
  fi
  umask 077
  snapshot_tmp="$("$mktemp_executable")"
  trap '"$rm_executable" -f "${snapshot_tmp:-}" "${state_tmp:-}"' EXIT
  "$gh_executable" pr view "${pr_args[@]}" \
    --json number,headRefName,headRefOid,baseRefName,baseRefOid,mergeable,updatedAt,url \
    >"$snapshot_tmp" || die "cannot resolve the selected pull request"
  validate_pr_snapshot "$snapshot_tmp"

  selected_pr="$("$jq_executable" -r '.number' "$snapshot_tmp")"
  if [[ -n "$requested_pr" && "$selected_pr" != "$requested_pr" ]]; then
    die "explicit PR $requested_pr resolved to PR $selected_pr"
  fi

  selected_head="$("$jq_executable" -r '.headRefOid' "$snapshot_tmp")"
  selected_base="$("$jq_executable" -r '.baseRefOid' "$snapshot_tmp")"
  [[ "$("$jq_executable" -r '.mergeable' "$snapshot_tmp")" == "MERGEABLE" ]] ||
    die "selected PR is not currently mergeable"
  assert_clean_exact_checkout "$selected_head"
  state_file="$state_directory/opening.${selected_pr}.${selected_head}.json"

  "$git_executable" fetch --no-tags origin "$("$jq_executable" -r '.baseRefName' "$snapshot_tmp")" ||
    die "cannot fetch the selected PR base"
  fetched_base="$("$git_executable" rev-parse FETCH_HEAD)" || die "cannot resolve fetched base"
  [[ "$fetched_base" == "$selected_base" ]] ||
    die "fetched base $fetched_base is not PR base $selected_base"
  merge_base="$("$git_executable" merge-base HEAD FETCH_HEAD)" || die "cannot resolve merge base"
  [[ "$merge_base" == "$selected_base" ]] ||
    die "PR head does not incorporate exact base $selected_base (merge base: $merge_base)"

  state_tmp="$("$mktemp_executable" "${state_file}.tmp.XXXXXX")" ||
    die "cannot create opening evidence state"
  # Basename by parameter expansion: one fewer external in the trust chain.
  state_session="${state_tmp##*/}"
  session_token="$("$openssl_executable" rand -hex 32)" ||
    die "cannot generate the operator-carried session token"
  [[ "$session_token" =~ ^[0-9a-f]{64}$ ]] ||
    die "generated session token has an invalid shape"
  session_token_digest="$(sha256_text "$session_token")" ||
    die "cannot hash the operator-carried session token"
  "$jq_executable" -n \
    --arg schemaVersion "2" \
    --arg session "$state_session" \
    --arg sessionTokenDigest "$session_token_digest" \
    --arg repository "$repo_full_name" \
    --arg selection "$(if [[ -n "$requested_pr" ]]; then printf explicit; else printf current-branch; fi)" \
    --arg localHeadOid "$selected_head" \
    --arg statePath "$state_file" \
    --arg worktree "$state_worktree" \
    --arg gitDirectory "$state_git_dir" \
    --slurpfile opening "$snapshot_tmp" \
    '{
      schemaVersion: ($schemaVersion | tonumber),
      session: $session,
      sessionTokenDigest: $sessionTokenDigest,
      repository: $repository,
      selection: $selection,
      localHeadOid: $localHeadOid,
      statePath: $statePath,
      worktree: $worktree,
      gitDirectory: $gitDirectory,
      opening: $opening[0]
    }' >"$state_tmp"
  opening_state_binding="$(state_binding "$session_token" "$(<"$state_tmp")")" ||
    die "cannot bind opening evidence state to the operator-carried session token"
  "$jq_executable" --arg openingStateBinding "$opening_state_binding" \
    '. + {openingStateBinding: $openingStateBinding}' "$state_tmp" >"${state_tmp}.bound" ||
    die "cannot finalize the opening evidence state"
  "$mv_executable" -f "${state_tmp}.bound" "$state_tmp" ||
    die "cannot finalize the opening evidence state"
  "$ln_executable" "$state_tmp" "$state_file" ||
    die "opening evidence state was created concurrently or replaced: $state_file"
  "$rm_executable" -f "$state_tmp"
  state_tmp=""
  trap - EXIT
  "$rm_executable" -f "$snapshot_tmp"
  printf 'Opening evidence bound to PR #%s at %s against %s.\n' \
    "$selected_pr" "$selected_head" "$selected_base" >&2
  printf '%s\n' "$session_token"
}

abort_collection() {
  [[ $# -eq 1 ]] || usage
  local session_token
  IFS= read -r session_token || die "session token must be supplied through stdin"
  [[ -n "$session_token" ]] || die "session token must be supplied through stdin"
  load_opening_state "$session_token" true
  local aborted_pr
  local aborted_head
  aborted_pr="$(printf '%s' "$opening_state_document" |
    "$jq_executable" -r '.opening.number')"
  aborted_head="$(printf '%s' "$opening_state_document" |
    "$jq_executable" -r '.opening.headRefOid')"
  "$rm_executable" -f -- "$state_file" || die "cannot abort the opening evidence state"
  printf 'Aborted pre-merge evidence session for PR #%s at %s.\n' \
    "$aborted_pr" "$aborted_head" >&2
}

collect_feedback_snapshot() {
  [[ $# -eq 5 ]] || die "feedback collector received an invalid argument count"
  local snapshot_dir="$1"
  local repo_full_name="$2"
  local repo_owner="$3"
  local repo_name="$4"
  local pr_number="$5"
  local review_threads_query
  local thread_comments_query
  local thread_id
  local thread_pages

  "$mkdir_executable" -p "$snapshot_dir"

  "$gh_executable" api --paginate --slurp \
    "repos/$repo_full_name/issues/$pr_number/comments?per_page=100" \
    >"$snapshot_dir/issue-comment-pages.json"
  "$jq_executable" '[.[][]] | sort_by(.id)' "$snapshot_dir/issue-comment-pages.json" \
    >"$snapshot_dir/issue-comments.json"

  "$gh_executable" api --paginate --slurp \
    "repos/$repo_full_name/pulls/$pr_number/reviews?per_page=100" \
    >"$snapshot_dir/review-pages.json"
  "$jq_executable" '[.[][]] | sort_by(.id)' "$snapshot_dir/review-pages.json" \
    >"$snapshot_dir/reviews.json"

  review_threads_query='query($owner:String!,$name:String!,$number:Int!,$endCursor:String){repository(owner:$owner,name:$name){pullRequest(number:$number){reviewThreads(first:100,after:$endCursor){nodes{id isResolved isOutdated path line originalLine}pageInfo{hasNextPage endCursor}}}}}'
  "$gh_executable" api graphql --paginate --slurp \
    -f query="$review_threads_query" \
    -F owner="$repo_owner" -F name="$repo_name" -F number="$pr_number" \
    >"$snapshot_dir/thread-pages.json"
  "$jq_executable" -e '
    type == "array" and length > 0 and
    (.[-1].data.repository.pullRequest.reviewThreads.pageInfo.hasNextPage == false)
  ' "$snapshot_dir/thread-pages.json" >/dev/null || die "review-thread pagination was incomplete"
  "$jq_executable" '[.[] | (.data.repository.pullRequest.reviewThreads.nodes // [])[]]' \
    "$snapshot_dir/thread-pages.json" >"$snapshot_dir/thread-index.json"
  [[ "$("$jq_executable" 'length' "$snapshot_dir/thread-index.json")" == \
     "$("$jq_executable" '[.[].id] | unique | length' "$snapshot_dir/thread-index.json")" ]] ||
    die "review-thread pagination returned duplicate identities"

  : >"$snapshot_dir/threads.jsonl"
  thread_comments_query='query($threadId:ID!,$endCursor:String){node(id:$threadId){... on PullRequestReviewThread{id isResolved isOutdated path line originalLine comments(first:100,after:$endCursor){nodes{author{login}body createdAt lastEditedAt url path line originalLine diffHunk}pageInfo{hasNextPage endCursor}}}}}'
  while IFS= read -r thread_id; do
    [[ -n "$thread_id" ]] || continue
    thread_pages="$snapshot_dir/thread-${thread_id//[^A-Za-z0-9_.-]/_}.json"
    "$gh_executable" api graphql --paginate --slurp \
      -f query="$thread_comments_query" -F threadId="$thread_id" \
      >"$thread_pages"
    "$jq_executable" -e --arg threadId "$thread_id" \
      --slurpfile index "$snapshot_dir/thread-index.json" '
      type == "array" and length > 0 and
      (.[-1].data.node.comments.pageInfo.hasNextPage == false) and
      (($index[0] | map(select(.id == $threadId))) as $matches |
        ($matches | length) == 1 and
        ($matches[0] as $expected |
          all(.[];
            (.data.node | type) == "object" and
            .data.node.id == $expected.id and
            .data.node.isResolved == $expected.isResolved and
            .data.node.isOutdated == $expected.isOutdated and
            .data.node.path == $expected.path and
            .data.node.line == $expected.line and
            .data.node.originalLine == $expected.originalLine))) and
      (([.[] | (.data.node.comments.nodes // [])[] | .url] | length) ==
        ([.[] | (.data.node.comments.nodes // [])[] | .url] | unique | length))
    ' "$thread_pages" >/dev/null ||
      die "comment pagination or thread identity changed for review thread $thread_id"
    "$jq_executable" -c '{
      id: .[0].data.node.id,
      isResolved: .[0].data.node.isResolved,
      isOutdated: .[0].data.node.isOutdated,
      path: .[0].data.node.path,
      line: .[0].data.node.line,
      originalLine: .[0].data.node.originalLine,
      comments: ([.[] | (.data.node.comments.nodes // [])[]] | sort_by(.createdAt, .url))
    }' "$thread_pages" >>"$snapshot_dir/threads.jsonl"
  done < <("$jq_executable" -r '.[].id' "$snapshot_dir/thread-index.json" | "$tr_executable" -d '\r')
  "$jq_executable" -s 'sort_by(.id)' "$snapshot_dir/threads.jsonl" \
    >"$snapshot_dir/threads.json"

  "$jq_executable" -n \
    --slurpfile issueComments "$snapshot_dir/issue-comments.json" \
    --slurpfile reviews "$snapshot_dir/reviews.json" \
    --slurpfile threads "$snapshot_dir/threads.json" \
    '{
      issueComments: $issueComments[0],
      reviewSummaries: $reviews[0],
      reviewThreads: $threads[0]
    }' >"$snapshot_dir/snapshot.json"
  "$jq_executable" -S -c '.' "$snapshot_dir/snapshot.json" \
    >"$snapshot_dir/canonical.json"
}

collect_checks_snapshot() {
  [[ $# -eq 2 ]] || die "checks collector received an invalid argument count"
  local snapshot_dir="$1"
  local pr_number="$2"
  local command_exit=0

  "$mkdir_executable" -p "$snapshot_dir"
  "$gh_executable" pr checks "$pr_number" \
    --json name,state,bucket,link,workflow >"$snapshot_dir/entries-raw.json" || command_exit=$?
  "$jq_executable" -e 'type == "array"' "$snapshot_dir/entries-raw.json" >/dev/null ||
    die "PR checks returned an invalid payload"
  "$jq_executable" 'sort_by(.name, .workflow, .link)' "$snapshot_dir/entries-raw.json" \
    >"$snapshot_dir/entries.json"
  "$jq_executable" -n \
    --argjson commandExit "$command_exit" \
    --slurpfile entries "$snapshot_dir/entries.json" \
    '{commandExit: $commandExit, entries: $entries[0]}' >"$snapshot_dir/snapshot.json"
  "$jq_executable" -S -c '.' "$snapshot_dir/snapshot.json" \
    >"$snapshot_dir/canonical.json"
}

# Definition reads resolve from the authenticated opening head OID, never from the mutable
# HEAD ref: a PR-controlled background process can move a symbolic HEAD's branch ref to the
# opening base for the duration of these reads and restore it before the closing checkout
# assertion, which would make every changed Gitleaks definition compare equal to its base.
bind_enforcing_gitleaks_definitions() {
  [[ $# -eq 4 ]] || return 1
  local opening_base="$1"
  local opening_head="$2"
  local caller_path="$3"
  local binding_file="$4"
  local caller_body
  local reusable_path
  local path
  local head_blob
  local base_blob
  local -a definition_paths=()

  caller_body="$("$git_executable" show "$opening_base:$caller_path")" || return 1
  reusable_path="$(printf '%s\n' "$caller_body" | "$awk_executable" '
    /^  secret-scan:/ { in_secret_scan = 1; next }
    in_secret_scan && /^  [^[:space:]][^:]*:/ { in_secret_scan = 0 }
    in_secret_scan && /^[[:space:]]+uses:[[:space:]]+\.\/\.github\/workflows\/reusable-gitleaks\.yml[[:space:]]*$/ {
      sub(/^[[:space:]]+uses:[[:space:]]+\.\//, "")
      print
      exit
    }
  ' )"
  [[ "$reusable_path" == ".github/workflows/reusable-gitleaks.yml" ]] || return 1

  definition_paths=(
    "$caller_path"
    "$reusable_path"
    ".gitleaks.toml"
    ".gitleaksignore"
  )
  : >"$binding_file"
  local definitions_match=true
  for path in "${definition_paths[@]}"; do
    base_blob="$("$git_executable" rev-parse "$opening_base:$path")" || return 1
    head_blob="$("$git_executable" rev-parse "$opening_head:$path")" || return 1
    if [[ "$head_blob" != "$base_blob" ]]; then
      definitions_match=false
    fi
    "$jq_executable" -n \
      --arg path "$path" --arg baseBlob "$base_blob" --arg headBlob "$head_blob" \
      --argjson matches "$([[ "$head_blob" == "$base_blob" ]] && printf true || printf false)" \
      '{path: $path, openingBaseBlob: $baseBlob, localHeadBlob: $headBlob, matchesOpeningBase: $matches}' \
      >>"$binding_file" || return 1
  done
  "$jq_executable" -s '.' "$binding_file" >"${binding_file}.json" || return 1
  "$mv_executable" -f "${binding_file}.json" "$binding_file" || return 1
  [[ "$definitions_match" == true ]]
}

finish_collection() {
  [[ $# -eq 1 ]] || usage
  local session_token
  local persistent_state_file
  local state_snapshot
  local snapshot_state_document
  IFS= read -r session_token || die "session token must be supplied through stdin"
  [[ -n "$session_token" ]] || die "session token must be supplied through stdin"
  load_opening_state "$session_token" false
  persistent_state_file="$state_file"
  state_snapshot="$("$mktemp_executable")" || die "cannot create immutable opening-state snapshot"
  "$cp_executable" -- "$persistent_state_file" "$state_snapshot" ||
    die "cannot snapshot opening evidence state"
  trap '"$rm_executable" -f "${state_snapshot:-}"' EXIT
  # Authenticating the persistent pathname and then copying it left a window in which a
  # PR-controlled background process could replace the state, so the copied bytes themselves
  # are authenticated here, before any field is read. Every later read consumes this exact
  # authenticated buffer rather than reopening a file that can still change.
  snapshot_state_document="$(<"$state_snapshot")" ||
    die "cannot read the immutable opening-state snapshot"
  assert_authenticated_state_document \
    "$session_token" false "$persistent_state_file" "$snapshot_state_document"

  local repo_full_name
  local repo_owner
  local repo_name
  local pr_number
  local opening_head
  local opening_head_ref
  local opening_base
  local opening_base_ref
  local temp_dir
  local required_secret_check_name="Secret Scan / Gitleaks Scan"
  local required_secret_workflow="CI"
  local required_secret_workflow_path=".github/workflows/ci-required.yml"
  local secret_count
  local clean_secret_count
  local secret_link
  local secret_link_prefix
  local secret_run_id=""
  local secret_run_path
  local secret_run_exit=0
  local secret_provenance_verified=false
  local secret_definitions_verified=false
  local secrets_verdict="NOT VERIFIED"

  repo_full_name="$(printf '%s' "$snapshot_state_document" | "$jq_executable" -r '.repository')"
  [[ "$repo_full_name" =~ ^[^/]+/[^/]+$ ]] || die "repository identity is invalid"
  repo_owner="${repo_full_name%%/*}"
  repo_name="${repo_full_name#*/}"
  pr_number="$(printf '%s' "$snapshot_state_document" | "$jq_executable" -r '.opening.number')"
  opening_head="$(printf '%s' "$snapshot_state_document" |
    "$jq_executable" -r '.opening.headRefOid')"
  opening_head_ref="$(printf '%s' "$snapshot_state_document" |
    "$jq_executable" -r '.opening.headRefName')"
  opening_base="$(printf '%s' "$snapshot_state_document" |
    "$jq_executable" -r '.opening.baseRefOid')"
  opening_base_ref="$(printf '%s' "$snapshot_state_document" |
    "$jq_executable" -r '.opening.baseRefName')"
  assert_clean_exact_checkout "$opening_head"

  temp_dir="$("$mktemp_executable" -d)"
  trap '"$rm_executable" -rf "${temp_dir:-}"; "$rm_executable" -f "${state_snapshot:-}"' EXIT
  printf '[]\n' >"$temp_dir/secret-definition-bindings.jsonl"

  collect_feedback_snapshot "$temp_dir/feedback-first" \
    "$repo_full_name" "$repo_owner" "$repo_name" "$pr_number"

  collect_checks_snapshot "$temp_dir/checks-first" "$pr_number"
  "$jq_executable" '[.[] | select(
    ((.name // "") | ascii_downcase | test("secret[ _-]*scan|gitleaks"))
  )]' "$temp_dir/checks-first/entries.json" >"$temp_dir/secret-candidates.json"
  "$jq_executable" \
    --arg requiredName "$required_secret_check_name" \
    --arg requiredWorkflow "$required_secret_workflow" \
    '[.[] | select(.name == $requiredName and .workflow == $requiredWorkflow)]' \
    "$temp_dir/checks-first/entries.json" >"$temp_dir/secret-checks.json"
  secret_count="$("$jq_executable" 'length' "$temp_dir/secret-checks.json")"
  clean_secret_count="$("$jq_executable" \
    '[.[] | select(.state == "SUCCESS" and .bucket == "pass")] | length' \
    "$temp_dir/secret-checks.json")"
  printf 'null\n' >"$temp_dir/secret-workflow-run.json"
  if [[ "$secret_count" -eq 1 && "$clean_secret_count" -eq 1 ]]; then
    secret_link="$("$jq_executable" -r '.[0].link' "$temp_dir/secret-checks.json")"
    secret_link_prefix="https://github.com/$repo_full_name/actions/runs/"
    secret_run_path="${secret_link#"$secret_link_prefix"}"
    if [[ "$secret_run_path" != "$secret_link" && \
          "$secret_run_path" =~ ^([1-9][0-9]+)(/job/[1-9][0-9]+)?$ ]]; then
      secret_run_id="${BASH_REMATCH[1]}"
      "$gh_executable" api "repos/$repo_full_name/actions/runs/$secret_run_id" \
        >"$temp_dir/secret-workflow-run.json" || secret_run_exit=$?
      if [[ "$secret_run_exit" -eq 0 ]] && "$jq_executable" -e \
        --argjson runId "$secret_run_id" \
        --argjson prNumber "$pr_number" \
        --arg workflowName "$required_secret_workflow" \
        --arg workflowPath "$required_secret_workflow_path" \
        --arg headOid "$opening_head" \
        --arg headRef "$opening_head_ref" \
        --arg baseOid "$opening_base" \
        --arg baseRef "$opening_base_ref" '
          .id == $runId and
          .name == $workflowName and
          .path == $workflowPath and
          .event == "pull_request" and
          .status == "completed" and
          .conclusion == "success" and
          .head_sha == $headOid and
          .head_branch == $headRef and
          any(.pull_requests[]?;
            .number == $prNumber and
            .head.sha == $headOid and
            .head.ref == $headRef and
            .base.sha == $baseOid and
            .base.ref == $baseRef)
        ' "$temp_dir/secret-workflow-run.json" >/dev/null; then
        secret_provenance_verified=true
      fi
    fi
  fi
  if [[ "$secret_provenance_verified" == true ]]; then
    if bind_enforcing_gitleaks_definitions "$opening_base" "$opening_head" \
      "$required_secret_workflow_path" \
      "$temp_dir/secret-definition-bindings.jsonl"; then
      secret_definitions_verified=true
    fi
  fi

  collect_feedback_snapshot "$temp_dir/feedback-second" \
    "$repo_full_name" "$repo_owner" "$repo_name" "$pr_number"
  "$cmp_executable" -s \
    "$temp_dir/feedback-first/canonical.json" \
    "$temp_dir/feedback-second/canonical.json" ||
    die "PR feedback changed while evidence was collected"
  collect_checks_snapshot "$temp_dir/checks-second" "$pr_number"
  "$cmp_executable" -s \
    "$temp_dir/checks-first/canonical.json" \
    "$temp_dir/checks-second/canonical.json" ||
    die "PR checks changed while evidence was collected"
  if [[ "$secret_provenance_verified" == true && "$secret_definitions_verified" == true ]]; then
    secrets_verdict="CLEAN"
  fi

  "$gh_executable" pr view "$pr_number" \
    --json number,headRefName,headRefOid,baseRefName,baseRefOid,mergeable,updatedAt,url \
    >"$temp_dir/closing.json" || die "cannot resolve the closing PR identity"
  validate_pr_snapshot "$temp_dir/closing.json"
  printf '%s' "$snapshot_state_document" |
    "$jq_executable" '.opening' >"$temp_dir/opening.json"
  "$jq_executable" -e -s '
    .[0] as $opening | .[1] as $closing |
    $opening.number == $closing.number and
    $opening.headRefName == $closing.headRefName and
    $opening.headRefOid == $closing.headRefOid and
    $opening.baseRefName == $closing.baseRefName and
    $opening.baseRefOid == $closing.baseRefOid and
    $opening.mergeable == $closing.mergeable and
    $opening.updatedAt == $closing.updatedAt
  ' "$temp_dir/opening.json" "$temp_dir/closing.json" >/dev/null ||
    die "PR identity or feedback timestamp changed while evidence was collected"
  assert_clean_exact_checkout "$opening_head"

  "$jq_executable" -n \
    --argjson state "$snapshot_state_document" \
    --slurpfile closing "$temp_dir/closing.json" \
    --slurpfile feedback "$temp_dir/feedback-second/snapshot.json" \
    --slurpfile checks "$temp_dir/checks-second/snapshot.json" \
    --slurpfile secretChecks "$temp_dir/secret-checks.json" \
    --slurpfile secretCandidates "$temp_dir/secret-candidates.json" \
    --slurpfile secretWorkflowRun "$temp_dir/secret-workflow-run.json" \
    --slurpfile secretDefinitionBindings "$temp_dir/secret-definition-bindings.jsonl" \
    --arg stateSession "$(printf '%s' "$snapshot_state_document" |
      "$jq_executable" -r '.session')" \
    --arg requiredSecretCheckName "$required_secret_check_name" \
    --arg requiredSecretWorkflow "$required_secret_workflow" \
    --arg requiredSecretWorkflowPath "$required_secret_workflow_path" \
    --argjson secretProvenanceVerified "$secret_provenance_verified" \
    --argjson secretDefinitionsVerified "$secret_definitions_verified" \
    --arg secretsVerdict "$secrets_verdict" \
    '{
      schemaVersion: 2,
      evidenceSession: $stateSession,
      repository: $state.repository,
      selection: $state.selection,
      pr: {
        number: $state.opening.number,
        url: $state.opening.url,
        opening: $state.opening,
        closing: $closing[0],
        localHeadOid: $state.localHeadOid
      },
      feedback: ($feedback[0] + {stableAcrossTwoSnapshots: true}),
      checks: ($checks[0] + {stableAcrossTwoSnapshots: true}),
      secrets: {
        requiredCheckName: $requiredSecretCheckName,
        requiredWorkflow: $requiredSecretWorkflow,
        requiredWorkflowPath: $requiredSecretWorkflowPath,
        verdict: $secretsVerdict,
        provenanceVerified: $secretProvenanceVerified,
        definitionsVerified: $secretDefinitionsVerified,
        definitionBindings: $secretDefinitionBindings[0],
        workflowRun: $secretWorkflowRun[0],
        evidence: $secretChecks[0],
        observedCandidates: $secretCandidates[0]
      },
      collectorState: (if $secretsVerdict == "CLEAN" then "COMPLETE" else "INCOMPLETE" end)
    }'

  if [[ "$secrets_verdict" != "CLEAN" ]]; then
    die "exact-head secret-scan evidence or enforcing definition binding is missing, changed, pending, failed, or ambiguous"
  fi
  "$rm_executable" -f "$persistent_state_file" ||
    die "cannot consume the completed opening evidence state"
}

case "$mode" in
  start)
    start_collection "$@"
    ;;
  abort)
    abort_collection "$@"
    ;;
  finish)
    finish_collection "$@"
    ;;
  *)
    usage
    ;;
esac
