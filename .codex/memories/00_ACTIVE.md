# Active Taskdeck Agent Context

Last updated: 2026-08-26

This file is the Codex active-gate pointer for Taskdeck. It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

## Current Authority

- Tier and push/merge authority: `.agent-harness/tier.json` (re-read live; do not infer authority from this summary)
- Current shipped state: `docs/STATUS.md`
- Active release/wave sequencing: `docs/REVIVAL_PLAN.md`
- Broader delivery/planning record: `docs/IMPLEMENTATION_MASTERPLAN.md`
- Stable invariants: `docs/GOLDEN_PRINCIPLES.md`
- Dependency-aware issue execution: `docs/ISSUE_EXECUTION_GUIDE.md`
- Testing operations: `docs/TESTING_GUIDE.md`
- MCP/tool usage: `docs/MCP_TOOLING_GUIDE.md`
- High-autonomy Codex workflow: `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`
- Fast agent seam map: `autodoc/AGENT_INDEX.md`
- Agentic protocols: `docs/agentic/QUESTION_PROTOCOL.md`, `docs/agentic/FAILURE_LEDGER.md`, `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, `docs/agentic/SKILL_REGISTRY.md`, `docs/agentic/AGENT_TOOL_PARITY.md`

## Current Focus Snapshot

**Direction (2026-08-26 checkpoint; ADR-0044 extended by ADR-0046 and ADR-0051):** Taskdeck is being revived and shipped as a **free open beta** — the local-first, review-first action-item engine (transcripts/notes/artefacts in → evidence-linked proposals out → human-approved board apply), with the write-gated MCP server as the developer-facing second act. Active sequencing is `docs/REVIVAL_PLAN.md`: the ratified REVIVAL/GEN waves remain the product spine, while ADR-0051 permits a bounded autonomous queue of acceptance-ready tracked backlog (maximum four `Now`, eight `Next`) without another owner decision. v0.1.2 shipped on 2026-08-25; its bounded follow-up is tracked through `#1947` and proposed ADR-0060 through ADR-0062. The 2026-06-13 archive pivot remains only the traction-checkpoint fallback, and new product surface still requires plan or Accepted-ADR authority. Merge authority is read live from `.agent-harness/tier.json`; CODEOWNERS is advisory routing rather than a blanket human gate. The project thesis remains unchanged:

**Historical delivery checkpoint (2026-08-24):** exact main was `f45a1fbb021d5bc2cbf8a94c42b52c3818fe15a0`. A six-PR wave merged after the earlier `55dbf6e14` checkpoint:

- `#2072` (`#1949`) — the dead-affordance guard tokenizes opening-tag attributes, so only real Vue event directives count as handler evidence.
- `#2037` (`#1938`) — persistent receipt controls localized in en/it/es; error toasts assertive, non-error toasts polite, in both skins.
- `#2074` (`#2004`) — an unbound Automation Chat session says it cannot act instead of returning prose that reads like completed work. **Honesty half only.**
- `#2077` (`#1940`) — `GET /api/workspace/collaboration` plus the Paper Review All/Mine gate built on it.
- `#2076` (`#1973`) — archived-board capture and decision history disclosed and reachable read-only. **Closed `#1973`.**
- `#2073` (`#1967`) — applied proposals render as read-only decision records, reachable from RECENTLY APPLIED and by hash deep link.

Both formerly saved heads are shipped: `origin/issue-1949/directive-attribute-tokenization` (`d89bd7cc3`) became `#2072`, and `origin/issue-1967/applied-read-only-detail` (`010021a745`) became `#2073`. Do not publish either again. v0.1.2 subsequently shipped; refresh live state before using this historical list.

**Current continuation:** exact `origin/main` at the 2026-08-26 intake was `ceeb27a6cc11e263d7b13be30579f845468ec8b1`. v0.1.2 is public, ADR-0059 is Accepted, and the dogfooding follow-up is documented in `docs/analysis/2026-08-26-v012-dogfooding-reconciliation.md`. Refresh open PRs and ProjectV2 before acting.

**Continuation order:**

1. Refresh Git, GitHub, ProjectV2, CI, review threads, milestones, releases, and worktrees; live state outranks this checkpoint.
2. Use standing tracker `#1947` and `docs/analysis/2026-08-26-v012-dogfooding-reconciliation.md` for the bounded follow-up wave. Do not create a replacement tracker.
3. Treat ADR-0060, ADR-0061, and ADR-0062 as Proposed. No dependent schema work, including `#2087`, starts until the relevant decision is accepted.
4. The next independent implementation candidate is `#2083`, the segmented-control interaction-state contrast defect. Do not start it during a docs/intake pass.
5. Keep the grouped maintainer decision in `OUTSTANDING_TASKS.md` open. The dogfooding/traction checkpoint remains separately governed by ADR-0044 and the live revival plan.

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

See the pointer in `AGENTS.md` — review doctrine lives in the global laws (`~/.claude/CLAUDE.md` laws 2 and 11) and the `review-and-ship` skill, not in this layer.

## Agent Coordination Rules

- Use spawned subagents without asking for extra permission when they are efficient or effective for safely parallelizable work.
- When implementation needs isolation, create real git worktrees with `scripts/git/New-CodexIssueWorktree.ps1`.
- Keep one coordinator responsible for issue selection, synthesis, docs rehydration, and final verification.
- Do not update canonical docs for local-only guidance unless behavior, roadmap state, testing expectations, or operator workflow changed.
- Use `docs/agentic/QUESTION_PROTOCOL.md` to batch true blockers and proceed with explicit assumptions for reversible choices.
- Use `docs/agentic/FAILURE_LEDGER.md` for unresolved failures or workarounds that future agents should not rediscover.
- Use `docs/agentic/AGENT_TOOL_PARITY.md` to keep Claude and Codex using equivalent capabilities through their runtime-native tools.
