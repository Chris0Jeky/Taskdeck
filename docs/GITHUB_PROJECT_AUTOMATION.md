# GitHub Project Automation Guide

This document defines the canonical setup for the `Taskdeck Execution` GitHub Project.
Use this to keep intake and status transitions consistent for every issue and PR.
Last Updated: 2026-07-26

## Canonical Status Model

Required `Status` options:
- `Pending` (default intake state)
- `Now`
- `Next`
- `Blocked`
- `Review`
- `Done`

Rules:
- Every new project item must receive `Status=Pending` automatically.
- `Done` is terminal for closed or merged work.
- `Now` is WIP-limited to one major item at a time (team discipline + weekly audit).

## Required Labels

Canonical descriptions and usage rules live in:
- `docs/ops/GITHUB_LABEL_TAXONOMY.md`

Operational labels:
- `bug` (GitHub default; keep it present because `bug_report` template uses it)
- `security`
- `hardening`
- `backend`
- `frontend`
- `ux`
- `testing`
- `docs`
- `refactor`
- `tech-debt`
- `starter-packs`
- `llm`
- `feature`
- `automation`
- `worker`
- `performance`
- `Priority I`
- `Priority II`
- `Priority III`
- `Priority IV`
- `Priority V`

Priority label rules:
- Every issue must have exactly one priority label.
- `Priority I` = highest urgency / current cycle blockers.
- `Priority II` = immediate next tranche after `Priority I`.
- `Priority III` = medium-term expansion tranche.
- `Priority IV` = later maturity tranche.
- `Priority V` = meta/historical/lowest urgency.

## Project Views

Keep these views:
- `Pending` (filter: `status:"Pending"`)
- `Now` (filter: `status:"Now"`)
- `Next` (filter: `status:"Next"`)
- `Blocked` (filter: `status:"Blocked"`)
- `Review` (filter: `status:"Review"`)
- `Done` (filter: `status:"Done"`)
- `Execution Board` (board view, `Column by: Status`)
- `Priority View` (board view, `Column by: Priority`)

Operational safety views:
- `No Status` table view with `Status` empty filter (`no:status`).
- `WIP Audit` table view with `status:"Now"` for weekly WIP cap validation.

Safety discipline:
- Check `No Status` before each release candidate and during weekly backlog seeding.
- Resolve all empty-status items before merge trains or release tagging.
- Check `Priority View` and ensure no issue/PR remains without a `Priority` field value.

## Priority Field Synchronization Policy (required)

Field:
- Project field `Priority` with options `Priority I` through `Priority V`.

Issue item rule:
- For every issue project item, `Priority` field must exactly match the issue's single priority label.
- If an issue has missing or multiple priority labels, fix labels first, then set project field.

Pull request item rule:
- Every PR project item must have a `Priority` field value.
- Derivation order:
1. Use linked closing issues (`closingIssuesReferences`) and inherit issue priority.
2. If no closing links, parse PR body references (`Closes #...`, `Fixes #...`, `Refs #...`) and inherit referenced issue priority.
3. If multiple priorities are discovered, choose the highest urgency (`Priority I` > `II` > `III` > `IV` > `V`).
4. If no issue priority can be derived, set `Priority V` and note the fallback in PR notes/comment when practical.

Operational timing:
- Apply/sync `Priority` field during issue seeding, issue relabeling, PR creation, and backlog reconciliation passes.

## Workflow Automation (GitHub Project UI)

Project: `Taskdeck Execution`

1. `Auto-add to project` (ON)
- Filter must include repository `Chris0Jeky/Taskdeck`.
- Intake filter must include both issues and pull requests.

2. `Item added to project` (ON)
- Action: `Set field`.
- Field: `Status`.
- Value: `Pending`.

3. `Item reopened` (ON)
- Action: set `Status=Pending`.

4. `Item closed` (ON)
- Action: set `Status=Done`.

5. `Pull request linked to issue` (ON)
- Action: set linked issue `Status=Review`.

6. `Pull request merged` (ON)
- Action: set `Status=Done`.

Optional:
- `Code review approved` can set `Status=Review`.
- `Code changes requested` can set `Status=Now` or `Status=Blocked`.

## Drift Controls

- Issue templates must only use labels that exist in the repo.
- Blank issues should be disabled to force templates.
- CI must run governance checks:
  - `node scripts/check-docs-governance.mjs`
  - `node scripts/check-github-ops-governance.mjs`

## Stable Project Snapshot Contract

The priority audit must collect a complete ProjectV2 item snapshot before it reports a result or
writes a field. Every page must preserve the initial `totalCount` and `updatedAt`; duplicate IDs,
cursor faults, truncated nested connections, malformed responses, limit ceilings, and policy
defects remain immediate non-retryable failures.

When pagination observes only a recognized `totalCount` or `updatedAt` drift, the helper may make at
most two whole-snapshot restarts. Each restart begins with `after = null` and discards every partial
item, cursor, ID, and snapshot value from the failed attempt. Exhaustion of the initial/pre-write
snapshot restart budget exits nonzero with deterministic diagnostics, emits no `complete: true`
result, and performs no project writes. Post-Apply audit exhaustion follows the separate completeness
rule below, where writes may already have occurred and final state is unknown. `-Apply` retains its
pre-write snapshot/plan drift guard and complete post-Apply audit; a restart does not weaken either
boundary.

## Verification Checklist

After setup changes:
- Create a test issue and confirm it auto-adds with `Status=Pending`.
- Reopen the issue and confirm it returns to `Pending`.
- Close the issue and confirm `Status=Done`.
- Create a PR linked to an issue and confirm issue `Status=Review`.
- Merge PR and confirm issue and PR items move to `Done`.
- Open `No Status` and confirm only empty-status items are listed.
- Open `Priority View` and confirm issue/PR items have non-empty `Priority` values.
- Run issue search and confirm zero issues without a priority label:
  - `is:issue -label:"Priority I" -label:"Priority II" -label:"Priority III" -label:"Priority IV" -label:"Priority V"`

## Codex Batch Helpers

Codex high-autonomy batches should follow `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`.

Local helper scripts:
- `scripts/github/Select-TaskdeckIssues.ps1` — shortlist candidate open issues by priority label and obvious blocked/dependency signals.
- `scripts/github/Inspect-TaskdeckPrs.ps1` — summarize open PRs, linked issues, comments, and check status counts.
- `scripts/github/Seed-TaskdeckFollowupIssue.ps1` — create explicit follow-up issues for accepted deferrals.
- `scripts/github/New-TaskdeckDocsRehydrationChecklist.ps1` — list recently merged PRs and docs that may need rehydration.
- `scripts/github/Sync-TaskdeckProjectPriority.ps1` — audit issue/PR Project v2 `Priority` drift and optionally apply fixes.

These helpers are fallbacks for GitHub MCP gaps. Project v2 priority audit needs `read:project`; applying field updates with `Sync-TaskdeckProjectPriority.ps1 -Apply` requires `gh auth refresh -s project`.
The sync helper enforces the PR rule above: complete same-repository closing issues take precedence over repository-aware body references, the highest derived urgency wins, and a PR with no authoritative issue references receives `Priority V`. If every closing Issue is external, the helper still evaluates the body for a same-repository fallback. Every body reference is resolved to a typed, repository-matching object. Only actual same-repository Issues contribute Priority labels. A validated PullRequest reference is ignored as a non-Issue. Cross-repository Issue authority is default-off: those references stay visible by exact PR/source/repository/number identity in human and JSON output, but their labels never enter ranking; mixed references derive only from same-repository Issues. An actual same-repository Issue whose priority is missing, ambiguous, unreadable, or identity-mismatched fails closed instead of becoming a fallback.

The priority helper's completeness contract is fail-closed:
- It walks the ProjectV2 item connection by cursor and reports clean only after the collected item count exactly matches a stable `totalCount`/`updatedAt` snapshot.
- It rejects repeated or non-advancing cursors, ordinal duplicate item IDs, truncated label/field-value/closing-issue connections, and project changes observed during pagination.
- External Issue or PullRequest content placed directly in the project is outside the canonical audit boundary and fails closed. This is distinct from an external Issue merely referenced by a same-repository PR, which is reported as visible non-authority.
- `-Limit 0` (the default) means no configured ceiling. A positive `-Limit` is a safety ceiling, not a sample size; if the project is larger, the command exits nonzero without a completeness claim.
- All same-repository project Issues with zero or multiple priority labels are aggregated and reported before any PR reference resolution. Issue priorities are never guessed; fix every listed label defect first under the issue-item rule above.
- `-Apply` validates every planned Priority option, rebuilds the complete snapshot and source-derived update plan immediately before writes, and aborts before the first write if either plan drifts. The guarded source fingerprint includes the exact ignored external Issue occurrences, so reference identity/count drift is fatal even when the Priority update plan is unchanged. After the first write attempt it always runs a complete post-apply audit, including when a later write fails, because ProjectV2 edits are not transactional as a batch. Success output is built from that verified post-state; a partial failure reports both the writer error and whether the final state was auditable.
- If the pre-write snapshot exhausts its restart budget, no writes have occurred. If the post-Apply audit snapshot exhausts its restart budget, writes may already have occurred because ProjectV2 edits are nontransactional, so the final project state is unknown; the complete post-Apply audit remains mandatory even after a partial writer failure.
- `-SelfTest` exercises the authentication-free parser/audit behavior for pagination, saturation, identity, cursor, nested-connection, typed reference authority, ignored-reference evidence/drift, plan drift, zero-write preflight, partial-writer failure, and post-apply output. Required CI runs the parser and this regression suite directly.

## Weekly Backlog Seeding Cadence (OPS-06)

Goal:
- Keep the project populated with near-horizon, dependency-aware items without overloading WIP.

Weekly process:
1. Review `docs/STATUS.md`, `docs/IMPLEMENTATION_MASTERPLAN.md`, and `docs/TaskdeckNextWorkChecklist.md`.
2. Select the highest-priority items whose dependencies are complete.
3. Create/update issues with explicit acceptance criteria and required labels.
4. Ensure each issue body includes dependency mapping (`Depends on #...`, `Unblocks #...` when applicable).
5. For product-facing slices, include thesis-alignment notes:
   - how the slice reduces maintenance overhead/capture friction, or
   - how the slice strengthens review-first trust/safety guarantees.
6. Sync project `Priority` field for issues and PRs per policy above.
7. Place items into project statuses according to WIP rules.

WIP-aware intake limits:
- Maximum 5 newly-seeded issues per week.
- Maximum 1 major issue in `Now`.
- Maximum 2 issues in `Next`.
- Remaining seeded issues stay in `Pending` until promoted.

Override rule:
- Maintainer may explicitly waive intake cap for one-off backlog seeding/reconciliation events.
- WIP execution discipline (`Now`/`Review` limits) remains in force even when intake cap is waived.

Evidence of execution:
- 2026-02-16 seeding pass populated Stage 0 governance issues (`#43`, `#59`, `#41`, `#55`, `#60`, `#56`) and Stage 1 security tranche issues.
- 2026-02-18 expansion pass seeded future-development waves (`#67` to `#111`) and applied priority labels across all issues.
- 2026-02-18 reconciliation pass applied issue-priority labels to all issues and synchronized project `Priority` for issues + PRs.
- 2026-04-25 roadmap-v4 reconciliation pass intentionally exceeded the normal weekly intake cap to seed tracker `#972` and child issues `#973`--`#984`; WIP status discipline still applies before implementation starts.
