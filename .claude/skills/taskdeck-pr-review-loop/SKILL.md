---
name: taskdeck-pr-review-loop
description: "Perform Taskdeck PR self-review or fresh adversarial review, inspect ALL comments (including bots), post actionable findings, fix everything, and verify."
user-invocable: true
---

# Taskdeck PR Review Loop

Use this skill for PR self-review, fresh adversarial review, and review-comment follow-up.
This is the Taskdeck-specific wrapper around `/adversarial-review` with domain knowledge baked in.

## Read First

1. `docs/STATUS.md`
2. `CLAUDE.md`
3. `AGENTS.md`
4. PR title/body/diff/commits
5. linked issue and acceptance criteria
6. relevant docs: `docs/TESTING_GUIDE.md`, feature docs

## Review Stance

Prioritize actionable findings:

- behavioral regressions
- security/authz gaps (cross-user data exposure, claims bypass)
- agent safety violations (GP-06: no approve_proposal or direct board mutation by agents)
- egress envelope enforcement gaps
- migration/data-loss risk
- race conditions (especially in quota/concurrent-run tracking)
- fail-open patterns where fail-closed is needed
- missing tests or weak assertions
- docs drift (STATUS.md, IMPLEMENTATION_MASTERPLAN.md)
- CI, scripts, or project automation breakage
- clean architecture violations (Domain referencing Infrastructure)
- HTTP semantics violations (wrong status codes)

Use severities: `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`. All accepted findings need a fix, invalidation evidence, or an explicit tracked follow-up.

## Bot Comment Check (MANDATORY)

Before posting findings, check ALL existing PR comments:

```bash
gh api repos/{owner}/{repo}/pulls/{number}/comments
gh pr view <number> --comments
gh api repos/{owner}/{repo}/issues/{number}/comments
```

Look for:
- Dependabot alerts or suggestions
- CodeQL / security scanning findings
- CI bot failure messages
- Previous adversarial review comments not yet resolved
- Any automated tool output needing action

Include bot findings in the review output under "### Bot Comments Addressed".

## Review Output

Post as a PR comment (`gh pr comment <number>`):

```
## Adversarial Code Review

### CRITICAL
- [findings or "None"]

### HIGH
- [findings]

### MEDIUM
- [findings]

### LOW
- [findings]

### Bot Comments Addressed
- [bot findings or "None"]

### Summary
[count by severity, merge-blocking assessment]
```

For each finding include:
- severity
- file and line
- what can go wrong
- expected fix
- test or verification expectation

If no findings, say so and still name residual risk and test gaps.

## Fix Loop

When addressing findings:

1. Map every finding to a fix, invalidation, or tracked follow-up
2. Make focused commits: `fix(<scope>): <severity> <description>`
3. Run targeted checks:
   - Backend: `dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~RelevantTest"`
   - Frontend: `cd frontend/taskdeck-web && npx vitest --run -t "relevant test"`
4. Push fixes
5. Re-review the changed diff
6. Post follow-up comment with fix evidence:

```
## Adversarial Review — Fixes Applied

| Finding | Severity | Fix Commit | Verified |
|---------|----------|-----------|----------|
| ... | ... | `abc1234` | tests pass |

All findings addressed. CI status: [GREEN/PENDING/RED]
```

## Rules

- NEVER pause between review, post, fix, push — it is one atomic operation
- NEVER skip MEDIUM or LOW findings
- Always check bot comments before posting findings
- Always post a follow-up comment after fixes are pushed
- Verify CI is green after pushing with `gh pr checks`
