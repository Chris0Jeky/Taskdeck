# ADR-0002: Claims-First Identity Model

- **Status**: Accepted
- **Date**: 2026-01 (Q1 security retrofit)
- **Deciders**: Project maintainers

## Context

Early controller implementations accepted caller-supplied user IDs in request bodies or query parameters for convenience. This created a spoofing vector: an authenticated user could supply another user's ID to access their resources. As Taskdeck moved toward multi-user and eventually hosted deployment, this pattern was unacceptable.

## Decision

Never trust caller-supplied identity for protected resources. Always derive the actor's identity from authenticated JWT claims (`sub` / `nameidentifier`). Remove all caller-supplied `userId` parameters from protected endpoints. Formalize as Golden Principle GP-02.

Retrofitted across all controller families: boards, columns, cards, labels, export/import, audit, llm-queue, board-access, users, chat, notifications, automation-proposals, archive, ops-cli, and logs.

## Alternatives Considered

- **Keep caller-supplied IDs with authorization checks**: Would allow admin impersonation but adds complexity and attack surface; rejected in favor of simpler, safer model.
- **API key per-resource**: Too granular for a local-first app; doesn't map to the JWT-based auth already in place.

## Consequences

- **Positive**: Eliminates identity spoofing; simplifies authorization logic (one path to actor identity); enables straightforward cross-user 403 enforcement.
- **Negative**: Breaking change for any external tooling that supplied user IDs; required touching every controller family.
- **Neutral**: Frontend audit view switched from user-id route calls to `/audit/users/me`.

## References

- `docs/GOLDEN_PRINCIPLES.md` — GP-02 Claims-First Identity
- SEC-11 convergence wave: `#152`
- `docs/STATUS.md` — auth posture section
