# ADR-0004: Multi-Tenancy Strategy — Shared Schema + TenantId

- **Status**: Accepted
- **Date**: 2026-02-22
- **Deciders**: Project maintainers

> **Archive-pivot note (2026-06-13):** The *multi-organization / hosted-SaaS tenancy* expansion premise is de-scoped — Taskdeck is single-user, local-first, SQLite-only per the archive pivot (see ADR-0038 and `docs/STATUS.md`). The **cross-user isolation behaviour this ADR set out to protect remains LIVE** — but it is enforced by per-`UserId` and board-access predicates plus the `403`/`404` cross-user existence policy (consistent with GP-02 Claims-First Identity and GP-03 Stable Error Contracts), **not** by the shared-schema `TenantId` mechanism in the Decision below. That `TenantId` column model was **never implemented**: there is no `TenantId` symbol in `backend/src`, `backend/tests`, or the frontend. The Decision section is retained as the original proposed design. Park the multi-tenant scale-out / multi-org premise and do not resurrect `TenantId`-keyed tenancy — but do not treat the live cross-user isolation security model as historical.

## Context

Taskdeck started as a single-tenant SQLite application. The cloud/collaboration roadmap requires supporting multiple users and eventually organizations. Three multi-tenancy models were evaluated for the transition from single-tenant to multi-tenant.

## Decision

> **Never implemented** (see the archive-pivot note above): the `TenantId` design below was the proposed target as of 2026-02-22 but was never built. The app shipped cross-user isolation via per-`UserId` and board-access predicates instead. The section is retained as the original decision record.

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
