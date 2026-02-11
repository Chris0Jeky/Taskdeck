# Taskdeck Implementation Masterplan

Last Updated: 2026-02-11  
Planning Horizon: Next 8 to 12 weeks  
Companion Status Doc: `docs/STATUS.md`

## Purpose

This is the active execution guide for sequencing implementation.
Update this file at the end of each meaningful delivery cycle.

## Planning Principles

- `docs/STATUS.md` is authoritative for current reality and test totals.
- Every behavior change ships with tests.
- Keep CI gates aligned with local commands.
- Deliver vertical slices that move scaffolding into enforceable behavior.
- Build interfaces that can later be safely used by an LLM agent.

## Current Cycle Outcome (Completed)

Delivered in this cycle:

1. Multi-user foundational domain model scaffolded:
   - `User`, `BoardAccess`, `AuditLog`, `LlmRequest`
   - `Board.OwnerId`
   - `UserRole`, `AuditAction`, `RequestStatus`
2. Application contracts scaffolded for:
   - authentication and authorization
   - board access management
   - export/import
   - history/audit access
   - LLM queue operations
3. Infrastructure scaffolded:
   - repository implementations for users/access/history/queue
   - EF configurations and migration (`20260211082334_AddUserPermissionsAuditQueue`)
   - DI registrations and unit-of-work extensions
4. New domain logic test coverage added for all newly introduced entities.
5. Supporting design guides added for permissions, export/import, and queue workflows.

This cycle intentionally stopped at scaffolding/contracts.
No new runtime behavior is exposed through API endpoints yet.

## Roadmap by Horizon

### Horizon A (Week 1 to 2): Authentication and Authorization Activation

- Implement password hashing and JWT issue/validation flow.
- Add register/login/change-password endpoints.
- Add authenticated user context in API pipeline.
- Implement authorization service with owner/admin/editor/viewer checks.
- Enforce write/delete permissions in board/column/card/label services.
- Add integration tests for auth and permission failures.

Exit Criteria:
- Auth is functional end-to-end.
- Unauthorized and forbidden paths are enforced and tested.
- Existing single-user behavior remains backward compatible.

### Horizon B (Week 3 to 6): Data Sharing and History

- Implement board export/import (JSON first, SQLite full backup second).
- Implement conflict strategy for import (`overwrite`, `merge`, `skip`).
- Implement user-mapping strategy during import.
- Implement audit/history service and endpoint surfaces.
- Ensure critical write actions produce audit entries.
- Add CLI commands for export/import/history where stable.

Exit Criteria:
- Projects are exportable and importable with clear conflict behavior.
- Audit history is queryable and tied to user identity.
- Write operations are traceable for rollback investigation.

### Horizon C (Week 7 to 12): LLM Queue and Safe Automation Controls

- Implement LLM queue service and background processing worker.
- Support queued voicenotes/transcripts when local LLM is offline.
- Add retry policy and terminal-failure handling.
- Introduce proposal/approval workflow for LLM-originated mutations.
- Add diff-style preview and action audit entries for applied proposals.
- Define rollback/compensation approach for failed or rejected actions.

Exit Criteria:
- Queue works reliably through local LLM downtime.
- LLM actions are safe-by-default (review before apply).
- History trail is sufficient to inspect and recover from bad mutations.

## Active Backlog (Prioritized)

1. P0: Implement JWT authentication and user identity propagation.
2. P0: Implement authorization service and enforce write guards.
3. P0: Add integration tests for forbidden write attempts.
4. P1: Implement board-level export/import JSON flow with conflict handling.
5. P1: Implement audit history service and core API endpoints.
6. P1: Implement basic LLM queue processor with retry policy.
7. P2: Add proposal/approval + diff preview for LLM-generated actions.
8. P2: Expand CLI automation contracts for new tracks.

## Next Best Steps (Execution Sequence)

1. Authentication slice:
   - implement hashing + JWT services
   - add `/api/users/register` and `/api/users/login`
   - add integration tests
2. Authorization slice:
   - enforce owner/admin/editor/viewer checks in existing services
   - add forbidden-path API integration tests
3. History slice:
   - implement `HistoryService`
   - write audit entries on create/update/delete/move/permission changes
4. Export/import slice:
   - ship JSON export/import for single board
   - add import conflict strategy and tests
5. Queue slice:
   - implement `LlmQueueService` + hosted worker
   - expose queue stats/status endpoints and tests

## Weekly Cadence

- Start of week:
  - reconcile `docs/STATUS.md`
  - select top 3 backlog items
- During week:
  - ship vertical slices with tests
  - keep scaffolding docs aligned with canonical docs
- End of week:
  - update this file with completed items and reprioritized next steps

## Risk Register

- Risk: Permissions are scaffolded but not enforced in runtime paths
  - Mitigation: prioritize authorization activation before adding new write-capable APIs
- Risk: Export/import introduces accidental privilege escalation
  - Mitigation: explicit import authorization checks and user-mapping controls
- Risk: LLM queue applies unsafe mutations
  - Mitigation: proposal-first flow, explicit approvals, diff visibility, and rollback path
- Risk: Documentation drift between deep-dive docs and canonical docs
  - Mitigation: treat `STATUS.md` and this file as authoritative and reconcile weekly
