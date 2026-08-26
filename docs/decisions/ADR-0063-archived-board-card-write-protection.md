# ADR-0063: Archived Boards Reject Card Writes Until Restored

- **Status**: Accepted (maintainer scope ruling on `#2080`, 2026-08-24)
- **Date**: 2026-08-26
- **Deciders**: Chris0Jeky (maintainer)
- **Related**: `#2080`, `#1973`, `#2114`, ADR-0007, ADR-0056

## Context

Archived-board history is readable, but direct card create, update, move, and delete operations previously remained available. That made the archive disclosure's restore-before-edit meaning unenforced and differed from existing bulk write paths, which already reject archived boards.

## Decision

CardService rejects card create, update, move, and delete operations when the owning board is archived. It returns `ErrorCodes.InvalidOperation`, which the API maps to the stable `409 Conflict` error contract. A board must be restored before a card write can proceed. Every card write marks the board row modified without advancing its concurrency token, so EF issues a conditional board update using the token it read. Board mutations, including archive and restore, advance that token. Therefore an archive that commits after the service read makes the stale card write conflict and roll back rather than mutating archived history, while independent card writes retain their established success semantics.

This guard applies at the shared service boundary used by HTTP, CLI, and proposal/MCP execution callers. Reads remain available. Board restoration and a frontend restore-first affordance are separate work.

The bulk writers — external import, starter-pack apply, and archive-item restore (its column half included: `RestoreColumnAsync` writes no cards, but `RestorePlanner`'s archived-target check governs every non-board restore, so guarding only the card half would leave the identical window open on the same predicate) — join the same conditional board update (`#2114`). They already rejected an archived board on read, but that check ran once before a whole batch was planned, so an archive committing in between was accepted silently. They now take the same non-advancing board touch, so a racing archive turns the batch into `ErrorCodes.Conflict` (`409`) and rolls it back. The token is deliberately still not advanced by any of them, so bulk writers do not invalidate each other or in-flight single-card writes on the same board.

## Consequences

- Archived boards are a read-only history state for card mutations.
- All CardService callers receive the same failure rather than relying on client-side controls.
- Existing archived-board bulk-write precedents and the API's `InvalidOperation` mapping remain consistent.

## Amendment 2026-08-26 — the board row is marked modified by a guard marker, not by `UpdatedAt` (`#2115`, `#2123`)

As first shipped, "marks the board row modified" meant re-stamping `Board.UpdatedAt` with the current
time. Two defects followed from overloading a user-visible field as a concurrency mechanism. The
per-user board list is cached (`CacheKeys.BoardListForUser`, `Cache:BoardListTtlSeconds`) and
CardService has no cache awareness, so every card write left that list serving a stale board
timestamp until the TTL expired (`#2115`). And re-stamping a timestamp with the current time is a
no-op inside one clock tick: the entity then stays `Unchanged`, EF emits no board update, and the
token predicate silently does not run — the guarantee this ADR records as unconditional was in fact
conditional on the clock advancing between two writes (`#2123`).

`Board` therefore carries `CardMutationMarker`, a monotonic `long` advanced by `RecordCardMutation`
and read by nothing. It is in no DTO, API contract, projection, or query; migration
`20260826201256_AddBoardCardMutationMarker` adds it as `INTEGER NOT NULL DEFAULT 0`. An incremented
counter always differs from the value that was read, so the board row joins the write's `UPDATE`
unconditionally and the token predicate always runs. It is not a reliable mutation count — two
writers that read the same value both persist value + 1 — which is harmless because nothing compares
it against an expected value. The token is still not advanced, so independent card writes keep their
established success semantics.

Consequence: `Board.UpdatedAt` reverts to meaning "board metadata last changed", as it did before this
guard existed. Card activity no longer feeds the board recency that `WorkspaceService` surfaces on
Home. That is a return to pre-guard behaviour rather than a new choice; making board recency reflect
card activity would be a deliberate product change and would need its own cache-invalidation design,
because the cached board list is keyed per user and a board is readable by more than its owner.

## Alternatives considered

**Client-only disablement.** Rejected because bookmarked routes, the CLI, MCP, and other callers would still bypass it.

**Implicit unarchive on write.** Rejected because it would make a deliberate archive state silently mutable.

**Restore-first user interface.** Deferred. It may improve recovery ergonomics but does not replace the service-level safety boundary.

**Invalidating the board-list cache from CardService instead of adding a marker (`#2115`).** Rejected. The cache is keyed per user and a board is readable by its owner *and* by everyone holding a `BoardAccess` row, so every card write would have to enumerate that membership and evict each entry — a membership query per card write, and a correctness bug the moment a new sharing path forgets to evict. It would also leave `#2123` open, since the board row would still enter the update only when the clock advanced.

**Forcing the row modified with `Entry(board).Property(b => b.UpdatedAt).IsModified = true` (`#2123`).** Rejected. It fixes the same-tick no-op but keeps a user-visible field as the carrier, so the stale-cache defect survives, and it puts an EF change-tracker call in the Application layer, which the layer boundaries do not permit.
