# Active Taskdeck Agent Context

Last updated: 2026-07-13

This file is the Codex active-gate pointer for Taskdeck. It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

## Current Authority

- Current shipped state: `docs/STATUS.md`
- Roadmap and sequencing: `docs/IMPLEMENTATION_MASTERPLAN.md`
- Stable invariants: `docs/GOLDEN_PRINCIPLES.md`
- Dependency-aware issue execution: `docs/ISSUE_EXECUTION_GUIDE.md`
- Testing operations: `docs/TESTING_GUIDE.md`
- MCP/tool usage: `docs/MCP_TOOLING_GUIDE.md`
- High-autonomy Codex workflow: `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`
- Fast agent seam map: `autodoc/AGENT_INDEX.md`
- Agentic protocols: `docs/agentic/QUESTION_PROTOCOL.md`, `docs/agentic/FAILURE_LEDGER.md`, `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, `docs/agentic/SKILL_REGISTRY.md`, `docs/agentic/AGENT_TOOL_PARITY.md`

## Current Focus Snapshot

**Direction (2026-07-10 revival pivot, ADR-0044; extended 2026-07-13 by ADR-0046):** Taskdeck is being revived and shipped as a **free open beta** — the local-first, review-first action-item engine (transcripts/notes/artefacts in → evidence-linked proposals out → human-approved board apply), with the write-gated MCP server as the developer wedge. Active sequencing is `docs/REVIVAL_PLAN.md` (waves REVIVAL-00..14 `#1297`–`#1311` + Phase 4 GEN-00..12 `#1315`–`#1327`); the v0.1 ship gate has top priority. The 2026-06-13 archive pivot is **superseded** (it survives only as the checkpoint fallback). ADR-0046 (Proposed) adds generalist reach in the single app — artefact intake beyond transcripts, project dossiers, mode-scoped legibility; the twin generalist app is deferred behind the GEN-12 (`#1326`) evidence gate. Work not on the ratified wave list is not taken. The project thesis remains unchanged:

- capture should be near-zero friction
- automation-originated board writes must remain proposal-first and human-approved
- provenance, confidence, side-effect disclosure, telemetry guardrails, and exfiltration controls are distinct safety surfaces
- product legibility and trust take priority over broad new autonomy

## Required Read Order

1. `docs/STATUS.md`
2. `AGENTS.md`
3. `.codex/README.md`
4. `docs/IMPLEMENTATION_MASTERPLAN.md`
5. `docs/GOLDEN_PRINCIPLES.md`
6. `docs/ISSUE_EXECUTION_GUIDE.md` when selecting or executing issues
7. `autodoc/AGENT_INDEX.md` for cheap seam orientation
8. the matching `.codex/skills/*/SKILL.md`
9. feature, testing, MCP, agentic, or project-automation docs relevant to the task

For Claude Code, read `docs/STATUS.md` first, then use `.claude/README.md` and `CLAUDE.md` for Claude-specific routing.

## Review Policy

Every review (self-review, adversarial, subagent) must: post findings on the PR as a comment, fix everything at every severity, check and address ALL existing PR comments (human, bot, previous reviews), and seed GitHub issues for out-of-scope findings. No "non-blocking" dismissals. See `AGENTS.md` Review Policy for full rules.

## Agent Coordination Rules

- Use spawned subagents without asking for extra permission when they are efficient or effective for safely parallelizable work.
- When implementation needs isolation, create real git worktrees with `scripts/git/New-CodexIssueWorktree.ps1`.
- Keep one coordinator responsible for issue selection, synthesis, docs rehydration, and final verification.
- Do not update canonical docs for local-only guidance unless behavior, roadmap state, testing expectations, or operator workflow changed.
- Use `docs/agentic/QUESTION_PROTOCOL.md` to batch true blockers and proceed with explicit assumptions for reversible choices.
- Use `docs/agentic/FAILURE_LEDGER.md` for unresolved failures or workarounds that future agents should not rediscover.
- Use `docs/agentic/AGENT_TOOL_PARITY.md` to keep Claude and Codex using equivalent capabilities through their runtime-native tools.
