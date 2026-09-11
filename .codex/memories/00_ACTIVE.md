# Active Taskdeck Agent Context

Last updated: 2026-09-11

This file is the active-gate pointer for every implementation agent on Taskdeck: Codex reaches it through `AGENTS.md` and `.codex/README.md`, Claude Code through the `CLAUDE.md` orient list (it is not auto-loaded for Claude). It intentionally summarizes routing only; the canonical state remains in `docs/STATUS.md`.

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

## Lane coordination (2026-09-04)

- Two implementation lanes run concurrently: `alpha-product-trust` (human work loop: Capture/Inbox, proposals, Review, Board/Paper/Legacy, a11y, product semantics) and `beta-platform-integrity` (runtime, security, delivery, CI, harness). An issue belongs to the lane that owns its primary acceptance outcome, not to whichever layer its files sit in. A programme coordinator session owns issue topology, milestones, Project state, this file, `autodoc/AGENT_INDEX.md` and `docs/releases/V0_3_0_READINESS.md`; it does not implement in a path a lane has leased, and it merges only its own coordination PRs under the ordinary tier gate (`.agent-harness/tier.json` is the authority, not this line).
- Claim before writing: post `[Claude lane claim v2]` on the issue (lane, base SHA, owned paths, shared-path leases, parallel-safe work, status) and `[Claude lane release v2]` with the exact head and result when done. `[Codex lane claim v2]` / `[Codex lane release v2]` are the same protocol with the same fields; search for both forms before claiming, because an existing open PR plus a current claim in either form outranks a new claim. A stale claim is one with no release and no branch activity; the coordinator reconciles it, not the other lane.
- One writer per canonical doc: the lane that merges a slice writes its own bounded `docs/STATUS.md` block and `OUTSTANDING_TASKS.md` tick; cross-lane reconciliation blocks and the readiness view are the coordinator's. Never edit a canonical doc that an open PR already edits without agreeing the order first.
- Control-plane PRs (`.github/workflows/**`, `ci/**`, `scripts/ci/**`, runner or branch-protection paths, and the `ci/policy.v1.json` control paths) merge only after the maintainer's own review plus one fresh-context review (ADR-0066 amendment 2026-09-03). Green is not authority. **SC-10 is closed** (all twelve PRs in that queue merged 2026-09-06), but the 2026-09-06 walkthrough ruling q-1 = A delegated **those twelve named PRs only**; it did not lift the amendment for a new control-plane PR. Practice has diverged three times, on 2026-09-08 (`#2772`, `#2787`) and again on 2026-09-10 (the CI-continuation train), and the divergence is an open human decision: `OUTSTANDING_TASKS.md` J.1, J.2 and J.3. Read J.3 before opening or merging a control-plane PR. A control-plane PR that is parked is recorded on J.2, which is where SC-10's role went when it closed.
- Codex review credits: SC-9 closed 2026-09-06 and the connector was reviewing normally when last observed (2026-09-10). **Read the connector's own comment on your PR rather than this line** - like milestone counts and CI colour, credit state is live GitHub, and this file's preamble says live GitHub outranks it. Standing rules either way: global law 2g, so a clean Codex outcome is the whole review gate for documentation-only or very-low-risk work and other work still gets one fresh-context independent review; and if a usage-limit notice does appear, it is informational, not a finding, and the gate falls back to one fresh-context review per PR. Do not spend a reviewer subagent on the assumption that the connector is unavailable without looking.
- Stacked PRs: a PR whose base is another PR's branch merges into that branch, not `main`. Merge the parent first, always; only after the parent has actually merged, re-target the child with `gh pr edit N --base main`, then confirm the new base via the API before merging it. Never re-target a child whose parent is still open, because that pulls the parent's unmerged commits into the child. Never `--delete-branch` a stacked base PR.

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
