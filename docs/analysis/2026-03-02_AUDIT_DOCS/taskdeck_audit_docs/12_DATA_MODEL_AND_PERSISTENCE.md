# Data Model and Persistence

Score: **7 / 10**  
(The schema is fairly rich and the repo supports export/import. The biggest concerns are retention, growth, and SQLite limitations if scaled.)

## 1) Core entities (inferred)

The EF Core DbContext includes entities for:
- Users
- Boards
- Columns
- Cards
- Labels and CardLabels
- Comments and Mentions
- LLM requests / queue
- Automation proposals / archives
- Audit logs
- Notifications
- Outbound webhook subscriptions and deliveries
- Ops CLI runs

**Evidence:** `backend/src/Taskdeck.Infrastructure/Persistence/TaskdeckDbContext.cs`

This is a non-trivial domain model — closer to a “real product” than a toy.

## 2) Schema management

- EF Core migrations exist under `Taskdeck.Infrastructure/Migrations`.
- Application runs `Database.Migrate()` at startup.

**Strength**
- Easy to keep schema up to date in self-hosted deployments.

**Risk**
- Auto-migrations can be risky in multi-instance deployments or when migrations are not backwards compatible.

## 3) Indexing

At least some indexes exist (e.g., on LLM request status and createdAt).

**Recommendation**
- Audit indexes for:
  - logs table (boardId, userId, correlationId, createdAt)
  - webhook deliveries (status, nextAttemptAt)
  - notifications (userId, createdAt, dedupeKey)

## 4) Export/import

### Board export/import
The repo supports board export and import with structured DTOs.

### Database export/import
Database export/import is guarded by sandbox settings (disabled outside development).

**Strength**
- This is a good approach: DB import is inherently dangerous.

**Operational note**
- Proxy upload limits may prevent large imports (nginx 10MB vs backend 50MB default).

## 5) Retention and growth

Entities like:
- logs
- webhook deliveries
- queue requests
- notifications
can grow without bound.

With SQLite, unbounded growth causes:
- file size bloat
- vacuum needs
- slower queries

**Recommendation**
- Add retention policies:
  - keep 30/90 days of logs by default
  - keep last N webhook deliveries per subscription
  - archive or delete completed queue items after N days

## 6) Data safety / privacy

- LLM requests may contain user-entered content and board data.
- Outbound webhooks may transmit sensitive data to third parties.

**Recommendations**
- Add a “data classification” doc:
  - what is stored
  - what is sent to LLMs
  - what is sent to webhooks
- Provide configuration to disable:
  - sending full card descriptions to LLMs
  - storing full LLM prompts/responses in logs

## 7) Persistence recommendations

### P1
- Add paging and limits to large list retrieval endpoints.
- Add retention/cleanup job (can be a background worker).

### P2
- Consider adding encryption-at-rest options for the SQLite DB file in self-hosted scenarios (depends on platform).
- Provide backup/restore guidance in docs.
