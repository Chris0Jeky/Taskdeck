# Issue Seeds — Capture MVP (Ready to copy into GitHub)
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

Use this file to create a new issue wave in GitHub.

Label rules (from repo):
- every issue must have exactly one `Priority` label
- use `backend`, `frontend`, `ux`, `testing`, `llm`, `docs`, `security`, `hardening` as appropriate
- keep acceptance criteria explicit and test-backed
- follow `docs/ISSUE_EXECUTION_GUIDE.md` ordering rules

## Epic (tracker)
### CAP-00 — Capture pipeline MVP (Inbox → triage → proposal)
Labels: `Priority III`, `backend`, `frontend`, `ux`, `llm`, `testing`, `docs`
Purpose:
- umbrella tracker for the entire slice
Definition of Done:
- CAP-01..CAP-10 closed
- STATUS + MASTERPLAN updated
- manual checklist updated
- E2E smoke added

---

## Backend / Domain / Persistence

### CAP-01 — Domain: Add CaptureArtifact + CaptureTriageRun entities + invariants
Labels: `Priority III`, `backend`
Dependencies: none
Acceptance criteria:
- domain types and enums exist (`CaptureSource`, `CaptureStatus`)
- invariants implemented (text length, status transitions)
- unit tests added for invariants
Verification:
- `dotnet test backend/Taskdeck.sln -c Release -m:1`

### CAP-02 — Infrastructure: EF Core persistence for capture artifacts and triage runs
Labels: `Priority III`, `backend`
Depends on: CAP-01
Acceptance criteria:
- DbSets + configurations exist
- migration added
- indexes added (owner+status, owner+createdAt)
- integration tests cover persistence roundtrip in SQLite test harness

### CAP-03 — API: Capture artifact CRUD endpoints (create/list/get/ignore)
Labels: `Priority III`, `backend`
Depends on: CAP-02
Acceptance criteria:
- `POST /api/capture/artifacts` returns 201 and id
- `GET /api/capture/artifacts` returns paged excerpt list
- `GET /api/capture/artifacts/{id}` returns full text
- `POST /api/capture/artifacts/{id}/ignore` works
- authz: cross-user returns 403, true missing returns 404
- errors use `ApiErrorResponse` contract
Tests:
- API integration suite includes 401/403/404 policy assertions

### CAP-04 — Application: Enqueue triage and status transitions
Labels: `Priority III`, `backend`, `llm`
Depends on: CAP-03
Acceptance criteria:
- enqueue endpoint exists: `POST /api/capture/artifacts/{id}/triage` → 202
- idempotency behavior defined (409 or “already triaging”)
- artifact status transitions to Triaging
Tests:
- application tests + API integration tests

### CAP-05 — Worker: CaptureTriageWorker (queue → provider → validate → proposal)
Labels: `Priority III`, `backend`, `llm`, `hardening`
Depends on: CAP-04
Acceptance criteria:
- worker consumes triage jobs
- calls provider with prompt v1
- validates JSON against schema
- persists triage run record
- creates AutomationProposal linked to triage run
- artifact status transitions: Triaged → ProposalCreated
Tests:
- application tests use Mock provider with deterministic output
- negative tests: invalid JSON → deterministic failure

---

## LLM contract

### CAP-06 — LLM: Add strict triage JSON schema + prompt versioning
Labels: `Priority III`, `backend`, `llm`, `testing`
Depends on: CAP-05 (or can be earlier)
Acceptance criteria:
- JSON schema stored in repo (code or file) and used for validation
- prompt version constant tracked (triage.v1)
- tests cover schema validation failures and boundary limits

---

## Frontend UX

### CAP-07 — Frontend: Add Inbox route + list view (New/Triaging/Triaged/Converted)
Labels: `Priority III`, `frontend`, `ux`
Depends on: CAP-03
Acceptance criteria:
- route `/workspace/inbox` exists in router/nav
- list view loads items and shows statuses
- list uses excerpt, not full text
- ignore action works
Tests:
- component/store unit tests

### CAP-08 — Frontend: Capture modal + command palette integration
Labels: `Priority III`, `frontend`, `ux`
Depends on: CAP-07
Acceptance criteria:
- command palette item “Capture”
- modal supports keyboard submit + escape-stack behavior
- creates artifact and shows toast
Tests:
- unit tests for modal behavior

### CAP-09 — Frontend: Triage trigger + proposal linking UI
Labels: `Priority III`, `frontend`, `ux`
Depends on: CAP-04, CAP-05
Acceptance criteria:
- triage button triggers enqueue
- UI shows triage in progress and eventual proposal link
- “Open proposal” navigates to proposal detail view
Tests:
- unit tests for state transitions (mock API)

### CAP-10 — Frontend: Provenance display in card modal
Labels: `Priority III`, `frontend`, `ux`
Depends on: CAP-05
Acceptance criteria:
- cards created by triage show “Source: Inbox” in modal
- click opens artifact detail
Tests:
- UI test + API contract test for provenance field

---

## End-to-end and docs

### CAP-11 — E2E: Capture → triage → approve → apply smoke test
Labels: `Priority III`, `testing`
Depends on: CAP-08, CAP-09
Acceptance criteria:
- Playwright test demonstrates full loop
- uses deterministic fixture provider outputs
- passes in CI lane

### CAP-12 — Docs: Update STATUS, MASTERPLAN, Manual checklist for capture MVP
Labels: `Priority III`, `docs`
Depends on: CAP-11
Acceptance criteria:
- STATUS includes capture MVP feature list
- MASTERPLAN includes next steps
- MANUAL_TEST_CHECKLIST includes Inbox section
- optional: add a short “How to use Inbox” section in README

---

## Deferred follow-ons (do not start until MVP is retained)
### CAP-20 — Transcript paste upload (file)
Labels: `Priority IV`, `frontend`, `backend`, `ux`

### CAP-21 — Voice capture + transcription (opt-in)
Labels: `Priority IV`, `frontend`, `backend`, `ux`, `security`

### CAP-22 — Batch triage and triage suggestions editing
Labels: `Priority IV`, `frontend`, `backend`, `ux`, `llm`
