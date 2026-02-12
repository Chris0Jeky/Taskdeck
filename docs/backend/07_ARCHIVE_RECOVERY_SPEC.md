# Archive Recovery Specification

Last Updated: 2026-02-12

## 1. Objective

Implement backend support for archive inventory and restore flows required by frontend archive route:
- list recoverable archived entities,
- restore archived entity with conflict checks,
- provide full audit trail.

## 2. Current Reality

- Boards support archive state (`IsArchived`).
- Columns and cards are currently delete-oriented in active flows.
- No dedicated API exists for archive inventory or restore.

## 3. Design Decision

Use a dedicated archive inventory model:
- `ArchiveItem` table stores restorable snapshots/tombstones.
- board archive uses live state + archive inventory metadata.
- card/column deletes create archive snapshots for optional recovery.

This avoids requiring immediate soft-delete conversion for every entity.

## 4. Data Contract

`ArchiveItem` fields:
- `Id`
- `EntityType` (`board`, `column`, `card`)
- `EntityId`
- `BoardId`
- `Name`
- `ArchivedByUserId`
- `ArchivedAt`
- `Reason` (optional)
- `SnapshotJson`
- `RestoreStatus` (`Available`, `Restored`, `Expired`, `Conflict`)
- `RestoredAt`
- `RestoredByUserId`

## 5. API Contract

- `GET /api/archive/items`
  - query params:
    - `entityType` optional
    - `boardId` optional
    - `status` optional (default `Available`)
    - `limit` and `cursor`

- `POST /api/archive/{entityType}/{id}/restore`
  - body:
    - `targetBoardId` optional
    - `restoreMode` (`inPlace`, `copy`)
    - `conflictStrategy` (`fail`, `rename`, `appendSuffix`)

## 6. Restore Rules

1. Access check:
   - user must satisfy `ArchiveRestore` policy for target board.
2. Conflict check:
   - title/name collisions,
   - missing parent container,
   - position index conflicts.
3. Validation:
   - snapshot schema version compatibility,
   - entity-level domain validation.
4. Restore:
   - apply entity reconstruction.
   - mark archive item status accordingly.

## 7. Audit and Logs

Required audit events:
- `ArchiveCreated`
- `ArchiveRestoreRequested`
- `ArchiveRestoreSucceeded`
- `ArchiveRestoreFailed`

Each includes:
- entity identifiers,
- actor ID,
- conflict strategy,
- correlation ID.

## 8. Expiration and Retention

- default retention: 90 days for archive snapshots.
- expired snapshots move to `Expired` status and are no longer restorable.
- optional policy for permanent purge by admin workflow.

## 9. Error Cases

- snapshot not found -> `404`
- unauthorized restore -> `403`
- conflict with strategy `fail` -> `409`
- invalid snapshot payload -> `422` or `400` based on parser layer

## 10. Test Requirements

Unit:
- snapshot serialization and reconstruction,
- conflict strategy behavior.

Integration:
- archive listing with filters and pagination,
- restore success and conflict paths,
- permission enforcement.

E2E:
- archive entity, restore entity, verify visibility and placement.

## 11. Acceptance Criteria

- archive list endpoint returns actionable restore inventory,
- restore endpoint handles conflict strategies predictably,
- restore operations are permission-gated and fully audited.
