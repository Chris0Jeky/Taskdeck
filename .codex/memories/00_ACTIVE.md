# Active Taskdeck Agent Context

Last updated: 2026-05-05

This file is the Codex active-gate pointer for Taskdeck. It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

## Current Authority

- Current shipped state: `docs/STATUS.md`
- Roadmap and sequencing: `docs/IMPLEMENTATION_MASTERPLAN.md`
- Stable invariants: `docs/GOLDEN_PRINCIPLES.md`
- Dependency-aware issue execution: `docs/ISSUE_EXECUTION_GUIDE.md`
- Testing operations: `docs/TESTING_GUIDE.md`
- MCP/tool usage: `docs/MCP_TOOLING_GUIDE.md`
- High-autonomy Codex workflow: `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`

## Current Focus Snapshot

As of the active docs, Taskdeck is in review-first AI and Paper backend gap delivery. The project thesis remains:

- capture should be near-zero friction
- automation-originated board writes must remain proposal-first and human-approved
- provenance, confidence, side-effect disclosure, telemetry guardrails, and exfiltration controls are distinct safety surfaces
- product legibility and trust take priority over broad new autonomy

## Required Read Order

1. `AGENTS.md`
2. `.codex/README.md`
3. `docs/STATUS.md`
4. `docs/IMPLEMENTATION_MASTERPLAN.md`
5. `docs/GOLDEN_PRINCIPLES.md`
6. `docs/ISSUE_EXECUTION_GUIDE.md` when selecting or executing issues
7. the matching `.codex/skills/*/SKILL.md`
8. feature, testing, MCP, or project-automation docs relevant to the task

For Claude Code, use `.claude/README.md` and `CLAUDE.md` in the same read sequence.

## Agent Coordination Rules

- Use spawned subagents without asking for extra permission when they are efficient or effective for safely parallelizable work.
- When implementation needs isolation, create real git worktrees with `scripts/git/New-CodexIssueWorktree.ps1`.
- Keep one coordinator responsible for issue selection, synthesis, docs rehydration, and final verification.
- Do not update canonical docs for local-only guidance unless behavior, roadmap state, testing expectations, or operator workflow changed.
