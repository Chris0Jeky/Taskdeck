# ADR-0023: SQLite-to-PostgreSQL Production Migration Strategy

- **Status**: Accepted
- **Date**: 2026-04-09
- **Deciders**: Project maintainers

## Context

Taskdeck currently uses SQLite as its sole persistence provider (ADR-0001 clean architecture, local-first thesis). SQLite is ideal for the current single-user, local-first deployment model — zero-config, file-based, and embedded in the application process.

However, the platform expansion strategy (ADR-0014, issue #531) targets hosted cloud deployment (v0.2.0) and collaboration features (v0.4.0). These milestones require a production database that supports:

- Concurrent write access from multiple API instances
- Connection pooling for horizontal scaling
- Row-level locking instead of database-level locking
- Robust backup, point-in-time recovery, and replication
- Managed hosting on all major cloud platforms (AWS RDS, Azure Database, GCP Cloud SQL)

The project needs a clear provider choice, a migration path, and a compatibility harness to catch provider-specific regressions before they reach production.

## Decision

**Adopt PostgreSQL as the target production database provider.** SQLite remains the default for local development, self-contained single-user deployments, and CI test runs.

The migration strategy is:

1. **Provider target decision now, runtime switch later**: PostgreSQL is the production target, but the current application runtime still hard-wires `UseSqlite()` in `Taskdeck.Infrastructure.DependencyInjection`. Adding runtime provider selection and Npgsql registration is follow-up implementation work, not something this ADR PR ships.

2. **SQLite-backed compatibility baseline first**: `DatabaseProviderCompatibilityTests` establishes the persistence behaviors that PostgreSQL support must preserve. Today those tests run against SQLite only. A future follow-up can add a provider-switching test factory and opt-in PostgreSQL execution once the runtime path exists.

3. **Schema migration remains blocked on follow-up implementation**: EF Core migrations stay the source of truth, but PostgreSQL schema application cannot be treated as ready until SQLite-only migration SQL (notably the FTS5 migration) is wrapped in provider-conditional logic and the application/test infrastructure can actually build `UseNpgsql()` contexts.

4. **Data migration planning uses row-count and foreign-key integrity verification**: The runbook documents dependency-ordered export/import and verification steps, but it is explicitly preparatory until the provider-switching and PostgreSQL-safe migration work lands.

## Alternatives Considered

### SQL Server

- **Pros**: First-class EF Core support, strong enterprise adoption, Azure-native.
- **Cons**: License cost for production use (Express edition has 10 GB limit), heavier resource footprint than PostgreSQL, less natural fit for the open-source/local-first ethos. Cloud portability is weaker — Azure SQL is easy, but AWS and GCP managed SQL Server options are more expensive and less common.
- **Verdict**: Rejected. The open-source, multi-cloud positioning of PostgreSQL better fits Taskdeck's platform expansion goals.

### CockroachDB

- **Pros**: PostgreSQL wire-compatible, built-in distributed SQL, strong horizontal scaling.
- **Cons**: Operational complexity disproportionate to Taskdeck's near-term scale requirements. CockroachDB's serverless tier has cold-start latency. EF Core compatibility is good but not identical to native PostgreSQL (some DDL differences, limited FTS support). The team would be adopting two new technologies (PostgreSQL dialect + CockroachDB operations) simultaneously.
- **Verdict**: Rejected for initial production deployment. Can be revisited if Taskdeck reaches scale requiring distributed SQL, since the PostgreSQL migration path makes CockroachDB a viable future option.

### MySQL / MariaDB

- **Pros**: Wide adoption, managed options on all clouds.
- **Cons**: EF Core's MySQL provider (Pomelo) is community-maintained rather than Microsoft-supported. `DateTimeOffset` handling requires workarounds. GUID storage is less ergonomic than PostgreSQL's native `uuid` type. Full-text search capabilities are weaker.
- **Verdict**: Rejected. PostgreSQL offers better EF Core alignment and richer type support.

### Keep SQLite for all deployments

- **Pros**: Zero migration effort, proven local-first behavior.
- **Cons**: Database-level write locking makes concurrent multi-user access impractical. No connection pooling. Backup and replication require file-system-level coordination. Not viable for the cloud/collaboration milestones in the platform expansion strategy.
- **Verdict**: Rejected for hosted deployments. SQLite remains the default for local/single-user mode.

## Consequences

### Positive

- PostgreSQL is open-source (PostgreSQL License), eliminating license cost concerns.
- Native `uuid`, `timestamptz`, `jsonb`, and full-text search types align well with the existing domain model (GUIDs, DateTimeOffset, JSON metadata columns, knowledge document FTS).
- EF Core's Npgsql provider is mature, Microsoft-co-maintained, and supports all EF Core features used in the project.
- Managed PostgreSQL is available on AWS (RDS/Aurora), Azure (Flexible Server), and GCP (Cloud SQL) with sub-$20/month entry points.
- The SQLite-backed compatibility harness documents the persistence behaviors that PostgreSQL support must preserve.
- CockroachDB remains a future option due to PostgreSQL wire compatibility.

### Negative

- The runtime application projects do not yet reference `Npgsql.EntityFrameworkCore.PostgreSQL` or expose a provider-selection path.
- SQLite-specific constructs (FTS5 virtual tables in `KnowledgeDocuments`) require provider-conditional migration code before PostgreSQL schema creation is possible.
- CI and the API test factory do not yet have a PostgreSQL execution path.
- The current runbook is therefore a preparatory operator document, not a fully executable migration recipe.

### Neutral

- No domain-layer or application-layer changes are required — the provider switch is entirely in Infrastructure and DI configuration.
- The existing `IsSqlite()` pattern (used in `AgentRunRepository`) provides a precedent for provider-conditional logic.
- Local development continues to use SQLite unless a developer opts into PostgreSQL.

## References

- Issue: #84 (PLAT-01: SQLite-to-production DB migration strategy)
- ADR-0001: Clean Architecture Layering
- ADR-0014: Platform Expansion — Four Pillars
- Issue #531: Platform expansion master tracker
- Issue #537: Cloud/collaboration pillar
- EF Core PostgreSQL provider: https://www.npgsql.org/efcore/
- Migration runbook: `docs/platform/SQLITE_TO_POSTGRES_MIGRATION_RUNBOOK.md`
