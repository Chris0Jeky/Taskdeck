# Capture Pipeline

> **Data model:** See [Data Model Reference](../architecture/DATA_MODEL.md) for entity fields, constraints, and relationships.

The capture pipeline is Taskdeck's quick-capture system. Raw text is captured, then triaged by a deterministic, offline rule-based extractor (no LLM call) to generate automation proposals. Proposals must be explicitly approved by the user before any board mutations occur (review-first principle).

## Capture flow

```
Create capture item  -->  Enqueue triage  -->  Proposal generated (async)
        |                                            |
        v                                            v
   Ignore / Cancel                          Review queue (approve/reject)
```

## Create a capture item

```bash
curl -s -X POST http://localhost:5000/api/capture/items \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "f5e6d7c8-...",
    "text": "Add pagination to the card list endpoint",
    "source": "api",
    "titleHint": "Card list pagination",
    "externalRef": "JIRA-1234"
  }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `boardId` | GUID | no | Target board (can be null for unscoped capture) |
| `text` | string | yes | Raw capture text |
| `source` | string | no | Origin identifier (e.g., `api`, `cli`, `browser`) |
| `titleHint` | string | no | Suggested card title for triage |
| `externalRef` | string | no | External reference ID for traceability |

Response (`201 Created`):

```json
{
  "id": "11223344-5566-7788-99aa-bbccddeeff00",
  "userId": "3fa85f64-...",
  "boardId": "f5e6d7c8-...",
  "status": "Pending",
  "source": "Api",
  "rawText": "Add pagination to the card list endpoint",
  "textExcerpt": "Add pagination to the card list endpoint",
  "createdAt": "2026-03-30T12:00:00Z",
  "processedAt": null,
  "retryCount": 0,
  "provenance": null
}
```

## List capture items

```bash
curl -s "http://localhost:5000/api/capture/items?status=Pending&limit=20" \
  -H "Authorization: Bearer $TOKEN"
```

Query parameters:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `status` | string | - | Filter by status: `Pending`, `Triaging`, `Processed`, `Ignored`, `Cancelled` |
| `boardId` | GUID | - | Filter by target board |
| `limit` | int | `50` | Maximum items to return |

Response (`200 OK`): array of `CaptureItemSummaryDto`.

## Get a capture item

```bash
curl -s "http://localhost:5000/api/capture/items/$CAPTURE_ID" \
  -H "Authorization: Bearer $TOKEN"
```

## Enqueue for triage

Enqueues the capture item for triage by the deterministic offline extractor (no LLM call) into proposal operations. The response is asynchronous -- the proposal will appear in the review queue when ready.

```bash
curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/triage" \
  -H "Authorization: Bearer $TOKEN"
```

### Optional request body

A proposal always targets a board, so triage needs one. Captures created without a `boardId` (quick capture from Home) can supply the target board at accept time with an optional JSON body:

```bash
curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/triage" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{ "boardId": "f5e6d7c8-..." }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `boardId` | GUID | no | Target board to link before triage. Only applies when the capture has no board yet; ignored when it already carries one. |

The body itself is optional -- an empty request body is accepted and behaves exactly as before. A capture that has no board and receives no `boardId` is rejected synchronously with `400 Bad Request` rather than queueing a job that could only fail later.

### Board access requirement

Triage requires **write-capable membership** on the target board -- the roles `BoardAccess.CanWrite()` admits (`Owner`, `Admin`, `Editor`), plus the board owner. A `Viewer` can read a board but cannot triage a capture into it, and gets `403 Forbidden`. The gate applies to both shapes:

- the `boardId` supplied in the request body, before the board is linked, and
- the board a capture already carries (a board can be attached at create time under read access alone, so the write bar is enforced here, not only on the body).

Triaging a capture into a board queues an automation proposal into that board's review queue, which only approvers can clear -- read access is not enough to put work there. Approval and execution authorization are unchanged: write access buys the right to *suggest*; every board mutation still needs an explicit approve and execute.

The same write bar is re-checked in the worker before proposal generation, so a capture enqueued while its author still had write access is rejected if that access was revoked in the meantime.

Response (`202 Accepted`):

```json
{
  "id": "11223344-...",
  "status": "Triaging",
  "alreadyTriaging": false
}
```

If the item is already being triaged, `alreadyTriaging` will be `true`.

| Status | When |
|--------|------|
| `202 Accepted` | Triage enqueued (or already in flight). |
| `400 Bad Request` | The capture has no target board and none was supplied. |
| `403 Forbidden` | The caller lacks write access to the target board, or the capture belongs to another user. |
| `404 Not Found` | Capture item (or the supplied board) not found. |
| `409 Conflict` | The capture cannot transition to `Triaging` from its current status. |
| `429 Too Many Requests` | Rate limit exceeded. |

## Ignore a capture item

Dismiss a capture item without processing:

```bash
curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/ignore" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`

## Cancel a capture item

Cancel a capture item (e.g., submitted in error):

```bash
curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/cancel" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`

## Capture statuses

| Status | Description |
|--------|-------------|
| `Pending` | Newly created, awaiting user action |
| `Triaging` | Being triaged by the deterministic extractor into proposal operations |
| `Processed` | Triage complete, proposal generated |
| `Ignored` | Dismissed by user |
| `Cancelled` | Cancelled by user |

## Rate limiting

The create and triage endpoints are rate-limited per user to prevent abuse. Exceeding the limit returns `429 Too Many Requests`.
