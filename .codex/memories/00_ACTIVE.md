# Active Taskdeck Agent Context

Last updated: 2026-08-24

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

**Direction (2026-08-24 checkpoint; ADR-0044 extended by ADR-0046 and ADR-0051):** Taskdeck is being revived and shipped as a **free open beta** — the local-first, review-first action-item engine (transcripts/notes/artefacts in → evidence-linked proposals out → human-approved board apply), with the write-gated MCP server as the developer-facing second act. Active sequencing is `docs/REVIVAL_PLAN.md`: the ratified REVIVAL/GEN waves remain the product spine, while ADR-0051 permits a bounded autonomous queue of acceptance-ready tracked backlog (maximum four `Now`, eight `Next`) without another owner decision. The v0.1.2 Honest Windows Beta correction is the immediate ship gate; the 2026-06-13 archive pivot remains only the traction-checkpoint fallback, and new product surface still requires plan or Accepted-ADR authority. Merge authority is read live from `.agent-harness/tier.json`; CODEOWNERS is advisory routing rather than a blanket human gate. The project thesis remains unchanged:

**Delivery checkpoint (2026-08-24):** exact main is `55dbf6e140a0af9df8ef99acdc613fb4b1e5e03a`. The latest bounded wave merged PRs `#2058`, `#2063`, `#2065`, `#2066`, `#2067`, `#2068`, `#2069`, and `#2070`: capture metadata now survives review/recovery; the proxy contract runs in required container CI; Legacy diff identity and mismatched deep links cannot silently expose another proposal; toast remounts do not replay announcer content; modified key handlers cannot satisfy the button guard; and Review retains truthful board-scoped decision receipts. No v0.1.2 tag or release exists.

**Saved next-session heads (not shipped):**

- `origin/issue-1949/directive-attribute-tokenization` → `d89bd7cc383a79b4384134fa520b96629c0d3038`
- `origin/issue-1967/applied-read-only-detail` → `010021a7455d54395105e6e388c79a1b765a29ff`

Both are one clean commit over `55dbf6e14`, have no open PR, and need a refreshed exact-head review/hosted gate. The applied-detail slice additionally needs full frontend or Playwright/manual browser evidence.

**Exact continuation order:**

1. Refresh Git, GitHub, ProjectV2, CI, review threads, and worktrees; live state outranks this checkpoint.
2. Publish/review the saved `#1949` tokenizer head, then disposition the remaining runtime/a11y/dead-keystroke guard scope.
3. Independently review and publish the saved `#1967` applied-detail head; run its exact keyboard/read-only browser seam.
4. Run one final synthetic desktop capture → approved receipt → explicit Apply → exactly-one-card journey on the resulting main.
5. Reconcile open milestone issues `#1242`, `#1938`, `#1940`, `#1947`, `#1949`, `#1967`, `#1973`, `#1992`, and `#2004`. Never infer the human/owner gates: `#1242` acceptance, `#1973` archive contract, `#1992` 404-vs-405 contract, `#2004` chat contract, ≥10 dogfooding days (not before 2026-09-01), or maintainer release-deck acceptance.

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
