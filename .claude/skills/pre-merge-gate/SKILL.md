---
name: pre-merge-gate
description: "Collect Taskdeck-local readiness evidence for the canonical review pipeline: tests, lint, type-check, build, diff inspection, comments, and exact-head CI."
user-invocable: true
---

# Pre-Merge Gate

Collect the Taskdeck-local validation packet that the global `review-and-ship` pipeline consumes.
Execute the local checks as one atomic operation; this skill does not decide review or merge policy.

## Arguments

`$ARGUMENTS` is a PR number or empty (current branch's PR).

## Step 1: Identify PR and branch

```bash
gh pr view $ARGUMENTS --json number,headRefName,baseRefName,mergeable,statusCheckRollup
```

If mergeable is "CONFLICTING", stop and report — do not auto-resolve.

## Step 2: Check bot comments

Read ALL comments on the PR:
```bash
gh api repos/{owner}/{repo}/pulls/{number}/comments
gh api repos/{owner}/{repo}/issues/{number}/comments
gh pr view $ARGUMENTS --comments
```

Check for unaddressed findings from any source:
- Human review comments not yet resolved
- Dependabot alerts or suggestions
- CodeQL / security scanning findings
- CI bot failure messages
- Previous adversarial review comments not yet resolved

Return the comment bodies and resolution state to the global pipeline for triage. If that pipeline
directs a fix, rerun the checks affected by the fix before returning the updated packet.

## Step 3: Run local checks

Run ALL of these:

```bash
dotnet build backend/Taskdeck.sln -c Release
dotnet test backend/Taskdeck.sln -c Release -m:1
cd frontend/taskdeck-web && npm run build && npx vitest --run --reporter=verbose
```

Report any failures immediately and do not mark the local evidence packet complete.

## Step 4: Taskdeck diff inspection

Read the full diff (`gh pr diff $ARGUMENTS`) and check for:

- Secrets accidentally committed (.env, tokens, keys, connection strings)
- Debug code left in (console.log, Console.WriteLine used for debugging, breakpoints)
- TODO comments without issue references
- Hardcoded values that violate conventions
- Missing tests for behavior changes
- Clean architecture violations (Domain referencing Infrastructure)
- Agent safety violations (GP-06: no approve_proposal or direct board mutation)
- HTTP semantics violations (wrong status codes)
- Unused `using` statements or dead code

Return any issues as Taskdeck-lens findings to the global pipeline. If it directs a fix, commit and
push the fix, then rerun the affected local checks before returning the updated packet.

## Step 5: CI status

```bash
gh pr checks $ARGUMENTS
```

Report the exact-head state of every check. A red required check makes the local packet incomplete;
route diagnosis and recovery through `taskdeck-ci-conflict-recovery`.

## Step 6: Report

Output a Taskdeck evidence summary:

```
## Taskdeck Evidence: PR #XXX

- [ ] Backend build: PASS/FAIL
- [ ] Backend tests: PASS/FAIL (N passed, M failed)
- [ ] Frontend build: PASS/FAIL
- [ ] Frontend tests: PASS/FAIL
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
