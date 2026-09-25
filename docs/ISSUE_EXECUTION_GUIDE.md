# Issue Execution Guide

Last Updated: 2026-09-21
Scope: How agents execute the GitHub issue backlog safely, in dependency order, and with explicit
priority, status, and milestone discipline.

**Active sequence:** the ratified REVIVAL wave in `docs/REVIVAL_PLAN.md` (direction:
`docs/strategy/PRODUCT_DIRECTION.md`), with current repository and v0.3 routing in
`docs/analysis/2026-09-21-repository-direction-and-v0.3-programme.md` and
`docs/releases/V0_3_0_READINESS.md`. ADR-0051 permits an authorized coordinator to admit an
acceptance-ready existing issue without a maintainer-request gate, within the caps below. New
tables, endpoints, mutation paths, connector types, top-level views, security posture, and other
architectural surprises still require `REVIVAL_PLAN.md` §7 or a later Accepted ADR/plan amendment.
When this guide and `docs/STATUS.md`/`docs/REVIVAL_PLAN.md` conflict, those documents win.

> The pre-revival stage listings (Stages 0–8, 2026-02 → 2026-05, all delivered) and the retired
> tranche-based Priority model now live in
> `docs/archive/planning-history/ISSUE_EXECUTION_STAGES_2026-02_to_2026-05.md`. They are a
> historical record, not an issue-selection source.

## Purpose

Use this file when starting backlog work. It prevents out-of-order development and keeps
security/ops/doc guardrails ahead of expansion.

## Issue-state model

GitHub metadata expresses three orthogonal facts; keep them consistent:

| Dimension | Mechanism | Values / meaning |
|---|---|---|
| Urgency | exactly one `Priority I`–`V` label, mirrored in the ProjectV2 `Priority` field | semantics in `docs/ops/GITHUB_LABEL_TAXONOMY.md` |
| Scheduling | ProjectV2 `Status` | `Pending` (backlog/evidence archive) · `Now` (actively owned, deps complete) · `Next` (staged, possibly behind a named `Now`) · `Blocked` (a named blocker is recorded on the issue) · `Review` (linked PR open) · `Done` |
| Delivery horizon | GitHub milestone | the release ladder in `docs/strategy/PRODUCT_DIRECTION.md` §5; no milestone = backlog/parked |

The backlog is deliberately **unbounded as memory** (observations, defects, ideas, decisions all
live as issues in `Pending`); only the execution queues are bounded. `decision` and `human-action`
labels mark issues agents must not convert into implementation or infer complete.

## Start Protocol (Required)

1. Read `docs/STATUS.md`.
2. Read `docs/IMPLEMENTATION_MASTERPLAN.md`.
3. Read `docs/GITHUB_PROJECT_AUTOMATION.md`.
4. Confirm current branch is clean and based on `main`.
5. Pick the highest-priority acceptance-ready existing issue admitted by the active plan or an
   Accepted ADR. A `Now` candidate must have complete dependencies; a `Next` candidate may be
   explicitly sequenced behind a named `Now` dependency.
6. Verify the issue has exactly one priority label (`Priority I` to `Priority V`) and that the
   Project `Priority` field matches it before promotion.
7. Use the project `No Status` view (`no:status`) and assign `Now` or `Next` before active work,
   respecting the four-`Now`/eight-`Next` caps.

## Programme routing, ownership, and stacked work (Required)

Before selecting or continuing an issue:

1. Refresh live issue, milestone, PR, branch, checks, review threads, Project status and recent
   commits. A dated document never proves live ownership or readiness.
2. Search both lane-claim marker families, open PRs and remote branches. Do not duplicate a current
   implementation because its claim format differs.
3. Determine the primary acceptance owner. File location alone does not decide whether work belongs
   to product trust, platform integrity, release control or documentation reconciliation.
4. For a stacked PR, record the parent PR, actual base branch and required landing order. A child
   based on an open parent is not independently mergeable to `main` even when GitHub reports it
   conflict-free.
5. Treat CI/review as exact-head evidence. Rebase, base merge, retarget, review repair, generated
   file update or parent refresh invalidates the old qualification claim.
6. Check canonical-document ownership before editing `STATUS`, the masterplan, readiness,
   `OUTSTANDING_TASKS`, this guide, or the agent index. Agree an integration order with any open PR
   already touching the file.
7. For control-plane work, read ADR-0066 plus `OUTSTANDING_TASKS.md` section J. Green checks do not
   grant merge authority.
8. For v0.3 work, prefer the release critical path. Future-horizon work remains valid backlog but
   does not displace an unfinished release dependency merely because it is easier.

Current named dependency examples are deliberately pointers, not permanent inventory:

- landed verification: merged foundations `#3156` and `#3167`, open parent `#3295`, stacked child
  `#3296`, then collector and workflow integration;
- private rehearsal: workflow inventory `#3297`, then CI-17 implementation `#3170`;
- runner recovery: PR `#3261` is FIX-FIRST evidence and must be ported minimally rather than merged
  wholesale.

## Project Status Workflow (Required)

- Move issue to `Now` only when active implementation starts and all dependencies are complete.
- Move an acceptance-ready existing issue to `Next` when it is explicitly sequenced behind a named
  `Now` dependency.
- Move issue to `Review` when PR is open and linked; if the PR closes without merging, move the
  issue back out of `Review` — `Review` with zero open PRs is always stale.
- Move issue to `Done` only after merge and verification notes are posted.
- If item is blocked by dependency or external input, move to `Blocked` and add a blocking note
  naming the blocker; re-check `Blocked` items when their named blocker closes.
- Ordinary merge eligibility does not require a human PR approval or owner click; use the canonical
  global review-and-ship pipeline and exact-head proving checks.

## Per-Issue Delivery Checklist

1. Branch from latest `main`.
   - If issue body includes `Suggested Branch Name`, use it directly.
2. Keep change scope limited to issue acceptance criteria.
   - Prefer incremental, file-scoped commits for incremental reviewability.
3. Add/update tests for behavior changes.
4. Run required verification commands.
5. Update docs (`STATUS`/`IMPLEMENTATION_MASTERPLAN`/test docs) if reality changed.
6. Open PR with linked issue, dependency/stack notes, exact base/head identity, and risk notes.
7. Enter the canonical global laws and `review-and-ship` pipeline; this guide adds no local
   reviewer-count, severity, convergence, or merge rule.
8. Move project item to `Review`.
9. After merge, move item to `Done` and post the merge SHA, exact-head verification, residuals, and final disposition.

## WIP Discipline

- Keep the autonomous queue bounded and inspectable:
  - at most 4 issue items in `Now`;
  - at most 8 issue items in `Next`;
  - each `Now` item has complete dependencies, and each staged `Next` dependency is named.
- Existing conflicting work must be finished or deliberately parked before its successor enters `Now`.
- If parallel work is needed, split by non-overlapping layers (example: docs-only issue plus one
  code issue).
- Authorized high-autonomy execution follows `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`; no separate
  maintainer request is needed for an acceptance-ready existing issue admitted under ADR-0051. Use
  isolated worktrees, one coordinator, linked PRs, the canonical review-and-ship pipeline, and
  final docs/project-status reconciliation.

## Seeding and reconciliation rules

- Update, merge, or close an existing issue before seeding a new one; search for duplicates and
  successor issues first.
- Never close an issue merely because its title sounds old; never close without a dated evidence
  comment. Never retain a tracker as active when every child is delivered or moved.
- A new issue needs observed evidence, provable acceptance criteria, and the normal intake
  severity/value bar; dogfooding findings (`dogfooding` label) are exempt from the severity bar.
- A `decision` issue may not be converted into implementation until the decision is recorded.

## Escalation Rules

Stop and ask for direction if:
- acceptance criteria conflict with `STATUS.md` reality,
- dependency issue is incomplete but required,
- the change would alter auth policy or project workflow conventions.
