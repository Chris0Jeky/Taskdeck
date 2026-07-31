# Orchestration State

Last Updated: 2026-05-17
Status: SUPERSEDED (2026-06-13 archive pivot)

> **⚠️ SUPERSEDED — 2026-06-13 archive pivot.** This autonomous-loop execution queue is stale: it is a point-in-time snapshot in which **some items were since delivered and ship live** (e.g. `#982`/`#983`/`#984` via `#1078`/`#1079`/`#1080`) while others sequence de-scoped external-product work (e.g. `#546` demo video, `#550` brand/domain, `#548` privacy policy). The active direction is finish-for-personal-use → archive; current sequencing follows the archive-pivot **waves** in the Direction section of `docs/IMPLEMENTATION_MASTERPLAN.md`, **not** the queue below. Do not resume work from this file's queue.

## Purpose

This file is the persistent memory and execution state for Claude Code's autonomous work loop. After context compaction, re-read this file FIRST to resume work. It tracks what's done, in-progress, next, and the full workflow protocol.

## How to Resume After Compaction

1. Read this file completely
2. Read `docs/STATUS.md` (first 40 lines for current state)
3. Check `git status` and `git log --oneline -5` for in-flight work
4. Check `gh pr list --state open --author Chris0Jeky` for active PRs
5. Resume from the "Current Work" section below

## Workflow Protocol

### For Each Issue:

1. **Branch**: `git checkout -b <branch-name>` from latest `main` (or from a stacked base branch if dependent)
2. **Implement**: Small, focused commits. One commit per logical unit.
3. **Test**: Run `dotnet test backend/Taskdeck.sln -c Release -m:1` and/or `cd frontend/taskdeck-web && npx vitest --run`
4. **PR**: Create with `gh pr create` linking the issue. Include Summary + Test Plan.
5. **Review**: Follow the authoritative `review-and-ship` pipeline named by root `AGENTS.md`; this snapshot sets no fixed review-round count or fix-all rule.
6. **Bot Check**: Read ALL PR comments (Gemini Code Assist, Dependabot, any bot). Address anything found.
7. **Verify**: Run tests again post-fix. Confirm CI passes (check via `gh pr checks <PR#>`).
8. **Merge gate**: Leave PRs open unless the active user request explicitly authorizes merging after the normal review, bot-comment, test, and CI gates. _(The 2026-05-17 cleanup session WAS merge-authorized after those gates; that authorization was session-scoped and is historical.)_
9. **Stack if needed**: If the next issue depends on this PR, branch from the PR branch.

### Parallel Subagent Protocol:

- Use `isolation: "worktree"` for parallel implementation work
- One coordinator (this conversation) owns synthesis, docs, and state
- Workers own implementation within their assigned scope
- After workers finish, verify main checkout is clean

### Docs Update Protocol:

- Update this file after each PR or significant state change
- Only update STATUS.md/MASTERPLAN after merges (don't update for open PRs)
- Update TESTING_GUIDE.md when test counts change measurably

## Execution Queue (Priority Order)

### Tier 0: Housekeeping (do first)
- [x] Close stale issues (#975, #976, #977, #980, #1066) — DONE 2026-05-16
- [x] Fix 156 encoding artifacts across 4 docs — DONE 2026-05-16
- [x] Update test counts and RFAI progress in docs — DONE 2026-05-16
- [x] Close PROD-00 tracker (#881) — all sub-items closed — DONE 2026-05-16
- [x] Commit docs cleanup work as PR #1075 — DONE 2026-05-16

### Tier 1: Quick Wins
- [x] #1070 TST-63: MFA setup 409-Conflict test — **delivered** (merged #1076)

### Tier 2: Feature Delivery
_(Point-in-time snapshot — several items below were **subsequently delivered** and ship live; do not re-pick them. See `docs/STATUS.md`.)_
- [x] #1001 PAPER-05: Board/Kanban surface in Paper — **delivered** (merged #1083; Paper board surface ships live)
- [x] #982 RFAI-10: PWA share-target quick capture — **delivered** (merged `#1078`; PWA share-target ships live)
- [x] #983 RFAI-11: Ambient channel hardening decision — **delivered** (merged `#1079`; VS Code/ambient prototype, ADR-0033)
- [x] #984 RFAI-12: Learning loop UI + beta gate — **delivered** (merged `#1080`; learning-loop UI + Ollama + ProvenanceDrawer). Only the onward beta-gate/distribution is de-scoped.

### Tier 3: Infrastructure & Hardening
- [ ] #655 MCP-04: MCP production hardening
- [ ] Audit pass: find and seed new issues from code/test gaps

### Tier 4: Strategy & External (lower priority) — **DE-SCOPED by the 2026-06-13 archive pivot; do not action**
- [ ] ~~#546 GTM-02: Demo video~~ — **DE-SCOPED (GTM, archive pivot)**
- [ ] ~~#550 BRAND-01: Domain/logo~~ — **DE-SCOPED (branding/GTM, archive pivot)**
- [ ] ~~#548 LEGAL-01: Privacy policy~~ — **DE-SCOPED (hosted-instance legal, archive pivot)**
- [ ] #219 CAP-21: Voice capture (Priority IV) — **DEFERRED, not de-scoped**: the `useVoiceCapture` prototype exists (STATUS) but isn't wired into the UI. Voice capture is a capture-friction improvement (not GTM/cloud/mobile), so the remaining UI integration stays a legitimate deferred follow-on — do not close as de-scoped without an explicit maintainer decision.

## Current Work

### Active Branch: `tst/1081-composable-coverage-part2` (cleanup pass in progress)

### Cleanup Snapshot (2026-05-17)
- PR #1076 `tst/1070-mfa-409-conflict`: CI green; project priority sync completed; merge candidate after final gate audit.
- PR #1077 `fix/paper-board-card-encoding-artifact`: CI green; final-diff adversarial review posted for `e6d920d9`; merge candidate after final gate audit.
- PR #1078 `feat/982-pwa-share-target`: queue ownership, login-required queue claim, terminal-failure parking, client replay plumbing, transient-only queue fallback, and misleading Background Sync removal fixed through `7a2ca22b`; CI and final review pending.
- PR #1079 `feat/983-ambient-channel-hardening`: #1078 merged forward, VS Code Git/API URL hardening, pathful API URL rejection, and voice overlap fixes pushed through `b7f67c47`; CI and final review pending.
- PR #1080 `feat/984-learning-loop-beta-gate`: #1079 merged forward, Ollama localhost selection/runtime/connect policy aligned, and provider connect guards corrected through `ee9d1c98`; CI and final review pending.
- PR #1082 `tst/1081-composable-test-coverage`: CI green at last inspection; current-head adversarial review clean; merge before #1084 to avoid `useReviewKeymap.spec.ts` overlap.
- PR #1083 `paper/1001-board-kanban-surface`: CI green at last inspection; current-head adversarial review clean.
- PR #1084 `tst/1081-composable-coverage-part2`: watcher review findings fixed in `a1d3449a`; merge-policy/date cleanup in progress after review findings; CI pending.

Session merge authorization remains subject to the authoritative `review-and-ship` pipeline named by root `AGENTS.md`, plus current exact-head proving, CI, and comment gates as remeasured live.

### Active PR Stack
- #1078 -> `main`
- #1079 -> #1078
- #1080 -> #1079
- #1082 -> `main`
- #1083 -> `main`
- #1084 -> `main`

## Dependency Graph

```
#983 RFAI-11 (ambient channel)
  └── #984 RFAI-12 (learning loop + beta gate) [also depends on #977✓, #980✓, #981✓]

#1001 PAPER-05 (board surface) ✓ DELIVERED (#1083) [was blocked by PAPER-01✓, PAPER-02✓, PAPER-03✓]

All others: independent
```

## Branch Naming Convention

- `tst/1070-mfa-409-conflict`
- `paper/1001-board-kanban-surface`
- `rfai-10/982-pwa-share-target`
- `rfai-11/983-ambient-channel`
- `rfai-12/984-learning-loop-ui`
- `docs/cleanup-encoding-and-counts`

## Key Verification Commands

```powershell
# Backend tests
dotnet test backend/Taskdeck.sln -c Release -m:1

# Frontend tests
cd frontend/taskdeck-web; npx vitest --run --reporter=verbose

# Frontend typecheck
cd frontend/taskdeck-web; npm run typecheck

# Frontend build
cd frontend/taskdeck-web; npm run build

# Lint
cd frontend/taskdeck-web; npm run lint

# Single backend test class
dotnet test backend/Taskdeck.sln -c Release --filter "FullyQualifiedName~MyClassName"

# Check PR status
gh pr checks <number>
gh pr view <number> --json comments
```

## Review Checklist (for each round)

- [ ] Read full diff (`gh pr diff <number>`)
- [ ] Check for: security issues, logic errors, missing error handling, test gaps, naming issues, code quality
- [ ] Post comment with severity-tagged findings (CRITICAL/HIGH/MEDIUM/LOW)
- [ ] Fix ALL findings, push
- [ ] Verify tests still pass
- [ ] Check all existing PR comments (bots, humans)

## Session Log

### 2026-05-16 Session 1
- Surveyed 15 PRs merged today (#1055-#1074)
- Fixed 156 mojibake artifacts across IMPLEMENTATION_MASTERPLAN.md and 3 ops docs
- Updated TESTING_GUIDE.md: backend 6,336→6,614 (locally verified), frontend 2,805→3,267
- Fixed RFAI count: 8/12 → 9/12 (RFAI-09 was delivered in PR #1052 but undocumented)
- Updated ISSUE_EXECUTION_GUIDE.md: marked RFAI-03/04/05/07/08/09 as delivered
- Closed 5 stale issues (#975, #976, #977, #980, #1066)
- All PROD-00 sub-issues confirmed closed
- Next: commit docs work, then start on #1070, #1001

### 2026-05-16 Session 2 (continued)
- PR #1084 (tst/1081-composable-coverage-part2): 266 new tests total
  - useInboxOrchestrator (41), useReviewProposals (39), useReviewKeymap (30)
  - cardFilterStore (24), columnStore (12), cardStore (24), labelStore (17)
  - boardCrudStore (28), cardCommentStore (17), boardStoreHelpers (13), boardUiStore (4)
  - agentApi (8), integrationsApi (9)
- Fixed CI lint failure (no-unused-vars in 3 test files)
- R1 adversarial review: 3 HIGH, 5 MEDIUM, 4 LOW — all HIGH fixed
- Frontend test count: 3,383 → 3,534
- All stores, composables, and API modules now have test coverage
- Remaining untested: inkBleedMotion.ts, useInkBleed.ts (animation utilities — low priority)
- Next: await CI green, then assess next tier items from execution queue
