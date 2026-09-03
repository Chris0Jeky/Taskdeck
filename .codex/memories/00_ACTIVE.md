# Active Taskdeck Agent Context

Last updated: 2026-09-03

This file is the Codex active-gate pointer for Taskdeck. It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

**This file is a pointer, not a record.** It carries routing, standing constraints, and unpushed-work protection only. Shipped reality belongs in `docs/STATUS.md`; delivery history and roadmap sequencing belong in `docs/IMPLEMENTATION_MASTERPLAN.md`, the split that `.codex/README.md` and that file both declare; release state, milestone counts, PR status, and CI colour come from live GitHub. All three outrank anything written here. If you are about to add a dated delivery narrative to this file, put it in `docs/IMPLEMENTATION_MASTERPLAN.md` instead.

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

## v0.3.0 north star (maintainer-issued 2026-08-30, for the v0.3 lane)

> A stranger downloads Taskdeck, double-clicks it, and can trust it: whatever context they paste becomes evidence-linked proposals they explicitly approve; agents and MCP clients act only through scoped, attributed, review-first paths; nothing degrades silently and nothing changes without a receipt. Every open v0.3 milestone issue is a gap between that sentence and the shipped ZIP/container — close it with a tested, reviewed, merged slice; prefer finishing over adding; anything outside the milestone becomes an issue, not code.

## Direction pointer

Taskdeck ships as a free open beta: the local-first, review-first action-item engine (transcripts, notes and artefacts in, evidence-linked proposals out, human-approved board apply), with the write-gated MCP server as the developer-facing second act. Strategy spine is `docs/strategy/PRODUCT_DIRECTION.md`; sequencing is `docs/REVIVAL_PLAN.md`. ADR-0044 as extended by ADR-0046 and ADR-0051 is the governing authority; ADR-0051 permits a bounded autonomous queue of acceptance-ready tracked backlog (maximum four `Now`, eight `Next`). ADR-0057 delegated autonomy is direction only and is not buildable without its own gate. New product surface still requires plan or Accepted-ADR authority.

## Context Fabric pointer (ADR-0065, accepted under delegation; NOT part of the v0.3 lane)

The architecture for "speak, type, paste, or drop" is `docs/decisions/ADR-0065-context-fabric-capture-representation-processing.md`, mapped in `docs/architecture/CONTEXT_FABRIC.md`, tracked on CF-00 `#2254` (children `#2255`-`#2277`, label `context-fabric`, milestones v0.4 foundation / v0.5 payoff / v0.6 rules). The current build base is **PR `#2417`** (merge `eaa996fa2`, 2026-09-03), which reconciles capture text before a disposition stamp lands. It sits on PR `#2344` (merge `a6cc459c9`, CF-01 durable Capture: ID-preserving backfill, dual-write on, Inbox reads through `ICaptureStore`), which in turn sits on PR `#2280` and the reconciliation pass PR `#2320` (SourceAsset foundation, three capture state axes, Worker Protocol v1-alpha, IBlobStore reference semantics, canonical `CaptureIntakeService`). Build on `#2417`, not on `#2344`, `#2320` or `#2280`'s originals. Do not pull CF issues into the v0.3 lane; do not add `CaptureSource` values or request-type lane predicates anywhere; do not build CF-22 (delegated authority) without its own maintainer go. Review-first automation is unchanged.

## Standing constraints

- capture should be near-zero friction
- automation-originated board writes must remain proposal-first and human-approved
- provenance, confidence, side-effect disclosure, telemetry guardrails, and exfiltration controls are distinct safety surfaces
- product legibility and trust take priority over broad new autonomy
- keep every unchecked human action in `OUTSTANDING_TASKS.md` open unless its full condition is directly verified; never infer approval or subjective acceptance

## Unpushed work protection

No saved head currently holds unshipped work. Record one here the moment a slice is parked without a PR, and say what it contains and how to resume it.

- Retired, do not resume: `origin/issue-1940/provenance-shortcut@c9135fef3b64da5d6c578bd4d9c76fe4fdb7eb65`. The ref still exists and is not an ancestor of `main`, but the slice it held shipped as PR `#2323` (merge `221aa88c8`), recorded in `docs/STATUS.md`. Recreating a worktree from it would redo landed work. `#1940` stays open for the two MEDIUM residuals named on the issue, not for this branch.

## Start of session

1. Refresh Git, GitHub, ProjectV2, CI, review threads, milestones, releases, and worktrees. Live state outranks this file.
2. Read `docs/STATUS.md` for shipped reality before any restart memory, including this one.
3. Read `.agent-harness/tier.json` for authority. Do not infer it.

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
