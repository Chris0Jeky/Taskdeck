#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
collector="$script_dir/collect-pre-merge-evidence.sh"
skill_file="$script_dir/../../.claude/skills/pre-merge-gate/SKILL.md"
real_jq="$(command -v jq)"
real_awk="$(command -v awk)"
if ! real_git="$(command -v git.exe 2>/dev/null)"; then
  real_git="$(command -v git)"
fi
fixture_root="$(mktemp -d)"
trap 'rm -rf "$fixture_root"' EXIT

passed=0

fail() {
  printf 'not ok - %s\n' "$*" >&2
  exit 1
}

pass() {
  passed=$((passed + 1))
  printf 'ok %d - %s\n' "$passed" "$1"
}

make_mocks() {
  local case_root="$1"
  mkdir -p "$case_root/bin"

  cat >"$case_root/bin/git" <<'MOCK_GIT'
#!/usr/bin/env bash
set -euo pipefail
printf 'git' >>"$MOCK_ROOT/calls.log"
printf ' %q' "$@" >>"$MOCK_ROOT/calls.log"
printf '\n' >>"$MOCK_ROOT/calls.log"

head_oid=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
base_oid=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
worktree_root="${MOCK_WORKTREE_ROOT:-/mock/checkouts/current}"
git_dir="$worktree_root/.git"
if [[ "$MOCK_SCENARIO" == "wrong-checkout" ]]; then
  head_oid=dddddddddddddddddddddddddddddddddddddddd
fi

case "${1-}" in
  rev-parse)
    case "${2-}" in
      HEAD) printf '%s\n' "$head_oid" ;;
      FETCH_HEAD) printf '%s\n' "$base_oid" ;;
      --show-toplevel) printf '%s\n' "$worktree_root" ;;
      --absolute-git-dir) printf '%s\n' "$git_dir" ;;
      --git-common-dir) printf '%s\n' "$git_dir" ;;
      *:*)
        revision=base
        if [[ "${2%%:*}" == "HEAD" ]]; then
          printf 'mock git: definition reads must bind to the authenticated head OID, not HEAD\n' >&2
          exit 3
        fi
        if [[ "${2%%:*}" == "$head_oid" ]]; then
          revision=head
        fi
        fixture_path="$MOCK_ROOT/$revision/${2#*:}"
        [[ -f "$fixture_path" ]] || exit 2
        sha1sum "$fixture_path" | awk '{print $1}'
        ;;
      *) exit 2 ;;
    esac
    ;;
  show)
    if [[ "${2-}" == "$base_oid:.github/workflows/ci-required.yml" ]]; then
      cat "$MOCK_ROOT/base/.github/workflows/ci-required.yml"
    else
      exit 2
    fi
    ;;
  status)
    ;;
  ls-files)
    if [[ "$MOCK_SCENARIO" == "index-inspection-failure" ]]; then
      count_file="$MOCK_ROOT/index-inspection-count"
      count=0; [[ -f "$count_file" ]] && count="$(<"$count_file")"
      count=$((count + 1)); printf '%s' "$count" >"$count_file"
      [[ "$count" -le 1 ]] || exit 9
    fi
    ;;
  for-each-ref)
    if [[ "$MOCK_SCENARIO" == "replacement-ref-inspection-failure" ]]; then
      count_file="$MOCK_ROOT/replacement-ref-inspection-count"
      count=0; [[ -f "$count_file" ]] && count="$(<"$count_file")"
      count=$((count + 1)); printf '%s' "$count" >"$count_file"
      [[ "$count" -le 1 ]] || exit 9
    fi
    ;;
  fetch)
    ;;
  merge-base)
    printf '%s\n' "$base_oid"
    ;;
  *)
    exit 2
    ;;
esac
MOCK_GIT

  cat >"$case_root/bin/gh" <<'MOCK_GH'
#!/usr/bin/env bash
set -euo pipefail
printf 'gh' >>"$MOCK_ROOT/calls.log"
printf ' %q' "$@" >>"$MOCK_ROOT/calls.log"
printf '\n' >>"$MOCK_ROOT/calls.log"

command_name="${1-}"
shift || true

head_oid=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
base_oid=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
if [[ -f "$MOCK_ROOT/head-oid" ]]; then
  head_oid="$(<"$MOCK_ROOT/head-oid")"
fi
if [[ -f "$MOCK_ROOT/base-oid" ]]; then
  base_oid="$(<"$MOCK_ROOT/base-oid")"
fi

# Install or remove a transient attack against a real Git checkout at exactly the point the
# collector reads the enforcing scan definitions: between the opening and closing
# clean-checkout assertions, where boundary checks cannot see it.
run_race_hook() {
  local phase="$1"
  local definition_path=.gitleaks.toml
  [[ -n "${MOCK_REAL_REPO:-}" && -n "${MOCK_REAL_GIT:-}" ]] || return 0
  case "$MOCK_SCENARIO" in
    replacement-ref-race)
      if [[ "$phase" == install ]]; then
        env -u GIT_NO_REPLACE_OBJECTS "$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" \
          replace --force "$head_oid" "$base_oid" >/dev/null || exit 91
        {
          printf 'redirected %s\n' "$(env -u GIT_NO_REPLACE_OBJECTS "$MOCK_REAL_GIT" \
            -C "$MOCK_REAL_REPO" rev-parse "$head_oid:$definition_path")"
          printf 'authentic %s\n' "$(env GIT_NO_REPLACE_OBJECTS=1 "$MOCK_REAL_GIT" \
            -C "$MOCK_REAL_REPO" rev-parse "$head_oid:$definition_path")"
        } >"$MOCK_ROOT/race-probe"
      else
        env -u GIT_NO_REPLACE_OBJECTS "$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" \
          replace -d "$head_oid" >/dev/null || exit 92
      fi
      ;;
    head-ref-race)
      if [[ "$phase" == install ]]; then
        "$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" update-ref refs/heads/main "$base_oid" || exit 93
        {
          printf 'redirected %s\n' "$("$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" \
            rev-parse "HEAD:$definition_path")"
          printf 'authentic %s\n' "$("$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" \
            rev-parse "$head_oid:$definition_path")"
        } >"$MOCK_ROOT/race-probe"
      else
        "$MOCK_REAL_GIT" -C "$MOCK_REAL_REPO" update-ref refs/heads/main "$head_oid" || exit 94
      fi
      ;;
  esac
}

if [[ "$command_name" == "repo" && "${1-}" == "view" ]]; then
  printf 'owner/repo\n'
  exit 0
fi

if [[ "$command_name" == "pr" && "${1-}" == "view" ]]; then
  shift
  number=""
  for argument in "$@"; do
    if [[ "$argument" =~ ^[1-9][0-9]*$ ]]; then
      number="$argument"
      break
    fi
  done
  if [[ -z "$number" ]]; then
    number=77
  fi

  view_count_file="$MOCK_ROOT/pr-view-count"
  view_count=0
  if [[ -f "$view_count_file" ]]; then
    view_count="$(<"$view_count_file")"
  fi
  view_count=$((view_count + 1))
  printf '%s' "$view_count" >"$view_count_file"

  mergeable=MERGEABLE
  updated_at=2026-08-01T12:00:00Z
  if [[ "$MOCK_SCENARIO" == "oid-drift" && "$view_count" -gt 1 ]]; then
    head_oid=cccccccccccccccccccccccccccccccccccccccc
  fi
  if [[ "$MOCK_SCENARIO" == "base-oid-drift" && "$view_count" -gt 1 ]]; then
    base_oid=eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee
  fi
  if [[ "$MOCK_SCENARIO" == "feedback-drift" && "$view_count" -gt 1 ]]; then
    updated_at=2026-08-01T12:00:01Z
  fi
  if [[ "$MOCK_SCENARIO" == "mergeability-drift" && "$view_count" -gt 1 ]]; then
    mergeable=CONFLICTING
  fi

  jq -n \
    --argjson number "$number" \
    --arg head "$head_oid" \
    --arg base "$base_oid" \
    --arg mergeable "$mergeable" \
    --arg updated "$updated_at" \
    '{
      number: $number,
      headRefName: "issue-1547/atomic-evidence",
      headRefOid: $head,
      baseRefName: "main",
      baseRefOid: $base,
      mergeable: $mergeable,
      updatedAt: $updated,
      url: ("https://github.com/owner/repo/pull/" + ($number | tostring))
    }'
  exit 0
fi

if [[ "$command_name" == "pr" && "${1-}" == "checks" ]]; then
  checks_count_file="$MOCK_ROOT/checks-count"
  checks_count=0
  if [[ -f "$checks_count_file" ]]; then
    checks_count="$(<"$checks_count_file")"
  fi
  checks_count=$((checks_count + 1))
  printf '%s' "$checks_count" >"$checks_count_file"
  printf '%s' "${2-}" >"$MOCK_ROOT/selected-pr-number"

  checks_exit=0
  case "$MOCK_SCENARIO" in
    secret-missing)
      printf '[{"name":"Docs Governance","state":"SUCCESS","bucket":"pass","link":"https://checks/docs","workflow":"CI"}]\n'
      ;;
    secret-advisory-only)
      printf '[{"name":"Secrets Detection (Gitleaks) / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://checks/advisory-secret","workflow":"CI Extended"}]\n'
      ;;
    secret-forged-workflow)
      printf '[{"name":"Secret Scan / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI Extended"}]\n'
      ;;
    secret-duplicate)
      printf '[{"name":"Secret Scan / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"},{"name":"Secret Scan / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://github.com/owner/repo/actions/runs/9002/job/8002","workflow":"CI"}]\n'
      ;;
    secret-pending)
      printf '[{"name":"Secret Scan / Gitleaks Scan","state":"PENDING","bucket":"pending","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"}]\n'
      checks_exit=8
      ;;
    secret-failed)
      printf '[{"name":"Secret Scan / Gitleaks Scan","state":"FAILURE","bucket":"fail","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"}]\n'
      checks_exit=1
      ;;
    check-state-drift)
      if [[ "$checks_count" -gt 1 ]]; then
        printf '[{"name":"Secret Scan / Gitleaks Scan","state":"FAILURE","bucket":"fail","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"}]\n'
        checks_exit=1
      else
        printf '[{"name":"Secret Scan / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"}]\n'
      fi
      ;;
    *)
      printf '[{"name":"Secret Scan / Gitleaks Scan","state":"SUCCESS","bucket":"pass","link":"https://github.com/owner/repo/actions/runs/9001/job/8001","workflow":"CI"},{"name":"Docs Governance","state":"SUCCESS","bucket":"pass","link":"https://checks/docs","workflow":"CI"}]\n'
      ;;
  esac
  exit "$checks_exit"
fi

if [[ "$command_name" == "api" ]]; then
  if [[ "${1-}" == "graphql" ]]; then
    query=""
    thread_id=""
    for argument in "$@"; do
      case "$argument" in
        query=*) query="${argument#query=}" ;;
        threadId=*) thread_id="${argument#threadId=}" ;;
      esac
    done

    if [[ "$query" == *'reviewThreads(first:100'* ]]; then
      snapshot_count_file="$MOCK_ROOT/feedback-snapshot-count"
      snapshot_count=0
      if [[ -f "$snapshot_count_file" ]]; then
        snapshot_count="$(<"$snapshot_count_file")"
      fi
      snapshot_count=$((snapshot_count + 1))
      printf '%s' "$snapshot_count" >"$snapshot_count_file"
      if [[ "$snapshot_count" -gt 1 ]]; then
        run_race_hook remove
      fi

      final_has_next=false
      thread_one_resolved=false
      if [[ "$MOCK_SCENARIO" == "incomplete-pagination" ]]; then
        final_has_next=true
      fi
      if [[ "$MOCK_SCENARIO" == "thread-resolution-drift" && "$snapshot_count" -gt 1 ]]; then
        thread_one_resolved=true
      fi
      jq -n \
        --argjson finalHasNext "$final_has_next" \
        --argjson threadOneResolved "$thread_one_resolved" '[
        {data:{repository:{pullRequest:{reviewThreads:{
          nodes:[{id:"THREAD_1",isResolved:$threadOneResolved,isOutdated:false,path:"one.cs",line:10,originalLine:10}],
          pageInfo:{hasNextPage:true,endCursor:"cursor-1"}
        }}}}},
        {data:{repository:{pullRequest:{reviewThreads:{
          nodes:[{id:"THREAD_2",isResolved:true,isOutdated:false,path:"two.cs",line:20,originalLine:20}],
          pageInfo:{hasNextPage:$finalHasNext,endCursor:null}
        }}}}}
      ]'
      exit 0
    fi

    if [[ "$thread_id" == "THREAD_1" ]]; then
      thread_one_resolved=false
      if [[ "$MOCK_SCENARIO" == "thread-resolution-drift" && \
            "$(<"$MOCK_ROOT/feedback-snapshot-count")" -gt 1 ]]; then
        thread_one_resolved=true
      fi
      jq -n --argjson threadOneResolved "$thread_one_resolved" '[
        {data:{node:{id:"THREAD_1",isResolved:$threadOneResolved,isOutdated:false,path:"one.cs",line:10,originalLine:10,comments:{nodes:[{author:{login:"reviewer"},body:"first page",createdAt:"2026-08-01T12:01:00Z",lastEditedAt:null,url:"https://comments/1",path:"one.cs",line:10,originalLine:10,diffHunk:"@@"}],pageInfo:{hasNextPage:true,endCursor:"comment-1"}}}}},
        {data:{node:{id:"THREAD_1",isResolved:$threadOneResolved,isOutdated:false,path:"one.cs",line:10,originalLine:10,comments:{nodes:[{author:{login:"author"},body:"second page",createdAt:"2026-08-01T12:02:00Z",lastEditedAt:null,url:"https://comments/2",path:"one.cs",line:10,originalLine:10,diffHunk:"@@"}],pageInfo:{hasNextPage:false,endCursor:null}}}}}
      ]'
      exit 0
    fi

    if [[ "$thread_id" == "THREAD_2" ]]; then
      cat <<'JSON'
[
  {"data":{"node":{"id":"THREAD_2","isResolved":true,"isOutdated":false,"path":"two.cs","line":20,"originalLine":20,"comments":{"nodes":[{"author":{"login":"reviewer"},"body":"resolved body","createdAt":"2026-08-01T12:03:00Z","lastEditedAt":null,"url":"https://comments/3","path":"two.cs","line":20,"originalLine":20,"diffHunk":"@@"}],"pageInfo":{"hasNextPage":false,"endCursor":null}}}}}
]
JSON
      exit 0
    fi
    exit 2
  fi

  endpoint=""
  for argument in "$@"; do
    if [[ "$argument" == repos/* ]]; then
      endpoint="$argument"
    fi
  done
  if [[ "$endpoint" == *'/issues/'*'/comments?'* ]]; then
    printf '[[{"id":1,"body":"top-level comment","html_url":"https://comments/top"}]]\n'
    exit 0
  fi
  if [[ "$endpoint" == *'/pulls/'*'/reviews?'* ]]; then
    printf '[[{"id":2,"state":"COMMENTED","body":"review summary","html_url":"https://reviews/2","commit_id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]]\n'
    exit 0
  fi
  if [[ "$endpoint" == "repos/owner/repo/actions/runs/9001" ]]; then
    workflow_path=.github/workflows/ci-required.yml
    if [[ "$MOCK_SCENARIO" == "secret-wrong-provenance" ]]; then
      workflow_path=.github/workflows/ci-extended.yml
    fi
    # The collector reads the scan definitions immediately after this call and takes its
    # closing clean-checkout snapshot well after it.
    run_race_hook install
    selected_pr="$(<"$MOCK_ROOT/selected-pr-number")"
    jq -n \
      --argjson number "$selected_pr" \
      --arg path "$workflow_path" \
      --arg head "$head_oid" \
      --arg base "$base_oid" \
      '{
        id: 9001,
        name: "CI",
        path: $path,
        event: "pull_request",
        status: "completed",
        conclusion: "success",
        head_sha: $head,
        head_branch: "issue-1547/atomic-evidence",
        pull_requests: [{
          number: $number,
          head: {sha:$head,ref:"issue-1547/atomic-evidence"},
          base: {sha:$base,ref:"main"}
        }]
      }'
    exit 0
  fi
fi

exit 2
MOCK_GH

  chmod +x "$case_root/bin/git" "$case_root/bin/gh"
  : >"$case_root/calls.log"
}

prepare_scan_definitions() {
  local scenario="$1"
  local case_root="$2"

  mkdir -p "$case_root/base/.github/workflows" "$case_root/head/.github/workflows"
  cat >"$case_root/base/.github/workflows/ci-required.yml" <<'CALLER'
name: CI
jobs:
  secret-scan:
    uses: ./.github/workflows/reusable-gitleaks.yml
CALLER
  cat >"$case_root/base/.github/workflows/reusable-gitleaks.yml" <<'REUSABLE'
name: Gitleaks Secrets Detection
jobs:
  gitleaks:
    steps:
      - run: gitleaks protect --config .gitleaks.toml
REUSABLE
  printf '%s\n' 'title = "Gitleaks"' >"$case_root/base/.gitleaks.toml"
  printf '%s\n' '# reviewed ignore list' >"$case_root/base/.gitleaksignore"
  cp -R "$case_root/base/." "$case_root/head/"

  case "$scenario" in
    secret-caller-noop)
      cat >"$case_root/head/.github/workflows/ci-required.yml" <<'CALLER_NOOP'
name: CI
jobs:
  secret-scan:
    runs-on: ubuntu-latest
    steps:
      - run: true
CALLER_NOOP
      ;;
    secret-reusable-noop)
      cat >"$case_root/head/.github/workflows/reusable-gitleaks.yml" <<'REUSABLE_NOOP'
name: Gitleaks Secrets Detection
on: workflow_call
jobs:
  gitleaks:
    runs-on: ubuntu-latest
    steps:
      - run: true
REUSABLE_NOOP
      ;;
    secret-config-noop)
      printf '%s\n' 'title = "Gitleaks disabled"' >"$case_root/head/.gitleaks.toml"
      ;;
    secret-ignore-noop)
      printf '%s\n' '*' >"$case_root/head/.gitleaksignore"
      ;;
  esac
}

run_start() {
  local scenario="$1"
  local case_root="$2"
  local state_file="$3"
  local token_tmp="$case_root/operator-session-token.tmp"
  local requested_pr="${4-}"
  local git_executable="${5:-$case_root/bin/git}"
  local -a start_args=()
  [[ -n "$git_executable" ]] || git_executable="$case_root/bin/git"
  [[ -n "$requested_pr" ]] && start_args=("$requested_pr")
  prepare_scan_definitions "$scenario" "$case_root"
  if MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario" MOCK_WORKTREE_ROOT="$case_root/checkout" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$git_executable" \
    TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$collector" start "${start_args[@]}" >"$token_tmp"; then
    mv -f "$token_tmp" "$case_root/operator-session-token"
  else
    rm -f "$token_tmp"
    return 1
  fi
}

run_finish() {
  local scenario="$1"
  local case_root="$2"
  local state_file="$3"
  local token_case_root="${4:-$case_root}"
  local git_executable="${5:-$case_root/bin/git}"
  local jq_executable="${6:-$real_jq}"
  local session_token
  session_token="$(tr -d '\r\n' <"$token_case_root/operator-session-token")"
  prepare_scan_definitions "$scenario" "$case_root"
  printf '%s\n' "$session_token" | env MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario" MOCK_WORKTREE_ROOT="$case_root/checkout" \
    REAL_JQ="$real_jq" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$git_executable" \
    TASKDECK_JQ_EXECUTABLE="$jq_executable" \
    "$collector" finish
}

run_abort() {
  local scenario="$1"
  local case_root="$2"
  local state_file="$3"
  local token_case_root="${4:-$case_root}"
  local session_token
  session_token="$(tr -d '\r\n' <"$token_case_root/operator-session-token")"
  prepare_scan_definitions "$scenario" "$case_root"
  printf '%s\n' "$session_token" | env MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario" MOCK_WORKTREE_ROOT="$case_root/checkout" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" \
    TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$collector" abort
}

# A jq wrapper that rewrites the persistent opening state at the exact moment the collector
# has finished authenticating it and is about to copy it, which is the only window in which
# the copied bytes can differ from the authenticated ones.
make_state_tampering_jq() {
  local case_root="$1"
  cat >"$case_root/bin/jq" <<'TAMPER_JQ'
#!/usr/bin/env bash
tamper=false
for argument in "$@"; do
  if [[ "$argument" == ".openingStateBinding" ]]; then
    tamper=true
  fi
done
"$REAL_JQ" "$@"
status=$?
if [[ "$tamper" == true && ! -f "$MOCK_ROOT/tampered" ]]; then
  state_path="$(printf '%s\n' "$MOCK_ROOT/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
  if [[ -f "$state_path" ]]; then
    "$REAL_JQ" '.selection = "forged-selection"' "$state_path" >"$state_path.tamper" &&
      mv -f "$state_path.tamper" "$state_path" &&
      : >"$MOCK_ROOT/tampered"
  fi
fi
exit "$status"
TAMPER_JQ
  chmod +x "$case_root/bin/jq"
}

# A real Git checkout whose PR head carries the requested change to an enforcing Gitleaks
# definition, so definition binding has something real to hide or reveal.
make_real_checkout() {
  local case_root="$1"
  local weakened_definition="$2"
  local repo="$case_root/checkout"
  local origin="$case_root/origin.git"

  "$real_git" init -q --bare -b main "$origin"
  "$real_git" init -q -b main "$repo"
  "$real_git" -C "$repo" config user.name "Evidence Canary"
  "$real_git" -C "$repo" config user.email "evidence-canary@example.invalid"
  "$real_git" -C "$repo" config commit.gpgsign false
  mkdir -p "$repo/.github/workflows"
  cat >"$repo/.github/workflows/ci-required.yml" <<'CALLER'
name: CI
jobs:
  secret-scan:
    uses: ./.github/workflows/reusable-gitleaks.yml
CALLER
  cat >"$repo/.github/workflows/reusable-gitleaks.yml" <<'REUSABLE'
name: Gitleaks Secrets Detection
jobs:
  gitleaks:
    steps:
      - run: gitleaks protect --config .gitleaks.toml
REUSABLE
  printf '%s\n' 'title = "Gitleaks"' >"$repo/.gitleaks.toml"
  printf '%s\n' '# reviewed ignore list' >"$repo/.gitleaksignore"
  "$real_git" -C "$repo" add -A
  "$real_git" -C "$repo" commit -q -m "opening base"
  "$real_git" -C "$repo" rev-parse HEAD >"$case_root/base-oid"
  "$real_git" -C "$repo" remote add origin "$origin"
  "$real_git" -C "$repo" push -q origin main
  if [[ "$weakened_definition" == true ]]; then
    printf '%s\n' 'title = "Gitleaks disabled"' >"$repo/.gitleaks.toml"
  else
    printf '%s\n' 'unrelated head change' >"$repo/README.md"
  fi
  "$real_git" -C "$repo" add -A
  "$real_git" -C "$repo" commit -q -m "pull request head"
  "$real_git" -C "$repo" rev-parse HEAD >"$case_root/head-oid"
  printf '%s\n' "$repo"
}

run_real() {
  local scenario="$1"
  local case_root="$2"
  local repo="$3"
  local phase="$4"
  local collector_path="${5:-$collector}"
  (
    cd "$repo" || exit 1
    export MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario"
    export MOCK_REAL_REPO="$repo" MOCK_REAL_GIT="$real_git"
    export TASKDECK_GH_EXECUTABLE="$case_root/bin/gh"
    export TASKDECK_GIT_EXECUTABLE="$real_git"
    export TASKDECK_JQ_EXECUTABLE="$real_jq"
    if [[ "$phase" == start ]]; then
      "$collector_path" start 42
    else
      "$collector_path" finish <"$case_root/operator-session-token"
    fi
  )
}

# A copy of the collector with one hardening removed, used to prove that a canary actually
# fails when the defect it targets is present.
make_defective_collector() {
  local case_root="$1"
  local name="$2"
  shift 2
  local variant_root="$case_root/defective-$name"
  local variant="$variant_root/scripts/github/collect-pre-merge-evidence.sh"
  local expression
  mkdir -p "$variant_root/scripts/github"
  cp "$collector" "$variant"
  for expression in "$@"; do
    sed -i "$expression" "$variant"
  done
  chmod +x "$variant"
  if cmp -s "$collector" "$variant"; then
    fail "defective collector variant $name is identical to the collector"
  fi
  bash -n "$variant" || fail "defective collector variant $name does not parse"
  printf '%s\n' "$variant"
}

case_root="$fixture_root/explicit"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
run_finish happy "$case_root" "$case_root/state.json" >"$case_root/packet.json"
"$real_jq" -e '
  .selection == "explicit" and .pr.number == 42 and
  .pr.opening.headRefOid == .pr.closing.headRefOid and
  (.feedback.issueComments | length) == 1 and
  (.feedback.reviewSummaries | length) == 1 and
  (.feedback.reviewThreads | length) == 2 and
  (.feedback.reviewThreads[0].comments | length) == 2 and
  .feedback.reviewThreads[0].isResolved == false and
  .feedback.reviewThreads[1].isResolved == true and
  .feedback.stableAcrossTwoSnapshots == true and
  .checks.stableAcrossTwoSnapshots == true and
  .secrets.requiredWorkflow == "CI" and
  .secrets.requiredWorkflowPath == ".github/workflows/ci-required.yml" and
  .secrets.provenanceVerified == true and
  .secrets.definitionsVerified == true and
  (.secrets.definitionBindings | length) == 4 and
  ([.secrets.definitionBindings[].matchesOpeningBase] | all) and
  .secrets.workflowRun.path == ".github/workflows/ci-required.yml" and
  .secrets.verdict == "CLEAN" and .collectorState == "COMPLETE"
' "$case_root/packet.json" >/dev/null || fail "explicit PR packet was incomplete"
if run_finish happy "$case_root" "$case_root/state.json" >/dev/null 2>&1; then
  fail "a completed evidence session was reused"
fi
pass "separate processes preserve the explicit PR and consume the checkout-bound evidence session"

case_root="$fixture_root/missing-state"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
rm -f "$case_root/checkout/.git/taskdeck-pre-merge-evidence/opening."*.json
if run_finish happy "$case_root" "$case_root/state.json" >/dev/null 2>&1; then
  fail "deleted opening evidence state was accepted"
fi
if [[ -s "$case_root/calls.log" ]] && rg -q 'gh (api|pr checks)' "$case_root/calls.log"; then
  fail "deleted opening evidence state reached feedback or checks"
fi
pass "deleted opening state fails closed before feedback and checks"

source_case="$fixture_root/substituted-state-source"
make_mocks "$source_case"
run_start happy "$source_case" "$source_case/state.json" 42 >/dev/null
case_root="$fixture_root/substituted-state-target"
make_mocks "$case_root"
mkdir -p "$case_root/checkout/.git/taskdeck-pre-merge-evidence"
cp "$source_case/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json \
  "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"
if run_finish happy "$case_root" "$case_root/state.json" "$source_case" >/dev/null 2>&1; then
  fail "opening evidence state from another checkout was accepted"
fi
if run_abort happy "$case_root" "$case_root/state.json" "$source_case" >/dev/null 2>&1; then
  fail "abort deleted opening evidence state from another checkout"
fi
if ! compgen -G "$case_root/checkout/.git/taskdeck-pre-merge-evidence/opening.*.json" >/dev/null; then
  fail "rejected abort did not preserve the substituted state for inspection"
fi
pass "substituted opening state cannot be finished or explicitly aborted from another checkout"

case_root="$fixture_root/substituted-pr-state"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
state_path="$(printf '%s\n' "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
"$real_jq" '.opening.number = 99' "$state_path" >"$state_path.tmp"
mv -f "$state_path.tmp" "$state_path"
if run_finish happy "$case_root" "$case_root/state.json" >/dev/null 2>&1; then
  fail "opening evidence state with a substituted PR number was accepted"
fi
pass "substituted PR state fails closed when it no longer matches its state path"

case_root="$fixture_root/mutable-opening-state"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
state_path="$(printf '%s\n' "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
"$real_jq" '
  .repository = "attacker/repository" |
  .opening.updatedAt = "2026-08-01T23:59:59Z" |
  .opening.baseRefName = "attacker-base" |
  .opening.baseRefOid = "cccccccccccccccccccccccccccccccccccccccc" |
  .opening.mergeable = "CONFLICTING"
' "$state_path" >"$state_path.tmp"
mv -f "$state_path.tmp" "$state_path"
if run_finish happy "$case_root" "$case_root/state.json" >/dev/null 2>&1; then
  fail "rewritten opening metadata was accepted with its original filename and path"
fi
run_abort happy "$case_root" "$case_root/state.json" >/dev/null ||
  fail "operator-carried token could not safely discard rewritten opening state"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
run_finish happy "$case_root" "$case_root/state.json" >"$case_root/restarted-packet.json"
"$real_jq" -e '.collectorState == "COMPLETE" and .pr.number == 42' \
  "$case_root/restarted-packet.json" >/dev/null ||
  fail "token-authenticated abort did not permit a clean restart"
pass "rewritten repository, timestamp, base, and mergeability fail closed with the original path"

case_root="$fixture_root/implicit"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" >/dev/null
run_finish happy "$case_root" "$case_root/state.json" >"$case_root/packet.json"
"$real_jq" -e '.selection == "current-branch" and .pr.number == 77' \
  "$case_root/packet.json" >/dev/null || fail "empty argument did not select the current branch PR"
pass "omitted argument selects only the current branch PR"

case_root="$fixture_root/wrong-checkout"
make_mocks "$case_root"
if run_start wrong-checkout "$case_root" "$case_root/state.json" 42 >/dev/null 2>&1; then
  fail "wrong checkout was accepted"
fi
if rg -q 'gh (api|pr checks)' "$case_root/calls.log"; then
  fail "wrong checkout reached feedback or check collection"
fi
pass "wrong checkout and explicit PR pairing fails before checks"

for scenario in oid-drift base-oid-drift feedback-drift mergeability-drift; do
  case_root="$fixture_root/$scenario"
  make_mocks "$case_root"
  run_start "$scenario" "$case_root" "$case_root/state.json" 42 >/dev/null
  if run_finish "$scenario" "$case_root" "$case_root/state.json" >"$case_root/packet.json" 2>/dev/null; then
    fail "$scenario was accepted"
  fi
done
pass "closing head, base, feedback, and mergeability drift invalidate the packet"

for scenario in index-inspection-failure replacement-ref-inspection-failure; do
  case_root="$fixture_root/$scenario"
  make_mocks "$case_root"
  run_start "$scenario" "$case_root" "$case_root/state.json" 42 >/dev/null
  if run_finish "$scenario" "$case_root" "$case_root/state.json" >"$case_root/packet.json" 2>/dev/null; then
    fail "$scenario was accepted"
  fi
done
pass "index and replacement-ref inspection failures fail closed"

case_root="$fixture_root/git-path-with-spaces"
make_mocks "$case_root"
spaced_git="$case_root/git executable/git"
mkdir -p "$(dirname "$spaced_git")"
cp "$case_root/bin/git" "$spaced_git"
chmod +x "$spaced_git"
run_start happy "$case_root" "$case_root/state.json" 42 "$spaced_git" >/dev/null
run_finish happy "$case_root" "$case_root/state.json" "$case_root" "$spaced_git" \
  >"$case_root/packet.json"
"$real_jq" -e '.collectorState == "COMPLETE"' "$case_root/packet.json" >/dev/null ||
  fail "Git executable path containing spaces did not complete evidence collection"
pass "Git executable paths containing spaces cover hidden-index and replacement-ref probes"

case_root="$fixture_root/expired-session-restart"
make_mocks "$case_root"
run_start oid-drift "$case_root" "$case_root/state.json" 42 >/dev/null
if run_finish oid-drift "$case_root" "$case_root/state.json" \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "expired-session fixture unexpectedly completed"
fi
if run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null 2>&1; then
  fail "an unfinished session was silently replaced"
fi
run_abort happy "$case_root" "$case_root/state.json" >/dev/null
if compgen -G "$case_root/checkout/.git/taskdeck-pre-merge-evidence/opening.*.json" >/dev/null; then
  fail "explicit abort retained the validated checkout-bound state"
fi
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
run_finish happy "$case_root" "$case_root/state.json" >"$case_root/restarted-packet.json"
"$real_jq" -e '.pr.number == 42 and .collectorState == "COMPLETE"' \
  "$case_root/restarted-packet.json" >/dev/null || fail "restarted session was incomplete"
pass "expired sessions require explicit validated abort and can then restart cleanly"

case_root="$fixture_root/thread-resolution-drift"
make_mocks "$case_root"
run_start thread-resolution-drift "$case_root" "$case_root/state.json" 42 >/dev/null
if run_finish thread-resolution-drift "$case_root" "$case_root/state.json" \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "review-thread resolution drift was accepted without parent PR metadata drift"
fi
pass "a second complete feedback snapshot rejects independent thread-resolution drift"

case_root="$fixture_root/check-state-drift"
make_mocks "$case_root"
run_start check-state-drift "$case_root" "$case_root/state.json" 42 >/dev/null
if run_finish check-state-drift "$case_root" "$case_root/state.json" \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "same-head check-state drift was accepted"
fi
pass "a second normalized checks snapshot rejects same-head CI drift"

case_root="$fixture_root/incomplete-pagination"
make_mocks "$case_root"
run_start incomplete-pagination "$case_root" "$case_root/state.json" 42 >/dev/null
if run_finish incomplete-pagination "$case_root" "$case_root/state.json" \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "incomplete review-thread pagination was accepted"
fi
pass "incomplete review-thread pagination fails closed"

for scenario in secret-missing secret-advisory-only secret-forged-workflow \
  secret-wrong-provenance secret-duplicate secret-pending secret-failed \
  secret-caller-noop secret-reusable-noop secret-config-noop secret-ignore-noop; do
  case_root="$fixture_root/$scenario"
  make_mocks "$case_root"
  run_start "$scenario" "$case_root" "$case_root/state.json" 42 >/dev/null
  if run_finish "$scenario" "$case_root" "$case_root/state.json" \
    >"$case_root/packet.json" 2>/dev/null; then
    fail "$scenario secret evidence was accepted"
  fi
  "$real_jq" -e \
    '.secrets.verdict == "NOT VERIFIED" and .collectorState == "INCOMPLETE"' \
    "$case_root/packet.json" >/dev/null || fail "$scenario emitted a false clean verdict"
done
"$real_jq" -e '
  .secrets.requiredCheckName == "Secret Scan / Gitleaks Scan" and
  (.secrets.evidence | length) == 0 and
  (.secrets.observedCandidates | length) == 1
' "$fixture_root/secret-advisory-only/packet.json" >/dev/null ||
  fail "advisory-only evidence was not distinguished from the enforcing check"
"$real_jq" -e '
  (.secrets.evidence | length) == 1 and
  .secrets.provenanceVerified == false and
  .secrets.workflowRun.path == ".github/workflows/ci-extended.yml"
' "$fixture_root/secret-wrong-provenance/packet.json" >/dev/null ||
  fail "wrong workflow-run provenance was not retained and rejected"
pass "forged, advisory, wrong-provenance, missing, duplicate, pending, and failed scans cannot emit CLEAN"
for scenario in secret-caller-noop secret-reusable-noop secret-config-noop secret-ignore-noop; do
  "$real_jq" -e '
    .secrets.provenanceVerified == true and
    .secrets.definitionsVerified == false and
    .secrets.verdict == "NOT VERIFIED" and
    .collectorState == "INCOMPLETE" and
    ([.secrets.definitionBindings[].matchesOpeningBase] | any(. == false))
  ' "$fixture_root/$scenario/packet.json" >/dev/null ||
    fail "$scenario did not retain the changed scan-definition evidence"
done
pass "same-name no-op caller, reusable workflow, and scan configuration cannot emit CLEAN"

if rg -q '^[[:space:]]*-[[:space:]]+\[[[:space:]]\][[:space:]]+Secrets scan:[[:space:]]+CLEAN[[:space:]]*$' \
  "$skill_file"; then
  fail "skill contains an unconditional CLEAN secrets verdict"
fi
rg -q 'Secrets scan: CLEAN/NOT VERIFIED' "$skill_file" ||
  fail "skill does not expose the evidence-backed secrets verdict states"
pass "skill report cannot unconditionally attest that the secrets scan is clean"

if rg -Fq '${ARGUMENTS' "$skill_file"; then
  fail "skill uses Bash parameter expansion instead of Claude's literal argument placeholder"
fi
rg -Fq '$ARGUMENTS' "$skill_file" || fail "skill omits Claude's literal argument placeholder"
pass "skill uses Claude's literal argument placeholder instead of Bash expansion"

if rg -Fq 'gh pr diff "$pr_number"' "$skill_file"; then
  fail "skill relies on a process-local PR variable that start cannot preserve"
fi
rg -Fq 'gh pr diff VALIDATED_PR_NUMBER' "$skill_file" ||
  fail "skill does not reuse the validated explicit PR for diff inspection"
rg -Fq '# gh pr diff' "$skill_file" ||
  fail "skill does not document current-branch diff inspection for omitted selection"
pass "diff inspection reuses the validated explicit or current-branch selection"

if rg -q 'evidence_session|\$\([^)]*collect-pre-merge-evidence\.sh start' "$skill_file"; then
  fail "skill captures the operator token in process-local shell state"
fi
rg -Fq 'collect-pre-merge-evidence.sh abort' "$skill_file" ||
  fail "skill does not require an explicit validated session token for abort"
rg -Fq 'collect-pre-merge-evidence.sh finish' "$skill_file" ||
  fail "skill does not require an explicit validated session token for finish"
rg -Fq 'coordinator/operator context' "$skill_file" ||
  fail "skill does not preserve the visible token across separate tool processes"
pass "visible operator token survives separate finish and abort tool commands"

# The immutable-snapshot and replacement-ref guards were presence checks here; both are now
# proven at runtime by the state-tampering, replacement-ref, and head-ref canaries below.
rg -Fq 'ls-files -v' "$collector" || fail "clean-check does not inspect hidden index flags"
rg -Fq 'session token must be supplied through stdin' "$collector" || fail "token is not protected stdin input"
rg -Fq 'finish "$session_token"' "$collector" &&
  fail "finish still accepts the session token through argv"
rg -Fq 'abort "$session_token"' "$collector" &&
  fail "abort still accepts the session token through argv"
pass "hidden-index inspection and the stdin-only session token are still declared"

case_root="$fixture_root/invalid-argument"
make_mocks "$case_root"
if run_start happy "$case_root" "$case_root/state.json" '42;echo-no' >/dev/null 2>&1; then
  fail "invalid PR argument was accepted"
fi
if [[ -s "$case_root/calls.log" ]]; then
  fail "invalid PR argument reached GitHub or Git"
fi
pass "non-numeric explicit PR arguments fail before external commands"

# The PATH-resolution half of this canary moved to the checkout-local PATH sanitization
# canary below: a checkout-local directory is now dropped from PATH before any tool is
# resolved, so a forged tool planted there is never a candidate and there is no rejection
# message to assert. What remains here is the explicit override, which names its program
# directly and is therefore still caught by the resolver's containment test.
case_root="$fixture_root/untrusted-path-tool"
make_mocks "$case_root"
planted_root="$case_root/planted-checkout"
mkdir -p "$planted_root/scripts/github" "$planted_root/.runtime-codex/bin"
cp "$collector" "$planted_root/scripts/github/collect-pre-merge-evidence.sh"
chmod +x "$planted_root/scripts/github/collect-pre-merge-evidence.sh"
cp "$case_root/bin/gh" "$planted_root/.runtime-codex/bin/gh"
chmod +x "$planted_root/.runtime-codex/bin/gh"
prepare_scan_definitions happy "$case_root"
: >"$case_root/calls.log"
if MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$planted_root/.runtime-codex/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$planted_root/scripts/github/collect-pre-merge-evidence.sh" start 42 \
  >"$case_root/planted-override.out" 2>"$case_root/planted-override.err"; then
  fail "a TASKDECK_GH_EXECUTABLE override inside the checkout was accepted"
fi
rg -Fq 'refusing an evidence tool resolved inside the collector checkout' \
  "$case_root/planted-override.err" ||
  fail "the checkout-local executable override was not rejected for the stated reason"
if rg -q '^gh ' "$case_root/calls.log"; then
  fail "the planted override gh executable was invoked"
fi
: >"$case_root/calls.log"
# The control removes both refusals that stand in this tool's way -- containment against the
# collector checkout and the runtime-directory name rule -- because the planted tool sits in a
# directory covered by each. With neither, the forged gh runs and answers for GitHub.
defective_collector="$(make_defective_collector "$case_root" trusted-tools \
  's@die "refusing an evidence tool resolved inside the collector checkout: $label ($resolved)"@:@' \
  's@^path_is_untrusted_tool_directory() {@path_is_untrusted_tool_directory() { return 1;@')"
mkdir -p "$(dirname "$defective_collector")/../../.runtime-codex/bin"
cp "$case_root/bin/gh" "$(dirname "$defective_collector")/../../.runtime-codex/bin/gh"
chmod +x "$(dirname "$defective_collector")/../../.runtime-codex/bin/gh"
MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$(dirname "$defective_collector")/../../.runtime-codex/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$defective_collector" start 42 >/dev/null 2>&1 || true
rg -q '^gh ' "$case_root/calls.log" ||
  fail "the trusted-tool canary passes even without the checkout-local rejection"
pass "checkout-local executable overrides cannot supply evidence tools"

# The planted directory is deliberately NOT named `.runtime-codex`: that name is refused
# outright, which would short-circuit the containment layer this canary exists to prove. Any
# PR-writable directory inside the measured checkout works as the vehicle.
case_root="$fixture_root/untrusted-worktree-tool"
make_mocks "$case_root"
mkdir -p "$case_root/checkout/tools/bin"
cp "$case_root/bin/gh" "$case_root/checkout/tools/bin/gh"
chmod +x "$case_root/checkout/tools/bin/gh"
prepare_scan_definitions happy "$case_root"
: >"$case_root/calls.log"
if MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$case_root/checkout/tools/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$collector" start 42 >/dev/null 2>"$case_root/measured.err"; then
  fail "an evidence tool inside the measured checkout was accepted"
fi
rg -Fq 'refusing an evidence tool resolved inside the measured checkout' "$case_root/measured.err" ||
  fail "the measured-checkout executable was not rejected for the stated reason"
if rg -q '^gh ' "$case_root/calls.log"; then
  fail "the evidence tool inside the measured checkout was invoked"
fi
# Watching gh alone left this canary blind to Git ordering. The measured checkout is only
# knowable by asking Git, so Git does run here -- but it must run exactly the three read-only
# identity queries and nothing else before the rejection. Any evidence-gathering Git command
# appearing in this log would mean the rejection had drifted later than the identity probe.
[[ "$(rg -c '^git ' "$case_root/calls.log" || true)" == "3" ]] ||
  fail "the measured-checkout rejection did not fire immediately after the Git identity probe"
[[ "$(rg -c '^git rev-parse --' "$case_root/calls.log" || true)" == "3" ]] ||
  fail "a Git command other than the checkout identity probe ran before the rejection"
pass "evidence tools inside the measured checkout are rejected after the Git identity probe and before any GitHub query"

case_root="$fixture_root/snapshot-tamper"
make_mocks "$case_root"
run_start snapshot-tamper "$case_root" "$case_root/state.json" 42 >/dev/null
make_state_tampering_jq "$case_root"
if run_finish snapshot-tamper "$case_root" "$case_root/state.json" \
  "$case_root" "$case_root/bin/git" "$case_root/bin/jq" \
  >"$case_root/packet.json" 2>"$case_root/finish.err"; then
  fail "an opening state rewritten between authentication and copy was accepted"
fi
[[ -f "$case_root/tampered" ]] ||
  fail "the state-tampering canary never rewrote the opening state"
rg -Fq 'forged-selection' \
  "$(printf '%s\n' "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)" ||
  fail "the state-tampering canary did not land its rewrite on the persistent state"
rg -Fq 'opening evidence state was rewritten after start' "$case_root/finish.err" ||
  fail "the copied opening state was not rejected for the stated reason"
tamper_case="$fixture_root/snapshot-tamper-control"
make_mocks "$tamper_case"
run_start snapshot-tamper "$tamper_case" "$tamper_case/state.json" 42 >/dev/null
make_state_tampering_jq "$tamper_case"
defective_collector="$(make_defective_collector "$tamper_case" snapshot-auth \
  's|"$session_token" false "$persistent_state_file" "$snapshot_state_document"|"$session_token" true "$persistent_state_file" "$snapshot_state_document"|')"
prepare_scan_definitions snapshot-tamper "$tamper_case"
printf '%s\n' "$(tr -d '\r\n' <"$tamper_case/operator-session-token")" |
  env MOCK_ROOT="$tamper_case" MOCK_SCENARIO=snapshot-tamper \
    MOCK_WORKTREE_ROOT="$tamper_case/checkout" REAL_JQ="$real_jq" \
    TASKDECK_GH_EXECUTABLE="$tamper_case/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$tamper_case/bin/git" \
    TASKDECK_JQ_EXECUTABLE="$tamper_case/bin/jq" \
    "$defective_collector" finish >"$tamper_case/packet.json" 2>/dev/null ||
  fail "the state-tampering canary passes even without authenticating the copy"
"$real_jq" -e '.selection == "forged-selection" and .collectorState == "COMPLETE"' \
  "$tamper_case/packet.json" >/dev/null ||
  fail "the state-tampering canary does not distinguish an unauthenticated copy"
pass "the copied opening-state snapshot is authenticated before any field is read"

case_root="$fixture_root/real-git-clean"
make_mocks "$case_root"
real_repo="$(make_real_checkout "$case_root" false)"
run_real happy "$case_root" "$real_repo" start >"$case_root/operator-session-token"
run_real happy "$case_root" "$real_repo" finish >"$case_root/packet.json"
"$real_jq" -e '
  .secrets.definitionsVerified == true and
  .secrets.verdict == "CLEAN" and
  .collectorState == "COMPLETE" and
  ([.secrets.definitionBindings[].matchesOpeningBase] | all)
' "$case_root/packet.json" >/dev/null ||
  fail "an unmodified real checkout did not produce a complete packet"
pass "a real Git checkout with unchanged scan definitions completes the evidence packet"

case_root="$fixture_root/replacement-ref-race"
make_mocks "$case_root"
real_repo="$(make_real_checkout "$case_root" true)"
run_real replacement-ref-race "$case_root" "$real_repo" start \
  >"$case_root/operator-session-token"
if run_real replacement-ref-race "$case_root" "$real_repo" finish \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "a transient replacement ref produced a complete packet"
fi
[[ -f "$case_root/race-probe" ]] ||
  fail "the replacement-ref canary never installed its transient ref"
[[ "$(awk '$1 == "redirected" {print $2}' "$case_root/race-probe")" != \
   "$(awk '$1 == "authentic" {print $2}' "$case_root/race-probe")" ]] ||
  fail "the replacement-ref canary installed a ref that redirected nothing"
"$real_jq" -e --arg authentic "$(awk '$1 == "authentic" {print $2}' "$case_root/race-probe")" '
  .secrets.definitionsVerified == false and
  .secrets.verdict == "NOT VERIFIED" and
  .collectorState == "INCOMPLETE" and
  (.secrets.definitionBindings[] | select(.path == ".gitleaks.toml") |
    .matchesOpeningBase == false and .localHeadBlob == $authentic)
' "$case_root/packet.json" >/dev/null ||
  fail "definition binding followed a transient replacement ref"
[[ -z "$(env -u GIT_NO_REPLACE_OBJECTS "$real_git" -C "$real_repo" \
  for-each-ref --format='%(refname)' refs/replace/)" ]] ||
  fail "the replacement-ref canary left its transient ref installed"
defective_collector="$(make_defective_collector "$case_root" replacement-refs \
  '/^export GIT_NO_REPLACE_OBJECTS=1$/d')"
rm -f "$case_root/race-probe" "$case_root/feedback-snapshot-count" \
  "$case_root/pr-view-count" "$case_root/checks-count"
rm -f "$real_repo/.git/taskdeck-pre-merge-evidence/"opening.*.json
run_real replacement-ref-race "$case_root" "$real_repo" start \
  >"$case_root/operator-session-token"
run_real replacement-ref-race "$case_root" "$real_repo" finish "$defective_collector" \
  >"$case_root/defective-packet.json" 2>/dev/null ||
  fail "the replacement-ref canary passes even without disabled replacement objects"
"$real_jq" -e '.secrets.verdict == "CLEAN" and .collectorState == "COMPLETE"' \
  "$case_root/defective-packet.json" >/dev/null ||
  fail "the replacement-ref canary does not distinguish enabled replacement objects"
pass "a transient replacement ref cannot make changed scan definitions bind as unchanged"

case_root="$fixture_root/head-ref-race"
make_mocks "$case_root"
real_repo="$(make_real_checkout "$case_root" true)"
run_real head-ref-race "$case_root" "$real_repo" start >"$case_root/operator-session-token"
if run_real head-ref-race "$case_root" "$real_repo" finish \
  >"$case_root/packet.json" 2>/dev/null; then
  fail "a transient branch-ref move produced a complete packet"
fi
[[ -f "$case_root/race-probe" ]] ||
  fail "the head-ref canary never moved the branch ref"
[[ "$(awk '$1 == "redirected" {print $2}' "$case_root/race-probe")" != \
   "$(awk '$1 == "authentic" {print $2}' "$case_root/race-probe")" ]] ||
  fail "the head-ref canary moved the branch ref without changing what HEAD resolves"
"$real_jq" -e --arg authentic "$(awk '$1 == "authentic" {print $2}' "$case_root/race-probe")" '
  .secrets.definitionsVerified == false and
  .secrets.verdict == "NOT VERIFIED" and
  .collectorState == "INCOMPLETE" and
  (.secrets.definitionBindings[] | select(.path == ".gitleaks.toml") |
    .matchesOpeningBase == false and .localHeadBlob == $authentic)
' "$case_root/packet.json" >/dev/null ||
  fail "definition binding followed the mutable HEAD ref"
defective_collector="$(make_defective_collector "$case_root" head-ref \
  's|rev-parse "$opening_head:$path"|rev-parse "HEAD:$path"|')"
rm -f "$case_root/race-probe" "$case_root/feedback-snapshot-count" \
  "$case_root/pr-view-count" "$case_root/checks-count"
rm -f "$real_repo/.git/taskdeck-pre-merge-evidence/"opening.*.json
run_real head-ref-race "$case_root" "$real_repo" start >"$case_root/operator-session-token"
run_real head-ref-race "$case_root" "$real_repo" finish "$defective_collector" \
  >"$case_root/defective-packet.json" 2>/dev/null ||
  fail "the head-ref canary passes even when definitions read the mutable HEAD ref"
"$real_jq" -e '.secrets.verdict == "CLEAN" and .collectorState == "COMPLETE"' \
  "$case_root/defective-packet.json" >/dev/null ||
  fail "the head-ref canary does not distinguish a mutable HEAD definition read"
pass "definition binding resolves from the authenticated head OID, not the mutable HEAD ref"

case_root="$fixture_root/persistent-replacement-ref"
make_mocks "$case_root"
real_repo="$(make_real_checkout "$case_root" false)"
env -u GIT_NO_REPLACE_OBJECTS "$real_git" -C "$real_repo" replace --force \
  "$(<"$case_root/head-oid")" "$(<"$case_root/base-oid")" >/dev/null
if run_real happy "$case_root" "$real_repo" start >/dev/null 2>"$case_root/start.err"; then
  fail "a checkout carrying a replacement ref opened an evidence session"
fi
rg -Fq 'rejects Git replacement refs' "$case_root/start.err" ||
  fail "a persistent replacement ref was not rejected for the stated reason"
pass "a checkout carrying a replacement ref cannot open an evidence session"

case_root="$fixture_root/token-file-channel"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
mkdir -p "$case_root/private"
(umask 077; tr -d '\r\n' <"$case_root/operator-session-token" >"$case_root/private/token")
printf '\n' >>"$case_root/private/token"
prepare_scan_definitions happy "$case_root"
env MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" \
  TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$collector" finish <"$case_root/private/token" >"$case_root/packet.json" ||
  fail "the documented token-file channel did not complete evidence collection"
"$real_jq" -e '.collectorState == "COMPLETE" and .pr.number == 42' \
  "$case_root/packet.json" >/dev/null ||
  fail "the documented token-file channel produced an incomplete packet"
if rg -Fq 'VALIDATED_SESSION_TOKEN' "$skill_file"; then
  fail "skill still substitutes the session token literal into command text"
fi
if rg -q 'printf[^|]*\|[^|]*collect-pre-merge-evidence\.sh (abort|finish)' "$skill_file"; then
  fail "skill still pipes the session token from parent command text into the collector"
fi
rg -Fq 'collect-pre-merge-evidence.sh abort <SESSION_TOKEN_FILE' "$skill_file" ||
  fail "skill does not document a redirected token file for abort"
rg -Fq 'collect-pre-merge-evidence.sh finish <SESSION_TOKEN_FILE' "$skill_file" ||
  fail "skill does not document a redirected token file for finish"
rg -Fq 'It does not hide the token from a process already running as the operator' "$skill_file" ||
  fail "skill does not state the limits of the token-file channel"
pass "the session token reaches abort and finish through a redirected file, never command text"

# A forged tool that records every invocation and, for the collector's digest pipeline, echoes
# back whatever the opening state already claims. Planting one of these is the whole of the
# awk bypass: `sha256_text` used to pipe sha256sum through a PATH-resolved awk, so a forged awk
# could return the recorded digest and the recorded binding in turn and authenticate an
# arbitrarily rewritten opening state.
make_forged_awk() {
  local destination="$1"
  local marker="$2"
  mkdir -p "$(dirname "$destination")"
  cat >"$destination" <<FORGED_AWK
#!/usr/bin/env bash
printf 'awk %q\n' "\${1-}" >>"$marker"
if [[ "\${1-}" == '{print \$1}' ]]; then
  IFS= read -r line
  first="\${line%% *}"
  if [[ "\$first" =~ ^[0-9a-f]{64}\$ ]]; then
    state="\$(printf '%s\n' "\$MOCK_ROOT/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
    counter="\$MOCK_ROOT/forged-awk-count"
    n=0
    [[ -f "\$counter" ]] && n="\$(<"\$counter")"
    n=\$((n + 1))
    printf '%s' "\$n" >"\$counter"
    if (( n % 2 == 1 )); then
      "$real_jq" -r '.sessionTokenDigest' "\$state"
    else
      "$real_jq" -r '.openingStateBinding' "\$state"
    fi
    exit 0
  fi
  printf '%s\n' "\$first"
  while IFS= read -r line; do printf '%s\n' "\${line%% *}"; done
  exit 0
fi
exec "$real_awk" "\$@"
FORGED_AWK
  chmod +x "$destination"
}

case_root="$fixture_root/path-sanitization"
make_mocks "$case_root"
planted_root="$case_root/planted-checkout"
mkdir -p "$planted_root/scripts/github" "$planted_root/.runtime-codex/bin"
cp "$collector" "$planted_root/scripts/github/collect-pre-merge-evidence.sh"
chmod +x "$planted_root/scripts/github/collect-pre-merge-evidence.sh"
make_forged_awk "$planted_root/.runtime-codex/bin/awk" "$case_root/forged-awk-calls"
prepare_scan_definitions happy "$case_root"
: >"$case_root/forged-awk-calls"
# `.codex/config.toml` really does prepend this directory to PATH, so the guarded run uses the
# same PATH shape a Codex session would. Every evidence tool that talks to GitHub is pinned to
# a trusted mock, so the only thing PATH decides here is where awk comes from.
if ! PATH="$planted_root/.runtime-codex/bin:$PATH" \
  MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$planted_root/scripts/github/collect-pre-merge-evidence.sh" start 42 \
  >"$case_root/operator-session-token" 2>"$case_root/sanitized.err"; then
  fail "checkout-local PATH sanitization broke an otherwise clean start: $(cat "$case_root/sanitized.err")"
fi
if [[ -s "$case_root/forged-awk-calls" ]]; then
  fail "the forged awk planted on PATH inside the collector checkout was invoked"
fi
sanitize_control="$(make_defective_collector "$case_root" path-sanitization \
  's@^sanitize_path_against_repository_roots$@:@')"
mkdir -p "$(dirname "$sanitize_control")/../../.runtime-codex/bin"
make_forged_awk "$(dirname "$sanitize_control")/../../.runtime-codex/bin/awk" \
  "$case_root/forged-awk-calls"
rm -f "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json
PATH="$(dirname "$sanitize_control")/../../.runtime-codex/bin:$PATH" \
  MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
  TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
  TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
  "$sanitize_control" start 42 >/dev/null 2>"$case_root/control.err" || true
rg -Fq 'refusing an evidence tool resolved inside the collector checkout' \
  "$case_root/control.err" ||
  fail "the PATH sanitization canary passes even without dropping checkout-local PATH entries"
pass "a checkout-local PATH entry is dropped before any evidence tool is resolved"

# The reviewed bypass, reproduced end to end: rewrite the persistent opening state after start,
# then plant a forged awk inside the MEASURED checkout so the digest pipeline authenticates the
# rewrite. awk is now a trusted evidence tool like gh and git, so the measured-checkout
# containment test rejects it before it can run. As in the measured-checkout canary above, the
# planted directory avoids the `.runtime-codex` name so the containment layer is what fires.
case_root="$fixture_root/forged-awk-measured"
make_mocks "$case_root"
run_start happy "$case_root" "$case_root/state.json" 42 >/dev/null
state_path="$(printf '%s\n' "$case_root/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
"$real_jq" '.selection = "forged-selection"' "$state_path" >"$state_path.tmp"
mv -f "$state_path.tmp" "$state_path"
make_forged_awk "$case_root/checkout/tools/bin/awk" "$case_root/forged-awk-calls"
: >"$case_root/forged-awk-calls"
prepare_scan_definitions happy "$case_root"
if printf '%s\n' "$(tr -d '\r\n' <"$case_root/operator-session-token")" |
  env PATH="$case_root/checkout/tools/bin:$PATH" \
    MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/checkout" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$collector" finish >"$case_root/packet.json" 2>"$case_root/forged-awk.err"; then
  fail "a forged awk inside the measured checkout produced a packet"
fi
rg -Fq 'refusing an evidence tool resolved inside the measured checkout: awk' \
  "$case_root/forged-awk.err" ||
  fail "the forged awk inside the measured checkout was not rejected for the stated reason"
if [[ -s "$case_root/forged-awk-calls" ]]; then
  fail "the forged awk inside the measured checkout was invoked"
fi
if [[ -s "$case_root/packet.json" ]]; then
  fail "a rejected forged awk still emitted packet content"
fi
# The control restores exactly the two lines this fix changed: awk absent from the trusted set,
# and sha256_text splitting the digest through a PATH-resolved awk. That is the collector as
# reviewed, and under it the forgery authenticates a rewritten state end to end.
awk_control_case="$fixture_root/forged-awk-control"
make_mocks "$awk_control_case"
run_start happy "$awk_control_case" "$awk_control_case/state.json" 42 >/dev/null
state_path="$(printf '%s\n' "$awk_control_case/checkout/.git/taskdeck-pre-merge-evidence/"opening.*.json)"
"$real_jq" '.selection = "forged-selection"' "$state_path" >"$state_path.tmp"
mv -f "$state_path.tmp" "$state_path"
make_forged_awk "$awk_control_case/checkout/tools/bin/awk" \
  "$awk_control_case/forged-awk-calls"
: >"$awk_control_case/forged-awk-calls"
awk_control="$(make_defective_collector "$awk_control_case" untrusted-awk \
  's@^register_trusted_executable awk .*@:@' \
  's@^awk_executable=.*@awk_executable=awk@' \
  's@^  digest="${digest_line%% \*}"@  digest="$(printf "%s" "$digest_line" | awk "{print \\$1}")"@')"
prepare_scan_definitions happy "$awk_control_case"
printf '%s\n' "$(tr -d '\r\n' <"$awk_control_case/operator-session-token")" |
  env PATH="$awk_control_case/checkout/tools/bin:$PATH" \
    MOCK_ROOT="$awk_control_case" MOCK_SCENARIO=happy \
    MOCK_WORKTREE_ROOT="$awk_control_case/checkout" \
    TASKDECK_GH_EXECUTABLE="$awk_control_case/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$awk_control_case/bin/git" TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$awk_control" finish >"$awk_control_case/packet.json" 2>/dev/null ||
  fail "the forged-awk canary passes even with awk outside the trusted set"
"$real_jq" -e '
  .selection == "forged-selection" and
  .secrets.verdict == "CLEAN" and
  .collectorState == "COMPLETE"
' "$awk_control_case/packet.json" >/dev/null ||
  fail "the forged-awk canary does not distinguish an untrusted hashing awk"
pass "a forged awk cannot authenticate a rewritten opening state"

# The reviewed ordering bypass: the collector runs from a LINKED WORKTREE while the writable
# PATH entry lives in the PRIMARY checkout that owns the shared Git directory. Git used to
# resolve the checkout roots that the rejection was then measured against, so a forged git in
# the primary tree executed first and named a decoy root that contained nothing. The primary
# checkout is now found by reading the worktree's `.git` file with bash, before any program
# runs. Note that the decoy root the mock reports never exists on disk, exactly as in the
# reported reproduction -- the rejection cannot depend on it.
case_root="$fixture_root/primary-checkout-git"
make_mocks "$case_root"
primary_root="$case_root/primary"
linked_worktree="$case_root/wt-issue-1547"
mkdir -p "$primary_root/tools/bin" "$primary_root/.runtime-codex/bin" \
  "$primary_root/.git/worktrees/wt-issue-1547" "$linked_worktree/scripts/github"
printf 'gitdir: %s\n' "$primary_root/.git/worktrees/wt-issue-1547" >"$linked_worktree/.git"
printf '%s\n' '../..' >"$primary_root/.git/worktrees/wt-issue-1547/commondir"
cp "$collector" "$linked_worktree/scripts/github/collect-pre-merge-evidence.sh"
chmod +x "$linked_worktree/scripts/github/collect-pre-merge-evidence.sh"
for forged_git_directory in "$primary_root/tools/bin" "$primary_root/.runtime-codex/bin"; do
  cp "$case_root/bin/git" "$forged_git_directory/git.exe"
  cp "$case_root/bin/git" "$forged_git_directory/git"
  chmod +x "$forged_git_directory/git.exe" "$forged_git_directory/git"
done
prepare_scan_definitions happy "$case_root"
# Without this the canary would silently stop being an attack: the forged git has to be what
# an unsanitized PATH would actually select.
[[ "$(PATH="$primary_root/tools/bin:$PATH" command -v git.exe)" == \
   "$primary_root/tools/bin/git.exe" ]] ||
  fail "the primary-checkout git canary does not shadow the real Git executable"
# Both runs execute from inside the linked worktree, the way an operator would, so the working
# directory is part of the fixture rather than whatever directory the suite happened to start
# in. That also keeps a real Git away from any real checkout.
run_primary_git_case() {
  local collector_path="$1"
  local worktree_path="$2"
  local forged_directory="$3"
  (
    cd "$worktree_path" || exit 1
    PATH="$forged_directory:$PATH" \
      MOCK_ROOT="$case_root" MOCK_SCENARIO=happy MOCK_WORKTREE_ROOT="$case_root/decoy" \
      TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" TASKDECK_JQ_EXECUTABLE="$real_jq" \
      "$collector_path" start 42
  )
}
# Sub-case A isolates the discovery layer: the forged Git sits in an ordinary directory of the
# primary checkout, so only the `.git`-file linkage can identify it as untrusted. Sub-case B
# below covers the `.runtime-codex` placement, which the name rule catches even unlinked.
: >"$case_root/calls.log"
if run_primary_git_case "$linked_worktree/scripts/github/collect-pre-merge-evidence.sh" \
  "$linked_worktree" "$primary_root/tools/bin" \
  >"$case_root/primary-git.out" 2>"$case_root/primary-git.err"; then
  fail "a forged git in the primary checkout opened an evidence session"
fi
if rg -q '^git ' "$case_root/calls.log"; then
  fail "the forged git in the primary checkout was invoked before the rejection"
fi
if rg -q '^gh ' "$case_root/calls.log"; then
  fail "a forged primary-checkout git reached GitHub"
fi
git_control="$(make_defective_collector "$case_root" primary-checkout-git \
  's@^sanitize_path_against_repository_roots$@:@' \
  's@^discover_repository_roots .*@:@')"
control_worktree="$case_root/defective-primary-checkout-git"
printf 'gitdir: %s\n' "$primary_root/.git/worktrees/wt-issue-1547" >"$control_worktree/.git"
: >"$case_root/calls.log"
run_primary_git_case "$git_control" "$control_worktree" "$primary_root/tools/bin" \
  >"$case_root/primary-git-control.out" 2>"$case_root/primary-git-control.err" ||
  fail "the primary-checkout git canary passes even without bash-only root discovery"
rg -q '^git rev-parse --show-toplevel' "$case_root/calls.log" ||
  fail "the primary-checkout git control never reached the forged Git executable"
rg -q '^[0-9a-f]{64}$' "$case_root/primary-git-control.out" ||
  fail "the primary-checkout git canary does not distinguish a Git-discovered checkout root"
# Same forgery, but in a sibling tree carrying no `.git` marker at all, so no root discovery
# can tie it to the collector's checkout. Containment cannot reach it; the `.runtime-codex`
# name rule is what refuses it. Removing the linkage is the difference between the two halves
# of this canary, which is why the name rule is defence in depth rather than the main control.
rm -f "$linked_worktree/.git"
: >"$case_root/calls.log"
if run_primary_git_case "$linked_worktree/scripts/github/collect-pre-merge-evidence.sh" \
  "$linked_worktree" "$primary_root/.runtime-codex/bin" \
  >/dev/null 2>"$case_root/unlinked-git.err"; then
  fail "a forged git in an unlinked runtime directory opened an evidence session"
fi
rg -Fq 'refusing an evidence tool resolved inside a PR-writable runtime directory' \
  "$case_root/unlinked-git.err" ||
  fail "the unlinked runtime directory was not refused for the stated reason"
if rg -q '^git ' "$case_root/calls.log"; then
  fail "the forged git in an unlinked runtime directory was invoked"
fi
name_control="$(make_defective_collector "$case_root" runtime-directory-name \
  's@^path_is_untrusted_tool_directory() {@path_is_untrusted_tool_directory() { return 1;@' \
  's@^sanitize_path_against_repository_roots$@:@' \
  's@^discover_repository_roots .*@:@')"
: >"$case_root/calls.log"
run_primary_git_case "$name_control" "$case_root/defective-runtime-directory-name" \
  "$primary_root/.runtime-codex/bin" >"$case_root/unlinked-control.out" 2>/dev/null || true
rg -q '^git rev-parse --show-toplevel' "$case_root/calls.log" ||
  fail "the unlinked runtime directory canary passes even without the runtime-directory rule"
pass "a forged git in the primary checkout never executes, so it cannot supply the roots its own rejection is measured against"

printf 'PASS: %d pre-merge evidence canaries passed.\n' "$passed"
