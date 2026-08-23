# Taskdeck Golden Principles

Last Updated: 2026-08-23
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/REVIVAL_PLAN.md`
- `docs/strategy/PRODUCT_DIRECTION.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Purpose

This document defines concise repository invariants that should remain stable as implementation details evolve.
Mechanical enforcement stays intentionally lightweight and focuses only on low-churn, high-signal checks.

## Golden Principles

- `GP-01 Layer Boundaries`
  - Keep domain/application/infrastructure/API boundaries coherent and avoid forbidden dependency direction.
- `GP-02 Claims-First Identity`
  - Do not trust caller-supplied identity for protected resources; derive actor identity from authenticated claims.
- `GP-03 Stable Error Contracts`
  - Preserve predictable JSON error payloads (`errorCode`, non-empty `message`) and consistent status semantics.
- `GP-04 Test and CI Evidence`
  - Ship behavior changes with deterministic tests and run the required verification commands.
- `GP-05 Canonical Docs Sync`
  - Keep canonical active docs aligned with shipped reality when behavior or workflow expectations change.
- `GP-06 Review-First Automation Safety`
  - Keep automation-originated board writes proposal-first; do not introduce silent/destructive autonomy by default. Manual board UI edits remain direct and auditable.
  - Direction note (2026-08-23): a future generalisation to user-sovereign delegated authority is recorded as [ADR-0057](decisions/ADR-0057-user-sovereign-delegated-authority.md) (**Proposed, not in force**). Until that ADR is Accepted, this principle, ADR-0003, and ADR-0056 remain fully operative and no auto-approval surface may be built.
- `GP-07 Lightweight Mechanical Governance`
  - Prefer maintainable, low-brittleness checks over broad regex or policy sprawl.
- `GP-08 Product Legibility Before Breadth`
  - Do not add major surface breadth ahead of a clear golden path; raw IDs in the happy path, dead-end empty states, or orphan pages without a next step are not novice-ready.
- `GP-09 Traceable Agent Expansion`
  - Do not expose agent/autonomy breadth unless runs, policies, and resulting proposals/artifacts stay inspectable in product language.
- `GP-10 Explicit Egress And Telemetry Boundaries`
  - Treat outbound data flow separately from mutation safety: every external destination must be disclosed/enforced, and local telemetry must reject user content by default.

## Mechanical Enforcement (Current)

- `scripts/check-docs-governance.mjs`
  - validates active canonical docs exist, are indexed, and keep required date stamps.
- `scripts/check-golden-principles.mjs`
  - validates this document exists, has a `Last Updated` line, and retains core principle IDs.
- `scripts/check-github-ops-governance.mjs`
  - validates issue-template and project-operations governance invariants.

## Deferred Follow-ups

- Snapshot/diff policy expansion should remain incremental and only move into CI after low-noise validation.
- Any additional mechanical checks should be introduced only when signal quality is demonstrated.
