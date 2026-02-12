# Automation Framework Specification

Last Updated: 2026-02-12

## 1. Objective

Define a reusable, safe framework for backend automations where:
- instructions are transformed into explicit mutation proposals,
- proposals are reviewable before apply,
- execution is idempotent, auditable, and policy-gated.

## 2. Core Principles

1. Proposal-first only: no direct autonomous mutation in initial activation.
2. Typed operations: every proposed change is explicit and schema-validated.
3. Policy gate before apply: risk and permission checks are mandatory.
4. Deterministic execution: idempotency and conflict handling required.
5. Full auditability: proposal, decision, execution, and results are logged.

## 3. Architecture

## 3.1 Components
- `AutomationPlannerService`
  - turns queue/chat instructions into typed proposal operations
- `AutomationPolicyEngine`
  - classifies risk and validates policy constraints
- `AutomationProposalService`
  - CRUD and lifecycle transitions for proposals
- `AutomationExecutorService`
  - executes approved proposals via operation handlers
- `IAutomationActionHandler<T>`
  - extension point for each operation type

## 3.2 Operation Handler Registry

Handler key format:
- `board.create`
- `board.update`
- `card.create`
- `card.update`
- `card.move`
- `card.archive`
- `column.reorder`
- `label.assign`

Each handler must provide:
- validation,
- dry-run diff preview,
- apply operation,
- compensation metadata for rollback tooling.

## 4. Data Model

### 4.1 Proposal entity

Fields:
- `Id`
- `SourceType` (`queue`, `chat`, `manual`)
- `SourceReferenceId`
- `BoardId` (nullable for global proposals)
- `RequestedByUserId`
- `Status` (`PendingReview`, `Approved`, `Rejected`, `Applied`, `Failed`, `Expired`)
- `RiskLevel` (`Low`, `Medium`, `High`, `Critical`)
- `Summary`
- `Operations` (ordered list)
- `DiffPreview`
- `ValidationIssues`
- `CreatedAt`
- `ExpiresAt`
- `DecidedAt`
- `DecidedByUserId`
- `AppliedAt`
- `FailureReason`
- `CorrelationId`

### 4.2 Proposal operation type

Fields:
- `Sequence`
- `ActionType`
- `TargetType`
- `TargetId` (nullable for create)
- `Parameters` (JSON payload)
- `IdempotencyKey`
- `ExpectedVersion` (optional optimistic concurrency guard)

## 5. API Contract

Required endpoints:
- `POST /api/automation/proposals`
- `GET /api/automation/proposals`
- `GET /api/automation/proposals/{id}`
- `POST /api/automation/proposals/{id}/approve`
- `POST /api/automation/proposals/{id}/reject`
- `POST /api/automation/proposals/{id}/edit`
- `GET /api/automation/proposals/{id}/diff`

Required behaviors:
- approve only allowed for authorized reviewers,
- reject requires reason for `High` and `Critical` risk,
- edit creates proposal revision trail,
- approval response includes accepted operation snapshot hash.

## 6. Lifecycle and State Rules

Allowed transitions:
- `PendingReview` -> `Approved`
- `PendingReview` -> `Rejected`
- `PendingReview` -> `Expired`
- `Approved` -> `Applied`
- `Approved` -> `Failed`

Forbidden:
- editing after `Approved`,
- approving `Expired` proposals,
- applying `Rejected` proposals.

## 7. Guardrails

Before approval:
- validate schema and domain constraints,
- validate permission scope for every operation,
- classify risk level.

Before apply:
- revalidate current state and `ExpectedVersion`,
- enforce idempotency keys,
- enforce max operation count and payload size.

After apply:
- emit audit log entries per operation,
- attach correlation ID to execution record.

## 8. Idempotency and Conflict Strategy

- Each proposal apply call requires `Idempotency-Key` header.
- Duplicate apply with same key returns stored result.
- Concurrency conflict returns `409 Conflict` with actionable detail:
  - conflicting resource,
  - expected vs actual version,
  - recovery hint (`refresh proposal`, `edit and re-approve`).

## 9. Rollback and Compensation

Initial mode:
- no automatic rollback transaction across heterogeneous operations.
- on partial failure, mark proposal as `Failed` and emit compensation suggestions.

Future mode:
- add optional compensation handlers for reversible operations.

## 10. Test Requirements

Unit:
- lifecycle transition guards,
- risk classification and policy decisions,
- handler-level validation and diff generation,
- idempotency behavior.

Integration:
- proposal create/edit/approve/reject/apply flows,
- apply conflict paths and stale proposal failure,
- audit log generation and correlation propagation.

E2E:
- queue or chat request creates proposal,
- reviewer edits and approves,
- board state reflects applied operations.

## 11. Acceptance Criteria

- proposals are always explicit and reviewable,
- no proposal bypasses policy checks,
- apply path is idempotent and conflict-aware,
- all decisions and executions are auditable.
