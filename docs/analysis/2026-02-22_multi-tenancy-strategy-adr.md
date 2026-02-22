# ARCH-01 ADR: Multi-Tenancy Strategy and Tenant-Isolation Readiness

Date: 2026-02-22  
Status: Accepted  
Issue: `#71`

## Context

Taskdeck currently runs as a single-tenant local-first baseline (SQLite, owner/board-access auth model, claims-first identity).  
Future expansion requires tenant isolation without breaking current delivery velocity, current auth policy (`403` for authenticated unauthorized access), or existing board-level collaboration semantics.

This ADR compares:
- `database-per-tenant`
- `schema-per-tenant`
- `shared-schema + TenantId`

and selects a phased target model plus readiness criteria.

## Decision Drivers

1. Preserve current development speed and migration simplicity.
2. Enforce deterministic tenant isolation in authz and data access.
3. Support incremental rollout from current single-tenant production baseline.
4. Keep a path open for stronger enterprise isolation when required.
5. Minimize operational overhead while platform maturity tracks (`#84`, `#85`, `#86`, `#111`) are still open.

## Options Considered

## Option A: Database-Per-Tenant

Pros:
- strongest runtime isolation boundary
- simpler legal/compliance story for high-assurance customers
- blast radius naturally constrained

Cons:
- highest operational complexity (provisioning, migrations, pooling, backup fan-out)
- heavier cost/monitoring overhead for small tenants
- premature burden for current single-tenant deployment posture

Taskdeck fit now:
- not selected as immediate default
- retained as a future promotion path for high-isolation tiers

## Option B: Schema-Per-Tenant

Pros:
- stronger separation than shared schema
- easier shared-instance operations than full DB-per-tenant

Cons:
- weak fit for current SQLite baseline and local-first posture
- schema lifecycle complexity still high
- migration tooling and test setup complexity significant

Taskdeck fit now:
- rejected as primary path

## Option C: Shared Schema + TenantId

Pros:
- lowest migration friction from current baseline
- single migration stream and simpler operational model
- works with current architecture and issue sequencing

Cons:
- correctness relies on strict tenant-scoping discipline everywhere
- cross-tenant leakage risk if any query/auth path omits tenant constraints
- needs stronger test and guardrail discipline

Taskdeck fit now:
- selected as primary target model

## Decision

Adopt `shared-schema + TenantId` as the immediate multi-tenant target, with a planned promotion path to `database-per-tenant` for high-isolation tiers.

Key policy:
- every tenant-owned resource must include immutable `TenantId`
- tenant scoping must be enforced in both authz and data access layers
- authenticated cross-tenant access attempts must return `403`
- true missing resources within tenant scope remain `404`

## Phased Rollout Plan

## Phase 0: Foundations (no tenant behavior change)

1. Introduce domain abstractions:
- `Tenant`
- membership mapping (`UserTenantMembership` / role per tenant)
- authenticated tenant context resolution contract
2. Add architecture guardrails for tenant-scoped repository/query patterns.
3. Add tenant context propagation conventions (API -> Application -> Infrastructure).

## Phase 1: Data Model and Migration Baseline

1. Add `TenantId` to tenant-owned entities (boards, columns, cards, labels, logs/audit rows, automation/chat artifacts where applicable).
2. Backfill existing rows to a seeded default tenant.
3. Add composite indexes (`TenantId` + frequently filtered keys) and unique constraints scoped by `TenantId`.
4. Keep migrations reversible and aligned with future DB migration strategy (`#84`).

## Phase 2: Enforcement

1. Require tenant context at authenticated entry points.
2. Enforce tenant predicates in repositories/specifications before materialization.
3. Enforce tenant checks in service-level authorization paths.
4. Expand API contract tests for cross-tenant access to deterministic `403`.

## Phase 3: Data Mobility and Operations

1. Make export/import tenant-scoped by default.
2. Make backup/restore tenant-aware (logical and physical strategies) with DR alignment (`#86`).
3. Add tenant-level audit/event tagging and observability dimensions.

## Phase 4: Enterprise Isolation Promotion Path

1. Define qualification criteria for DB-per-tenant migration.
2. Introduce tenant-aware connection resolution and migration orchestration.
3. Support mixed mode (`shared` + `dedicated`) with deterministic routing.

## Tenant-Isolation Readiness Checklist

## Auth and Identity

- [ ] authenticated principal includes a resolved active tenant context
- [ ] tenant membership and role checks exist before board/resource authorization
- [ ] tenant switching is explicit and auditable

## Authorization and API Semantics

- [ ] cross-tenant authenticated access returns `403` (never silent fallback)
- [ ] missing-in-tenant resources return `404`
- [ ] admin/internal endpoints define explicit tenant bypass policy (default deny)

## Query and Persistence Safety

- [ ] every tenant-owned query predicate includes `TenantId`
- [ ] write paths assert tenant ownership before mutation
- [ ] uniqueness constraints are tenant-scoped
- [ ] bulk/background jobs apply tenant partitions, not global scans

## Export/Import and Backups

- [ ] export defaults to tenant-scoped payloads
- [ ] import validates tenant ownership and rejects cross-tenant references
- [ ] backup/restore runbooks define tenant-granular and full-system recovery

## Observability and Audit

- [ ] audit records include tenant identity and actor identity
- [ ] traces/metrics include tenant dimensions (with PII-safe tagging policy)
- [ ] alerting includes tenant-isolation breach signals

## Test Strategy (Cross-Tenant Isolation)

1. Unit tests:
- tenant context resolution rules
- tenant-aware authorization helpers
- repository/specification predicate construction

2. Application tests:
- service-level read/write flows reject cross-tenant access with deterministic errors
- tenant-scoped list/filter paths do not leak foreign-tenant rows

3. API integration tests:
- authenticated cross-tenant access returns `403`
- true missing resource in-tenant returns `404`
- tenant-scoped list endpoints never materialize or return foreign-tenant entities

4. E2E tests:
- two-tenant browser sessions cannot read/mutate each other's entities
- invitation/membership and tenant-switching flows enforce boundaries

5. Load/concurrency tests:
- include mixed-tenant traffic slices in harness scenarios
- assert no cross-tenant leakage under concurrent operations

## Follow-Through Mapping

This ADR defines strategy only; implementation is staged into existing roadmap tracks:
- `#84` DB migration strategy
- `#85` distributed cache strategy (tenant key partitioning)
- `#86` backup/restore DR playbook
- `#111` cloud topology/autoscaling ADR
- security/policy hardening tracks (`#80`, `#81`, `#82`, `#83`, `#110`)

Immediate next architectural execution target after this ADR: tenant context + `TenantId` propagation design slice aligned to Priority II sequencing.
