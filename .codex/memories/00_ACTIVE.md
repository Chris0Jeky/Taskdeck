# Active Taskdeck Agent Context

Last updated: 2026-08-28

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

**Current continuation (2026-08-29, post-promotion):** `v0.2.0` shipped — annotated tag peels to `48c05e1dcd3cc8d3072ba60f0e89258d25ac4422`, public Release published 2026-08-29T04:40:53Z, milestone v0.2 closed 0/15 (ship record in `docs/STATUS.md`). `integration/v0.3.0` (last lane head `ad5325419`) was merged into `main` with a merge commit via PR `#2196`, so every v0.3-lane feature is on `main` and none is in a published release; the next release is the v0.3 RC (target 2026-09-04), which needs its own deck. ADR-0060/0062 are Accepted and ADR-0061 Accepted as direction only (`#2189`). Parked PR `#2165` still targets `integration/v0.3.0` — retarget it to `main` before resuming. Refresh all of these live before acting; this is a restart pointer, not current proof.

**Previous continuation (2026-08-28 closeout — superseded by the line above; kept as the record):** `integration/v0.3.0` is saved after last product merge `778a9f8470197cf2f92231813eac743e804f03e9`; `main` remains `927236bd0304e9dfae59a7116394e4fcb7b0ec07`. No remote `v0.2.0` tag or GitHub release exists. Milestone `v0.3 — Open Beta + Accountable Agents` has 20 open and 1 closed issue. Parked PR `#2165` is the only open PR targeting integration. Refresh all of these live before acting; this is a restart pointer, not current proof.

**Continuation order:**

1. Refresh Git, GitHub, ProjectV2, CI, review threads, milestones, releases, and worktrees; live state outranks this checkpoint.
2. The saved `#1940` provenance-shortcut slice is `origin/issue-1940/provenance-shortcut@c9135fef3b64da5d6c578bd4d9c76fe4fdb7eb65`, with no PR. It is deliberately incomplete and test-only. Recreate an isolated worktree from that remote head, install locked frontend dependencies, prove the new regression red, then wire the smallest controlled `ReviewProvenance` → `ReviewMain` → `PaperReviewView` seam while preserving key guards, manual activation, independent disclosures, proposal reset, and decision/receipt behavior.
3. If a fresh lane is selected instead, `#1309` has one known LOW archive-validator slice: reject missing `result.protocolVersion` and a missing terminal newline. Before implementation, pin whether the terminal policy permits CRLF or requires LF-only; do not infer it.
4. Do not manufacture work around gates: `#1307` still needs the owner to choose batch-execute atomic-versus-partial failure semantics; `#1992` still needs case/encoded-slash parity decisions; `#1949` has no acceptance-ready next contract; `#2090` remains Blocked behind parked PR `#2165`, and `.worktrees/codex-2090-focus-following-recovery` is preserved.
5. Final integration → `main` remains unauthorized until a v0.2 release exists and the v0.3 completion audit passes.
6. Keep every unchecked human action in `OUTSTANDING_TASKS.md` open unless its full condition is directly verified; never infer approval or subjective acceptance.

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
