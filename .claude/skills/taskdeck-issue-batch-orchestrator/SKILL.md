---
name: taskdeck-issue-batch-orchestrator
description: Coordinate high-autonomy Taskdeck issue batches from selection through worktrees, PRs, canonical review handoff, CI recovery, docs reconciliation, and handoff. Use when asked to take care of many issues, pick next issues, coordinate agents, or reconcile GitHub project status.
---

# Taskdeck Issue Batch Orchestrator

Use this skill for Claude Code batch work. It mirrors the Codex workflow in `.codex/skills/taskdeck-issue-batch-orchestrator` while using Claude's worktree protocol where applicable.

## Read First

Orient via `autodoc/AGENT_INDEX.md` (the seam map) — find your area in its seams table and jump to the entry point. Read only the relevant section of `docs/STATUS.md` (source of truth; ~1.3k lines — never read end-to-end); don't bulk-read `docs/IMPLEMENTATION_MASTERPLAN.md`. Root `CLAUDE.md`/`AGENTS.md` auto-load — don't re-read them.

Read as needed: `docs/GITHUB_PROJECT_AUTOMATION.md` (Status/Priority project-board sync — this skill's operational reference).

## Coordinator Responsibilities

The coordinator owns issue selection, dependency checks, worktree prompts, conflict resolution, PR
quality, project status/priority sync, canonical review-pipeline evidence handoff, CI/comment
recovery, docs rehydration, and final handoff.

Do not delegate final synthesis. Do not silently defer work.

## Work Splitting

Split only by non-overlapping ownership:

- one backend issue per worker
- one frontend issue per worker
- one docs-only issue per worker
- review workers only when the global pipeline requests Taskdeck lenses
- one CI/conflict worker per failing PR

Avoid concurrent edits to the same view, store, service, migration chain, project file, or canonical doc unless the coordinator controls merge order.

## Read-only Inventory Hygiene

- Route every delegated shell-backed Git or GitHub inventory command through
  `scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1`; pass an argv array, for example
  `& scripts/github/Invoke-TaskdeckReadOnlyInventory.ps1 -Command @("gh", "pr", "list", "--state", "open")`.
  Run its `-SelfTest` mode when changing the wrapper or this routing contract.
- A purpose-built connector whose exposed operation is intrinsically read-only may be used directly.
  Direct `git` or `gh` belongs to the coordinator's separately authorized mutation lane, not to a
  delegated read-only inventory lane.
- The wrapper is an opt-in routed entry point, not a project command-deny hook. It validates command
  argv and blocks known write and execution surfaces; never describe it as enforcement over commands
  that bypass the entry point.
- A read-only inventory lane is filesystem-read-only as well as GitHub-read-only. Process bounded Git, GitHub, CI, and ProjectV2 responses in memory or stream them directly to the coordinator; never redirect a snapshot into the primary checkout or any worktree.
- If a tool genuinely requires materialization, allocate a unique path below the operating system's temporary directory, outside every repository and worktree. Record that exact path in the lane handoff; generic repo-root names such as `.tmp-*.json` are forbidden.
- Before cleanup, prove the lane created that exact absent path during its current turn. A collision, pre-existing path, missing provenance, or uncertain ownership means preserve the artifact and report it to the coordinator; never overwrite or delete it.
- The coordinator compares primary-checkout status before and after every inventory wave and investigates any delta before accepting the handoff or starting another writer.

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

1. Use Claude `isolation: "worktree"` or the repo worktree script from the main checkout, depending on runtime; the repo helper rejects linked-source invocation.
2. Do not include absolute main-checkout paths in worker prompts.
3. When the repo helper was used, require its complete printed PowerShell handoff block as the first worker commands. Its first command invokes the exact absolute target `worktree_guard.ps1` with pinned Git; the bounded exact-target `Initialize-CodexIssueWorktree.ps1` follows on guard success, binds the detached base, and only then runs `switch -c`. A late collision removes the unused detached worktree only when its tracked, untracked, and ignored inventory is empty; otherwise it is preserved for inspection. The helper validates target guard/initializer bytes against reviewed raw blobs before emitting the block, but same-user replacement after emission remains outside this boundary. When launch authorization requires PowerShell rules, use both exact additive full-command rules printed by the helper (guard plus initializer), including every applicable pinned argument and no wildcard; pass its ordered rule array as two `--allowedTools` argv values, never a generic relative handoff rule. Start `claude -p` in the exact helper-created target without `--worktree`; accept project trust interactively before relying on project settings. The project does not enable the unsandboxed Windows PowerShell tool or grant generic PowerShell access; two narrow manual failure-ledger utility rules remain in committed settings. When the trusted host enables the tool for handoff, review those two rules together with the exact guard and initializer rules, restore the prior host value when the launch returns, then keep later commands on Git Bash as the documented portable shell. Taskdeck installs no project command-deny hook. For an untrusted launch, supply every allow through CLI argv. Unsupported clients require an interactive coordinator launch.
4. From a Bash worker, launch a reviewed absolute PowerShell application in the worktree and run that whole block unchanged; never resolve bare `powershell`. Otherwise use the first guard command from `docs/WORKTREE_AGENT_PROTOCOL.md`; never substitute a PATH-first batch shim.
5. Assign explicit file/module ownership.
6. Tell workers they are not alone in the codebase and must not revert others' edits.
7. Require targeted tests and handoff into the canonical review pipeline.
8. Require every file-editing worker prompt to restate the structured patch discipline above.

## Review And CI

Enter ready PRs into the global `review-and-ship` pipeline (laws 2 and 11). Supply these surfaces
as Taskdeck-specific risk context:

- auth, sessions, tokens, security, secrets, redaction
- migrations, persistence, deletion, import/export
- capture, inbox, proposal review, execute, provenance
- MCP or external-agent write surfaces
- CI, project automation, scripts
- broad frontend route/store/shell changes

Use `taskdeck-pr-review-loop` and `taskdeck-ci-conflict-recovery` for Taskdeck lenses and recovery
work. Reviewer invocation, counts, severity, convergence, aging, and merge disposition remain in
the global pipeline.

## Final Reconciliation

Before handoff, reconcile:

- `docs/STATUS.md` when shipped reality changed
- `docs/IMPLEMENTATION_MASTERPLAN.md` when sequencing or delivery history changed
- `docs/TESTING_GUIDE.md` when testing expectations or totals changed
- `docs/MANUAL_TEST_CHECKLIST.md` or runbooks when manual verification became recurring
- GitHub project `Status` and `Priority` when issue/PR state changed

