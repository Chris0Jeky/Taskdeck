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

`.claude/skills/` is the canonical tree for the shared workflows above and adds the Claude-only helpers `issue-to-pr`, `pre-merge-gate`, and `docs-sweep`. `.codex/skills/` is the Codex adapter: same workflow names, Codex-shaped frontmatter and orientation order, per-skill `agents/` folders. The two trees are not byte mirrors (measured 2026-09-02) — port a shared-workflow change from the canonical tree to the adapter in the same PR. The review pipeline lives in neither; it is the global `review-and-ship` skill.

## Maintenance

- Keep `SKILL.md` concise and procedural.
- Move long examples or background into existing topical docs or `references/` only when needed.
- When a shared workflow changes, edit `.claude/skills/` first and port the change to the `.codex/skills/` adapter in the same PR.
- Keep tool capability parity aligned through `docs/agentic/AGENT_TOOL_PARITY.md`.
- Use `scripts/agent_hooks/render_failure_ledger.py` before promoting recurring failures into skills.
