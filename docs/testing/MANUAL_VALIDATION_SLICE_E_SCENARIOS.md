# Manual Validation Slice E: Starter Packs, Archive Recovery, and Activity Traceability

Last Updated: 2026-04-15

Companion references:
- `docs/MANUAL_TEST_CHECKLIST.md` (parent checklist, sections B.5-6, G, O)
- `docs/STATUS.md` (current implementation snapshot)
- `docs/TESTING_GUIDE.md` (test operations reference)
- `docs/testing/manual-validation-a-workspace-board-ux.md` (Slice A)
- `docs/testing/manual-validation-b-authz-contracts.md` (Slice B)

## Purpose

Validate starter pack catalog and application lifecycle, archive/recovery flows, and activity/audit traceability. This slice covers three related but distinct surfaces that share a theme: structured board seeding, soft-delete recovery, and mutation observability.

## Environment Setup

### Prerequisites

1. Clean backend database (remove `backend/src/Taskdeck.Api/taskdeck.db` if present).
2. Start backend:
   ```bash
   dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
   ```
3. Start frontend:
   ```bash
   cd frontend/taskdeck-web
   npm run dev
   ```
4. Open `http://localhost:5173` (or fallback port printed by Vite).
5. Register a test user and obtain a bearer token for API-level scenarios.

### Run Metadata (Record Before and After Each Run)

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | |
| Browser and version | |
| OS | |
| DB baseline | `fresh` / `existing` |
| Env flags changed | |
| Artifacts collected | |

---

## Category 1: Starter Packs

### TST11-SC-001: Browse Starter Pack Catalog

**Goal:** Verify the catalog endpoint returns available packs.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board via `POST /api/boards` | 201 with board ID |
| 2 | `GET /api/boards/{boardId}/starter-packs/catalog` | 200 with array of catalog entries, each containing `packId`, `displayName`, `description`, `tags` |
| 3 | Verify at least one built-in pack exists | Catalog is non-empty |

**Evidence:** Response JSON showing catalog entries.

---

### TST11-SC-002: Preview Pack Contents via Dry Run

**Goal:** Verify dry-run mode previews changes without applying them.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a fresh board | 201 with board ID |
| 2 | `POST /api/boards/{boardId}/starter-packs/apply` with `dryRun: true` and a valid manifest | 200 with `dryRun: true`, `applied: false`, `actions` array listing intended changes |
| 3 | `GET /api/boards/{boardId}/columns` | Board has no new columns (dry-run did not mutate) |
| 4 | `GET /api/boards/{boardId}/labels` | Board has no new labels |

**Evidence:** Dry-run response JSON and empty board state verification.

---

### TST11-SC-003: Apply Pack to Empty Board

**Goal:** Verify a starter pack applies successfully to a board with no existing content.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a fresh board | 201 with board ID |
| 2 | `POST /api/boards/{boardId}/starter-packs/apply` with `dryRun: false` and a manifest containing columns, labels, and seed cards | 200 with `applied: true`, `dryRun: false`, no blocking conflicts |
| 3 | `GET /api/boards/{boardId}/columns` | Columns match manifest (names and positions) |
| 4 | `GET /api/boards/{boardId}/labels` | Labels match manifest (names and colors) |
| 5 | `GET /api/boards/{boardId}/cards` | Seed cards present with correct titles, column assignments, and label bindings |

**Evidence:** Screenshots or JSON responses confirming board matches manifest.

---

### TST11-SC-004: Re-apply Same Pack (Idempotency)

**Goal:** Verify re-applying the same pack does not create duplicates.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Use the board from TST11-SC-003 (pack already applied) | Board has columns, labels, cards from first apply |
| 2 | `POST /api/boards/{boardId}/starter-packs/apply` with same manifest and `dryRun: true` | Response includes conflicts for existing columns/labels (position or name collisions) |
| 3 | Verify `hasBlockingConflicts` is `true` or conflicts array is non-empty | Dry-run correctly detects collisions |
| 4 | `GET /api/boards/{boardId}/columns` | Column count unchanged from step 1 |

**Evidence:** Dry-run conflict response and unchanged board state.

---

### TST11-SC-005: Apply Pack to Board with Existing Content (Conflict Detection)

**Goal:** Verify conflict detection when board already has content at overlapping positions.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board and add a column at position 0 named "Occupied Lane" | Column created at position 0 |
| 2 | `POST /api/boards/{boardId}/starter-packs/apply` with `dryRun: true` and a manifest requesting a column at position 0 | 200 with `dryRun: true`, `conflicts` array containing `ColumnPositionConflict` |
| 3 | Verify conflict entry includes `code`, `path`, `message`, `existingValue`, `incomingValue` | All conflict fields populated |
| 4 | `POST /api/boards/{boardId}/starter-packs/apply` with `dryRun: false` and same manifest | Response has `hasBlockingConflicts: true`, `applied: false` |

**Evidence:** Conflict response JSON with detailed conflict entries.

---

### TST11-SC-006: Dry-Run Preview with Conflict Highlighting

**Goal:** Verify the dry-run response distinguishes blocking vs. warning conflicts.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board with a column at position 0 | Board has one column |
| 2 | `POST /api/boards/{boardId}/starter-packs/apply` with `dryRun: true` and manifest with a column at same position | 200 response |
| 3 | Inspect each conflict entry for `severity` field | Severity is either `"blocking"` or `"warning"` |
| 4 | Verify `hasBlockingConflicts` matches presence of blocking-severity conflicts | Boolean correctly computed |

**Evidence:** Conflict entries with severity annotations.

---

### TST11-SC-007: Pack with Labels, Columns, and Blueprint Cards

**Goal:** Verify a complex manifest with all entity types applies correctly.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a fresh board | Board created |
| 2 | Apply manifest with 3+ labels, 4 columns (one with WIP limit), 1 template, 3 seed cards spanning multiple columns and labels | 200 with `applied: true` |
| 3 | Verify columns have correct WIP limits | `GET /api/boards/{boardId}/columns` shows `wipLimit` values |
| 4 | Verify cards are placed in correct columns | Card `columnId` matches expected column |
| 5 | Verify card-label bindings | Cards have expected label associations |

**Evidence:** Full board state JSON after apply.

---

### TST11-SC-008: Apply Multiple Packs to Same Board

**Goal:** Verify sequential pack applications accumulate content without corruption.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a fresh board and apply "small" fixture pack | Board has 2 columns, 1 label, 1 card |
| 2 | Apply a second manifest with non-overlapping columns (positions 2, 3) and different labels | 200 with `applied: true`, no blocking conflicts |
| 3 | `GET /api/boards/{boardId}/columns` | Board now has 4 columns (original 2 + new 2) |
| 4 | `GET /api/boards/{boardId}/labels` | Board has combined labels from both packs |

**Evidence:** Board state after each apply showing accumulation.

---

### TST11-SC-009: Validate Manifest JSON Endpoint

**Goal:** Verify the manifest validation endpoint checks structural correctness.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | `POST /api/boards/{boardId}/starter-packs/validate-manifest` with valid manifest JSON | 200 with `isValid: true`, empty errors |
| 2 | `POST /api/boards/{boardId}/starter-packs/validate-manifest` with invalid JSON (missing required fields) | 200 with `isValid: false`, errors array with path and message |
| 3 | `POST /api/boards/{boardId}/starter-packs/validate-manifest` with empty body | 400 validation error |

**Evidence:** Validation responses for valid, invalid, and empty inputs.

---

## Category 2: Archive and Recovery

### TST11-SC-010: Archive a Board

**Goal:** Verify archiving a board removes it from the default board list.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board with at least one column and one card | Board visible in `GET /api/boards` |
| 2 | `PUT /api/boards/{boardId}` with `isArchived: true` | 200 with updated board showing `isArchived: true` |
| 3 | `GET /api/boards` (default, no `includeArchived`) | Archived board absent from list |
| 4 | `GET /api/boards?includeArchived=true` | Archived board present with `isArchived: true` |

**Evidence:** Board list before and after archival.

---

### TST11-SC-011: View Archived Boards in Archive Workspace

**Goal:** Verify `/workspace/archive` displays archived boards.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Archive a board (per TST11-SC-010) | Board is archived |
| 2 | Navigate to `/workspace/archive` | View loads with "Archived Boards" section |
| 3 | Verify archived board appears in the list | Board name visible with "board" badge and "Restore Board" button |
| 4 | Verify entity type filter select is present | Dropdown with "All types", "Boards", "Columns", "Cards" options |

**Evidence:** Screenshot of archive view showing archived board.

---

### TST11-SC-012: Restore Archived Board

**Goal:** Verify restoring an archived board returns it to the default board list.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Archive a board | Board archived |
| 2 | Navigate to `/workspace/archive` | Archived board visible |
| 3 | Click "Restore Board" on the archived board (accept confirmation) | Success toast, board removed from archive list |
| 4 | Navigate to `/workspace/boards` | Restored board appears in board list |
| 5 | Open restored board | Board state (columns, cards, labels) preserved |

**Evidence:** Screenshots before restore, after restore, and board detail showing preserved state.

---

### TST11-SC-013: Archive Board with Active Cards (State Preservation)

**Goal:** Verify card state is fully preserved through archive/restore cycle.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board with 3 columns, 5 cards across columns, 2 labels applied to cards | Board has rich state |
| 2 | Record card positions, column assignments, and label bindings | Baseline captured |
| 3 | Archive the board | Board archived |
| 4 | Restore the board | Board restored |
| 5 | `GET /api/boards/{boardId}/cards` | Card positions, column IDs, titles, descriptions, and label bindings match baseline |
| 6 | `GET /api/boards/{boardId}/columns` | Column names, positions, WIP limits match baseline |

**Evidence:** Before/after JSON comparison of board state.

---

### TST11-SC-014: Archive Recovery via Archive API

**Goal:** Verify the archive recovery endpoint restores entities by type.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | `GET /api/archive/items` | Returns list of archive items (may be empty on fresh DB) |
| 2 | `GET /api/archive/items?entityType=board` | Filters to board-type archive items only |
| 3 | If available items exist, `POST /api/archive/{entityType}/{entityId}/restore` with valid body | 200 with restore result showing `success: true` |
| 4 | Verify restored item no longer appears in `GET /api/archive/items` | Item removed from archive |

**Evidence:** Archive items list and restore response.

---

### TST11-SC-015: Restore Conflict Detection (Name Collision)

**Goal:** Verify archive restore handles name collisions gracefully.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create and archive a board named "Project Alpha" | Board archived |
| 2 | Create a new board also named "Project Alpha" | Second board exists with same name |
| 3 | Attempt to restore the archived board | Restore succeeds with name resolution or reports conflict clearly |
| 4 | Verify no data corruption on either board | Both boards accessible with correct state |

**Evidence:** Restore response and both boards' state.

---

### TST11-SC-016: Cross-User Archive Isolation

**Goal:** Verify users cannot see or restore other users' archived items.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | UserA creates and archives a board | Board archived for UserA |
| 2 | UserB calls `GET /api/archive/items` | UserA's archived items not visible to UserB |
| 3 | UserB calls `GET /api/boards?includeArchived=true` | UserA's archived board not in UserB's list |
| 4 | UserA calls `GET /api/archive/items` | UserA's archived items visible |

**Evidence:** Archive item lists for both users showing isolation.

---

### TST11-SC-017: Archive Unauthorized Access

**Goal:** Verify archive endpoints require authentication.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | `GET /api/archive/items` without bearer token | 401 with `errorCode` and `message` |
| 2 | `POST /api/archive/board/{entityId}/restore` without bearer token | 401 |
| 3 | `GET /api/archive/items/{id}` without bearer token | 401 |

**Evidence:** Error response payloads.

---

## Category 3: Activity and Traceability

### TST11-SC-018: View Board-Scoped Activity Timeline

**Goal:** Verify board history shows all mutations within a board.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board, add columns, cards, and labels | Board has mutation history |
| 2 | `GET /api/audit/boards/{boardId}` | 200 with timeline entries including board creation, column additions, card additions, label additions |
| 3 | Verify entries have timestamps and action descriptions | Each entry has meaningful `action`, `timestamp`, and `entityType` |

**Evidence:** Audit timeline JSON for the board.

---

### TST11-SC-019: View Entity-Scoped Activity (Card Level)

**Goal:** Verify entity-level audit returns history for a specific card.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a card on a board | Card exists |
| 2 | Move the card to a different column | Card moved |
| 3 | `GET /api/audit/entities/card/{cardId}` | Timeline shows card creation and move events |
| 4 | Verify each entry references the correct entity | `entityType` is "Card", entity ID matches |

**Evidence:** Card-level audit entries showing create and move.

---

### TST11-SC-020: View User-Scoped Activity

**Goal:** Verify user activity timeline shows the current user's actions.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Perform several board mutations (create board, add card, move card) | Actions recorded |
| 2 | `GET /api/audit/users/me` | 200 with entries for all actions performed by the current user |
| 3 | Verify entries span multiple boards if applicable | Cross-board user timeline |

**Evidence:** User audit timeline JSON.

---

### TST11-SC-021: Verify Audit Entries for Board Mutations

**Goal:** Verify create, rename, archive, and restore actions all generate audit entries.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a board | Audit entry for board creation |
| 2 | Rename the board via `PUT /api/boards/{boardId}` | Audit entry for rename |
| 3 | Archive the board (`PUT` with `isArchived: true`) | Audit entry for archive |
| 4 | Restore the board (`PUT` with `isArchived: false`) | Audit entry for restore/unarchive |
| 5 | `GET /api/audit/boards/{boardId}` | All four mutation types present in timeline |

**Evidence:** Audit timeline showing create, rename, archive, restore entries.

---

### TST11-SC-022: Verify Audit Entries for Proposal Operations

**Goal:** Verify approve, reject, and execute actions on proposals generate audit entries.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Create a capture item and triage it to produce a proposal | Proposal created |
| 2 | Approve the proposal | Audit entry recorded |
| 3 | Execute (apply) the proposal | Audit entry recorded |
| 4 | `GET /api/audit/users/me` | Timeline includes proposal approval and execution entries |

**Evidence:** Audit entries for proposal lifecycle.

---

### TST11-SC-023: Activity View Mode Switching

**Goal:** Verify the Activity view supports mode selection between board, entity, and user.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/activity` | View loads with mode selector (board, entity, user) |
| 2 | Select "board" mode | Board selector appears; entity fields hidden |
| 3 | Select a board and click Fetch | Board history timeline displayed |
| 4 | Switch to "entity" mode | Entity type selector and entity selector appear |
| 5 | Switch to "user" mode | Selectors simplified to user history fetch |

**Evidence:** Screenshots of each mode state.

---

### TST11-SC-024: Activity Selector Discoverability

**Goal:** Verify activity selectors are discoverable without manual ID entry.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/activity` | Activity view loads |
| 2 | In "board" mode, verify board selector shows board names (not raw IDs) | Dropdown contains readable board names |
| 3 | In "entity" mode, verify entity type selector lists Board, Column, Card, Label | All entity types available |
| 4 | After selecting entity type and board context, verify entity selector populates with named options | Entities listed by name, not raw ID |
| 5 | Verify help callout is visible | "Why do these selectors matter?" callout present |

**Evidence:** Screenshot showing populated selectors and help callout.

---

### TST11-SC-025: Audit Unauthorized Access

**Goal:** Verify audit endpoints require authentication and authorization.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | `GET /api/audit/boards/{boardId}` without bearer token | 401 |
| 2 | `GET /api/audit/entities/card/{cardId}` without bearer token | 401 |
| 3 | `GET /api/audit/users/me` without bearer token | 401 |
| 4 | UserB calls `GET /api/audit/boards/{userA-boardId}` | 403 (no read access to UserA's board) |

**Evidence:** Error response payloads.

---

## Traceability Matrix

| Scenario ID | Manual Checklist Section | API Route | Surface |
|---|---|---|---|
| TST11-SC-001 | B.5 | `GET /api/boards/{id}/starter-packs/catalog` | Starter Packs |
| TST11-SC-002 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-003 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-004 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-005 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-006 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-007 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-008 | B.5 | `POST /api/boards/{id}/starter-packs/apply` | Starter Packs |
| TST11-SC-009 | B.5 | `POST /api/boards/{id}/starter-packs/validate-manifest` | Starter Packs |
| TST11-SC-010 | B.3, G | `PUT /api/boards/{id}` | Archive |
| TST11-SC-011 | G.1 | `/workspace/archive` | Archive |
| TST11-SC-012 | G.3-4 | `/workspace/archive`, `PUT /api/boards/{id}` | Archive |
| TST11-SC-013 | G.3-4 | Archive/restore cycle | Archive |
| TST11-SC-014 | G.1-3 | `GET/POST /api/archive/*` | Archive |
| TST11-SC-015 | P2.5 | `POST /api/archive/{type}/{id}/restore` | Archive |
| TST11-SC-016 | P.4, K4 | `GET /api/archive/items` | Archive |
| TST11-SC-017 | P.4 | `GET /api/archive/items` | Archive |
| TST11-SC-018 | O.2 | `GET /api/audit/boards/{id}` | Activity |
| TST11-SC-019 | O.3 | `GET /api/audit/entities/{type}/{id}` | Activity |
| TST11-SC-020 | O.4 | `GET /api/audit/users/me` | Activity |
| TST11-SC-021 | O.2 | `GET /api/audit/boards/{id}` | Activity |
| TST11-SC-022 | O.2-4 | `GET /api/audit/users/me` | Activity |
| TST11-SC-023 | O.1 | `/workspace/activity` | Activity |
| TST11-SC-024 | O.1-3 | `/workspace/activity` | Activity |
| TST11-SC-025 | P (API spot checks) | `GET /api/audit/*` | Activity |
