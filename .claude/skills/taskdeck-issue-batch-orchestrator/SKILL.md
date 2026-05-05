---
name: taskdeck-issue-batch-orchestrator
description: Coordinate high-autonomy Taskdeck issue batches from selection through worktrees, PRs, review, CI recovery, docs reconciliation, and handoff. Use when asked to take care of many issues, pick next issues, coordinate agents, run review loops, or reconcile GitHub project status.
---

# Taskdeck Issue Batch Orchestrator

Use this skill for Claude Code batch work. It mirrors the Codex workflow in `.codex/skills/taskdeck-issue-batch-orchestrator` while using Claude's worktree protocol where applicable.

## Read First

1. `docs/STATUS.md`
2. `CLAUDE.md`
3. `AGENTS.md`
4. `docs/IMPLEMENTATION_MASTERPLAN.md`
5. `docs/ISSUE_EXECUTION_GUIDE.md`
6. `docs/GITHUB_PROJECT_AUTOMATION.md`
7. `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` for shared batch discipline; substitute Claude entrypoints for Codex-specific preflight.
8. `docs/WORKTREE_AGENT_PROTOCOL.md` when using Claude worktree agents

## Coordinator Responsibilities

The coordinator owns issue selection, dependency checks, worktree prompts, conflict resolution, PR quality, project status/priority sync, adversarial review assignment, CI/comment loops, docs rehydration, and final handoff.

Do not delegate final synthesis. Do not silently defer work.

## Work Splitting

Split only by non-overlapping ownership:

- one backend issue per worker
- one frontend issue per worker
- one docs-only issue per worker
- one reviewer per PR
- one CI/conflict worker per failing PR

Avoid concurrent edits to the same view, store, service, migration chain, project file, or canonical doc unless the coordinator controls merge order.

## Worker Setup

For isolated workers:

1. Use Claude `isolation: "worktree"` or the repo worktree script, depending on runtime.
2. Do not include absolute main-checkout paths in worker prompts.
3. Require the first command from `docs/WORKTREE_AGENT_PROTOCOL.md` or `powershell -File scripts/worktree_guard.ps1`.
4. Assign explicit file/module ownership.
5. Tell workers they are not alone in the codebase and must not revert others' edits.
6. Require targeted tests and self-review before handoff.

## Review And CI

Every PR needs self-review. Sensitive PRs need fresh adversarial review:

- auth, sessions, tokens, security, secrets, redaction
- migrations, persistence, deletion, import/export
- capture, inbox, proposal review, execute, provenance
- MCP or external-agent write surfaces
- CI, project automation, scripts
- broad frontend route/store/shell changes

Use `taskdeck-pr-review-loop` and `taskdeck-ci-conflict-recovery` for review and recovery work.

## Final Reconciliation

Before handoff, reconcile:

- `docs/STATUS.md` when shipped reality changed
- `docs/IMPLEMENTATION_MASTERPLAN.md` when sequencing or delivery history changed
- `docs/TESTING_GUIDE.md` when testing expectations or totals changed
- `docs/MANUAL_TEST_CHECKLIST.md` or runbooks when manual verification became recurring
- GitHub project `Status` and `Priority` when issue/PR state changed

