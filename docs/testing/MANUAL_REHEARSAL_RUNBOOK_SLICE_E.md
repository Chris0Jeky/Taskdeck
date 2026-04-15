# Manual Rehearsal Runbook: Slice E -- Starter Packs, Archive Recovery, Activity Traceability

Last Updated: 2026-04-15

Companion scenarios: `docs/testing/MANUAL_VALIDATION_SLICE_E_SCENARIOS.md`
Automated tests: `frontend/taskdeck-web/tests/e2e/validation-starter-packs.spec.ts`, `validation-archive-recovery.spec.ts`, `validation-activity-traceability.spec.ts`

## Purpose

Step-by-step rehearsal guide for a human operator to execute manual validation of starter packs, archive/recovery, and activity/audit surfaces. Each section includes evidence capture instructions and expected timing.

## Pre-Rehearsal Setup

### Environment

1. Remove `backend/src/Taskdeck.Api/taskdeck.db` for a clean baseline.
2. Start backend:
   ```bash
   dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
   ```
3. Start frontend:
   ```bash
   cd frontend/taskdeck-web
   npm run dev
   ```
4. Open `http://localhost:5173` (or fallback port).
5. Prepare a screenshots folder: `evidence/slice-e-{date}/`.

### Accounts

| Alias | Username | Email | Password | Purpose |
|---|---|---|---|---|
| **Operator** | `slice-e-user` | `slice-e@test.local` | `TestPass1!` | Primary test user |
| **Isolator** | `slice-e-iso` | `slice-e-iso@test.local` | `TestPass2!` | Cross-user isolation checks |

Register both accounts via `POST /api/auth/register` and save bearer tokens.

```bash
# Register operator
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"slice-e-user","email":"slice-e@test.local","password":"TestPass1!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_op.txt

# Register isolator
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"slice-e-iso","email":"slice-e-iso@test.local","password":"TestPass2!"}' \
  | tee /dev/stderr | jq -r '.token' > /tmp/token_iso.txt

TOKEN_OP=$(cat /tmp/token_op.txt)
TOKEN_ISO=$(cat /tmp/token_iso.txt)
```

### Run Metadata (Record Before Starting)

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | |
| Browser and version | |
| OS | |
| DB baseline | `fresh` |
| Env flags changed | |
| Estimated duration | 45-60 minutes |

---

## Phase 1: Starter Packs (est. 15-20 min)

### 1.1 Browse Catalog (TST11-SC-001)

```bash
# Create a board
BOARD_ID=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"name":"Starter Pack Test Board"}' | jq -r '.id')

# Get catalog
curl -s http://localhost:5000/api/boards/$BOARD_ID/starter-packs/catalog \
  -H "Authorization: Bearer $TOKEN_OP" | jq .
```

**Evidence:** Save catalog response JSON. Note pack count and IDs.

**Pass criteria:** 200 response, array of catalog entries.

### 1.2 Dry-Run Preview (TST11-SC-002)

```bash
curl -s -X POST http://localhost:5000/api/boards/$BOARD_ID/starter-packs/apply \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{
    "manifest": {
      "schemaVersion": "1.0",
      "packId": "rehearsal-small",
      "displayName": "Rehearsal Small",
      "description": "Test pack",
      "compatibility": {"minTaskdeckVersion":"1.0.0","requiredFeatures":["boards"]},
      "tags": ["test"],
      "labels": [{"name":"urgent","color":"#DC2626","description":"Urgent"}],
      "columns": [{"name":"Backlog","position":0},{"name":"Done","position":1}],
      "templates": [],
      "seedCards": [{"title":"Test Card","description":"Seed","columnName":"Backlog","labels":["urgent"]}]
    },
    "dryRun": true
  }' | jq .
```

**Evidence:** Save dry-run response. Verify `dryRun: true`, `applied: false`, `actions` non-empty.

Then confirm board is unchanged:
```bash
curl -s http://localhost:5000/api/boards/$BOARD_ID/columns \
  -H "Authorization: Bearer $TOKEN_OP" | jq .
# Should return empty array
```

**Pass criteria:** Board has no columns after dry-run.

### 1.3 Apply to Empty Board (TST11-SC-003)

```bash
# Same manifest, dryRun: false
curl -s -X POST http://localhost:5000/api/boards/$BOARD_ID/starter-packs/apply \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{
    "manifest": {
      "schemaVersion": "1.0",
      "packId": "rehearsal-small",
      "displayName": "Rehearsal Small",
      "description": "Test pack",
      "compatibility": {"minTaskdeckVersion":"1.0.0","requiredFeatures":["boards"]},
      "tags": ["test"],
      "labels": [{"name":"urgent","color":"#DC2626","description":"Urgent"}],
      "columns": [{"name":"Backlog","position":0},{"name":"Done","position":1}],
      "templates": [],
      "seedCards": [{"title":"Test Card","description":"Seed","columnName":"Backlog","labels":["urgent"]}]
    },
    "dryRun": false
  }' | jq .
```

**Evidence:** Save apply response. Verify `applied: true`.

```bash
# Verify board state
curl -s http://localhost:5000/api/boards/$BOARD_ID/columns \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[].name'

curl -s http://localhost:5000/api/boards/$BOARD_ID/labels \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[].name'

curl -s http://localhost:5000/api/boards/$BOARD_ID/cards \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[].title'
```

**Pass criteria:** Columns: Backlog, Done. Labels: urgent. Cards: Test Card.

### 1.4 Re-apply Idempotency (TST11-SC-004)

Re-run the same apply call from 1.3 with `dryRun: true`.

**Pass criteria:** Conflicts array non-empty, board state unchanged.

### 1.5 Conflict with Existing Content (TST11-SC-005)

```bash
# Create a new board with a pre-existing column
CONFLICT_BOARD=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"name":"Conflict Test Board"}' | jq -r '.id')

curl -s -X POST http://localhost:5000/api/boards/$CONFLICT_BOARD/columns \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d "{\"boardId\":\"$CONFLICT_BOARD\",\"name\":\"Occupied Lane\",\"position\":0,\"wipLimit\":null}"
```

Apply the same manifest with `dryRun: true`.

**Pass criteria:** Response includes `ColumnPositionConflict` in conflicts array.

---

## Phase 2: Archive and Recovery (est. 15-20 min)

### 2.1 Archive a Board (TST11-SC-010)

```bash
# Create a board with content
ARCH_BOARD=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"name":"Archive Test Board"}' | jq -r '.id')

# Archive it
curl -s -X PUT http://localhost:5000/api/boards/$ARCH_BOARD \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"isArchived": true}' | jq '.isArchived'
# Should print: true

# Verify absent from default list
curl -s http://localhost:5000/api/boards \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[].name'

# Verify present with includeArchived
curl -s "http://localhost:5000/api/boards?includeArchived=true" \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[] | select(.name=="Archive Test Board")'
```

**Evidence:** Default list without board, includeArchived list with board.

**Pass criteria:** Board absent from default, present with includeArchived=true.

### 2.2 Archive View in UI (TST11-SC-011)

1. Log in as Operator in the browser.
2. Navigate to `/workspace/archive`.
3. Verify "Archived Boards" section shows the archived board.
4. Screenshot the view.

**Evidence:** Screenshot showing archived board with Restore button.

### 2.3 Restore Archived Board (TST11-SC-012)

1. In the archive view, click "Restore Board" on the archived board.
2. Accept the confirmation dialog.
3. Verify success toast appears.
4. Navigate to `/workspace/boards`.
5. Verify the board reappears.

**Evidence:** Screenshot of boards list after restore.

### 2.4 State Preservation (TST11-SC-013)

```bash
# Create a rich board, add columns, cards, then archive and restore
RICH_BOARD=$(curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"name":"Rich Board"}' | jq -r '.id')

# Add columns
for col in '{"name":"Todo","position":0}' '{"name":"Doing","position":1}' '{"name":"Done","position":2}'; do
  curl -s -X POST http://localhost:5000/api/boards/$RICH_BOARD/columns \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $TOKEN_OP" \
    -d "{\"boardId\":\"$RICH_BOARD\",$col,\"wipLimit\":null}"
done

# Get Todo column ID
TODO_COL=$(curl -s http://localhost:5000/api/boards/$RICH_BOARD/columns \
  -H "Authorization: Bearer $TOKEN_OP" | jq -r '.[] | select(.name=="Todo") | .id')

# Add card
curl -s -X POST http://localhost:5000/api/boards/$RICH_BOARD/cards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d "{\"boardId\":\"$RICH_BOARD\",\"columnId\":\"$TODO_COL\",\"title\":\"Preserved Card\",\"description\":\"Survives cycle\",\"position\":0}"

# Capture baseline
curl -s http://localhost:5000/api/boards/$RICH_BOARD/cards \
  -H "Authorization: Bearer $TOKEN_OP" | jq . > /tmp/baseline_cards.json

# Archive
curl -s -X PUT http://localhost:5000/api/boards/$RICH_BOARD \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"isArchived": true}'

# Restore
curl -s -X PUT http://localhost:5000/api/boards/$RICH_BOARD \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_OP" \
  -d '{"isArchived": false}'

# Compare
curl -s http://localhost:5000/api/boards/$RICH_BOARD/cards \
  -H "Authorization: Bearer $TOKEN_OP" | jq . > /tmp/restored_cards.json

diff /tmp/baseline_cards.json /tmp/restored_cards.json
```

**Pass criteria:** `diff` produces no output (identical state).

### 2.5 Cross-User Isolation (TST11-SC-016)

```bash
# Isolator should not see Operator's archived boards
curl -s "http://localhost:5000/api/boards?includeArchived=true" \
  -H "Authorization: Bearer $TOKEN_ISO" | jq '.[].name'

curl -s http://localhost:5000/api/archive/items \
  -H "Authorization: Bearer $TOKEN_ISO" | jq '.[].name'
```

**Pass criteria:** Operator's boards not visible to Isolator.

### 2.6 Unauthorized Access (TST11-SC-017)

```bash
# No bearer token
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/archive/items
# Should print 401
```

**Pass criteria:** 401 response.

---

## Phase 3: Activity and Traceability (est. 15-20 min)

### 3.1 Board-Scoped Audit (TST11-SC-018)

```bash
# Use an existing board that has had mutations
curl -s http://localhost:5000/api/audit/boards/$BOARD_ID \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[0:5]'
```

**Evidence:** Save first 5 audit entries. Note action types and timestamps.

**Pass criteria:** Non-empty timeline with action and timestamp fields.

### 3.2 Entity-Scoped Audit (TST11-SC-019)

```bash
# Get a card ID from the test board
CARD_ID=$(curl -s http://localhost:5000/api/boards/$BOARD_ID/cards \
  -H "Authorization: Bearer $TOKEN_OP" | jq -r '.[0].id')

curl -s http://localhost:5000/api/audit/entities/card/$CARD_ID \
  -H "Authorization: Bearer $TOKEN_OP" | jq .
```

**Evidence:** Card-level audit entries.

**Pass criteria:** At least one entry (creation event).

### 3.3 User-Scoped Audit (TST11-SC-020)

```bash
curl -s http://localhost:5000/api/audit/users/me \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[0:10]'
```

**Evidence:** First 10 user-scoped audit entries.

**Pass criteria:** Non-empty timeline spanning actions from all phases.

### 3.4 Board Mutation Audit Trail (TST11-SC-021)

Verify the archive test board's audit trail includes create, rename (if performed), archive, and restore entries:

```bash
curl -s http://localhost:5000/api/audit/boards/$ARCH_BOARD \
  -H "Authorization: Bearer $TOKEN_OP" | jq '.[].action'
```

**Pass criteria:** Multiple distinct action types in the timeline.

### 3.5 Activity View UI (TST11-SC-023, TST11-SC-024)

1. Navigate to `/workspace/activity`.
2. Verify page loads with heading "Activity".
3. Verify help callout "Why do these selectors matter?" is visible.
4. Verify "Open Review" and "Open Boards" buttons are present.
5. Select board mode, pick a board, click Fetch.
6. Verify timeline entries appear.
7. Screenshot the populated activity view.

**Evidence:** Screenshots of activity view in each mode.

### 3.6 Unauthorized Audit Access (TST11-SC-025)

```bash
# No bearer token
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/audit/boards/$BOARD_ID
# Should print 401

curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/audit/users/me
# Should print 401

# Cross-user check
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/audit/boards/$BOARD_ID \
  -H "Authorization: Bearer $TOKEN_ISO"
# Should print 403 or 404
```

**Pass criteria:** 401 without token, 403/404 for cross-user access.

---

## Post-Rehearsal Checklist

| Item | Status |
|---|---|
| All Phase 1 scenarios executed | |
| All Phase 2 scenarios executed | |
| All Phase 3 scenarios executed | |
| Evidence screenshots collected | |
| API response JSONs saved | |
| Cross-user isolation verified | |
| Unauthorized access verified | |
| Run metadata recorded at top | |

## Known Limitations

- Starter pack catalog may be empty if no built-in packs are registered in the application layer. The apply endpoint works with any valid manifest regardless.
- Archive recovery inventory (`/api/archive/items`) tracks items archived through the archive recovery service. Board archival via `PUT /api/boards/{id}` uses a simpler `isArchived` flag and may not always create a recovery inventory entry.
- Activity audit granularity depends on which mutations trigger audit logging. Not all property-level changes may generate individual entries.
