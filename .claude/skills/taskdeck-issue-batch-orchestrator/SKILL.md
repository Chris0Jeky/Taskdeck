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

## Worker Setup

For isolated workers:

1. Use Claude `isolation: "worktree"` or the repo worktree script from the main checkout, depending on runtime; the repo helper rejects linked-source invocation.
2. Do not include absolute main-checkout paths in worker prompts.
3. When the repo helper was used, require its complete printed PowerShell handoff block as the first worker commands. Its exact absolute target `Initialize-CodexIssueWorktree.ps1` wrapper runs the pinned-Git guard first, binds the exact helper-created worktree and detached base, and only then runs `switch -c`. Creation-time blob checks do not authenticate target bytes at execution, so same-user replacement before or during handoff remains outside this boundary. When launch authorization requires a PowerShell rule, use the exact additive full-command rule printed by the helper, including every pinned argument and no wildcard; pass its emitted single-quoted here-string variable as one `--allowedTools` argv value, never a generic relative initializer rule. Start `claude -p` in the exact helper-created target without `--worktree`; accept project trust interactively before relying on project settings or hooks. Enable the unsandboxed Windows PowerShell tool only through the trusted host environment for the exact initializer rule, then keep later commands on Taskdeck's Bash-only hook surface. For an untrusted launch, supply every allow through CLI argv. Unsupported clients require an interactive coordinator launch.
4. From a Bash worker, launch a reviewed absolute PowerShell application in the worktree and run that whole block unchanged; never resolve bare `powershell`. Otherwise use the first guard command from `docs/WORKTREE_AGENT_PROTOCOL.md`; never substitute a PATH-first batch shim.
5. Assign explicit file/module ownership.
6. Tell workers they are not alone in the codebase and must not revert others' edits.
7. Require targeted tests and self-review before handoff.

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

