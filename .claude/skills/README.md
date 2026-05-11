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

## Agentic Operating Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-question-batch` | deciding whether to ask, assume, or proceed on ambiguous work |
| `taskdeck-failure-capture` | classifying failed tools, tests, CI, docs checks, and workarounds |
| `taskdeck-interface-map` | maintaining agent-facing maps for complex seams and context traps |

## Usage Pattern

1. Read `docs/STATUS.md`.
2. Read `CLAUDE.md` and `AGENTS.md`.
3. Read `autodoc/AGENT_INDEX.md` for cheap seam orientation.
4. Choose the smallest matching skill set.
5. Use `docs/agentic/QUESTION_PROTOCOL.md` for blockers and `docs/agentic/FAILURE_LEDGER.md` for unresolved failures.
6. Use `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` for shared high-autonomy batch discipline, substituting Claude entrypoints for Codex-specific preflight.
7. Use `docs/WORKTREE_AGENT_PROTOCOL.md` for Claude worktree isolation.
8. Report commands run, test results, docs updated, and unresolved risks.

