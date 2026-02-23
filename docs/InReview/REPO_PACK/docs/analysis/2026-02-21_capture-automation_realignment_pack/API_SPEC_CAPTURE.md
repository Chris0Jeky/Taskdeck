# API Spec — Capture and Inbox
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

This spec assumes the existing API base route prefix: `/api`.

All endpoints:
- require `Authorization: Bearer <token>`
- derive `OwnerUserId` from claims
- return `ApiErrorResponse { errorCode, message }` on errors

## 1) Create capture artifact
`POST /api/capture/artifacts`

Request body:
```json
{
  "source": "Typed",
  "titleHint": "optional short title",
  "boardIdHint": null,
  "rawText": "my messy note or transcript..."
}
```

Response `201`:
```json
{
  "id": "guid",
  "createdAtUtc": "2026-02-21T12:34:56Z",
  "status": "New"
}
```

Validation:
- `rawText` required, non-empty, max length enforced
- `source` must be known enum value

Error responses:
- `400 capture.invalid_text`
- `401 unauthorized`
- `500` mapped via centralized exception handler (existing work)

## 2) List artifacts (Inbox)
`GET /api/capture/artifacts?status=New&limit=50&cursor=...&q=search`

Response `200`:
```json
{
  "items": [
    {
      "id": "guid",
      "createdAtUtc": "2026-02-21T12:34:56Z",
      "source": "Typed",
      "status": "New",
      "titleHint": "optional",
      "excerpt": "first 140 chars...",
      "lastTriageRunId": null,
      "proposalId": null
    }
  ],
  "nextCursor": null
}
```

Notes:
- Provide `excerpt` server-side to avoid shipping full text for list view.
- Fetch full text via `GET /api/capture/artifacts/{id}`.

## 3) Get artifact details
`GET /api/capture/artifacts/{id}`

Response `200`:
```json
{
  "id": "guid",
  "createdAtUtc": "2026-02-21T12:34:56Z",
  "source": "Typed",
  "status": "Triaged",
  "titleHint": null,
  "boardIdHint": null,
  "rawText": "full text...",
  "lastTriageRunId": "guid",
  "latestTriage": {
    "id": "guid",
    "status": "Succeeded",
    "createdAtUtc": "2026-02-21T12:35:20Z",
    "provider": "Mock",
    "model": "mock",
    "promptVersion": "triage.v1",
    "proposalId": "guid"
  }
}
```

Error responses:
- `404 capture.not_found` (true missing)
- `403 capture.forbidden` (cross-user access attempt)
- `401 unauthorized`

## 4) Enqueue triage
`POST /api/capture/artifacts/{id}/triage`

Headers:
- optional `Idempotency-Key: <uuid or random string>`

Response `202`:
```json
{
  "artifactId": "guid",
  "status": "Triaging",
  "triageRunId": "guid"
}
```

Error responses:
- `409 capture.already_triaging`
- `404 capture.not_found`
- `403 capture.forbidden`
- `400 capture.invalid_state` (cannot triage ignored/converted artifacts unless re-triage flag exists)

## 5) Ignore artifact
`POST /api/capture/artifacts/{id}/ignore`

Response `200`:
```json
{ "status": "Ignored" }
```

## 6) Optional: Batch triage (future)
`POST /api/capture/artifacts/triage`

Request:
```json
{ "artifactIds": ["guid","guid"] }
```

Response:
```json
{ "accepted": ["guid"], "rejected": [{ "id":"guid", "errorCode":"capture.already_triaging"}] }
```

## Error contract rules
- Maintain stable `errorCode` values for frontend logic and tests.
- Do not leak internal exception details in `message`.
