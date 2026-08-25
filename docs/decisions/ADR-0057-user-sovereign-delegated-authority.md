# ADR-0057: User-Sovereign Delegated Authority for Automation

- **Status**: Accepted (ratified by the maintainer in-session on 2026-08-24 — guided-walkthrough
  reply q-7 A, recorded on `#2011` — **with an explicit openness caveat**: the thesis remains open
  to adjustment as the product's workflow and its perceived trust factor evolve, and amending or
  superseding this ADR on real dogfooding/beta evidence is the intended path, not an exception.
  GP-06, ADR-0003, ADR-0017, and ADR-0056 remain fully operative until any implementation is
  separately gated; no auto-approval surface may be built without that separate gate.)
- **Date**: 2026-08-23 (accepted 2026-08-24)
- **Deciders**: Chris0Jeky (maintainer). Drafted from the maintainer's 2026-08
  decision-studio export (Q12 "user-selectable full autonomy", Q13 "auto-apply meaning is
  user-defined per project/board", Q32 "full agent runtime as the foundation, access stripped by
  the user", Q34 "granular scoped keys behind presets") via the 2026-08-23 realignment brief.
- **Related**: ADR-0003 (proposal-first automation), ADR-0017 (agent tool registry, review-first),
  ADR-0056 (direct human editing first-class; proposal loop governs non-human actors), GP-06,
  `docs/strategy/PRODUCT_DIRECTION.md` §3.

## Context

Taskdeck's shipped trust model is review-first: automation-originated board writes stop at a
proposal, a human explicitly approves, then explicitly executes; the MCP surface deliberately has
no approve/apply tool. That model is the product's credibility today, and ADR-0056 recently
clarified its boundary: the proposal loop governs **non-human actors**, never humans.

The maintainer's recorded direction is broader: users — not the product — should decide how much
authority agents receive, per board/project, up to and including full autonomy, with informed risk
disclosure. Several current documents state or imply that per-action human review is a permanent
invariant ("AI cannot silently change your system"). Under the recorded direction that phrasing
would become false. The gap between shipped truth and intended direction needs a decision record so
neither is silently rewritten.

The essential tension: **user sovereignty** (the user may grant autonomy) vs **accountability**
(no unattributable or unbounded state change) vs the existing promise that agents cannot approve
their own work.

## Decision (accepted 2026-08-24; see the openness caveat in the Status line)

### 1. The invariant generalises

> **Automation may act only within explicit, user-created delegated authority. Every action remains
> attributable, inspectable, bounded, and recoverable where practical. Manual review is the default
> policy, not the only policy.**

"No silent writes" is replaced by "**no unaccountable writes**": notification and review intensity
become policy; attribution and audit do not.

### 2. Separation of duties survives autonomy

Even at full autonomy there is no agent self-approval:

1. An agent or automation submits a proposed operation or change bundle.
2. Taskdeck's **policy engine** evaluates the request against an explicit user-created grant.
3. The policy engine — not the proposing agent — records the approval decision when the grant
   permits it (a *policy-authorised* decision, distinct from a human decision).
4. The execution service applies exactly the authorised bundle.
5. The audit record carries proposing principal, policy version, approving authority (human or
   named policy), operations, target preconditions, result, and receipt.

The MCP surface therefore still exposes **no agent-callable approve/apply tool**; delegated
execution is represented as delegated authority, never as a faked human approval act.

### 3. Authority is a hierarchy, not a toggle

Policy layers, narrower layers able to reduce authority automatically, increases always requiring
deliberate user action: (1) non-bypassable product safety ceiling; (2) user default; (3) workspace
policy; (4) project/board policy; (5) agent profile; (6) credential or session grant; (7)
operation classification; (8) one-time override with receipt and expiry.

### 4. Users see presets, not matrices

**Observe** (read + explain) · **Suggest** (proposals only; today's shipped default) · **Assist**
(reversible housekeeping directly; consequential changes proposed) · **Operate** (allow-listed
operations within targets, budgets, time limits) · **Autonomous/Expert** (broad user-defined
authority with full attribution, kill switch, explicit risk acknowledgement) · **Custom** (the raw
capability model). Every grant carries mandatory safeguards: automatic expiry, operation and target
allow-lists, budgets/max operation counts, immediate revocation, and simulation/dry-run where the
operation class supports it.

### 5. Sequencing (still binding after acceptance)

- Acceptance ratified the direction, not an implementation: nothing in this ADR is implemented
  until each implementation slice is separately gated behind its own issues. Product docs continue
  to describe the shipped review-first default until shipped behaviour actually changes.
- What the 2026-08-24 acceptance ratified: the invariant wording (§1), the preset set (§4), the
  operation-classification dimensions (substantive/mechanical, internal/external,
  reversible/compensatable/irreversible, evidence-backed/inferred, security/cost effect), and the
  audit schema (§2.5) — all under the openness caveat in the Status line: these provisions are
  expected to be re-derived from real dogfooding/beta evidence before any implementation gate
  opens, and amending them on that evidence is the intended path.
- Implementation lands earliest in the v0.3 "Accountable Agents" horizon, behind its own issues.

## Consequences

- The review-first machinery (proposals, revisions, preview==apply, receipts) is not legacy — it
  becomes the substrate every policy level runs through; the human-review path is one policy.
- Public copy must migrate from "AI cannot silently change your system" to "automation acts only
  under rules you chose; nothing is unaccountable" — only where shipped behaviour actually
  matches, which today it does not: the shipped default remains review-first everywhere.
- GP-06 carries a direction note referencing this ADR; its operative wording amends only when an
  implementation slice ships behind its own gate.
- The acceptance is deliberately revisable (Status-line caveat): superseding or amending this ADR
  on dogfooding/beta evidence — including retreating to permanent per-action human review — remains
  a legitimate outcome, recorded on `#2011`.
