# Active Taskdeck Agent Context

Last updated: 2026-09-21

This file is the active-gate pointer for every implementation agent on Taskdeck: Codex reaches it through `AGENTS.md` and `.codex/README.md`, Claude Code through the `CLAUDE.md` orient list (it is not auto-loaded for Claude). It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

**This file is a pointer, not a record.** It carries routing, standing constraints, and unpushed-work protection only. Shipped reality belongs in `docs/STATUS.md`; delivery history and roadmap sequencing belong in `docs/IMPLEMENTATION_MASTERPLAN.md`, the split that `.codex/README.md` and that file both declare; release state, milestone counts, PR status, and CI colour come from live GitHub. All three outrank anything written here. If you are about to add a dated delivery narrative to this file, put it in `docs/IMPLEMENTATION_MASTERPLAN.md` instead.

## Current Authority

- Tier and push/merge authority: `.agent-harness/tier.json` (re-read live; do not infer authority from this summary)
- Current shipped state: `docs/STATUS.md`
- Active release/wave sequencing: `docs/REVIVAL_PLAN.md`
- Current repository programme brief: `docs/analysis/2026-09-21-repository-direction-and-v0.3-programme.md`
- Live v0.3 gate: `docs/releases/V0_3_0_READINESS.md`
- Broader delivery/planning record: `docs/IMPLEMENTATION_MASTERPLAN.md`
- Stable invariants: `docs/GOLDEN_PRINCIPLES.md`
- Dependency-aware issue execution: `docs/ISSUE_EXECUTION_GUIDE.md`
- Testing operations: `docs/TESTING_GUIDE.md`
- MCP/tool usage: `docs/MCP_TOOLING_GUIDE.md`
- High-autonomy Codex workflow: `docs/tooling/CODEX_AUTONOMY_RUNBOOK.md`
- Fast agent seam map: `autodoc/AGENT_INDEX.md`
- Grok routing: `.grok/README.md` (Grok loads `.claude/skills/`; no third skill tree)
- Agentic protocols: `docs/agentic/QUESTION_PROTOCOL.md`, `docs/agentic/FAILURE_LEDGER.md`, `docs/agentic/GUIDE_UPDATE_PROTOCOL.md`, `docs/agentic/SKILL_REGISTRY.md`, `docs/agentic/AGENT_TOOL_PARITY.md`

## v0.3.0 north star (maintainer-issued 2026-08-30, for the v0.3 lane)

> A stranger downloads Taskdeck, double-clicks it, and can trust it: whatever context they paste becomes evidence-linked proposals they explicitly approve; agents and MCP clients act only through scoped, attributed, review-first paths; nothing degrades silently and nothing changes without a receipt. Every open v0.3 milestone issue is a gap between that sentence and the shipped ZIP/container — close it with a tested, reviewed, merged slice; prefer finishing over adding; anything outside the milestone becomes an issue, not code.

## Direction pointer

Taskdeck ships as a free open beta: the local-first, review-first action-item engine (transcripts, notes and artefacts in, evidence-linked proposals out, human-approved board apply), with the write-gated MCP server as the developer-facing second act. Strategy spine is `docs/strategy/PRODUCT_DIRECTION.md`; sequencing is `docs/REVIVAL_PLAN.md`. ADR-0044 as extended by ADR-0046 and ADR-0051 is the governing authority; ADR-0051 permits a bounded autonomous queue of acceptance-ready tracked backlog (maximum four `Now`, eight `Next`). ADR-0057 delegated autonomy is direction only and is not buildable without its own gate. New product surface still requires plan or Accepted-ADR authority.

## Context Fabric pointer (ADR-0065, accepted under delegation; NOT part of the v0.3 lane)

The architecture for "speak, type, paste, or drop" is `docs/decisions/ADR-0065-context-fabric-capture-representation-processing.md`, mapped in `docs/architecture/CONTEXT_FABRIC.md`, tracked on CF-00 `#2254` (children `#2255`-`#2277`, label `context-fabric`, milestones v0.4 foundation / v0.5 payoff / v0.6 rules). The CF-01 foundation chain (`#2280` scaffold, `#2320` reconciliation, `#2344` durable Capture, `#2417` capture-text reconciliation before disposition stamps) is fully merged; **build on current `main`**, not on a named historical PR head. That ordering is delivery history and lives in `docs/IMPLEMENTATION_MASTERPLAN.md`. The immediate v0.4 foundation subset, open as of 2026-09-10, is CF-01b `#2345`, CF-02 `#2256`, CF-03 `#2257`, CF-04 `#2258`, CF-05 `#2259`, CF-06 `#2260` (slices landing), CF-07 `#2261` and CF-23 `#2276`. That is a subset, not an inventory: CF-08 through CF-22 and CF-24A/CF-24B are also open, across v0.4, v0.5 and v0.6 - some rows span milestones. `#2254` and `docs/architecture/CONTEXT_FABRIC.md` carry the full map. Do not pull CF issues into the v0.3 lane; do not add `CaptureSource` values or request-type lane predicates anywhere; do not build CF-22 (delegated authority) without its own maintainer go. Review-first automation is unchanged.

## Standing constraints

- capture should be near-zero friction
- automation-originated board writes must remain proposal-first and human-approved
- provenance, confidence, side-effect disclosure, telemetry guardrails, and exfiltration controls are distinct safety surfaces
- product legibility and trust take priority over broad new autonomy
- keep every unchecked human action in `OUTSTANDING_TASKS.md` open unless its full condition is directly verified; never infer approval or subjective acceptance

## Unpushed work protection

Saved work without a PR must remain discoverable even after its worktree is removed. Refresh the
issue and remote ref before resuming; this pointer does not claim current CI or delivery.

- Parked by the maintainer's hard-work selection preference: #2940,
  `origin/issue-2940/demo-archive-history@f8d8460dbac6c311847e454964dd279a5ed828a4`.
  Contains a demo archive-history UI/test slice; source is pushed, worktree removed, broader checks
  incomplete. Do not resume it as a convenience task. Read
  [the issue receipt](https://github.com/Chris0Jeky/Taskdeck/issues/2940#issuecomment-5626011852)
  before any deliberate resumption.

- Retired, check before resuming: `origin/issue-2198/batch-approve-focus@9a2d723771b8fda0a04bb22e87056a5ee5b63289`. Not an ancestor of `main`. `#2198` closed 2026-09-04 on PR `#2534` (merge `d11bd4ada`, branch `issue-2198/batch-approve-focus-v2`). The alpha lane's 2026-09-03 release note says the original branch preserves a verified composable-only settle-outcome slice that never opened a PR; whether that slice is still wanted after `#2534` is unrecorded, so read `#2198` first and do not recreate a worktree from it blindly.

- Retired, do not resume: `origin/issue-1940/provenance-shortcut@c9135fef3b64da5d6c578bd4d9c76fe4fdb7eb65`. The ref still exists and is not an ancestor of `main`, but the slice it held shipped as PR `#2323` (merge `221aa88c8`), recorded in `docs/STATUS.md`. Recreating a worktree from it would redo landed work. `#1940` stays open for the two MEDIUM residuals named on the issue, not for this branch.

## Current programme routing (2026-09-21)

- Taskdeck is in v0.3 release convergence. Read the current programme brief and live readiness view
  before selecting work. The old alpha/beta lane names are historical coordination aids, not current
  ownership authority.
- Live GitHub owns milestone counts, PR state, CI, review and branch ancestry. An open PR inventory is
  not an admission list. Select work through the active plan, accepted ADRs, issue dependencies and
  current ownership evidence.
- Claim before writing: search both `[Claude lane claim v2]` and `[Codex lane claim v2]`, open PRs,
  branches and recent issue comments. An existing current claim or open PR outranks a new claim.
- One writer per canonical doc. The implementation owner writes the bounded shipped-truth update for
  its merged slice; programme-wide reconciliation and the release readiness view remain
  coordinator-owned. Do not edit a canonical document already touched by an open PR without an
  explicit integration order.
- Stacked PRs are dependency graphs. Verify the actual base and parent head through GitHub. Merge the
  parent first, refresh/retarget the child only after the parent lands, and rerun exact-head evidence.
  Never infer that a mergeable stacked child is independently ready for `main`.
- Any base refresh, merge from `main`, stack collapse, review repair or generated-file change creates
  a new exact head. Earlier green CI and review are historical until the current head is qualified.
- Current release-control state: merged foundations `#3156`/`#3167`; open parent `#3295` before
  stacked child `#3296`; CI-17 inventory `#3297` before implementation `#3170`; post-merge Windows
  timeout reconciliation after `#3162`; remaining `#2335` acceptance after merged `#2838`; corrected
  runner work after FIX-FIRST recovery PR `#3261`.
- Control-plane work still follows ADR-0066 and `OUTSTANDING_TASKS.md` section J. Green is evidence,
  not merge authority. The September directives are recorded per named wave; they do not silently
  settle the standing rule for every future control-plane PR.
- Read the review connector's live result. A usage-limit notice is informational, not a finding. Use
  the repository's documented fresh-context fallback when the connector cannot review.

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
