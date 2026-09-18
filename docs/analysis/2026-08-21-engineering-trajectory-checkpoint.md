# Engineering trajectory checkpoint — 2026-08-21

Last Reconciled: 2026-09-13

**Status: HISTORICAL / NON-AUTHORITATIVE.** This document preserves a dated engineering and
product-trajectory assessment. It is **not** a source of current shipped state, issue priority,
release readiness, or execution order. For current truth, use
[`strategy/PRODUCT_DIRECTION.md`](../strategy/PRODUCT_DIRECTION.md),
[`STATUS.md`](../STATUS.md), and [`REVIVAL_PLAN.md`](../REVIVAL_PLAN.md).

The original static analysis used source snapshot `f9d9b84c19514b3d886aa61eb076bf7622e51a26`.
GitHub delivery state was reconciled through live main
`e338566d0f3d1fc65004beb5a29c10778e6e61bc` and PR #1929 on 2026-08-21; the two
commits between the source snapshot and that live main were documentation-only. The 2026-09-13
reconciliation below inspected current main
`2cdc4525766101211fe04787e23bfb46b6aa4011` and refreshed repository counters. The companion
[`metrics.json`](2026-08-21-engineering-trajectory-metrics.json) retains the measured baseline and
reconciliation deltas in machine-readable form.

## Why this checkpoint should survive

The useful part of the original audit was not its temporary issue list. It captured a stage in
Taskdeck's evolution where engineering maturity, assurance depth and autonomous development
throughput had moved materially ahead of product validation. Since then the repository has
advanced by hundreds of merged PRs, multiple releases and a new Context Fabric direction. Keeping
this checkpoint makes those changes measurable and preserves the reasoning behind several durable
lessons:

- Taskdeck's strongest differentiator is an authority boundary around machine-proposed work.
- High agentic throughput is both an engineering advantage and a strategic multiplier of wrong
  scope.
- The test, CI, documentation and agent-governance estate is a first-class subsystem with real
  maintenance cost.
- Product proof must eventually outrank internal completeness when admitting work.
- Replacement cost, engineering quality and market value are distinct quantities.

## Baseline at the audit cutoff

The source-classification rules separated handwritten production, generated EF migrations, tests,
documentation and support/tooling surface. These numbers are a dated baseline, not current totals.

| Metric | 2026-08-21 baseline |
| --- | ---: |
| Repository files | 2,966 |
| Text lines | 664,597 |
| Handwritten production lines | 158,312 |
| Generated EF migration lines | 81,814 |
| Test lines | 243,246 |
| Documentation lines | 94,469 |
| Repository script lines | 28,865 |
| Test / handwritten-production ratio | 1.536 |
| C# / TypeScript / Vue files | 1,349 / 599 / 168 |
| Heuristic test files / declarations | 893 / 12,804 |
| Workflow files / heuristic jobs / steps | 33 / 128 / 323 |
| ADRs | 55 |
| Controllers / HTTP route attributes | 45 / 195 |
| Unique packages | 88 |

GitHub state at the same cutoff was 1,068 PRs, 992 merged PRs, 861 standalone issues,
143 open issues and no open PRs. Public counters were zero stars, forks and watchers. Those
figures describe the visible repository at that moment; they do not measure private users or
off-GitHub feedback.

## Original diagnosis

The audit's central diagnosis was:

> Taskdeck was already a team-scale, agent-native engineering system wrapped around a credible
> local-first beta. Its technical and assurance maturity materially exceeded its external product
> validation.

The strongest evidence was structural rather than rhetorical:

1. Automation-originated writes travelled through proposal, review, approval and explicit
   execution rather than mutating boards silently.
2. The MCP surface exposed proposal creation and inspection without giving an agent the ability to
   approve or apply its own proposal.
3. Claims-first identity, default-deny authorization, stable error contracts, redaction, egress
   controls and adversarial tests made the local/self-hosted threat model unusually mature for the
   stage.
4. Exact-head and untouched-artifact release proofs treated evidence identity as part of the
   product contract.
5. Agent worktrees, bounded authority, review ceilings and executable governance formed an
   engineering operating system rather than a collection of prompts.

The principal risks were product-validation lag, backlog and priority entropy, large policy-rich
hotspots, documentation truth-source overload, dual Paper/Legacy cost, CI economics and the ability
of autonomous throughput to build the wrong direction very efficiently.

## Reconciliation against current main

### Repository growth

The counters below are a second dated sample, refreshed on 2026-09-13. They are **not** direct
productivity measures: issues and PRs vary radically in scope, and the repository deliberately
records review residuals as issues.

| Metric | 2026-08-21 | 2026-09-13 | Delta |
| --- | ---: | ---: | ---: |
| Pull requests | 1,068 | 1,791 | +723 (+67.7%) |
| Merged pull requests | 992 | 1,679 | +687 (+69.3%) |
| Open pull requests | 0 | 11 | +11 |
| Standalone issues | 861 | 1,300 | +439 (+51.0%) |
| Open standalone issues | 143 | 278 | +135 (+94.4%) |
| GitHub repository size | 23,560 KiB | 38,798 KiB | +15,238 KiB (+64.7%) |

Current main was PR #3082 at `2cdc4525`. The scale of the delta validates the old observation that
Taskdeck can convert a direction into a very large implementation, verification and follow-up
surface in a short period. It also means every old absolute count and issue-state claim is now
historical.

### Findings that remain source-true

**The authority boundary remains structural.** Current
[`ProposalTools.cs`](../../backend/src/Taskdeck.Api/Mcp/ProposalTools.cs) still states that
`approve_proposal` is intentionally excluded under GP-06 and exposes only status/list/dismiss
operations. Current repo guidance still defines preview and Apply over the latest proposal revision,
requires explicit approval followed by explicit execution, and states that the MCP server has no
approve/apply tool. The longer-term delegated-authority direction does not replace this shipped
contract without a separately gated implementation.

**The transcript wedge advanced rather than invalidating the thesis.** Current
[`CLAUDE.md`](../../CLAUDE.md) identifies `LlmCaptureTriageExtractor` as the transcript-source
strategy, with failure returned as an outcome and deterministic degradation rather than capture
failure. The old recommendation to prove transcript-to-evidence-to-Apply value is therefore partly
delivered and has evolved into the broader Context Fabric direction.

**The agentic engineering operating system became stronger.** Current [`AGENTS.md`](../../AGENTS.md)
adds a low-context seam index, skill routing, worktree handoff contracts, project-operations
synchronization and explicit proving checks. This supports the original assessment that the agent
substrate is itself one of the repository's most mature artifacts.

**Product validation remains the limiting evidence, but the wording must change.** The old audit
said sustained dogfooding had not started. It later did start. The append-only
[`dogfooding/LOG.md`](../dogfooding/LOG.md) records the 2026-08-22 start and the maintainer's
2026-08-27 conclusion that v0.1.2 was not yet sufficient for his personal workflow. Dogfooding was
therefore re-scoped from a release gate to a standing tracker. This is more useful evidence than the
original absence: the core loop was live-verified, while voluntary daily-driver value remained
insufficient.

**Backlog and documentation economics remain material.** Open issues nearly doubled during the
reconciliation window, while the current product direction now spans a release ladder through
Context Fabric, hosted beta, voice, delegated authority, project dossiers, teams and GA. This does
not make the direction wrong; it strengthens the need to keep current blockers, review residuals,
future research and superseded strategy visibly separate.

### Findings that are no longer current

- The original Windows first-run recommendation is historical. #1877 closed on 2026-08-21, #1876
  closed on 2026-08-23 and #1242 closed on 2026-08-24; v0.1.2 shipped on 2026-08-25.
- v0.2.0 shipped on 2026-08-29, and v0.3.0-rc.1 shipped on 2026-08-30. The release ladder and current
  release gates now live in `strategy/PRODUCT_DIRECTION.md` and `REVIVAL_PLAN.md`.
- The old open-issue, open-PR, test-total, package, hotspot and maturity numbers are not current-state
  claims. Recompute them from a pinned tree before using them for planning.
- The statement "dogfooding has not started" was superseded by the mixed-outcome evidence described
  above.
- The audit's 1–5 maturity scores were comparative judgments at the cutoff, not repository KPIs or
  acceptance gates.

## Durable engineering and product lessons

### 1. Preserve the trust boundary while generalising the product

The current product destination is broader than the 2026-08-21 audit: an adaptive work operating
system with a Context Fabric. The old thesis still supplies a constraint on that expansion. New
capture modes, processors, agents and policies should terminate in accountable changes with
attribution, evidence, an authority decision, execution and a receipt.

### 2. Put an evidence gate above the code-quality gate

Taskdeck already has a strong implementation gate. The scarce gate is whether a proposed surface
has repeated user evidence and a maintenance budget. A useful admission question is:

> Which observed user behavior, current release gate or measured system limit makes this work more
> valuable than simplifying, finishing or using what already exists?

This is complementary to code review. It prevents high-quality execution from becoming evidence
that the underlying product decision was correct.

### 3. Treat the issue tracker as several ledgers

The tracker serves product planning, release control, residual-risk retention, review follow-up,
research and historical strategy. One undifferentiated count cannot represent health. At minimum,
retain distinct views for:

1. current product/release blockers;
2. security, data-loss and reliability debt;
3. bounded review residuals;
4. future research and provisional direction;
5. superseded or parked strategy.

### 4. Preserve dated measurements, but do not hand-maintain them as living truth

The original metrics are useful because they establish scale and attention allocation. Their useful
future form is a reproducible script or generated artifact pinned to a commit. Hand-copying totals
into living strategy docs creates reconciliation work and makes old numbers look current.

### 5. Replacement cost is not valuation

The audit estimated roughly 1,000–3,400 hours of direct human oversight under heavy agent leverage
and 8,000–18,000 conventional engineering hours to reproduce the then-current breadth and assurance.
Those ranges quantify engineering surface, not market value. Product value still depends on
retention, distribution, support economics and repeated outcomes.

## How to use this artifact

Use this checkpoint for:

- before/after trajectory comparisons;
- explaining why trust, provenance and human authority became core architecture;
- comparing repository growth with user evidence and simplification;
- reconstructing the engineering economics of the 2025-11 to 2026-08 build phase;
- deciding whether a new phase resolves or repeats earlier strategic risks.

Do **not** use it for:

- current issue or milestone state;
- current release readiness;
- current test totals or code-size claims;
- merge authority or execution order;
- deciding whether an old recommendation is still open.

## Method and limits

- Static counts came from the pinned uploaded snapshot and separated generated EF migrations from
  handwritten production where practical.
- Test declarations were heuristic and could double-count parameterized cases.
- Full .NET, frontend and Docker suites were not independently rerun during the original external
  audit; the audit instead reran available governance, syntax and release-contract checks and
  labelled that boundary.
- GitHub counters do not include private users or off-GitHub feedback.
- PR, issue and line counts are scale indicators, not measures of intelligence, quality or value.
- The original effort and replacement-cost ranges are sensitivity models, not time records or a
  business valuation.
