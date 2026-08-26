# ADR-0061: Trusted Shared Instance and Managed SaaS Boundary

- **Status**: Proposed
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer, decision pending)
- **Related**: `#1772`, `#1325`, `#1879`, `#2012`, ADR-0002, ADR-0012, ADR-0023,
  ADR-0025, ADR-0044

## Context

Taskdeck already has authentication, registration modes, board access roles, per-board SignalR,
health checks, a combined frontend/API container, and SQLite persistence. Those capabilities make a
private shared deployment plausible, but they do not make a dependable team service or managed
public SaaS complete.

Static frontend hosting also does not provide the API, authentication, persistent SQLite data, or
SignalR service. It cannot be used as collaboration evidence.

## Proposed decision

Treat collaboration hosting as three distinct milestones.

### 1. Trusted shared instance

Use one invite-only container for a few known users. This is the v0.3 collaboration proof owned by
`#1772` and extends, rather than duplicates, the friends-and-family work in `#1325`.

While SQLite is used, the milestone requires:

- exactly one application instance and one persistent volume;
- WAL, short write transactions, and database-authoritative state after reconnect;
- Closed or InviteOnly registration;
- verified HTTPS and SignalR/WebSocket proxy behavior;
- optimistic concurrency for collaborative writes and explicit stale-write UX;
- backup of both SQLite and the connector-encryption key;
- one real restore drill;
- two-user permission, reconnect, and destructive-action walkthroughs;
- BYO or explicitly operator-funded LLM credentials with cost and egress disclosure.

This remains local-first in ownership and self-hostability. Browser clients still depend on the
server; this milestone is not offline browser/cloud synchronization.

### 2. Dependable small-team alpha

Harden the trusted deployment for regular use: invitation and member-management UX, human/agent
attribution, conflict handling, realtime recovery, backups, monitoring, support diagnostics,
concurrency testing, and representative board performance.

### 3. Managed public SaaS

Treat managed SaaS as a separate later product and operating model. It requires tenancy,
PostgreSQL, billing and entitlements, account recovery and transactional email, abuse controls,
observability, privacy and legal operations, incident response, disaster recovery, and support.
Approval of a trusted instance does not approve this milestone.

Move from SQLite when multiple API instances, measured write contention, point-in-time recovery,
approved multi-tenant hosting, or operational isolation justify it. PostgreSQL is not a prerequisite
for the single-instance trusted proof.

## Decisions still required

The maintainer must decide:

1. whether the v0.3 proof is maintainer-operated self-hosting or the start of a managed service;
2. who pays for LLM usage in a shared instance;
3. whether the commercial/licensing decision in `#2012` blocks any public hosted path.

The proposed default is maintainer-operated trusted self-hosting, BYO provider keys where practical,
and no managed-service commitment before `#2012` and retention evidence.

## Alternatives considered

**Launch a public managed service from the current container.** Rejected because a public URL does
not supply tenancy, billing, recovery, abuse controls, operations, or support.

**Require PostgreSQL before any collaboration proof.** Rejected because it delays direct evidence
and is unnecessary for one application instance with a few trusted users.

**Treat the static demo as hosted collaboration.** Rejected because it has no durable application
backend or realtime collaboration path.

## Consequences

- `#1772` remains the single trusted-instance issue and links existing readiness work.
- v0.3 can test real collaboration without committing to multi-tenancy or horizontal scaling.
- SQLite operation gains explicit single-instance, backup, restore, and concurrency constraints.
- Managed SaaS remains post-v0.3 and requires a separate accepted decision and operating plan.

