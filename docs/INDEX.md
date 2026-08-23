# Documentation Index

This directory is the canonical documentation entrypoint for Taskdeck.

Root `docs/` is reserved for living documents that change regularly or are direct operator/contributor entrypoints.
Stable reference material belongs in topical subfolders.

## Root Living Docs

- `STATUS.md`
  - Current shipped reality, verified state, current focus, and active constraints.
- `IMPLEMENTATION_MASTERPLAN.md`
  - Delivery history, the revival direction (free open beta → commercial horizon, ADR-0044), and the priority-labeled backlog map. *(The forward "horizons" / "release framing" / priority tranches are historical pre-pivot framing; the active sequence is `REVIVAL_PLAN.md`.)*
- `strategy/PRODUCT_DIRECTION.md`
  - **The canonical current strategy document** (2026-08-23): three-layer product identity (adaptive work OS destination · context-to-action engine · transcripts/notes/captures wedge), trust-model reconciliation (shipped review-first vs the Proposed ADR-0057 delegated-authority direction), release-theme ladder, and the open decision surfaces.
- `REVIVAL_PLAN.md`
  - The active execution plan (2026-07-10 revival pivot, ADR-0044; direction now owned by `strategy/PRODUCT_DIRECTION.md`): business posture, phased waves with completion conditions, the v0.1 ship gate, the issue map, and the traction checkpoint.
- `PROJECT_TRAJECTORY.md`
  - 2026-07-02 whole-project analysis, part 1: what the project genuinely does well, the four pivot goals honestly scored, and the effective remaining path (adversarially verified evidence; closeout wave tracker `#1278`).
- `COURSE_CORRECTION.md`
  - 2026-07-02 whole-project analysis, part 2: the central strategic and execution problems, fix-vs-write-off calls, the ordered closeout plan with completion conditions, and the proposed archive exit criteria (ratification via `#1278`).
- `GOLDEN_PRINCIPLES.md`
  - Stable repository invariants and decision rules.
- `ISSUE_EXECUTION_GUIDE.md`
  - Dependency-aware issue order and execution protocol.
- `TaskdeckNextWorkChecklist.md`
  - Lightweight promotion and wave-checklist companion to the masterplan and issue guide.
- `TESTING_GUIDE.md`
  - Automated verification commands, current totals, smoke posture, and release-gate guidance.
- `MANUAL_TEST_CHECKLIST.md`
  - Human verification checklist for product, security, and ops flows.
- `START_HERE.md`
  - Audience-first first-entry product guide.
- `../UPGRADING.md` (repository root)
  - Per-version upgrade notes for self-hosters: where the SQLite file lives, "backup = copy this one file", the automatic pre-migration snapshot, and the shipped export surfaces.
- `USER_MANUAL.md`
  - Manual index for the shipped product shell and chapter map.
- `GITHUB_PROJECT_AUTOMATION.md`
  - Canonical GitHub Project status, priority, and workflow rules.
- `MCP_TOOLING_GUIDE.md`
  - Canonical MCP selection and fallback rules for agents.
- `LIVING_DOCUMENTS_GUIDE.md`
  - Root-doc change map: what changes often, what to update, and in what order.

## Recommended Read Paths

- New user or evaluator:
  - `START_HERE.md` -> `USER_MANUAL.md` -> `manual/02_home_and_today.md` -> `manual/04_inbox_and_review.md` -> `manual/09_troubleshooting.md`
- Regular product user:
  - `START_HERE.md` -> `manual/02_home_and_today.md` -> `manual/03_projects_and_cards.md` -> `manual/08_recipes.md`
- Maintainer or planner:
  - `strategy/PRODUCT_DIRECTION.md` -> `STATUS.md` -> `REVIVAL_PLAN.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `TESTING_GUIDE.md`
  - active execution order: the revival **phases** in `REVIVAL_PLAN.md` (truth + safety → transcript engine → open-beta launch → checkpoint). *Historical:* the archive-pivot waves (2026-06-13→2026-07-10, superseded by ADR-0044 but retained as the checkpoint fallback), `PROJECT_TRAJECTORY.md`/`COURSE_CORRECTION.md` (2026-07-02 analysis pair), root `taskdeck-12-week-roadmap-v4.md`, tracker `#972`
- Contributor or agent:
  - `STATUS.md` -> `IMPLEMENTATION_MASTERPLAN.md` -> `GOLDEN_PRINCIPLES.md` -> `ISSUE_EXECUTION_GUIDE.md` -> `MCP_TOOLING_GUIDE.md` -> `../autodoc/AGENT_INDEX.md` -> `agentic/SKILL_REGISTRY.md`
- Demo operator:
  - `START_HERE.md` -> `product/DEMO_PLAYBOOK.md` -> `product/SAUL_DEMO_REHEARSAL_CONTRACT.md` -> `product/SCENARIOS.md` -> `product/DOGFOODING_GUIDE.md`

## Topical Folders

- `product/`
  - Product-facing guides, demo playbook, and scenario reference.
- `dogfooding/`
  - The active dogfooding protocol and LOG (the ADR-0044 acceptance test; sprint started
    2026-08-22, checkpoint floor 2026-09-01 per walkthrough q-8). Supersedes
    `product/DOGFOODING_GUIDE.md` for cadence truth.
- `manual/`
  - Shipped manual chapters, future-facing placeholders, and in-app help-to-manual mapping.
- `ops/`
  - Deployment, observability, human operator runbooks, and session-start checklists.
- `platform/`
  - Provider, import-adapter, and starter-pack platform or reference docs.
- `security/`
  - Active security and abuse-protection policies or baselines.
- `codex-tasks/`
  - Codex-friendly task catalog: self-contained, token-efficient task prompts for lightweight agents. Organized by tier (frontend-api, frontend-composables, frontend-stores, backend-domain, backend-services). Each `.md` is a standalone prompt with source paths, pattern files, test cases, and verify commands. Tracked as `TST-CODEX-*` issues (`#415`-`#429`).
- `tooling/`
  - MCP operations, harness or tooling guidance, and deferred tooling backlog.
- `agentic/`
  - Shared agent question, failure, guide-update, tool-parity, and skill-registry protocols. This is a contributor/agent operating layer, not product truth.
- `analysis/`
  - Dated reconciliation notes, audits, and planning snapshots. Non-authoritative unless promoted.
  - includes `analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md` for ongoing promotion of `docs/InReview/MVP_EXPANSION/` into canonical docs and backlog guidance.
  - includes `analysis/2026-03-07_mvp-expansion-gap-map.md` for the dated doc and issue reconciliation baseline.
  - includes `analysis/2026-03-07_mvp-expansion-source-coverage-audit.md` for the full file-by-file and snippet-by-snippet audit of what from `MVP_EXPANSION/` is promoted, deferred, or carried into later-wave issue scope.
  - includes `analysis/2026-03-07_mvp-productization-seeding-plan.md` for the concrete GitHub issue-seeding and duplicate-resolution record for the seeded Wave P productization tranche.
  - includes `analysis/2026-02-23_testing-harness-synthesis.md` for testing-harness wave reconciliation (`#254` to `#260`).
  - includes `analysis/2026-02-23_outreach-crm-synthesis.md` for outreach CRM deferred-wave reconciliation (`#262` to `#268`).
  - includes `analysis/2026-07-10_revival_assessment.md` — the revive-vs-archive evidence base (7 code-review dimensions with adversarially verified claims + market research) behind ADR-0044 and `REVIVAL_PLAN.md`.
- `InReview/`
  - Human or in-review source packs awaiting extraction into canonical docs or issue waves.
- `archive/`
  - Historical and superseded docs. Non-authoritative by default.
- `WIP/`
  - External or unpromoted working material that has not yet been reconciled.
- `../autodoc/`
  - Agent-facing seam maps and context-trap indexes. These maps are pointers over the codebase, not canonical status docs.

## Working Notes

- `analysis/2026-03-07_mvp-expansion-reconciliation-tracker.md`
  - Canonical continuity log for the MVP expansion reconciliation.
- `analysis/2026-03-07_mvp-expansion-source-coverage-audit.md`
  - File-by-file and snippet-by-snippet audit for `docs/InReview/MVP_EXPANSION/`.
- `analysis/2026-03-07_mvp-expansion-gap-map.md`
  - Current Wave P and later-wave backlog mapping with reuse-anchor summary.

## Governance Rules

- Do not add new root docs by default.
- If a document is stable reference material rather than a living source of truth, put it in a topical folder.
- If a note is historical, archive it instead of leaving it at root.
- Every meaningful behavior or workflow change should update:
  1. `STATUS.md`
  2. `IMPLEMENTATION_MASTERPLAN.md`
  3. `ISSUE_EXECUTION_GUIDE.md` when order or dependencies change
  4. `TESTING_GUIDE.md` or `MANUAL_TEST_CHECKLIST.md` when verification changes
- Keep root `docs/` readable at a glance; use `LIVING_DOCUMENTS_GUIDE.md` before promoting new recurring docs into root.
