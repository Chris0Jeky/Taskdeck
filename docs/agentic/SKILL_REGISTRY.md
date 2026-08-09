# Agent Skill Registry

Use one primary skill per task and at most one support skill. Skills are for targeted workflows, not a reason to load the whole repo.

## Codex Skills

| Skill | Trigger |
| --- | --- |
| `taskdeck-repo-onramp` | Broad orientation, unfamiliar areas, current-reality reconciliation. |
| `taskdeck-backend-slice` | .NET API, application, domain, infrastructure, worker, auth, provider runtime, import/export, notification, archive, or persistence behavior. |
| `taskdeck-frontend-workspace-slice` | Vue shell, workspace, route, help-state, store, keyboard, and novice-legibility work outside core capture-review semantics. |
| `taskdeck-capture-review-loop` | Inbox, capture, triage, automation proposals, proposal summaries, approve/reject/execute, provenance, or board handoff semantics. |
| `taskdeck-demo-regression` | Seeded demo state, Playwright proof, screenshots, or stakeholder walkthrough evidence. |
| `taskdeck-issue-batch-orchestrator` | Many issues, issue selection, worktree/subagent coordination, PR/review/CI batches, or docs/project reconciliation. |
| `taskdeck-worktree-issue-worker` | One assigned issue in an isolated worktree. |
| `taskdeck-pr-review-loop` | Taskdeck-specific review lenses for a PR; the review pipeline itself is the global `review-and-ship` skill. |
| `taskdeck-ci-conflict-recovery` | Failing CI, stale branches, merge conflicts, blocked PRs, or bot failure comments. |
| `taskdeck-verification-doc-sync` | Final checks, doc-sync decisions, and handoff summary. |
| `taskdeck-question-batch` | Ambiguous tasks where the agent must decide whether to ask, assume, or proceed. |
| `taskdeck-failure-capture` | Failed tools, tests, CI, docs checks, MCP calls, workarounds, or recurring friction. |
| `taskdeck-interface-map` | Adding, splitting, refactoring, or documenting complex agent-facing seams. |

## Claude Skills

Claude skills under `.claude/skills/` mirror the Taskdeck workflows above and add Claude-oriented helpers such as `issue-to-pr`, `pre-merge-gate`, and `docs-sweep`. The review pipeline is not mirrored here — it lives in the global `review-and-ship` skill.

## Maintenance

- Keep `SKILL.md` concise and procedural.
- Move long examples or background into existing topical docs or `references/` only when needed.
- Keep Codex and Claude mirrors aligned when a workflow is shared.
- Keep tool capability parity aligned through `docs/agentic/AGENT_TOOL_PARITY.md`.
- Use `scripts/agent_hooks/render_failure_ledger.py` before promoting recurring failures into skills.
