# 03 — Testing Architecture vNext (How to Keep It Maintainable)

Date: 2026-02-23  
Goal: keep tests **fast**, **deterministic**, and **easy to extend** as features grow.

---

## 1) Test taxonomy (explicit)

### Backend
- **Domain unit tests**
  - pure invariants, no EF, no HTTP, no filesystem
- **Application/service unit tests**
  - mocked repositories + policy engines
  - focus: orchestration rules, authz decisions, idempotency logic
- **API integration tests**
  - real EF + SQLite
  - verify: HTTP contracts, auth, cross-user, DB persistence, error mapping
- **Contract tests**
  - OpenAPI generation + drift detection
  - CLI JSON shape contract tests (you already have)
- **Architecture tests**
  - dependency direction enforcement
  - (optional) namespace rules + “controller must be thin” rules

### Frontend
- **Unit tests**
  - stores, utils, composables, small components
- **Route-level integration tests**
  - mount router + stores + view; mock network
- **E2E tests**
  - critical UX flows only

---

## 2) Design rules that keep tests stable

### Rule 1 — Don’t “sleep”
- Prefer deterministic control.
- In JS tests, prefer fake timers (`vi.useFakeTimers`) if you must.
- In C#, prefer a TimeProvider abstraction if time is a core invariant.

### Rule 2 — Stable selectors
- Prefer `getByRole` and `aria-label` (it makes accessibility better).
- For interactions, standardize:
  - `data-action="..."` (you already do)
  - `data-testid="..."` for component tests

### Rule 3 — One assertion target per test (mostly)
- It’s okay to verify multiple assertions if they are the same behavioral unit.
- Avoid “kitchen sink” tests that fail for 9 reasons.

### Rule 4 — Avoid over-mocking
- Mock boundaries where it matters:
  - LLM provider should be mocked in unit tests (you do)
  - EF/DB should not be mocked in API integration tests

### Rule 5 — Fixture builders everywhere
- You already have `TestDataBuilder` for Application tests.
- Extend the same idea across:
  - API integration tests (DTO builder + “seed board” helper)
  - frontend unit tests (factory helpers for store state and DTOs)

---

## 3) Backend test patterns (recommended)

### Pattern A — “Arrange with a builder, Act once, Assert invariant”
- Keep entity creation consistent.
- Example: `TestDataBuilder.CreateBoardWithColumnsAndCards()` is good.

### Pattern B — “Authz regression matrices”
- You already have `AuthzRegressionMatrixApiTests.cs`.
- Keep it up to date by:
  - making endpoint inventory mechanical (see Guardrails doc).

### Pattern C — “HTTP error contract harness”
- `ApiTestHarness.AssertErrorContractAsync` is exactly the pattern you want.
- Extend it to assert:
  - content-type JSON
  - request-id header presence
  - consistent status mapping

### Pattern D — “Behavioral transaction tests”
- Pick 2–3 most complex workflows and cover as API integration tests.
- These tests are expensive but high-value.

---

## 4) Frontend test patterns (recommended)

### Pattern E — Store-first testing
- Use Pinia stores as a boundary.
- Tests should assert:
  - state transitions
  - error handling
  - caching behavior (if any)
  - role/permission gating logic

### Pattern F — Route-level integration testing
- Mount `BoardView` + router + store.
- Mock API at the http layer (axios/fetch wrapper).
- Assert:
  - optimistic update flows
  - error toast behavior
  - keyboard navigation state

### Pattern G — E2E as regression guards only
- Keep E2E:
  - board workflow smoke
  - realtime propagation
  - drag/drop persistence
  - WIP enforcement
  - “automation proposal approval” (already partially covered)

---

## 5) Naming + file layout (consistency)
- Backend: `XyzServiceTests.cs`, `XyzApiTests.cs`, `XyzTests.cs` for entities.
- Frontend: `ComponentName.spec.ts`, `storeName.spec.ts`.
- E2E: group by feature:
  - `smoke.spec.ts`
  - `boards.spec.ts`
  - `realtime.spec.ts`
  - `automation.spec.ts`
  - `ops.spec.ts`
  - `starterpacks.spec.ts`

---

## 6) “Definition of Done” for a new feature slice
A feature slice is “done” when:
1) backend unit tests cover orchestration logic
2) API integration tests cover:
   - 401 behavior
   - cross-user isolation
   - error contract mapping for validation failures
3) frontend unit/integration tests cover:
   - store actions
   - error rendering
4) optionally: 1 E2E test if it’s user-critical

---

## 7) When to add property-based testing
Use property tests when:
- inputs have combinatorial explosion (manifests, filters, parsing)
- you’ve fixed the same class of edge-case bug more than once
- you fear “unknown unknowns” more than “known regressions”
