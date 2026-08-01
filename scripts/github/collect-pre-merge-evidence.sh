#!/usr/bin/env bash
set -euo pipefail

die() {
  printf 'BLOCKED: %s\n' "$*" >&2
  exit 1
}

require_executable() {
  local executable="$1"
  if [[ "$executable" == */* ]]; then
    [[ -x "$executable" ]] || die "required executable is unavailable: $executable"
  else
    command -v "$executable" >/dev/null 2>&1 || die "required executable is unavailable: $executable"
  fi
}

gh_executable="${TASKDECK_GH_EXECUTABLE:-gh}"
jq_executable="${TASKDECK_JQ_EXECUTABLE:-jq}"
cmp_executable="${TASKDECK_CMP_EXECUTABLE:-cmp}"
if [[ -n "${TASKDECK_GIT_EXECUTABLE:-}" ]]; then
  git_executable="$TASKDECK_GIT_EXECUTABLE"
elif command -v git.exe >/dev/null 2>&1; then
  git_executable="$(command -v git.exe)"
else
  git_executable="git"
fi

require_executable "$gh_executable"
require_executable "$git_executable"
require_executable "$jq_executable"
require_executable "$cmp_executable"

usage() {
  cat >&2 <<'EOF'
Usage:
  collect-pre-merge-evidence.sh start STATE_FILE [PR_NUMBER]
  collect-pre-merge-evidence.sh finish STATE_FILE

The start phase binds the current clean checkout to an explicit PR number or,
when PR_NUMBER is omitted/empty, only to the current branch's pull request.
Run the change-specific checks between phases. The finish phase captures every
feedback surface and current check, then fails if the closing PR identity differs.
EOF
  exit 2
}

[[ $# -ge 2 ]] || usage
mode="$1"
state_file="$2"

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

  local_head="$("$git_executable" rev-parse HEAD)" || die "cannot resolve local HEAD"
  [[ "$local_head" == "$expected_head" ]] ||
    die "local HEAD $local_head is not PR head $expected_head"

  worktree_status="$("$git_executable" status --porcelain=v1 --untracked-files=all)" ||
    die "cannot inspect worktree status"
  [[ -z "$worktree_status" ]] || die "exact-head evidence requires a clean worktree"
}

start_collection() {
  [[ $# -le 3 ]] || usage
  local requested_pr="${3-}"
  local repo_full_name
  local selected_pr
  local selected_head
  local selected_base
  local fetched_base
  local merge_base
  local state_tmp
  local -a pr_args=()
  local snapshot_tmp

  if [[ -n "$requested_pr" ]]; then
    [[ "$requested_pr" =~ ^[1-9][0-9]*$ ]] || die "PR number must be a positive integer"
    pr_args=("$requested_pr")
  fi

  repo_full_name="$("$gh_executable" repo view --json nameWithOwner --jq .nameWithOwner)" ||
    die "cannot identify the current GitHub repository"
  [[ "$repo_full_name" =~ ^[^/]+/[^/]+$ ]] || die "repository identity is invalid"

  snapshot_tmp="$(mktemp)"
  trap 'rm -f "${snapshot_tmp:-}" "${state_tmp:-}"' EXIT
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

  "$git_executable" fetch --no-tags origin "$("$jq_executable" -r '.baseRefName' "$snapshot_tmp")" ||
    die "cannot fetch the selected PR base"
  fetched_base="$("$git_executable" rev-parse FETCH_HEAD)" || die "cannot resolve fetched base"
  [[ "$fetched_base" == "$selected_base" ]] ||
    die "fetched base $fetched_base is not PR base $selected_base"
  merge_base="$("$git_executable" merge-base HEAD FETCH_HEAD)" || die "cannot resolve merge base"
  [[ "$merge_base" == "$selected_base" ]] ||
    die "PR head does not incorporate exact base $selected_base (merge base: $merge_base)"

  state_tmp="$(mktemp "${state_file}.tmp.XXXXXX")"
  "$jq_executable" -n \
    --arg schemaVersion "1" \
    --arg repository "$repo_full_name" \
    --arg selection "$(if [[ -n "$requested_pr" ]]; then printf explicit; else printf current-branch; fi)" \
    --arg localHeadOid "$selected_head" \
    --slurpfile opening "$snapshot_tmp" \
    '{
      schemaVersion: ($schemaVersion | tonumber),
      repository: $repository,
      selection: $selection,
      localHeadOid: $localHeadOid,
      opening: $opening[0]
    }' >"$state_tmp"
  mv -f "$state_tmp" "$state_file"
  state_tmp=""
  trap - EXIT
  rm -f "$snapshot_tmp"
  printf 'Opening evidence bound to PR #%s at %s against %s.\n' \
    "$selected_pr" "$selected_head" "$selected_base" >&2
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

  mkdir -p "$snapshot_dir"

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
  done < <("$jq_executable" -r '.[].id' "$snapshot_dir/thread-index.json" | tr -d '\r')
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

  mkdir -p "$snapshot_dir"
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

finish_collection() {
  [[ $# -eq 2 ]] || usage
  [[ -f "$state_file" ]] || die "opening evidence state is missing: $state_file"
  "$jq_executable" -e '
    .schemaVersion == 1 and
    (.repository | type == "string") and
    (.localHeadOid | type == "string") and
    (.opening | type == "object")
  ' "$state_file" >/dev/null || die "opening evidence state is invalid"

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
  local secrets_verdict="NOT VERIFIED"

  repo_full_name="$("$jq_executable" -r '.repository' "$state_file")"
  [[ "$repo_full_name" =~ ^[^/]+/[^/]+$ ]] || die "repository identity is invalid"
  repo_owner="${repo_full_name%%/*}"
  repo_name="${repo_full_name#*/}"
  pr_number="$("$jq_executable" -r '.opening.number' "$state_file")"
  opening_head="$("$jq_executable" -r '.opening.headRefOid' "$state_file")"
  opening_head_ref="$("$jq_executable" -r '.opening.headRefName' "$state_file")"
  opening_base="$("$jq_executable" -r '.opening.baseRefOid' "$state_file")"
  opening_base_ref="$("$jq_executable" -r '.opening.baseRefName' "$state_file")"
  assert_clean_exact_checkout "$opening_head"

  temp_dir="$(mktemp -d)"
  trap 'rm -rf "${temp_dir:-}"' EXIT

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
  if [[ "$secret_provenance_verified" == true ]]; then
    secrets_verdict="CLEAN"
  fi

  "$gh_executable" pr view "$pr_number" \
    --json number,headRefName,headRefOid,baseRefName,baseRefOid,mergeable,updatedAt,url \
    >"$temp_dir/closing.json" || die "cannot resolve the closing PR identity"
  validate_pr_snapshot "$temp_dir/closing.json"
  "$jq_executable" '.opening' "$state_file" >"$temp_dir/opening.json"
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
    --slurpfile state "$state_file" \
    --slurpfile closing "$temp_dir/closing.json" \
    --slurpfile feedback "$temp_dir/feedback-second/snapshot.json" \
    --slurpfile checks "$temp_dir/checks-second/snapshot.json" \
    --slurpfile secretChecks "$temp_dir/secret-checks.json" \
    --slurpfile secretCandidates "$temp_dir/secret-candidates.json" \
    --slurpfile secretWorkflowRun "$temp_dir/secret-workflow-run.json" \
    --arg requiredSecretCheckName "$required_secret_check_name" \
    --arg requiredSecretWorkflow "$required_secret_workflow" \
    --arg requiredSecretWorkflowPath "$required_secret_workflow_path" \
    --argjson secretProvenanceVerified "$secret_provenance_verified" \
    --arg secretsVerdict "$secrets_verdict" \
    '{
      schemaVersion: 1,
      repository: $state[0].repository,
      selection: $state[0].selection,
      pr: {
        number: $state[0].opening.number,
        url: $state[0].opening.url,
        opening: $state[0].opening,
        closing: $closing[0],
        localHeadOid: $state[0].localHeadOid
      },
      feedback: ($feedback[0] + {stableAcrossTwoSnapshots: true}),
      checks: ($checks[0] + {stableAcrossTwoSnapshots: true}),
      secrets: {
        requiredCheckName: $requiredSecretCheckName,
        requiredWorkflow: $requiredSecretWorkflow,
        requiredWorkflowPath: $requiredSecretWorkflowPath,
        verdict: $secretsVerdict,
        provenanceVerified: $secretProvenanceVerified,
        workflowRun: $secretWorkflowRun[0],
        evidence: $secretChecks[0],
        observedCandidates: $secretCandidates[0]
      },
      collectorState: (if $secretsVerdict == "CLEAN" then "COMPLETE" else "INCOMPLETE" end)
    }'

  [[ "$secrets_verdict" == "CLEAN" ]] ||
    die "exact-head secret-scan evidence is missing, pending, failed, or ambiguous"
}

case "$mode" in
  start)
    start_collection "$@"
    ;;
  finish)
    finish_collection "$@"
    ;;
  *)
    usage
    ;;
esac
