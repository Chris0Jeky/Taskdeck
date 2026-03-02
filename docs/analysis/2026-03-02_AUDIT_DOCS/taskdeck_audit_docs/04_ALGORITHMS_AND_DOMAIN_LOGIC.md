# Algorithms and Domain Logic

Score: **7.5 / 10**  
(The domain logic is generally clear and “business-safe”, but there are places where algorithms are correct-but-inefficient or correct-but-underspecified for larger scale.)

## 1) Board / Column / Card manipulation

### Ordering and positioning
Columns and cards use an integer `Position` field with “re-sequencing” behavior:
- column creation chooses next position
- card creation uses next position in target column
- moving cards triggers position updates for the affected column(s)

**Strengths**
- Simple mental model.
- Easy to serialize/import/export.

**Weak points / failure modes**
- For large columns, resequencing is O(n) and can generate many DB updates.
- No fractional positioning strategy; frequent reorder operations can be costly.
- SQLite is fine here but large writes can cause lock contention.

**Potential improvements**
- Use a “sparse ordering” strategy (e.g., decimal ranks / lexorank) to reduce full resequencing.
- Or keep current model but add:
  - max cards/column constraints
  - batching optimizations

### WIP limit enforcement
Card creation/move checks `Column.WouldExceedWipLimitIfAdded()`.

**Strength**
- Enforcement is centralized (domain method), used in multiple flows.

**Watch-outs**
- Concurrency: two clients can pass WIP checks simultaneously if no DB-level constraint exists.
  - For a local-first app this may be fine.
  - For multi-user concurrency, you can still exceed WIP unless you lock or use a transaction with repeatable read (not in SQLite).

## 2) Optimistic concurrency control (user-facing correctness)

Card updates use an “expectedUpdatedAt” pattern:
- the client sends an `ExpectedUpdatedAt`
- server compares with current `UpdatedAt`
- mismatch yields `Conflict`

**Strengths**
- Predictable user experience: “someone changed this card, reload”.
- Works well for SPAs.

**Weaknesses**
- Timestamp comparison is sensitive to precision differences (SQLite stores DateTimeOffset as text).
- Without EF concurrency tokens, you rely on application logic; it’s correct but easy to bypass in future code.

**Possible improvement**
- Add a concurrency token / rowversion-like mechanism if you migrate to Postgres.
- In SQLite, keep the manual approach but make it explicit as a project standard.

## 3) LLM request queueing and processing

There are two distinct concerns:
- **Queue management** (`LlmQueueService` + repository)
- **Worker processing** (`LlmQueueToProposalWorker` background service)

### Queue selection (“process-next”)
`ProcessNextRequestAsync` selects the next pending non-capture request ordered by creation time.

**Correctness**
- Deterministic, simple.

**Big issue**
- It is not scoped by user or board, and is callable via an API endpoint.
- Algorithmically it’s “correct”, but it violates multi-user isolation.

### Worker fairness and throughput
The worker:
- reads pending items
- separates “capture triage” requests from normal requests
- processes with concurrency and backoff

**Strengths**
- Uses a structured loop with cancellation tokens and heartbeat concepts.
- Avoids infinite blocking; has delay/backoff.
- Tracks tokens used and statuses (good for auditing and cost).

**Weaknesses**
- If pending list grows large, loading and sorting in memory can get expensive.
- If multiple API instances run workers concurrently, you need strong claiming semantics.
  - Some claim logic exists, but cross-instance “exactly-once” is still hard.

**Improvements**
- Make queue reads paginated (e.g., fetch N oldest pending).
- Ensure claims are atomic in the DB (some flows already do this).
- Separate worker into its own host so scaling HTTP doesn’t scale workers unintentionally.

## 4) Outbound webhook delivery (retries + leasing)

Outbound webhook delivery worker shows a mature pattern:
- claim pending delivery records
- attempt HTTP delivery with security guard
- mark success/failure
- retry with backoff
- recover “stuck” deliveries

**Strengths**
- Avoids duplicate delivery via DB-level claiming and status changes.
- Includes stuck-work recovery logic.
- Uses an explicit SSRF guard in the HTTP connect callback.

**Possible weaknesses**
- Uses per-item scopes and `Task.Run` scheduling patterns that add overhead.
- Retry/backoff strategy should be explicitly documented:
  - max retries
  - dead-letter behavior
  - retention policy

## 5) CSV import parsing and deduplication

The CSV import adapter:
- enforces payload byte limit
- enforces max rows
- normalizes headers and detects duplicates
- generates stable dedupe keys
- emits detailed conflict reasons

**Strengths**
- This is very robust “business logic parsing”.
- Defensive programming is strong: explicit bounds, detailed diagnostics.

**Weaknesses**
- Max sizes are hard-coded; might need configuration.
- Dedupe strategies can drift if profile changes; maintain versioning carefully.

## 6) “Prompt injection denylist” (ChatService)

The chat service blocks requests containing certain denylisted substrings.

**Reality check**
- This is not a real prompt-injection defense.
- It will:
  - miss most malicious prompts
  - produce false positives
  - create a false sense of security

**Recommendation**
- Treat it as a UX filter only.
- For real safety:
  - constrain tool/action execution via policy engine (already exists)
  - validate proposed actions against board authorization + schemas
  - log and rate-limit “actionable” attempts

## Summary recommendations (algorithmic)

- Keep simple O(n) resequencing for now (fits local-first), but document its limits.
- Add paging to queue and log retrieval endpoints.
- Formalize concurrency patterns (timestamp-based optimistic concurrency) so new code doesn’t bypass them.
- Treat denylist prompt-injection checks as “nice-to-have”, not a security boundary.
