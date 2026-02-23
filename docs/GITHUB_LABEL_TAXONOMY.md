# GitHub Label Taxonomy

Last Updated: 2026-02-23

This file is the canonical description source for repository issue labels.

Note:
- GitHub MCP issue tooling used in this repository does not currently expose label-description update operations.
- Keep this file aligned with GitHub label metadata when manual label edits are performed in the GitHub UI.

## Operational Labels

- `bug`
  - Defect where current behavior is incorrect, unstable, or regressed.
- `good first issue`
  - Beginner-friendly task suitable for onboarding and low-risk contributions.
- `security`
  - Authentication, authorization, data protection, abuse prevention, and compliance-related changes.
- `hardening`
  - Reliability, safety, operability, and production-readiness improvements.
- `backend`
  - Primary implementation impact in .NET API/domain/application/infrastructure.
- `frontend`
  - Primary implementation impact in Vue/TypeScript UI and client runtime.
- `ux`
  - Discoverability, accessibility, interaction model, and user workflow quality.
- `testing`
  - Test coverage, harnesses, regression prevention, and verification pipeline work.
- `docs`
  - Documentation, runbooks, governance, and process clarity updates.
- `refactor`
  - Structural code improvement without intended behavior change.
- `tech-debt`
  - Debt cleanup or deferred engineering quality work with limited product-surface change.
- `starter-packs`
  - Work specific to package manifests, prepackaged states, and fixture packs.
- `llm`
  - AI/provider/planner/executor/chat-related implementation and policy work.
- `feature`
  - User-facing functionality delivery slices (new capabilities or clear product-surface enhancements).
- `automation`
  - Proposal/triage/workflow automation mechanics, orchestration behavior, and automation policy control planes.
- `worker`
  - Background worker runtime behavior, queue processing, retry semantics, and worker observability.
- `performance`
  - Latency, throughput, responsiveness, and resource-efficiency improvements across API, worker, and frontend surfaces.

## Priority Labels

Rule:
- Every issue must have exactly one priority label.

- `Priority I`
  - Highest urgency; active cycle blockers and immediate execution path.
- `Priority II`
  - Immediate next tranche after `Priority I`; foundation work.
- `Priority III`
  - Medium-term expansion tranche (analytics/security/compliance growth).
- `Priority IV`
  - Later maturity tranche (platform/test/UX/docs deepening).
- `Priority V`
  - Lowest urgency, meta-tracking, or historical/archive context.
