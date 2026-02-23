# Testing and Verification Plan — Capture MVP
Date: 2026-02-21
Status: Draft (analysis pack; non-authoritative)

## Quality goals
- Capture pipeline is reliable and does not leak sensitive text into logs.
- Schema validation prevents unsafe model outputs from mutating boards.
- Proposals remain the only board mutation mechanism for triage.

## Backend test plan

### Domain tests
- status transition rules (New → Triaging → Triaged/Failed/Converted)
- invariants: max text length, required fields
- ownership is not part of domain entity construction (enforced in application layer)

### Application tests
- Create artifact validation
- List artifacts filtering and paging
- Enqueue triage idempotency
- Triage transform:
  - valid JSON output creates a proposal with expected operations
  - invalid JSON output fails deterministically with error code
  - output with too many tasks is rejected
- Provenance is attached to proposal and to created cards (once implemented)

### API integration tests
- All endpoints require auth (`401`)
- Cross-user access returns `403` (policy) and true missing returns `404`
- `POST /triage` returns `202` and artifact status transitions
- error payload uses `ApiErrorResponse` contract

## Frontend test plan

### Unit/component tests
- Capture modal:
  - submit creates artifact
  - escape closes modal
- Inbox list:
  - status chips render correctly
  - triage button calls API and updates status
- Inbox details:
  - renders raw text
  - shows proposal link when available

### E2E tests (Playwright)
Minimum smoke:
1) login
2) capture text
3) open inbox and triage item
4) open proposal, approve, execute
5) verify cards exist on board with provenance marker

## Manual testing additions
Add a new section to `docs/MANUAL_TEST_CHECKLIST.md`:
- capture modal + keyboard flows
- inbox triage
- proposal apply
- provenance check

## Exploratory testing charters (session-based)
Charter examples (45–60 min each):
- “Try to break capture validation and ensure errors are clear and stable.”
- “Feed ambiguous notes and verify the UI does not mislead users; check clarifying question handling.”
- “Stress test: 50 quick captures; ensure list performance and no UI jank.”
- “Privacy check: verify raw text never appears in logs by default.”
