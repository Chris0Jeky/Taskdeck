# Extensibility and Modularity

Score: **7.5 / 10**  
(There are good “seams” for extension — providers, adapters, notifications, realtime. The main limitation is that some capabilities are deeply tied to EF Core + SQLite patterns and to the monolithic host.)

## 1) Good extension seams already present

### LLM providers
- `ILlmProvider` abstraction exists.
- Provider selection is policy-driven (`LlmProviderSelectionPolicy`).
- Settings objects exist per provider.

**What this enables**
- adding new LLM providers (Anthropic, local models) with minimal disruption.

### External import adapters
- `IExternalImportAdapter` with provider-specific implementations (e.g., CSV).
- Parse result includes structured conflicts.

**What this enables**
- adding new import formats (Trello, Jira, Notion exports).

### Notifications
- There’s a notification model and a notifier abstraction.
- Realtime notifications integrate via SignalR.

### Outbound webhooks
- Subscription model + delivery worker.
- Signature/secret rotation exists.
- Security guard for endpoint targeting exists.

## 2) Where extensibility will get harder

### A) SQLite-specific workarounds are spreading
When a database limitation forces raw SQL or in-memory ordering, it tends to:
- leak into repositories
- require special tests
- complicate future DB migration

This isn’t fatal, but it should be treated as a deliberate decision with an exit plan.

### B) Background workers inside API host
This limits modular deployment options:
- you can’t scale HTTP and worker capacity independently
- you can’t deploy “worker only” for automation-heavy environments

### C) Configuration location
Settings classes in Application layer can become:
- “kitchen sink config”
- unclear ownership of what belongs to business logic vs hosting policy

## 3) Extensibility recommendations

### Near-term (no big refactor)
- Create a “module map”:
  - Boards
  - Automation
  - Capture
  - Webhooks
  - Ops
  - Auth
- For each, define:
  - public service interfaces
  - controller surfaces
  - key domain entities

### Mid-term
- Split workers into their own host process / container.
- Add versioning strategy to external import profiles and outbound webhook event schemas.

### Long-term (if you go beyond local-first)
- Replace SQLite with Postgres
- Add a message bus for:
  - webhook deliveries
  - LLM processing
  - notifications

## 4) Extension ideas (product + engineering)

- Pluggable “automation rules” engine that can run without LLMs (deterministic triggers).
- Offline-first support and local caching on the client (if the goal is truly local-first).
- Webhook event types beyond board changes (comments, mentions, automation runs).
- Multi-provider LLM routing with:
  - cost-based selection
  - quality-based fallback
  - per-board provider policy
