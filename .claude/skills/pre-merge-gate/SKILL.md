---
name: pre-merge-gate
description: "Collect Taskdeck-local readiness evidence for review-and-ship: exact-head identity, seam tests, diff inspection, comments, CI."
argument-hint: "[PR number]"
user-invocable: true
disable-model-invocation: true
---

# Pre-Merge Gate

Collect the Taskdeck-local validation packet that the global `review-and-ship` pipeline consumes.
Execute the local checks as one atomic operation; this skill does not decide review or merge policy.

## Arguments

`$ARGUMENTS` is a PR number or empty (current branch's PR).

## Step 1: Prove the exact PR head and base

```bash
set -euo pipefail

pr_args=()
if [[ -n "${ARGUMENTS:-}" ]]; then
  pr_args=("$ARGUMENTS")
fi

pr_fields="$(gh pr view "${pr_args[@]}" \
  --json number,headRefName,headRefOid,baseRefName,baseRefOid,mergeable \
  --jq '[.number,.headRefName,.headRefOid,.baseRefName,.baseRefOid,.mergeable] | @tsv')"
IFS=$'\t' read -r pr_number pr_head_ref pr_head_oid pr_base_ref pr_base_oid pr_mergeable \
  <<<"$pr_fields"

local_head_oid="$(git rev-parse HEAD)"
if [[ "$local_head_oid" != "$pr_head_oid" ]]; then
  echo "BLOCKED: local HEAD $local_head_oid is not PR head $pr_head_oid" >&2
  exit 1
fi
if [[ -n "$(git status --porcelain=v1)" ]]; then
  echo "BLOCKED: exact-head evidence requires a clean worktree" >&2
  exit 1
fi

git fetch --no-tags origin "$pr_base_ref"
fetched_base_oid="$(git rev-parse FETCH_HEAD)"
if [[ "$fetched_base_oid" != "$pr_base_oid" ]]; then
  echo "BLOCKED: fetched base $fetched_base_oid is not PR base $pr_base_oid" >&2
  exit 1
fi

merge_base_oid="$(git merge-base HEAD FETCH_HEAD)"
if [[ "$merge_base_oid" != "$pr_base_oid" ]]; then
  echo "BLOCKED: PR head does not incorporate exact base $pr_base_oid (merge base: $merge_base_oid)" >&2
  exit 1
fi
```

Any lookup, fetch, or identity mismatch stops the gate before tests. Run this skill only from the
PR's exact-head worktree. If `pr_mergeable` is `CONFLICTING`, stop and report; do not auto-resolve.

## Step 2: Check bot comments

Read ALL comments on the PR:

```bash
gh api repos/{owner}/{repo}/pulls/{number}/comments
gh api repos/{owner}/{repo}/issues/{number}/comments
gh pr view $ARGUMENTS --comments
```

Look for unaddressed findings from any source: human review comments, Dependabot, CodeQL / security
scanning, CI bot failures, previous adversarial review comments. Return the comment bodies and
resolution state to the global pipeline for triage. If that pipeline directs a fix, rerun the checks
affected by the fix before returning the updated packet.

## Step 3: Run the seam's local checks

Run the proving checks from the root `CLAUDE.md` table for every seam the diff touches — not the full
backend solution plus the full frontend suite, which takes minutes and duplicates `ci-required`:

```bash
git diff --name-only "$pr_base_oid"..HEAD
```

- `backend/src/<Layer>/**` or `backend/tests/<Layer>.Tests/**` → `dotnet test backend/tests/Taskdeck.<Layer>.Tests/Taskdeck.<Layer>.Tests.csproj -c Release -m:1`;
  cross-layer changes → `dotnet build backend/Taskdeck.sln -c Release` then the full `dotnet test backend/Taskdeck.sln -c Release -m:1`.
- `frontend/taskdeck-web/**` → `cd frontend/taskdeck-web && npm run typecheck && npm run build && npx vitest --run --maxWorkers=2 <touched specs>`
  (bare `vitest --run` OOMs on this box; broaden to `npx vitest --run --maxWorkers=2` only for shared
  store/router/api changes).
- `docs/**`, root `*.md` → `node scripts/check-docs-governance.mjs`; `docs/GOLDEN_PRINCIPLES.md` → `node scripts/check-golden-principles.mjs`.
- `ci/**`, `scripts/ci/smart-ci/**` → `node --test scripts/ci/smart-ci/*.test.mjs`.
- `scripts/agent_hooks/**` → `py -3 -B -m unittest discover -s scripts/agent_hooks -p "test_render_failure_ledger.py"`.
- `scripts/agentic/**`, `scripts/git/**` → `powershell -File scripts/agentic/Test-Assert-TaskdeckCheckoutFingerprint.ps1`,
  `powershell -File scripts/git/Test-New-CodexIssueWorktree.ps1`.

Report any failure immediately and do not mark the local evidence packet complete.

## Step 4: Taskdeck diff inspection

Read the full diff (`git diff "$pr_base_oid"..HEAD`, not `gh pr diff` — API budget) and check for:

- secrets (.env, tokens, keys, connection strings); debug leftovers (console.log, Console.WriteLine,
  breakpoints); TODOs without issue references; hardcoded values that violate conventions
- missing tests for behavior changes; weak assertions
- clean-architecture violations (Domain referencing Infrastructure)
- agent safety violations (GP-06: no approve_proposal or direct board mutation)
- HTTP semantics (wrong status codes); unused `using` statements or dead code

Return issues as Taskdeck-lens findings to the global pipeline. If it directs a fix, commit and push
the fix, then rerun the affected local checks before returning the updated packet.

## Step 5: CI status

```bash
gh pr checks $ARGUMENTS
```

Report the exact-head state of every check. A red required check makes the local packet incomplete;
route diagnosis and recovery through `taskdeck-ci-conflict-recovery`.

## Step 6: Report

```
## Taskdeck Evidence: PR #XXX

- [ ] Local HEAD equals remote PR head OID: PASS/FAIL
- [ ] Exact-head worktree is clean: PASS/FAIL
- [ ] Fetched base equals remote PR base OID: PASS/FAIL
- [ ] Merge base equals current remote base OID: PASS/FAIL
- [ ] Seam checks run (list each command + result): PASS/FAIL
- [ ] CI checks: GREEN/RED
- [ ] Diff inspection: CLEAN/FINDINGS RETURNED
- [ ] PR feedback surfaces: CAPTURED
- [ ] Secrets scan: CLEAN

**Evidence state**: COMPLETE / INCOMPLETE (reason)
**Canonical pipeline state**: <state returned by `review-and-ship`, or NOT YET RUN>
```

## Rules

- This skill only collects local evidence; it never decides review or merge disposition.
- Finding severity, comment triage, reviewer invocation, convergence, and merge disposition belong
  only to the global `review-and-ship` skill and global laws 2 and 11.
