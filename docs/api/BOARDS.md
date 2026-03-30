# Boards, Columns, Cards, and Labels

All board-related endpoints require authentication via JWT Bearer token.

## Boards

### List boards

```bash
curl -s http://localhost:5000/api/boards \
  -H "Authorization: Bearer $TOKEN"
```

Query parameters:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `search` | string | - | Filter boards by name substring |
| `includeArchived` | bool | `false` | Include archived boards |

Response (`200 OK`):

```json
[
  {
    "id": "f5e6d7c8-1234-5678-abcd-ef0123456789",
    "name": "Sprint 42",
    "description": "Current sprint board",
    "isArchived": false,
    "createdAt": "2026-03-28T09:00:00Z",
    "updatedAt": "2026-03-29T14:30:00Z"
  }
]
```

### Get board detail

```bash
curl -s http://localhost:5000/api/boards/$BOARD_ID \
  -H "Authorization: Bearer $TOKEN"
```

Returns the board with its columns included:

```json
{
  "id": "f5e6d7c8-...",
  "name": "Sprint 42",
  "description": "Current sprint board",
  "isArchived": false,
  "createdAt": "2026-03-28T09:00:00Z",
  "updatedAt": "2026-03-29T14:30:00Z",
  "columns": [
    {
      "id": "a1b2c3d4-...",
      "boardId": "f5e6d7c8-...",
      "name": "To Do",
      "position": 0,
      "wipLimit": null,
      "cardCount": 3,
      "createdAt": "2026-03-28T09:01:00Z",
      "updatedAt": "2026-03-28T09:01:00Z"
    }
  ]
}
```

### Create a board

```bash
curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "New Board",
    "description": "Optional description"
  }'
```

Response: `201 Created` with the board object.

### Update a board

```bash
curl -s -X PUT http://localhost:5000/api/boards/$BOARD_ID \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "Updated Name",
    "isArchived": false
  }'
```

All fields are optional -- only provided fields are updated.

### Delete a board

```bash
curl -s -X DELETE http://localhost:5000/api/boards/$BOARD_ID \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`

---

## Columns

Columns live under a board and define workflow stages.

**Base URL:** `api/boards/{boardId}/columns`

### List columns

```bash
curl -s "http://localhost:5000/api/boards/$BOARD_ID/columns" \
  -H "Authorization: Bearer $TOKEN"
```

Response (`200 OK`):

```json
[
  {
    "id": "a1b2c3d4-...",
    "boardId": "f5e6d7c8-...",
    "name": "To Do",
    "position": 0,
    "wipLimit": null,
    "cardCount": 3,
    "createdAt": "2026-03-28T09:01:00Z",
    "updatedAt": "2026-03-28T09:01:00Z"
  },
  {
    "id": "b2c3d4e5-...",
    "boardId": "f5e6d7c8-...",
    "name": "In Progress",
    "position": 1,
    "wipLimit": 5,
    "cardCount": 2,
    "createdAt": "2026-03-28T09:01:00Z",
    "updatedAt": "2026-03-28T09:01:00Z"
  }
]
```

### Create a column

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/columns" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "'$BOARD_ID'",
    "name": "Done",
    "position": 2,
    "wipLimit": null
  }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `boardId` | GUID | yes | Overridden by route parameter |
| `name` | string | yes | Column display name |
| `position` | int | no | Position index (auto-assigned if omitted) |
| `wipLimit` | int? | no | Maximum cards allowed in this column |

### Update a column

```bash
curl -s -X PATCH "http://localhost:5000/api/boards/$BOARD_ID/columns/$COLUMN_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"wipLimit": 3}'
```

### Delete a column

```bash
curl -s -X DELETE "http://localhost:5000/api/boards/$BOARD_ID/columns/$COLUMN_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`. The column must be empty (no cards).

### Reorder columns

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/columns/reorder" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "columnIds": [
      "b2c3d4e5-...",
      "a1b2c3d4-...",
      "c3d4e5f6-..."
    ]
  }'
```

All column IDs for the board must be included in the desired order.

---

## Cards

Cards are individual work items within a column.

**Base URL:** `api/boards/{boardId}/cards`

### Search cards

```bash
curl -s "http://localhost:5000/api/boards/$BOARD_ID/cards?search=dark+mode" \
  -H "Authorization: Bearer $TOKEN"
```

Query parameters:

| Parameter | Type | Description |
|-----------|------|-------------|
| `search` | string | Text search on title/description |
| `labelId` | GUID | Filter by label |
| `columnId` | GUID | Filter by column |

Response (`200 OK`):

```json
[
  {
    "id": "d4e5f6a7-...",
    "boardId": "f5e6d7c8-...",
    "columnId": "a1b2c3d4-...",
    "title": "Add dark mode",
    "description": "Implement dark theme toggle in settings",
    "dueDate": "2026-04-15T00:00:00Z",
    "isBlocked": false,
    "blockReason": null,
    "position": 0,
    "labels": [
      {
        "id": "e5f6a7b8-...",
        "boardId": "f5e6d7c8-...",
        "name": "enhancement",
        "colorHex": "#10B981",
        "createdAt": "2026-03-28T09:02:00Z",
        "updatedAt": "2026-03-28T09:02:00Z"
      }
    ],
    "createdAt": "2026-03-29T10:00:00Z",
    "updatedAt": "2026-03-29T10:00:00Z"
  }
]
```

### Create a card

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/cards" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "'$BOARD_ID'",
    "columnId": "'$COLUMN_ID'",
    "title": "Implement search",
    "description": "Full-text search across cards",
    "dueDate": "2026-04-10T00:00:00Z",
    "labelIds": ["e5f6a7b8-..."]
  }'
```

Response: `201 Created` with the card object.

### Update a card

Uses `PATCH` with optional fields. Supports optimistic concurrency:

```bash
curl -s -X PATCH "http://localhost:5000/api/boards/$BOARD_ID/cards/$CARD_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "title": "Updated title",
    "isBlocked": true,
    "blockReason": "Waiting for design review",
    "expectedUpdatedAt": "2026-03-29T10:00:00Z"
  }'
```

If `expectedUpdatedAt` is provided and the card has been modified since that timestamp, the API returns `409 Conflict`.

### Move a card

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/cards/$CARD_ID/move" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "targetColumnId": "b2c3d4e5-...",
    "targetPosition": 0
  }'
```

Returns `400 Bad Request` if the target column's WIP limit would be exceeded.

### Delete a card

```bash
curl -s -X DELETE "http://localhost:5000/api/boards/$BOARD_ID/cards/$CARD_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`

### Card capture provenance

View the link between a card and the capture item/proposal that created it:

```bash
curl -s "http://localhost:5000/api/boards/$BOARD_ID/cards/$CARD_ID/provenance" \
  -H "Authorization: Bearer $TOKEN"
```

```json
{
  "cardId": "d4e5f6a7-...",
  "captureItemId": "11223344-...",
  "proposalId": "55667788-...",
  "proposalStatus": "Approved",
  "triageRunId": "99aabbcc-..."
}
```

---

## Labels

Labels are board-scoped color-coded tags for cards.

**Base URL:** `api/boards/{boardId}/labels`

### List labels

```bash
curl -s "http://localhost:5000/api/boards/$BOARD_ID/labels" \
  -H "Authorization: Bearer $TOKEN"
```

### Create a label

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/labels" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "'$BOARD_ID'",
    "name": "bug",
    "colorHex": "#EF4444"
  }'
```

### Update a label

```bash
curl -s -X PATCH "http://localhost:5000/api/boards/$BOARD_ID/labels/$LABEL_ID" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name": "critical-bug", "colorHex": "#DC2626"}'
```

### Delete a label

```bash
curl -s -X DELETE "http://localhost:5000/api/boards/$BOARD_ID/labels/$LABEL_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`
