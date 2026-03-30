# Capture Pipeline

The capture pipeline is Taskdeck's quick-capture system. Raw text is captured, then triaged by the LLM to generate automation proposals. Proposals must be explicitly approved by the user before any board mutations occur (review-first principle).

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

Sends the capture item to the LLM for proposal generation. The response is asynchronous -- the proposal will appear in the review queue when ready.

```bash
curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/triage" \
  -H "Authorization: Bearer $TOKEN"
```

Response (`202 Accepted`):

```json
{
  "id": "11223344-...",
  "status": "Triaging",
  "alreadyTriaging": false
}
```

If the item is already being triaged, `alreadyTriaging` will be `true`.

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
| `Triaging` | Sent to LLM for proposal generation |
| `Processed` | Triage complete, proposal generated |
| `Ignored` | Dismissed by user |
| `Cancelled` | Cancelled by user |

## Rate limiting

The create and triage endpoints are rate-limited per user to prevent abuse. Exceeding the limit returns `429 Too Many Requests`.
