# PR Merge-Readiness Report (2026-02-12)

## Scope Reviewed

- Branch diff against `main` for:
  - backend services/controllers related to auth, board access, export/import, history, LLM queue
  - new and existing backend tests
  - `docs/STATUS.md`
  - `docs/IMPLEMENTATION_MASTERPLAN.md`
- Runtime validation via backend, frontend unit, and frontend E2E suites.

## What Was Found

1. Board-access update/revoke operations did not enforce route `boardId` ownership of `accessId`.
2. Board-access grant/update/revoke flows did not validate acting user existence/permission.
3. Export/import JSON round-trip was broken:
   - export JSON shape and import JSON shape were incompatible.
   - `/api/import/boards/json` used `string` body binding, which rejects normal JSON object payloads.
4. Export service lacked board read-permission checks.
5. Auth service could fail at runtime when JWT config was missing/invalid (default app settings had no JWT section).
6. History querying accepted unsafe/unbounded limits and weak input validation.
7. New side-track features were under-tested (missing authentication/export-import coverage and API-level coverage).
8. Docs had directional drift risk: wording implied side-track capabilities were fully active runtime behavior.

## What Was Changed

### Backend code hardening

- Board-access:
  - Added board-route scope enforcement in update/revoke service methods.
  - Added acting-user existence checks.
  - Added manage-access permission checks (owner/admin path) with transitional ownerless-board bootstrap (first successful grant claims ownership).
  - Added board existence checks to list access by board and user existence checks to list boards by user.
- Export/import:
  - Added requester existence/read-permission checks for export.
  - Removed null-owner implicit read bypass for export authorization.
  - Added importer existence checks.
  - Added export-shape JSON -> import DTO conversion path for true export/import round-trip compatibility.
  - Added duplicate/invalid reference validation (duplicate labels/columns, unknown column/label references).
  - Added tolerant deserialization fallback between import-shape and export-shape payloads.
- API controllers:
  - `BoardAccessController` now calls board-scoped update/revoke service signatures.
  - `AuditController` now maps validation/not-found errors explicitly and supports validated `limit` query usage.
  - `ExportController` now accepts JSON object payloads for `/api/import/boards/json` via `JsonElement`.
  - `LlmQueueController` now rejects undefined enum values for status filter.
- Auth:
  - Added JWT configuration validation guardrails (secret length, issuer/audience, expiration sanity).
  - Added inactive-user guard for password change.
  - Added development JWT settings in `backend/src/Taskdeck.Api/appsettings.Development.json` for local/test usability.
  - Added safer startup gating for JWT middleware configuration in `Program.cs`.
- Repository loading:
  - Extended board detail eager-loading to include card-label relationships for export correctness.

### Test suite expansion/revision

- Added:
  - `backend/tests/Taskdeck.Application.Tests/Services/AuthenticationServiceTests.cs`
  - `backend/tests/Taskdeck.Application.Tests/Services/ExportImportServiceTests.cs`
  - `backend/tests/Taskdeck.Api.Tests/AdvancedFeaturesApiTests.cs`
- Revised:
  - `backend/tests/Taskdeck.Application.Tests/Services/BoardAccessServiceTests.cs`
  - `backend/tests/Taskdeck.Application.Tests/Services/ExportImportServiceTests.cs`
  - `backend/tests/Taskdeck.Application.Tests/Services/HistoryServiceTests.cs`
  - `backend/tests/Taskdeck.Application.Tests/Services/UserServiceTests.cs`
  - `backend/tests/Taskdeck.Api.Tests/AdvancedFeaturesApiTests.cs`

## Vision Preservation Check (Docs Diff Intent)

Cross-checking `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` against `main`:

Preserved:
- Primary Phase 4 direction remains CI/reliability/CLI hardening first.
- Agent-compatible safety model (proposal/review/diff/rollback) remains the strategic end state.
- Side-track (auth/permissions/export-import/queue/audit) remains a sidecar track, not promoted over primary roadmap.

Corrected to avoid drift:
- Status wording now clarifies side-track features are implemented slices, not fully activated runtime behavior.
- Added explicit residual gaps (auth enforcement across legacy endpoints, ownerless legacy boards, query/body actor IDs).
- Added this hardening pass outcomes without changing roadmap priority.

## Focused Auth Rollout Design (Boards/Columns/Cards/Labels)

Scope in this pass is design-only for existing endpoints; no authorization rollout code was applied to these endpoints yet.

### Target authorization model

1. Authentication gate:
   - Require JWT auth for all board/column/card/label endpoints.
   - Derive actor identity only from token claims (`sub`), not query/body user IDs.
2. Resource authorization:
   - Use `AuthorizationService` as the single board permission source.
   - Enforce board-level permissions before each mutable/read action.
3. Backward-compatibility boundary:
   - Keep legacy `OwnerId == null` behavior explicit during transition.
   - Do not silently tighten ownerless boards without migration/backfill plan.

### Endpoint permission matrix

| Endpoint | Permission |
|---|---|
| `GET /api/boards` | Authenticated; return only boards with `CanReadBoard` true |
| `GET /api/boards/{id}` | `CanReadBoard` |
| `POST /api/boards` | Authenticated; creator becomes owner (`OwnerId = currentUserId`) |
| `PUT /api/boards/{id}` | `CanWriteBoard` for content edits; `CanDeleteBoard` for archive/unarchive state changes |
| `DELETE /api/boards/{id}` | `CanDeleteBoard` |
| `GET /api/boards/{boardId}/columns` | `CanReadBoard` |
| `POST /api/boards/{boardId}/columns` | `CanWriteBoard` |
| `PATCH /api/boards/{boardId}/columns/{columnId}` | `CanWriteBoard` |
| `DELETE /api/boards/{boardId}/columns/{columnId}` | `CanWriteBoard` |
| `POST /api/boards/{boardId}/columns/reorder` | `CanWriteBoard` |
| `GET /api/boards/{boardId}/cards` | `CanReadBoard` |
| `POST /api/boards/{boardId}/cards` | `CanWriteBoard` |
| `PATCH /api/boards/{boardId}/cards/{cardId}` | `CanWriteBoard` |
| `POST /api/boards/{boardId}/cards/{cardId}/move` | `CanWriteBoard` |
| `DELETE /api/boards/{boardId}/cards/{cardId}` | `CanWriteBoard` |
| `GET /api/boards/{boardId}/labels` | `CanReadBoard` |
| `POST /api/boards/{boardId}/labels` | `CanWriteBoard` |
| `PATCH /api/boards/{boardId}/labels/{labelId}` | `CanWriteBoard` |
| `DELETE /api/boards/{boardId}/labels/{labelId}` | `CanWriteBoard` |

### Rollout sequence (low-risk)

1. Identity plumbing:
   - Add `ICurrentUserContext` abstraction in API layer.
   - Resolve current user ID from JWT `sub`; fail with `401` if missing/invalid.
2. Controller gating:
   - Add `[Authorize]` on `BoardsController`, `ColumnsController`, `CardsController`, `LabelsController`.
   - Remove any future actor query/body parameters for these endpoints.
3. Service-level authorization enforcement:
   - Inject `AuthorizationService` into `BoardService`, `ColumnService`, `CardService`, `LabelService`.
   - Execute permission checks before repository mutations/reads.
4. Ownership consistency:
   - Update board creation flow to set `OwnerId = currentUserId`.
   - Decide and execute ownerless-board migration strategy before strict enforcement in production.
5. Contract cleanup:
   - Standardize `403` payload shape for authorization failures.
   - Keep `404` for true missing board/resource, avoid permission leaks where needed.

### Required test additions for rollout implementation

1. API integration:
   - 401 for missing token on all four controller surfaces.
   - 403 for authenticated-but-unauthorized board access.
   - Positive cases per role (`Owner`, `Admin`, `Editor`, `Viewer`) for read/write/delete semantics.
2. Application/service tests:
   - Permission checks invoked before mutation.
   - Archive/delete split behavior on board update/delete.
3. Migration/compatibility:
   - Ownerless-board behavior explicitly tested until migration complete.
   - Post-migration strict ownership tests added.

## Test Evidence Executed

- Backend:
  - `dotnet test backend/Taskdeck.sln`
  - Result:
    - Domain: 68/68
    - Application: 158/158
    - API integration: 34/34
    - CLI contract: 4/4
- Frontend:
  - `cd frontend/taskdeck-web; npx vitest run`
    - 115/115 passing
  - `cd frontend/taskdeck-web; $env:TASKDECK_E2E_DB='taskdeck.e2e.local.db'; npx playwright test`
    - 8/8 passing

Combined automated total after this pass: **387/387 passing**.

## Remaining Risks (Not Claimed as Solved Here)

1. Full `[Authorize]` rollout on legacy board/column/card/label endpoints is still pending.
2. Actor identity still relies on query/body IDs in side-track endpoints until claim-based enforcement is adopted.
3. `ExportDatabaseAsync` / `ImportDatabaseAsync` remain stubbed.
4. LLM queue background processor (`IHostedService`) is still pending.
5. Automatic audit logging integration into existing CRUD flows is still pending.

## Final Verification of This Artifact

Checklist completed in this pass:

1. Facts cross-checked against current branch code and test outputs.
2. Test totals and commands in this artifact match latest executed runs.
3. Direction check confirmed:
   - primary roadmap preserved
   - side-track not promoted over core Phase 4 track
4. Auth rollout section now explicitly covers only board/column/card/label endpoint design and rollout sequencing.
