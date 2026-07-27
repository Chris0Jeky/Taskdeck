---
name: taskdeck-issue-batch-orchestrator
description: Coordinate high-autonomy Taskdeck issue batches from selection through worktrees, PRs, review, CI recovery, docs reconciliation, and handoff. Use when asked to take care of many issues, pick next issues, coordinate agents, run review loops, or reconcile GitHub project status.
---

# Taskdeck Issue Batch Orchestrator

Use this skill for Claude Code batch work. It mirrors the Codex workflow in `.codex/skills/taskdeck-issue-batch-orchestrator` while using Claude's worktree protocol where applicable.

## Read First

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

Read as needed: `docs/GITHUB_PROJECT_AUTOMATION.md` (Status/Priority project-board sync — this skill's operational reference).

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

## Structured Patch Discipline

When editing with structured patches:

- Default each `apply_patch` (or equivalent structured patch operation) to one target file. Use a multi-file operation only for a tightly homogeneous mechanical set where every file has its own independently stable anchor.
- In prose or Unicode-bearing files, anchor hunks on nearby ASCII-stable headings or lines instead of typography-sensitive exact text.
- After a context rejection, inspect the live target before retrying, then reduce the retry to the smallest independently anchored hunk.
- Never repeat the same broad multi-file patch after it is rejected.
- Record every repeated or unresolved patch rejection in the active orchestrator/run ledger and final handoff, even when work resumes.
- Keep the record bounded and sanitized: include target(s), a failure class/error summary, a safe reproduction pointer when available, and the working invocation or workaround. Redact secrets, omit full patch payloads, and truncate oversized error context.
- Then invoke `taskdeck-failure-capture` to classify the failure and escalate it to the repository failure ledger when that skill's criteria apply.

## Worker Setup

For isolated workers:

1. Use Claude `isolation: "worktree"` or the repo worktree script, depending on runtime.
2. Do not include absolute main-checkout paths in worker prompts.
3. Require the first command from `docs/WORKTREE_AGENT_PROTOCOL.md` or `powershell -File scripts/worktree_guard.ps1`.
4. Assign explicit file/module ownership.
5. Tell workers they are not alone in the codebase and must not revert others' edits.
6. Require targeted tests and handoff to the global `review-and-ship` pipeline.
7. Require every file-editing worker prompt to restate the structured patch discipline above.

## Review And CI

Review rounds, tier gate, severity bar, and comment triage: the global `review-and-ship` skill
(laws 2 and 11). Satisfy the repository's current declared tier before merge; only lenses beyond
that canonical gate are discretionary. Flag these surfaces when deciding whether an additional
lens is warranted:

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

