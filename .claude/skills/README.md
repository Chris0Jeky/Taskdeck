# Taskdeck Claude Skills

Claude Code skills live here and are the canonical Taskdeck workflow skills. `.codex/skills/` is the
Codex adapter (different frontmatter and orientation order, plus per-skill `agents/` folders); when a
shared workflow changes, update this tree first and port the change there.

Skills trigger by `description`; heavy workflow skills carry `disable-model-invocation: true` so they run
only when you invoke them (`/name`). Region skills carry `paths:` so they activate only for matching files.

## High-Autonomy Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-issue-batch-orchestrator` | many issues, worker/worktree coordination, PR/review/CI batches, docs reconciliation |
| `taskdeck-worktree-issue-worker` | one issue in an isolated worktree |
| `issue-to-pr` | one issue → worktree → change → tests → ready PR (`/issue-to-pr <number>`) |
| `pre-merge-gate` | Taskdeck-local evidence packet for `review-and-ship` (`/pre-merge-gate [PR]`) |
| `taskdeck-pr-review-loop` | Taskdeck review lenses (pipeline: global `review-and-ship`) |
| `taskdeck-ci-conflict-recovery` | failing CI, stale branches, merge conflicts, blocked PRs |
| `docs-sweep` | live-doc refresh after merges (`/docs-sweep [#N-#M]`) |

## Domain Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-repo-onramp` | broad orientation and current-reality reconciliation |
| `taskdeck-backend-slice` | backend/API/application/infrastructure/worker/auth behavior (`backend/**`) |
| `taskdeck-frontend-workspace-slice` | Vue workspace, routes, shell, stores, and UI polish (`frontend/**`) |
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

1. Orient in the order root `CLAUDE.md` gives: `autodoc/AGENT_INDEX.md` → the relevant section of
   `docs/STATUS.md` → `OUTSTANDING_TASKS.md`. Root `CLAUDE.md` and region rules auto-load; `AGENTS.md`
   is Codex-facing and is not auto-loaded.
2. Choose the smallest matching skill set.
3. Use `docs/agentic/QUESTION_PROTOCOL.md` for blockers and `docs/agentic/FAILURE_LEDGER.md` for
   unresolved failures.
4. Use `docs/WORKTREE_AGENT_PROTOCOL.md` for worktree isolation and the helper handoff contract.
5. Report commands run, test results, docs updated, unresolved risks, and open `OUTSTANDING_TASKS.md`
   items.
