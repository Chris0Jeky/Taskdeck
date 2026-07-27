---
name: pre-merge-gate
description: "Final validation gate before merging a PR: tests, lint, type-check, build, review evidence, and CI green check. Use before merging."
user-invocable: true
---

# Pre-Merge Gate

Run all validation checks before a PR can be merged. Execute as one atomic operation.

## Arguments

`$ARGUMENTS` is a PR number or empty (current branch's PR).

## Step 1: Identify PR and branch

```bash
gh pr view $ARGUMENTS --json number,headRefName,baseRefName,headRefOid,baseRefOid,mergeable,statusCheckRollup
```

If mergeable is "CONFLICTING", stop and report — do not auto-resolve.

## Step 2: Check bot comments

Read ALL comments on the PR:
```bash
gh api --paginate repos/{owner}/{repo}/pulls/{number}/comments
gh api --paginate repos/{owner}/{repo}/pulls/{number}/reviews
gh api --paginate repos/{owner}/{repo}/issues/{number}/comments
gh pr view $ARGUMENTS --comments
```

Also query the PR's GraphQL `reviewThreads` connection (or the equivalent GitHub MCP review-thread
read) through its final cursor because REST comment lists do not expose `isResolved` and a partial
connection is not evidence that all threads are settled.

Check for unaddressed findings from any source:
- Human review comments not yet resolved
- Dependabot alerts or suggestions
- CodeQL / security scanning findings
- CI bot failure messages
- Previous adversarial review comments not yet resolved

If any comment lacks a recorded disposition from the global `review-and-ship` pipeline, report the
PR as blocked and stop. This gate does not create a separate finding-disposition or fix loop.

## Step 3: Run local checks

Run ALL of these:

```bash
dotnet build backend/Taskdeck.sln -c Release
dotnet test backend/Taskdeck.sln -c Release -m:1
cd frontend/taskdeck-web && npm run build && npx vitest --run --reporter=verbose
```

Report any failures immediately — do not proceed to merge.

## Step 4: Confirm exact-head/base declared-review evidence

Read the repository's current authority and the canonical pipeline result instead of copying its
tier row here. When that declared gate requires independent review, use the `headRefOid` and
`baseRefOid` from Step 1 and the reviews, review summaries, and comments already read in Step 2.
Require an **arrived independent review of that exact head and base** which posts either findings or
an explicit no-finding result. The evidence artifact must record both reviewed OIDs (or a stable
diff identity derived from both), and the gate must compare them with the current values. GitHub's
review `commit_id` binds only the head; it is not proof of the reviewed base. A review request,
acknowledgement/reaction, worker-authored review, or coordinator metadata scan does not satisfy an
independent-review requirement. Evidence from an older head or an older base is stale.

Confirm that every finding from that review has a recorded disposition and that all review threads
are settled. If exact-head independent-review evidence is missing or unsettled, report the PR as
blocked and return it to the global `review-and-ship` pipeline. This gate confirms the completed
review; it does not perform another review lens or start a separate review/fix cycle.

## Step 5: CI status

```bash
gh pr checks $ARGUMENTS
```

All checks must be terminal and green. If any are pending or red, report the PR as not ready. Route
red-check diagnosis and any canonical-pipeline-selected fix batch through
`taskdeck-ci-conflict-recovery`; this gate does not choose or execute fixes.

## Step 6: Report

Output a merge-readiness summary:

```
## Merge Readiness: PR #XXX

- [ ] Backend build: PASS/FAIL
- [ ] Backend tests: PASS/FAIL (N passed, M failed)
- [ ] Frontend build: PASS/FAIL
- [ ] Frontend tests: PASS/FAIL
- [ ] CI checks: GREEN/RED
- [ ] Declared exact-head/base review evidence: SATISFIED/MISSING (head SHA, base SHA, and evidence URL)
- [ ] Bot comments: ADDRESSED/NONE
- [ ] Secrets scan: CLEAN

**Verdict**: READY TO MERGE / BLOCKED (reason)
```

## Rules

- Do NOT merge the PR — only validate and report readiness
- If CI is red or review evidence is missing, report not ready and return the PR to the owning
  canonical pipeline/recovery lane; do not introduce a local fix threshold
- Finding severity, comment triage, and how many review rounds are owed: the global
  `review-and-ship` skill and global laws 2 and 11. This skill only runs the local checks.
