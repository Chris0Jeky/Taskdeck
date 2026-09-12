# Typed card relations

Implementation contract for [#2092](https://github.com/Chris0Jeky/Taskdeck/issues/2092), under
[ADR-0060](../decisions/ADR-0060-canonical-work-model-and-compatibility-path.md). This candidate
is being implemented and has not completed integration or hosted qualification.

## Meaning and scope

Relations connect two existing cards on the same board. They do not grant access, change card
status, assign work, establish parenthood, move cards, or change estimates. In particular, a
**blocks** relation is different from setting a card's existing blocked state.

| Input | Meaning | Canonical storage |
| --- | --- | --- |
| A relates-to B | An undirected association | One pair in deterministic endpoint order |
| A blocks B | A is a prerequisite of B | A → B, `blocks` |
| A depends-on B | B is a prerequisite of A | B → A, `blocks` |
| A duplicates B | B is the original represented by A | A → B, `duplicates` |
| A spawned-from B | B is A's recorded origin | A → B, `spawned-from` |

The original/origin directions adopt the assumption recorded in the issue's September 11 contract
preparation. They are implementation choices within the accepted same-board direction, rather than
an additional maintainer ruling. Each directed kind rejects cycles independently. All kinds reject
self-links, duplicate canonical edges, missing endpoints and cross-board endpoints. The board
admits at most 500 canonical relations in total; the database also enforces uniqueness under races.

## Compatibility and lifecycle

One relational edge store holds every kind, including existing dependencies. The existing
`BoardDependencies` row remains the board's revision header, including when no edges exist.
`GET/PUT /api/boards/{boardId}/dependencies` retains its existing shape and revision behavior through
an adapter. A legacy full active-graph replacement changes only active dependency edges, retaining
other relation kinds and edges incident to archived cards. A separate writable JSON graph is not
retained as another source of truth.

Archive preserves incident links and invalidates the editable projection's revision. Hard deletion
removes incident links atomically with an actor-attributed board audit receipt; its existing legacy
revision behavior is retained. Physical board deletion removes its links. Account deletion preserves
links on the shared boards and cards that the existing erasure contract retains.

## Reads and reviewed writes

`GET /api/boards/{boardId}/relations` requires board read authority and returns `boardId`, `revision`,
`relations` and `canWrite`. Each relation has `sourceCardId`, `targetCardId` and a canonical string
`relationType`. Reads include retained links with archived endpoints. New typed changes require
two active endpoints and a writable, unarchived board.

New typed writes create proposals through the existing review and Apply flow. The card-targeted
actions are `add-relation` and `remove-relation`; their parameters are `boardId`, `cardId`,
`relatedCardId`, `relationType` and the caller's observed `expectedRevision`. Producers do not
silently replace that version with a newer read. The existing direct dependency editor keeps its
compatibility route.

The same canonicalization and graph rules govern preview and Apply. A proposal can contain one
explicit relation operation, including references to cards created earlier in that proposal.
Mixing that operation with lifecycle archive/restore or card deletion is refused in this first
slice. Unrelated edits retain their existing contracts. A stale graph or competing endpoint change
fails the whole transaction; audit and realtime publication follow successful commit.

MCP exposes `get_board_card_relations` with Read scope and `add_card_relation` /
`remove_card_relation` with Propose scope. Chat uses `propose_add_card_relation` and
`propose_remove_card_relation`. These producers preserve the authenticated principal and proposal
provenance; neither supplies an approval or direct typed-mutation capability.

## Portability and downgrade

Board JSON uses a version-5 envelope when typed relations are present. The legacy dependency
projection may coexist only when it matches the canonical relation projection; contradictory or
duplicate input is rejected. Existing legacy and version-2 through version-4 files remain accepted.
Both endpoints use the existing `SourceId` map when import creates fresh card IDs. Preview and
invalid imports retain their existing rollback guarantees. Archived links remain portable.

Both buffered and streaming account exports include relations only within their existing authorized
board/card content scope. Excluding card content also excludes its relations. This adds no account
import route and widens no export authority.

The in-place migration preserves card IDs and the current graph revisions. It maps surviving
same-board legacy prerequisites to inverse `blocks` edges, including archived endpoints, and omits
already-dangling JSON references left by hard deletion. Downgrade reconstructs the **latest**
dependency graph, including edits made after upgrade and empty/new revision headers. Other kinds
have no legacy representation: downgrade loses their relation metadata and cannot recreate it on
re-upgrade. Preserve a supported backup before a downgrade.

## Qualification still required

The candidate must prove the legacy dependency adapter, canonical rules and uniqueness, endpoint
and graph concurrency, ordered proposal rollback and attribution, board/account portability,
migration/Down/Up, permissions, realtime ordering and both frontend experiences. Local build or
source review alone does not establish this delivery. Physical-device, screen-reader and release
acceptance remain separate in [OUTSTANDING_TASKS](../../OUTSTANDING_TASKS.md).
