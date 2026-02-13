# Taskdeck Implementation Masterplan

Last Updated: 2026-02-13  
Planning Horizon: Next 8 to 12 weeks  
Companion Status Doc: `docs/STATUS.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for what is shipped and verified.
- Every behavior change ships with tests.
- Security hardening is now the primary constraint, not feature breadth.
- Prefer finishing incomplete cross-cutting concerns over adding new surface area.
- Keep automation safe-by-default: proposal first, explicit approval, auditable execution.
- Keep documentation narrow and maintained; archive drift instead of accumulating stale plans.

## Current Cycle Outcome (Completed)

Delivered during the latest merge cycle (PRs #20 and #21):

1. Added backend automation stack end-to-end:
   - proposal service/controller with approval/rejection/execution/diff flow
   - policy engine, planner, and executor services
2. Added archive recovery stack:
   - archive entity/repository/service/controller
   - restore flow with conflict strategies and permission checks
3. Added chat stack:
   - chat sessions/messages/streaming endpoints
   - proposal handoff path from actionable chat prompts
4. Added ops/logging stack:
   - ops template execution service/controller
   - log query/stream/correlation endpoints
5. Added worker/health runtime:
   - queue-to-proposal worker with retries
   - proposal-housekeeping worker
   - heartbeat registry + readiness checks
6. Added infrastructure/migration support for automation/archive/chat/ops entities.
7. Expanded test coverage substantially:
   - backend application tests now 256 passing
   - backend API tests now 86 passing
   - frontend unit tests now 238 passing
   - Playwright E2E now 11 passing
8. Frontend integration completed for automations, chat, ops, and archive views/API clients.

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Security and Identity Convergence

Focus:
- Apply `[Authorize]` and claim-based user resolution across legacy controllers.
- Remove query/body acting-user parameters where claims are required.
- Normalize permission enforcement paths for board/card/column/label/export/audit/queue surfaces.
- Add regression integration tests for unauthorized, forbidden, and cross-user access paths.

Exit Criteria:
- No production endpoint depends on caller-supplied `userId` for identity.
- Endpoint auth behavior is consistent and test-backed.
- Legacy + new controller families use the same security posture.

### Horizon B (Week 3 to 6): Automation Hardening and Real Provider Path

Focus:
- Introduce a production-capable LLM provider path behind clear configuration/feature flags.
- Expand planner coverage beyond narrow regex patterns (typed operation extraction and safer validation).
- Tighten executor behavior (operation coverage, error semantics, compensating-path definitions).
- Improve audit fidelity for executed operations (entity IDs and consistent change payloads).

Exit Criteria:
- Planner/executor support a broader realistic command set with deterministic validation.
- LLM integration can be switched between mock and real providers safely.
- Automation execution has stronger audit and failure semantics.

### Horizon C (Week 7 to 12): Operational Robustness and Data Portability

Focus:
- Implement database-level export/import.
- Improve log query performance to avoid broad in-memory fan-out under load.
- Resolve nullable warning debt introduced by new entities (`CS8618` set).
- Expand E2E beyond smoke/ops happy paths to cover auth boundaries and recovery edge cases.

Exit Criteria:
- DB-level portability works and is test-backed.
- Log queries are performant for realistic datasets.
- Warning profile is materially reduced and tracked.
- E2E includes security and recovery regression slices, not only happy paths.

## Active Backlog (Prioritized)

1. P0: Enforce authentication/authorization on all legacy controllers.
2. P0: Replace endpoint-level actor `userId` parameters with claims-based identity.
3. P0: Add integration tests for unauthorized/forbidden permutations across controller families.
4. P1: Add real LLM provider path and environment-safe toggling.
5. P1: Expand planner grammar -> structured operation extraction coverage.
6. P1: Improve automation executor semantics for partial failure and richer audit attribution.
7. P1: Implement database export/import.
8. P2: Optimize `LogQueryService` query strategy and avoid broad in-memory composition.
9. P2: Eliminate key nullable warnings in newly added domain entities.
10. P2: Execute documentation cleanup/archive pass for stale deep-dive specs.

## Next Best Steps (Immediate)

1. Land auth + claims retrofit for `Boards/Columns/Cards/Labels` controllers first.
2. Retrofit `Export/Audit/LlmQueue/BoardAccess/Users` controllers to claim-based identity.
3. Add API integration tests that assert all retrofitted endpoints reject missing/invalid tokens.
4. Define provider contract for real LLM integration and add config-driven provider selection.
5. Open a docs cleanup PR that archives stale reference specs and updates `docs/INDEX.md` accordingly.

## Documentation Cleanup Plan (Companion Track)

Objective:
- Reduce drift and planning noise while preserving useful reference material.

Execution:
1. Keep only two authoritative planning docs active:
   - `docs/STATUS.md`
   - `docs/IMPLEMENTATION_MASTERPLAN.md`
2. For each deep-dive spec under `docs/backend/*` and `docs/frontend/*`:
   - mark as `Maintained` or `Archive`
   - if archived, move to `docs/archive/` with a date suffix
3. Update `docs/INDEX.md` so ownership and authority are explicit.
4. Add a lightweight docs-review checklist item to PR template/workflow.

Exit Criteria:
- Every top-level doc has clear authority + owner.
- Stale planning docs are archived, not left ambiguous.
- Future drift is caught during normal PR flow.

## Weekly Cadence

- Start of week:
  - reconcile `docs/STATUS.md`
  - choose top 3 backlog items that move one horizon forward
- During week:
  - ship vertical slices with tests
  - avoid introducing new top-level planning docs
- End of week:
  - update this file with completed work, deltas, and reprioritized next steps

## Risk Register

- Risk: security retrofit introduces regressions in existing client flows
  - Mitigation: staged rollout + integration coverage for both happy and forbidden paths
- Risk: mock-to-real LLM transition changes behavior unexpectedly
  - Mitigation: provider abstraction, feature flags, and replayable integration tests
- Risk: automation parser/executor mismatch causes unsafe/incorrect operations
  - Mitigation: strict schema validation, policy gates, and explicit approval requirements
- Risk: log query performance degrades with dataset growth
  - Mitigation: query strategy refactor + targeted performance tests
- Risk: documentation drift returns after cleanup
  - Mitigation: enforce status/masterplan update policy and archive stale specs quickly
