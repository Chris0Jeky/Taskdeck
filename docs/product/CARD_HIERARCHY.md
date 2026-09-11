# Card parents

Cards may have one optional parent on the same board. Task, Epic and Spike are equally eligible.
The complete hierarchy, including archived descendants, supports at most three links (four levels).
Self-parenting, cycles, missing parents, cross-board parents and moves that make a subtree too deep
are rejected. Restore an archived card before assigning it as a parent or changing its own parent.

The shared Paper and Legacy card editor offers a Parent card selector. Saving requires the card's
current version. Parent changes preserve IDs, columns, positions, labels, comments and history.
This groups related work without requiring a new capture flow or changing review-first trust.

## Archive and delete

Before archiving or deleting a parent, request its detach preview and confirm the complete list.
Every direct child becomes parentless, including archived children. Grandchildren stay attached to
their own parent. This is an atomic change with per-child audit records and board realtime events.
The parent action does not delete, archive, restore or move any child. Restoring the parent never
reattaches children. A column containing any card, including an archived card, cannot be deleted.

The preview includes target timestamp, every affected child's ID/title/archive state/version, and
a deterministic `v1:` SHA-256 fingerprint of sorted child ID, parent ID and UTC updated-at ticks.
The confirmation pins the displayed timestamp and fingerprint. A child added, removed, reparented
or changed after preview causes a conflict; refresh and explicitly confirm the new list. Missing
fingerprints are compatible only when the authoritative direct-child set is empty.

Graph decisions read the board concurrency token before the complete card graph. Parent assignment,
creation with a parent, archive, restore, delete and empty-column deletion advance that token without
changing the board metadata timestamp. Competing graph writes cannot commit a cycle or excess depth.
Ordinary independent card writes retain their existing concurrency behavior; an ordinary write racing
a hierarchy mutation can receive a retryable conflict.

## API, proposals and MCP

- Card DTOs and create requests add nullable `parentCardId`. Update requests use `parentCardId` to
  assign or `clearParent: true` to remove, with `expectedUpdatedAt`; both cannot be supplied together.
- `GET /api/boards/{boardId}/cards/{cardId}/detach-preview` requires write access and returns the full
  confirmation snapshot. Archive bodies and delete query parameters accept `expectedUpdatedAt` and
  `expectedChildrenFingerprint` from that preview.
- Existing create/update proposal operations carry these same parent fields. Archive-lifecycle and
  delete operations carry the immutable detach pins. Preview, approval and Apply validate current
  state. The readable diff lists every derived detachment from the same validated snapshot.
- One hierarchy-affecting operation per board proposal is supported. A detach proposal cannot also
  edit its parent or affected children. Use separate proposals; there is no ordered graph simulator.
- MCP card detail exposes parent and detach preview. The existing `update_card` tool accepts
  `parent_card_id` or `clear_parent` plus `expected_updated_at`; `archive_card_lifecycle` accepts the
  preview's `expected_children_fingerprint`. These only create proposals. Explicit approval and Apply
  remain required. Legacy `archive_card` still means Block.

## Portability and migration

`AddCardParentHierarchy` adds one nullable column. Existing cards become parentless; no IDs or
placements change. Its Down migration removes the parent field while retaining cards and other data.

Board JSON with parents uses the `taskdeck-board` version-3 envelope, so old importers reject it
instead of silently dropping relationships. Existing plain and version-2 files remain accepted.
Import preallocates fresh IDs for all source cards, remaps parents even when children come first,
and validates the complete graph before commit. Missing, duplicate or cyclic references roll back
the whole import. A payload naming an archived card as a parent is rejected before any board, column
or card is created, matching the create, update and proposal lanes; an archived child whose parent
stays active still imports. Buffered and streamed account exports include parents within existing
read scopes; account deletion retains the existing account boundary.

Typed relationships, cascaded lifecycle actions and cross-board parents are outside this contract.
