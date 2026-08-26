# ADR-0063: Archived Boards Reject Card Writes Until Restored

- **Status**: Accepted (maintainer scope ruling on `#2080`, 2026-08-24)
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#2080`, `#1973`, ADR-0007, ADR-0056

## Context

Archived-board history is readable, but direct card create, update, move, and delete operations previously remained available. That made the archive disclosure's restore-before-edit meaning unenforced and differed from existing bulk write paths, which already reject archived boards.

## Decision

CardService rejects card create, update, move, and delete operations when the owning board is archived. It returns `ErrorCodes.InvalidOperation`, which the API maps to the stable `409 Conflict` error contract. A board must be restored before a card write can proceed. Each accepted card write also advances a board concurrency token; archive advances the same token. EF persists that token as a concurrency predicate, so an archive that commits after the service read makes the stale card write conflict and rolls back rather than mutating archived history.

This guard applies at the shared service boundary used by HTTP, CLI, and proposal/MCP execution callers. Reads remain available. Board restoration and a frontend restore-first affordance are separate work.

## Consequences

- Archived boards are a read-only history state for card mutations.
- All CardService callers receive the same failure rather than relying on client-side controls.
- Existing archived-board bulk-write precedents and the API's `InvalidOperation` mapping remain consistent.

## Alternatives considered

**Client-only disablement.** Rejected because bookmarked routes, the CLI, MCP, and other callers would still bypass it.

**Implicit unarchive on write.** Rejected because it would make a deliberate archive state silently mutable.

**Restore-first user interface.** Deferred. It may improve recovery ergonomics but does not replace the service-level safety boundary.
