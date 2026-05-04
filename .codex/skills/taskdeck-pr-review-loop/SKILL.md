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

Post findings in severity order. Each finding needs:

- file and line if available
- concrete risk
- expected fix or test
- whether it blocks merge

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

## Addressing feedback

When assigned to fix comments:

1. Read all unresolved review threads and bot comments.
2. Group by root cause.
3. Make focused fix commits.
4. Run targeted checks.
5. Reply to each thread with what changed and verification.
6. Re-review the fixed diff.

Never mark feedback resolved mentally without either fixing it, explaining why it is invalid, or seeding a follow-up approved by the coordinator/user.

## Comment hygiene

Use GitHub MCP for issue/PR metadata and comments when available. Use `gh` fallback when MCP is unavailable.

Do not merge PRs. Do not change repo settings, secrets, protections, environments, or workflow permissions.
