# ADR-0037: Idempotency-Key Contract for Automation Proposal Operations

- **Status**: Accepted
- **Date**: 2026-06-06
- **Deciders**: Repository maintainers

## Context

Automation proposal operations carry an `IdempotencyKey` field (max 100 characters, non-empty, enforced at the domain level). The key serves two purposes: preventing duplicate operations from being persisted, and enabling safe replay of proposal execution.

Prior to PR #1146, a duplicate key insertion produced an unhandled `DbUpdateException` surfacing as HTTP 500. The fix translated this into a 409 Conflict response, but the contract governing key format, scope, and replay semantics was never formally documented. Different callers generate keys using different patterns, and the uniqueness constraint is global (not scoped to a user or board), which has implications for key collision and cross-user visibility.

## Decision

### Key format

The idempotency key is a free-form string of 1–100 characters. Callers choose a generation strategy appropriate to their domain:

| Caller | Pattern | Example |
|--------|---------|---------|
| CaptureTriageService | `SHA256(captureItemId:sequence:normalizedTitle)` | Deterministic — same input always produces the same key |
| InboxTriageAssistant | `"inbox-triage:{itemId:N}:{boardId:N}"` | Per-board, per-item scoped |
| InboxTriageDigestAgent | `"digest:{runId:N}:{itemId:N}"` | Per-digest-run, per-item scoped |
| MCP write tools / chat / tests | `Guid.NewGuid().ToString()` | One-shot, no replay intent |

No single format is mandated. Callers that need replay idempotency (capture triage) use deterministic keys; callers that do not use random GUIDs.

### Uniqueness scope

The unique constraint is **global** across the entire `AutomationProposalOperations` table (index `IX_AutomationProposalOperations_IdempotencyKey`). It is not scoped to a specific user or board.

This means:
- Two different users cannot independently use the same idempotency key.
- Deterministic key generation (SHA256-based) must include user-scoped or board-scoped identifiers in the hash input to avoid cross-user collisions.
- Random GUID keys have negligible collision probability and are safe for one-shot use.

### Duplicate key behavior

When an operation with a duplicate `IdempotencyKey` is inserted:
1. The EF Core `SaveChangesAsync` call throws `DbUpdateException` with a UNIQUE constraint violation.
2. `UnitOfWork.IsOperationIdempotencyKeyUniqueViolation` detects this by matching the SQLite error message against the constraint name or column.
3. The exception is translated to a `DomainException` with `ErrorCodes.Conflict`.
4. The API returns **HTTP 409 Conflict** with `{"errorCode": "Conflict", "message": "An automation operation with this idempotency key already exists."}`.

This is a **reject** strategy, not an **upsert** or **return-existing** strategy. The caller receives an error and must decide how to proceed.

### Execution replay

Proposal execution (via `AutomationExecutorService`) is independently idempotent:
- The execute endpoint requires an `Idempotency-Key` HTTP header (400 if missing).
- If the proposal is already in `Applied` status, execution returns success without re-applying.
- This is orthogonal to the operation-level idempotency key — it guards against double-execution of the same proposal, not against duplicate operation creation.

### Validation

- Domain: `AutomationProposalOperation` constructor throws `DomainException` if `IdempotencyKey` is null or empty.
- Database: unique index enforces global uniqueness.
- API: `ProposalOperationInputValidator` validates operation shape (actionType, targetType, parameters) but does not validate key format — the key is opaque to the validator.

## Alternatives Considered

### Per-user or per-board scoped uniqueness

A composite unique index `(UserId, IdempotencyKey)` or `(BoardId, IdempotencyKey)` would allow different users to reuse the same key independently. Rejected because:
- Operations belong to proposals, not directly to users — adding a userId column to the operations table breaks the current entity model.
- The SHA256-based keys from capture triage already include board/item context, making cross-user collision practically impossible.
- Global uniqueness is simpler to reason about and provides a stronger guarantee.

### Return-existing on duplicate (upsert)

Instead of 409, the API could return the existing operation/proposal. Rejected because:
- The caller may have different parameters for the same key, and silently ignoring them would violate least-surprise.
- 409 is explicit and lets the caller decide: retry with a new key, fetch the existing proposal, or treat as expected.
- Matches the HTTP semantics: 409 signals a conflict with the current state of the resource.

### Structured key format enforcement

Requiring all keys to follow a specific format (e.g., `{source}:{scope}:{hash}`). Rejected because:
- The key is opaque at the database level — format enforcement adds complexity without measurable benefit.
- Different callers have legitimately different key strategies.
- The 100-character limit is sufficient for all current patterns.

## Consequences

### Positive
- Clear contract for callers: deterministic keys enable safe retry, random keys are fire-and-forget.
- 409 response is explicit and actionable.
- Execution idempotency is independently guaranteed via the `Idempotency-Key` header.

### Negative
- Global uniqueness means callers must ensure their key generation includes sufficient entropy or scoping.
- The "reject" strategy means clients cannot blindly retry create requests — they must handle 409 and decide.

### Neutral
- No migration needed — the unique index and domain validation already exist.
- This ADR documents existing behavior rather than introducing changes.

## References

- Issue #1149 (this ADR)
- PR #1146 — duplicate idempotency key returns 409 not 500
- PR #1151 — `ProposalOperationInputValidator` for malformed operation input
- `AutomationProposalOperation` entity — domain validation
- `AutomationProposalOperationConfiguration` — EF Core unique index
- `UnitOfWork.IsOperationIdempotencyKeyUniqueViolation` — conflict detection
- `AutomationExecutorService` — execution-level idempotency
- `AutomationProposalsController` — `Idempotency-Key` header requirement
- Roadmap invariant INV-07 — proposal execution uses idempotency keys
