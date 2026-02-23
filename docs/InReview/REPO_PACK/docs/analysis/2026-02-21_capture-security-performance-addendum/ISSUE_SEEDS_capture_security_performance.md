# Issue Seeds — Capture Inbox + Security + Performance

Last Updated: 2026-02-21  
Purpose: ready-to-copy issue drafts with acceptance criteria and file-level guidance.

---

## CAP-01 — Capture API slice (create/list/get/cancel)

**Suggested labels:** `feature`, `automation`, `Priority II`  
**Depends on:** none (but keep an eye on `API-06 centralized exception handling` for error uniformity)

### Scope
Introduce `/api/capture/*` endpoints that wrap the queue and expose “capture items” as first-class UX concepts.

### Acceptance Criteria
- [ ] `POST /api/capture/items` returns `201 Created` and a `CaptureItemDto`
- [ ] `GET /api/capture/items` returns user-scoped list (most recent first)
- [ ] `GET /api/capture/items/{id}` returns item if owned, else `403`
- [ ] `POST /api/capture/items/{id}/cancel` cancels idempotently (204)
- [ ] All endpoints require JWT (401 when missing)
- [ ] Error contract `{ errorCode, message }` preserved

### Implementation Notes
- Add `Taskdeck.Application/Services/CaptureService`
- Store payload as `CapturePayloadV1` JSON in `LlmRequest.Payload`
- Use `RequestType = inbox.capture.v1`
- Prefer reusing `AuthenticatedControllerBase` for claims-derived user id

### Tests
- [ ] `backend/tests/Taskdeck.Api.Tests/CaptureApiTests.cs`

---

## CAP-02 — Worker routing + deterministic triage → proposals

**Suggested labels:** `feature`, `automation`, `worker`, `Priority II`  
**Depends on:** CAP-01

### Scope
Route queue items with `RequestType inbox.capture.*` to a triage service that creates a proposal (PendingReview).

### Acceptance Criteria
- [ ] Worker routes `inbox.capture.*` items to triage path
- [ ] Bullet/checklist captures produce N create-card operations
- [ ] Non-task text produces a single “Captured note” card
- [ ] Proposal created with `SourceType = Queue` and `SourceReferenceId = requestId`
- [ ] Queue item status becomes `Completed` on success, `Failed` on error (with message)
- [ ] Retry behavior remains bounded

### Implementation Notes
- Add `Taskdeck.Application/Services/CaptureTriageService`
- Default column = first column by Position
- Enforce card description length <= 2000

### Tests
- [ ] `backend/tests/Taskdeck.Application.Tests/CaptureTriageServiceTests.cs`
- [ ] Optional: API test that triggers `POST /api/llm-queue/process-next`

---

## CAP-03 — Queue proposals provenance fix (Queue != Manual)

**Suggested labels:** `bug`, `automation`, `Priority II`  
**Depends on:** none (independent improvement)

### Problem
`LlmQueueToProposalWorker` currently uses `AutomationPlannerService.ParseInstructionAsync`, which creates proposals with `ProposalSourceType.Manual`.

### Acceptance Criteria
- [ ] Queue-created proposals use `ProposalSourceType.Queue`
- [ ] `SourceReferenceId` is set to queue request id
- [ ] Existing manual instructions continue working unchanged

### Implementation Notes
- Extend `ParseInstructionAsync` signature to accept `sourceType/sourceReferenceId/correlationId` (optional)
- Update worker to pass queue id

### Tests
- [ ] Update or add integration test asserting created proposal metadata.

---

## SEC-13 — Rate limiting baseline for auth + capture endpoints

**Suggested labels:** `security`, `hardening`, `Priority III`  
**Depends on:** `API-06 centralized exception handling` (recommended)

### Acceptance Criteria
- [ ] Login/register endpoints have per-IP limit
- [ ] Capture endpoints have per-user limit
- [ ] Rejected requests return consistent error contract
- [ ] Limits configurable via appsettings

---

## SEC-14 — Logging redaction policy and guardrails

**Suggested labels:** `security`, `observability`, `Priority III`

### Acceptance Criteria
- [ ] Add `docs/SECURITY_LOGGING_POLICY.md` (or similar)
- [ ] No logs contain Authorization headers or raw capture payload by default
- [ ] Add a small unit test or analyzer rule if feasible

---

## PERF-07 — List virtualization pass (front-end)

**Suggested labels:** `frontend`, `performance`, `Priority IV`  
**Depends on:** `FE-11 lint baseline`, `FE-12 coverage thresholds` (recommended)

### Acceptance Criteria
- [ ] Board view remains responsive with 500+ cards
- [ ] Queue and activity log views virtualize long lists
- [ ] No noticeable scroll jank
