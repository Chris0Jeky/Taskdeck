#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
collector="$script_dir/collect-pre-merge-evidence.sh"
skill_file="$script_dir/../../.claude/skills/pre-merge-gate/SKILL.md"
real_jq="$(command -v jq)"
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
      *:*)
        revision=base
        if [[ "${2%%:*}" == "HEAD" ]]; then
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

  head_oid=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
  base_oid=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
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
    selected_pr="$(<"$MOCK_ROOT/selected-pr-number")"
    jq -n \
      --argjson number "$selected_pr" \
      --arg path "$workflow_path" \
      '{
        id: 9001,
        name: "CI",
        path: $path,
        event: "pull_request",
        status: "completed",
        conclusion: "success",
        head_sha: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        head_branch: "issue-1547/atomic-evidence",
        pull_requests: [{
          number: $number,
          head: {sha:"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",ref:"issue-1547/atomic-evidence"},
          base: {sha:"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",ref:"main"}
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
  shift 3
  prepare_scan_definitions "$scenario" "$case_root"
  MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario" MOCK_WORKTREE_ROOT="$case_root/checkout" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" \
    TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$collector" start "$@"
}

run_finish() {
  local scenario="$1"
  local case_root="$2"
  local state_file="$3"
  prepare_scan_definitions "$scenario" "$case_root"
  MOCK_ROOT="$case_root" MOCK_SCENARIO="$scenario" MOCK_WORKTREE_ROOT="$case_root/checkout" \
    TASKDECK_GH_EXECUTABLE="$case_root/bin/gh" \
    TASKDECK_GIT_EXECUTABLE="$case_root/bin/git" \
    TASKDECK_JQ_EXECUTABLE="$real_jq" \
    "$collector" finish
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
if run_finish happy "$case_root" "$case_root/state.json" >/dev/null 2>&1; then
  fail "opening evidence state from another checkout was accepted"
fi
pass "substituted opening state fails closed when its checkout binding differs"

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

case_root="$fixture_root/invalid-argument"
make_mocks "$case_root"
if run_start happy "$case_root" "$case_root/state.json" '42;echo-no' >/dev/null 2>&1; then
  fail "invalid PR argument was accepted"
fi
if [[ -s "$case_root/calls.log" ]]; then
  fail "invalid PR argument reached GitHub or Git"
fi
pass "non-numeric explicit PR arguments fail before external commands"

printf 'PASS: %d pre-merge evidence canaries passed.\n' "$passed"
