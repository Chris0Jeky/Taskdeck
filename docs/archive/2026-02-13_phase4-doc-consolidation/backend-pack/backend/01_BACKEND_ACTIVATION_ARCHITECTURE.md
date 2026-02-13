# Backend Activation Architecture

Last Updated: 2026-02-12

## 1. Goal

Activate backend capabilities so the current frontend overhaul can run against complete, safe, production-grade backend behavior.

Activation targets:
- enforce JWT auth and role-based authorization across legacy endpoints,
- provide missing endpoints required by frontend placeholder surfaces,
- introduce proposal-first automation execution with explicit approval,
- support chat-driven LLM instruction intake and safe action planning,
- add operational APIs for logs and CLI bridge with strict guardrails,
- deepen reliability via hosted workers, health checks, retries, and metrics.

## 2. Current State vs Target State

Current:
- Core board and task APIs exist and are tested.
- Side-track service slices exist: auth, board access, audit, export/import, queue.
- Frontend has completed routes/views for automation, ops, and archive but several backend endpoints are missing.
- `LlmQueueService` supports queue CRUD and manual process-next, but no end-to-end execution worker.
- JWT middleware is configured, but not fully enforced via endpoint policy attributes.

Target:
- All frontend routes are backed by stable backend contracts.
- No write operation trusts caller-provided actor IDs from query/body.
- Automation operations flow through proposal -> review -> apply lifecycle.
- Chat instructions can generate proposals through a provider-agnostic LLM orchestration layer.
- Ops and observability endpoints exist with access controls and full audit trail.
- Reliability controls prevent stuck queues, runaway retries, and silent CI hangs.

## 3. System Boundaries

In scope:
- `backend/src/Taskdeck.Api`
- `backend/src/Taskdeck.Application`
- `backend/src/Taskdeck.Domain`
- `backend/src/Taskdeck.Infrastructure`
- `backend/tests/*`
- `.github/workflows/ci.yml`
- frontend integration contracts and E2E tests for new backend behavior

Out of scope for first activation:
- autonomous mutation mode without human approval,
- external multi-provider orchestration at runtime (single configured provider adapter only),
- distributed queue infra (SQLite remains default baseline).

## 4. Target Logical Architecture

```text
Frontend Views/Stores
  -> API Controllers
    -> Application Services
      -> Policy/Guardrail Layer
        -> Repositories + UnitOfWork
          -> SQLite

LLM/Automation Path:
Chat/Queue Input
  -> Planner (instruction parsing + intent extraction)
  -> Proposal Store (pending review)
  -> Reviewer Decision (approve/edit/reject)
  -> Executor (idempotent apply handlers)
  -> Audit + Logs + Correlation IDs
```

### 4.1 New Backend Components

- `AutomationProposalsController`
- `ArchiveController`
- `OpsController` (CLI bridge)
- `LogsController`
- `LlmChatController`
- `AutomationProposalService`
- `AutomationPlannerService`
- `AutomationExecutorService`
- `AutomationPolicyEngine`
- `ArchiveRecoveryService`
- `CommandExecutionService`
- `LogQueryService` + streaming publisher
- `LlmChatService`
- Hosted workers:
  - queue-to-proposal worker
  - proposal housekeeping worker
  - optional retention/cleanup worker

### 4.2 Data Entities to Add

- `AutomationProposal`
- `AutomationProposalOperation`
- `AutomationDecision`
- `ChatSession`
- `ChatMessage`
- `CommandRun`
- `CommandRunLog`
- `ArchiveItem` (snapshot/tombstone record)

## 5. Activation Phases

### Phase A: Identity and Permission Enforcement
- Add `[Authorize]` to protected endpoints.
- Move actor identity to claims.
- Add explicit policy attributes for read/write/manage/delete.
- Keep temporary compatibility for transitional query actor fields (warn and ignore).

### Phase B: Missing Endpoint Activation
- Implement archive, proposal, ops, and logs endpoints.
- Implement chat session/message endpoints and streaming output path.

### Phase C: Automation Runtime
- Add planner/policy/executor services.
- Add queue worker that creates proposals, never auto-applies.
- Add idempotency and conflict handling.

### Phase D: Reliability and Observability
- Add health/readiness endpoints and worker health state.
- Add structured logs, correlation ID lookups, and metrics.
- Add timeout and backoff policies to long-running operations.

### Phase E: Test and Rollout Hardening
- Expand backend unit/integration coverage.
- Expand Playwright to non-smoke journeys.
- Add CI anti-hang controls and artifact policies.

## 6. Decision Locks

1. Safety mode: proposal-only.
2. Actor identity source: JWT claims only.
3. Provider strategy: abstraction-first with deterministic mock baseline.
4. Realtime transport: SSE-first for logs/chat streams.
5. Database baseline: SQLite with provider-safe query patterns.
6. Rollout strategy: feature-flag and policy-gated slices, no big-bang switch.

## 7. Non-Functional Requirements

- Security: strict allowlists, authorization checks on every mutation path.
- Reliability: bounded retries with dead-letter behavior, no unbounded loops.
- Observability: every request has correlation ID and structured logs.
- Performance: paginated list endpoints, bounded payload sizes.
- Operability: clear diagnostics for failed queue/proposal/chat operations.

## 8. Completion Criteria

Backend activation is complete when:
- frontend placeholder surfaces no longer depend on stub behavior,
- auth/authorization enforcement is active across all sensitive endpoints,
- automation proposals are reviewable and auditable end-to-end,
- chat-driven instruction flow can produce proposals safely,
- extended E2E and CI reliability gates pass consistently.
