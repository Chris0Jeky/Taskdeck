# Taskdeck Claude Skills

Claude Code skills live here and mirror the Taskdeck workflows exposed to Codex under `.codex/skills/`.

## High-Autonomy Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-issue-batch-orchestrator` | many issues, worker/worktree coordination, PR/review/CI batches, docs reconciliation |
| `taskdeck-worktree-issue-worker` | one issue in an isolated worktree |
| `taskdeck-pr-review-loop` | self-review, fresh adversarial review, review/bot comments |
| `taskdeck-ci-conflict-recovery` | failing CI, stale branches, merge conflicts, blocked PRs |
| `issue-to-pr` | simple one-issue-to-PR flow |

## Domain Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-repo-onramp` | broad orientation and current-reality reconciliation |
| `taskdeck-backend-slice` | backend/API/application/infrastructure/worker/auth behavior |
| `taskdeck-frontend-workspace-slice` | Vue workspace, routes, shell, stores, and UI polish |
| `taskdeck-capture-review-loop` | capture, inbox, proposal review, execute, provenance, board handoff |
| `taskdeck-demo-regression` | seeded demo evidence, Playwright proof, walkthrough validation |
| `taskdeck-verification-doc-sync` | final checks, doc sync, handoff summary |

## Usage Pattern

1. Read `CLAUDE.md`, `AGENTS.md`, and `docs/STATUS.md`.
2. Choose the smallest matching skill set.
3. Use `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` for high-autonomy batch discipline even when the executing agent is Claude.
4. Use `docs/WORKTREE_AGENT_PROTOCOL.md` for Claude worktree isolation.
5. Report commands run, test results, docs updated, and unresolved risks.

