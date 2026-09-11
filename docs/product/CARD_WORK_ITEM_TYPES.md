# Card work item types

Cards have exactly one work item type: `Task`, `Epic`, or `Spike`. Existing cards and
create requests without `workItemType` default to `Task`. These are explicit user
choices; capture actions, decisions and questions do not automatically select a type.
[Parent links](CARD_HIERARCHY.md) are also implemented. Type and containment remain independent:
any admitted type may parent another, within the same-board three-link/four-level limit.

The shared Paper and Legacy card editor displays the type and lets a board writer
change it. Quick card creation still creates a Task; open the card to select Epic
or Spike. Save uses the displayed card version. A failed save keeps the draft and
shows recovery guidance. Archived cards and boards cannot accept type changes.
Changing type retains the card ID, column, position, labels, block state and history.

## API and proposals

`POST /api/boards/{boardId}/cards` accepts optional `workItemType`. `PATCH` on a card
accepts it with `expectedUpdatedAt`; omitting the type leaves it unchanged. Allowed
wire values are the case-sensitive strings `Task`, `Epic` and `Spike`. Unknown types
return 400; a stale displayed version returns 409 without updating the card.
Existing claims and board permissions apply.

Proposal `card/create` and `card/update` operations accept the same type strings.
A type update requires `expectedUpdatedAt` and must be the only operation for that
card in the proposal. Preview names the old and new types; Apply repeats validation.
Proposing, previewing and approving do not change the card. Explicit Apply remains
required. Existing proposals without the field retain their previous behavior.

MCP `create_card` and `update_card` accept optional `work_item_type`. Type updates
also require `expected_updated_at` from a fresh card read. Both tools only propose;
they cannot apply the change. MCP reads include the type and current timestamp.

Board JSON export/import preserves types, including archived cards, while imported
cards retain the existing fresh-ID policy. Older files default to Task. Buffered
and streamed account exports include the same type through scoped card projections.
There are no new account identifiers or changes to account deletion.

## Migration

`AddCardWorkItemType` adds a required integer column with default 0 (Task), without
replacing card rows. Epic is 1 and Spike is 2. Down removes the type column while
retaining the cards and their other fields. Type distinctions are lost on downgrade;
reapplying the migration defaults every existing card to Task.
