# GitHub Label Taxonomy

Last Updated: 2026-08-23

This file is the canonical description source for repository issue labels. Keep GitHub label
metadata aligned with this file (`gh label edit <name> --description ... --color ...` works; the
older note that tooling could not update descriptions is obsolete). Labels classify; scheduling
truth lives in the ProjectV2 **Status** field and GitHub **milestones**
(see `docs/ISSUE_EXECUTION_GUIDE.md`).

## Type labels

- `bug` — Defect where current behavior is incorrect, unstable, or regressed.
- `feature` — User-facing functionality delivery slices (new capabilities or clear product-surface enhancements).
- `security` — Authentication, authorization, data protection, abuse prevention, and compliance-related changes.
- `hardening` — Reliability, safety, operability, and production-readiness improvements.
- `testing` — Test coverage, harnesses, regression prevention, and verification pipeline work.
- `docs` — Documentation, runbooks, governance, and process clarity updates.
- `refactor` — Structural code improvement without intended behavior change.
- `tech-debt` — Debt cleanup or deferred engineering quality work with limited product-surface change.
- `performance` — Latency, throughput, responsiveness, and resource-efficiency improvements.
- `dependencies` — Dependency version updates (Dependabot or manual hygiene).
- `ci` — CI/CD pipeline, workflow, and build infrastructure changes.

## Area labels

- `backend` — Primary implementation impact in .NET API/domain/application/infrastructure.
- `frontend` — Primary implementation impact in Vue/TypeScript UI and client runtime.
- `ux` — Discoverability, accessibility, interaction model, and user workflow quality.
- `ui` — Visual design, theming, and component presentation work.
- `llm` — AI/provider/planner/executor/chat-related implementation and policy work.
- `automation` — Proposal/triage/workflow automation mechanics and automation policy control planes.
- `worker` — Background worker runtime behavior, queue processing, retry semantics, observability.
- `mcp` — MCP server, Model Context Protocol integration, and external AI agent access work.
- `starter-packs` — Package manifests, prepackaged states, and fixture packs.
- `packaging` — Packaging, distribution, installation, and self-contained executable delivery. Active: the Windows desktop path is the primary v0.1.x run path.
- `cloud` — Cloud hosting, SaaS deployment, online access, and multi-tenant infrastructure. Active again in bounded form: the trusted shared-instance collaboration proof; broad SaaS work stays post-retention.
- `mobile` — Mobile platform, PWA, responsive design, and touch experience. Bounded scope: responsive capture/review; native apps are out of scope pre-v1.
- `marketing` — Market adoption, go-to-market execution, and growth. Deferred until after beta retention evidence; design-partner outreach comes first.

## Semantic/state labels

- `decision` — Blocked on an explicit maintainer, legal, or product ruling; must not become implementation work until the decision is recorded (ADR, walkthrough ruling, or issue comment).
- `human-action` — Requires maintainer credentials, external settings, legal judgement, purchases, or subjective confirmation. Agents must never infer these complete (see `OUTSTANDING_TASKS.md` rule 3).
- `dogfooding` — Sourced directly from real personal/beta use; evidence-grade product feedback (dogfooding findings are exempt from the intake severity bar per `docs/REVIVAL_PLAN.md` §4).
- `product-truth` — The product or its docs claim something unsupported, misleading, or silently untrue; truth-repair work.
- `historical` — Retained for provenance only; not active work. The supersession record lives on the issue.
- `strategy` — Strategic planning, direction, and cross-pillar coordination work.
- `question` — Further information is requested; discussion-style issues awaiting clarification.
- `duplicate` — Already exists; used to close duplicates with a cross-reference.
- `invalid` — Out of scope or misreported; used on closure.
- `wontfix` — Intentionally deferred or rejected; used on closure.
- `help wanted` / `good first issue` — Contribution signals (standard GitHub semantics).

## Wave labels (provenance of seeded waves)

- `revival` — Revival pivot wave (ADR-0044, `docs/REVIVAL_PLAN.md`): REVIVAL-00..14.
- `generalist` — Generalist expansion wave (ADR-0046): artefact intake, dossiers, GEN-00..12.
- `archive-closeout` — **Historical provenance.** The 2026-06-13 archive-pivot closeout wave (ARCHIVE-00..09), superseded by the ADR-0044 revival on 2026-07-10; archive remains only the checkpoint fallback. Issues keep this label for history; their still-live work is owned by the revival direction.
- `codex` — Reserved for Codex-agent-specific test coverage and contribution tasks (TST-CODEX-* series).

## Priority labels

Rule: **every issue carries exactly one Priority label**, and the ProjectV2 `Priority` field must
match it. Priority expresses urgency within the active direction
(`docs/strategy/PRODUCT_DIRECTION.md`); the ProjectV2 `Status` field (`Pending`/`Now`/`Next`/
`Blocked`/`Review`/`Done`) expresses scheduling, and milestones express the delivery horizon.

- `Priority I` — Release-blocking or trust-breaking now: active-milestone blockers, data-loss, security, or product-truth defects on the primary path. (Walkthrough q-3, 2026-08-23: the open Priority I tranche defines the v0.1.2 scope together with `#1876`.)
- `Priority II` — The active direction's next tranche: wedge capabilities, significant defects, and hardening with near-term user impact.
- `Priority III` — Valuable but unscheduled: residuals, tech-debt, performance, and depth work admitted when capacity allows.
- `Priority IV` — Later maturity/deepening; revisited at horizon planning, not during normal admission.
- `Priority V` — Meta-tracking, archival consistency, and historical context.

> The pre-2026-06 tranche meanings ("Phase 4 completion", "expansion tranche" etc.) are historical;
> they were retired with the archive pivot and the revival. Do not resurrect them when triaging.
