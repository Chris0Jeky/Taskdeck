# Orchestration State

Last Updated: 2026-05-16
Status: ACTIVE

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
5. **Review Round 1**: Use `adversarial-review` skill or manual review. Post ALL findings as a PR comment. Fix every finding (CRITICAL through LOW). Push fixes.
6. **Review Round 2**: Fresh adversarial review of the fixes. Post findings. Fix everything. Push.
7. **Bot Check**: Read ALL PR comments (Gemini Code Assist, Dependabot, any bot). Address anything found.
8. **Verify**: Run tests again post-fix. Confirm CI passes (check via `gh pr checks <PR#>`).
9. **Do NOT merge** to main. Leave PR open.
10. **Stack if needed**: If the next issue depends on this PR, branch from the PR branch.

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
- [ ] #1070 TST-63: MFA setup 409-Conflict test (small, isolated)

### Tier 2: Feature Delivery
- [ ] #1001 PAPER-05: Board/Kanban surface in Paper (frontend, clear spec)
- [ ] #982 RFAI-10: PWA share-target quick capture (Priority III)
- [ ] #983 RFAI-11: Ambient channel hardening decision (Priority IV)
- [ ] #984 RFAI-12: Learning loop UI + beta gate (depends on #983)

### Tier 3: Infrastructure & Hardening
- [ ] #655 MCP-04: MCP production hardening
- [ ] Audit pass: find and seed new issues from code/test gaps

### Tier 4: Strategy & External (lower priority)
- [ ] #546 GTM-02: Demo video
- [ ] #550 BRAND-01: Domain/logo
- [ ] #548 LEGAL-01: Privacy policy
- [ ] #219 CAP-21: Voice capture (Priority IV)

## Current Work

### Active Branch: `tst/1070-mfa-409-conflict` (next)

### In-Progress PRs:
- PR #1075 (docs/cleanup-encoding-and-counts) — orchestration file, under review

### Stacked Branches: (none)

## Dependency Graph

```
#983 RFAI-11 (ambient channel)
  └── #984 RFAI-12 (learning loop + beta gate) [also depends on #977✓, #980✓, #981✓]

#1001 PAPER-05 (board surface) [blocked by PAPER-01✓, PAPER-02✓, PAPER-03✓]

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
