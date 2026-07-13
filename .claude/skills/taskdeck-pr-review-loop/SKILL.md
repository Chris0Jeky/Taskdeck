---
name: taskdeck-pr-review-loop
description: "Perform Taskdeck PR self-review or fresh adversarial review, inspect ALL comments (including bots), post actionable findings, fix everything, and verify."
user-invocable: true
---

# Taskdeck PR Review Loop

Use this skill for PR self-review, fresh adversarial review, and review-comment follow-up.
This is the Taskdeck-specific wrapper around `/adversarial-review` with domain knowledge baked in.

## Read First

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

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

Use severities: `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`. All findings at every severity need a fix, invalidation evidence, or a tracked follow-up (GitHub issue). There is no "non-blocking" category — everything gets addressed. Out-of-scope findings must be seeded as issues, never silently dropped.

## PR Comment Check (MANDATORY)

Before posting findings, check ALL existing PR comments (human reviews, bot comments, and previous review threads):

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
- Human review comments not yet addressed
- Any automated tool output needing action

Address everything unaddressed: fix it, reply with invalidation evidence, or seed a tracked GitHub issue. Include all comment findings in the review output under "### Existing Comments Addressed".

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
- NEVER skip MEDIUM or LOW findings — there is no "non-blocking" category
- Always check ALL existing PR comments (human, bot, previous reviews) before posting findings
- Address every unaddressed comment: fix, invalidate with evidence, or seed a GitHub issue
- Out-of-scope findings must be seeded as GitHub issues, never silently dropped
- Always post a follow-up comment after fixes are pushed
- Verify CI is green after pushing with `gh pr checks`
- Tech debt from reviews must be zero
