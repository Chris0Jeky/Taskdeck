---
name: taskdeck-pr-review-loop
description: Perform Taskdeck PR self-review or fresh adversarial review, inspect comments, post actionable findings, and verify fixes.
---

# Taskdeck PR Review Loop

Use this skill for PR self-review, fresh adversarial review, and review-comment follow-up.

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
- security/authz gaps
- migration/data-loss risk
- missing tests or weak assertions
- docs drift
- CI, scripts, or project automation breakage
- product-thesis violations around review-first automation

Use severities: `CRITICAL`, `HIGH`, `MEDIUM`, `LOW`. All accepted findings need a fix, invalidation evidence, or an explicit tracked follow-up.

## Review Output

For each finding include:

- severity
- file and line
- what can go wrong
- expected fix
- test or verification expectation

If no findings, say so and still name residual risk and test gaps.

## Fix Loop

When addressing findings:

1. map every finding to a fix, invalidation, or tracked follow-up
2. make focused commits
3. run targeted checks
4. re-review the changed diff
5. comment with fix evidence and verification results

