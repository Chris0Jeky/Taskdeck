# Archive Recovery and Export/Import Specification

Last Updated: 2026-02-12
Status: Design complete, implementation pending

## 1. Purpose

Define UX and contract behavior for:
- recovering archived items,
- exporting/importing board data,
- maintaining safe and auditable data portability.

This supports the stated requirement to recover archived items and expose major system capabilities in frontend.

## 2. Current Capability Snapshot

Implemented now:
- board archive behavior exists via update/delete semantics in core board flows
- export/import endpoints exist for board-level JSON operations

Missing now:
- dedicated archive listing/restoration API
- frontend surfaces for export/import
- UI for archived entity recovery (board/column/card)

## 3. Target Surfaces

Routes:
- `/workspace/archive`
- `/workspace/settings/export-import`

Components:
- archive inventory table
- restore action flow
- export dialog with format options
- import wizard with validation preview and conflict strategy

## 4. Archive Recovery Design

Archive inventory should display:
- entity type
- name/title
- board context
- archived timestamp
- archived by
- reason (if available)

Restore action requirements:
- permission pre-check
- conflict validation before apply
- post-restore navigation option to entity context

Required backend endpoints:
- `GET /api/archive/items`
- `POST /api/archive/{entityType}/{id}/restore`

## 5. Export UX Design

Export options:
- export board DTO
- export board raw JSON

Required UX behavior:
- precheck user permission
- show metadata (board name, exported at, actor)
- offer download and copy output
- provide explicit error details on forbidden/not-found

## 6. Import UX Design

Import modes:
- structured form import (`/api/import/boards`)
- raw JSON import (`/api/import/boards/json`)

Wizard steps:
1. source input (upload/paste)
2. parse + validate
3. preview entities to create
4. conflict handling choice
5. commit and result summary

Validation reporting:
- duplicate label references
- unknown column/label references
- empty/invalid required fields

## 7. Conflict Resolution Policy

Default strategy:
- non-destructive import into new board context

Optional strategies (future):
- merge into existing board
- skip duplicate entities

Conflict report must include:
- conflict type
- affected entity
- suggested resolution

## 8. Identity and Permission Handling

Current export/import endpoints require query `userId`.

Transition approach:
- use session actor adapter now
- remove query actor when backend claims-based enforcement is ready

Role guidance:
- export: read permission minimum
- import: write/admin depending on board targeting mode
- archive restore: write/admin or owner based on entity type

## 9. Auditing Requirements

Every export/import/restore action must generate auditable entries containing:
- actor
- action type
- timestamp
- target entity
- summary outcome (`success`, `partial`, `failed`)

## 10. Testing Requirements

Unit:
- import parse/validation helpers
- conflict report formatter

Integration:
- export call flows and permission errors
- import structured and JSON workflows
- restore actions and response handling

E2E:
- export board JSON and verify download/content display
- import valid board payload and verify created entities
- import invalid payload and verify precise error reporting
- restore archived entity and verify visibility in workspace

## 11. Definition of Done

Archive/export-import slice is complete when:
- archive inventory and restore are available in UI,
- export/import flows are user-operable without manual API calls,
- conflict and validation outcomes are explicit,
- actions are traceable in activity/log surfaces.
