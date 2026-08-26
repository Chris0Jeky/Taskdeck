# ADR-0063: Archived Boards Reject Card Writes Until Restored

- **Status**: Accepted (maintainer scope ruling on `#2080`, 2026-08-24)
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#2080`, `#1973`, `#2114`, ADR-0007, ADR-0056

## Context

Archived-board history is readable, but direct card create, update, move, and delete operations previously remained available. That made the archive disclosure's restore-before-edit meaning unenforced and differed from existing bulk write paths, which already reject archived boards.

## Decision

CardService rejects card create, update, move, and delete operations when the owning board is archived. It returns `ErrorCodes.InvalidOperation`, which the API maps to the stable `409 Conflict` error contract. A board must be restored before a card write can proceed. Every card write touches the board without advancing its concurrency token, so EF issues a conditional board update using the token it read. Board mutations, including archive and restore, advance that token. Therefore an archive that commits after the service read makes the stale card write conflict and roll back rather than mutating archived history, while independent card writes retain their established success semantics.

This guard applies at the shared service boundary used by HTTP, CLI, and proposal/MCP execution callers. Reads remain available. Board restoration and a frontend restore-first affordance are separate work.

The bulk card writers — external import, starter-pack apply, and archive-item restore — join the same conditional board update (`#2114`). They already rejected an archived board on read, but that check ran once before a whole batch was planned, so an archive committing in between was accepted silently. They now take the same non-advancing board touch, so a racing archive turns the batch into `ErrorCodes.Conflict` (`409`) and rolls it back. The token is deliberately still not advanced by any of them, so bulk writers do not invalidate each other or in-flight single-card writes on the same board.

## Consequences

- Archived boards are a read-only history state for card mutations.
- All CardService callers receive the same failure rather than relying on client-side controls.
- Existing archived-board bulk-write precedents and the API's `InvalidOperation` mapping remain consistent.

## Alternatives considered

**Client-only disablement.** Rejected because bookmarked routes, the CLI, MCP, and other callers would still bypass it.

**Implicit unarchive on write.** Rejected because it would make a deliberate archive state silently mutable.

**Restore-first user interface.** Deferred. It may improve recovery ergonomics but does not replace the service-level safety boundary.
