# ADR-0004: Multi-Tenancy Strategy — Shared Schema + TenantId

- **Status**: Accepted
- **Date**: 2026-02-22
- **Deciders**: Project maintainers

## Context

Taskdeck started as a single-tenant SQLite application. The cloud/collaboration roadmap requires supporting multiple users and eventually organizations. Three multi-tenancy models were evaluated for the transition from single-tenant to multi-tenant.

## Decision

Adopt **shared-schema + TenantId** as the immediate target:

- Add `TenantId` column to all tenant-scoped tables.
- Enforce tenant predicates in all repository queries (global query filter in EF Core).
- Cross-tenant access always returns 403 (never leaks existence via 404).
- Preserve existing SQLite storage for local-first single-user mode.

Plan a promotion path to **database-per-tenant** for high-isolation tiers (enterprise, regulated industries) in later versions.

## Alternatives Considered

- **Database-per-tenant**: Strongest isolation, simplest queries (no filter needed), but operational complexity (migrations, connection management, backup per tenant) is prohibitive for an early-stage product.
- **Schema-per-tenant**: Middle ground but PostgreSQL-specific; doesn't work with SQLite; adds schema migration complexity.

## Consequences

- **Positive**: Minimal schema change; works with SQLite and future PostgreSQL; low operational overhead; single migration path.
- **Negative**: Tenant isolation is application-enforced (bug = data leak); query performance degrades at extreme tenant counts without partitioning; all tenants share resource limits.
- **Neutral**: Promotion to database-per-tenant is additive, not a rewrite.

## References

- `docs/analysis/2026-02-22_multi-tenancy-strategy-adr.md` — full comparison document
- ARCH-01 in `docs/IMPLEMENTATION_MASTERPLAN.md`
