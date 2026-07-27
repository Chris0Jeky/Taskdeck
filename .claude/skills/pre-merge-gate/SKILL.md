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

## Step 4: Confirm review evidence

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

If this scan finds an untriaged issue, report the PR as blocked and return it to the global
`review-and-ship` pipeline. Do not start a second review pipeline from this gate.

## Step 5: CI status

```bash
gh pr checks $ARGUMENTS
```

All checks must be green. If any are failing, diagnose and fix.

## Step 6: Report

Output a merge-readiness summary:

```
## Merge Readiness: PR #XXX

- [ ] Backend build: PASS/FAIL
- [ ] Backend tests: PASS/FAIL (N passed, M failed)
- [ ] Frontend build: PASS/FAIL
- [ ] Frontend tests: PASS/FAIL
- [ ] CI checks: GREEN/RED
- [ ] Review evidence: PRESENT/MISSING
- [ ] Bot comments: ADDRESSED/NONE
- [ ] Secrets scan: CLEAN

**Verdict**: READY TO MERGE / BLOCKED (reason)
```

## Rules

- Do NOT merge the PR — only validate and report readiness
- If CI is red, attempt to fix — only report "blocked" if the fix is non-trivial
- Finding severity, comment triage, and how many review rounds are owed: the global
  `review-and-ship` skill and global laws 2 and 11. This skill only runs the local checks.
