# Taskdeck Codex Skills

Repo-local Codex skills live here and supplement `AGENTS.md`. Use the smallest skill set that matches the task; do not load every skill by default.

## High-Autonomy Skills

| Skill | Use it for | Pair with |
| --- | --- | --- |
| `taskdeck-repo-onramp` | broad orientation, unfamiliar areas, reconciling current Taskdeck reality | `.codex/memories/00_ACTIVE.md`, `docs/STATUS.md` |
| `taskdeck-issue-batch-orchestrator` | many issues, worker coordination, PR/review/CI batches, project sync | `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md` |
| `taskdeck-worktree-issue-worker` | one assigned issue in an isolated worktree | `scripts/worktree_guard.ps1`, issue acceptance criteria |
| `taskdeck-pr-review-loop` | Taskdeck review lenses (pipeline: global `review-and-ship`) | PR diff and linked issue |
| `taskdeck-ci-conflict-recovery` | failing CI, stale branches, conflicts, blocked PRs | CI logs, `scripts/github/Inspect-TaskdeckPrs.ps1` |
| `taskdeck-verification-doc-sync` | final checks, docs sync decisions, handoff summary | `docs/TESTING_GUIDE.md`, active docs |

## Agentic Operating Skills

| Skill | Use it for | Pair with |
| --- | --- | --- |
| `taskdeck-question-batch` | deciding whether to ask, assume, or proceed on ambiguous work | `docs/agentic/QUESTION_PROTOCOL.md` |
| `taskdeck-failure-capture` | classifying failed tools, tests, CI, docs checks, and workarounds | `docs/agentic/FAILURE_LEDGER.md` |
| `taskdeck-interface-map` | maintaining agent-facing maps for complex seams and context traps | `autodoc/AGENT_INDEX.md` |

## Domain Skills

| Skill | Use it for |
| --- | --- |
| `taskdeck-backend-slice` | .NET API/application/infrastructure/worker/auth changes |
| `taskdeck-frontend-workspace-slice` | Vue workspace, routes, shell, stores, and UI legibility work |
| `taskdeck-capture-review-loop` | capture, inbox, proposals, execute flow, provenance, and board handoff semantics |
| `taskdeck-demo-regression` | seeded demo state, Playwright proof, screenshots, stakeholder walkthrough validation |

## Usage Pattern

1. Read `docs/STATUS.md`.
2. Read `AGENTS.md` and `.codex/memories/00_ACTIVE.md`.
3. Choose the skill matching the request.
4. Read that skill's `SKILL.md`.
5. Pair it with active docs, `autodoc/AGENT_INDEX.md`, and the relevant feature or testing guide.
6. Use `docs/agentic/QUESTION_PROTOCOL.md` for blockers and `docs/agentic/FAILURE_LEDGER.md` for unresolved failures.
7. Keep changes narrow, verify locally, and update canonical docs only when project reality changed.

