# Import Adapters Guide

Last Updated: 2026-02-24

## Scope

This guide documents the external import-adapter foundation (`#75`) and the first supported provider path:

- provider: `csv`
- profile: `outreach.contacts.v1`
- behavior: board-scoped card upsert via dry-run or apply

## API Contract

Endpoint:

- `POST /api/boards/{boardId}/imports/external`

Request body:

```json
{
  "provider": "csv",
  "payload": "Display Name,Company,Email Address\nAlice Example,Acme,alice@example.com",
  "targetColumnName": "Imported",
  "dryRun": true,
  "profile": "outreach.contacts.v1",
  "csv": {
    "displayNameColumn": "Display Name",
    "companyColumn": "Company",
    "roleColumn": "Position",
    "emailColumn": "Email Address",
    "linkedInUrlColumn": "LinkedIn URL",
    "lastTouchAtColumn": "Connected On"
  }
}
```

Response includes:

- `rowsReceived`, `rowsParsed`
- `rowsCreated`, `rowsUpdated`, `rowsSkipped`
- `conflicts[]` with structured `{ code, path, message, existingValue, incomingValue }`
- `hasConflicts`

Behavior:

- `dryRun=true`: preview only, no mutation.
- `dryRun=false`: apply only when no conflicts exist.

## Outreach Mapping Preset

Default `outreach.contacts.v1` field resolution:

- `display_name`: `Display Name` (fallback to `First Name + Last Name`)
- `company`: `Company`
- `role`: `Position` / `Role`
- `email`: `Email Address`
- `last_touch_at`: `Connected On` / `last_touch_at`
- `linkedin_url`: `LinkedIn URL`

Generated card content stores import metadata in the first description line:

- prefix: `[taskdeck-import-meta] `
- payload: JSON with provider/profile/dedupe key and mapped outreach fields

This metadata enables deterministic re-import update matching.

## Dedupe Key Policy

Deterministic order:

1. `linkedin_url`
2. `email`
3. normalized(`display_name + company`)

If no dedupe key can be derived, the record is reported as a conflict.

## Conflict Classes

Representative conflict codes:

- `MissingDedupeKey`
- `DuplicateInputRecord`
- `InvalidDate`
- `ExistingDuplicateDedupeKey`
- `AmbiguousExistingMatch`

`path` points to row/field context (for example `$.rows[4].last_touch_at`).

## Adapter Extensibility

Provider handling is registry-based through `IExternalImportAdapter`.

To add a new provider (for example Trello/Jira/GitHub):

1. Implement `IExternalImportAdapter`.
2. Register adapter in API composition root.
3. Reuse `IExternalImportService` orchestration path (dry-run/apply/conflict handling).

No core import-service rewrite is required.
