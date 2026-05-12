---
name: taskdeck-pr-review-loop
description: Perform Taskdeck PR self-review or fresh adversarial review, post review findings, address review comments and bot comments, verify fixes, and re-review. Use when the user asks to review PRs, spin fresh reviewers, check comments, address feedback, or run another adversarial pass on sensitive or failing work.
---

# Taskdeck PR Review Loop

Review like a maintainer: find bugs, risks, missing tests, and docs drift before merge.

## Read first

1. `docs/STATUS.md`
2. `AGENTS.md`
3. `docs/GOLDEN_PRINCIPLES.md`
4. The linked issue and PR body
5. The PR diff

## Review focus

Prioritize:

- auth/authz and cross-user policy regressions
- proposal-first automation safety
- persistence/migration correctness
- error contract stability
- missing tests or weak assertions
- frontend keyboard/accessibility/loading/error states
- docs/testing-guide drift
- CI workflow or project automation side effects

Do not spend review energy on unrelated style unless it creates risk.

## Review output

Unless the user explicitly says otherwise, post findings as a PR comment organized by severity. Each finding needs:

- file and line if available
- concrete risk
- expected fix or test
- severity (CRITICAL, HIGH, MEDIUM, LOW)

All findings at every severity must be addressed — there is no "non-blocking" category. Do not skip lower-priority findings.

If a finding is real but out of scope for this PR, seed a GitHub issue to track it. Never silently drop findings. Tech debt from reviews must be zero.

If there are no findings, post an explicit no-finding comment with residual risk and test gaps.

## Sensitive PR rule

Run a second fresh review when the PR touches:

- security/auth/session/token behavior
- migrations or data deletion/retention
- MCP or external agent write surfaces
- capture/review/proposal execution
- GitHub workflows/project automation
- broad frontend route or state flow
- flaky/failing CI

The second reviewer should not rely on the first review summary unless addressing already-posted comments.

## PR Comment Check (MANDATORY)

Before posting findings, read ALL existing PR comments:

```bash
gh api repos/{owner}/{repo}/pulls/{number}/comments
gh pr view <number> --comments
gh api repos/{owner}/{repo}/issues/{number}/comments
```

Look for:
- Human review comments not yet addressed
- Dependabot alerts or suggestions
- CodeQL / security scanning findings
- CI bot failure messages
- Previous adversarial review comments not yet resolved
- Any automated tool output needing action

Address everything unaddressed: fix it, reply with invalidation evidence, or seed a tracked GitHub issue.

## Addressing feedback

When assigned to fix comments:

1. Read all unresolved review threads, human comments, and bot comments.
2. Group by root cause.
3. Make focused fix commits.
4. Run targeted checks.
5. Reply to each thread with what changed and verification.
6. Post a follow-up PR comment mapping each finding to its fix commit and verification result.
7. Re-review the fixed diff.

Never mark feedback resolved mentally without either fixing it, explaining why it is invalid, or seeding a follow-up as a GitHub issue.

## Comment hygiene

Use GitHub MCP for issue/PR metadata and comments when available. Use `gh` fallback when MCP is unavailable.

## Rules

- NEVER skip MEDIUM or LOW findings — there is no "non-blocking" category
- Always check ALL existing PR comments (human, bot, previous reviews) before posting findings
- Address every unaddressed comment: fix, invalidate with evidence, or seed a GitHub issue
- Out-of-scope findings must be seeded as GitHub issues, never silently dropped
- Always post a follow-up comment after fixes are pushed
- Tech debt from reviews must be zero

Do not merge PRs. Do not change repo settings, secrets, protections, environments, or workflow permissions.
