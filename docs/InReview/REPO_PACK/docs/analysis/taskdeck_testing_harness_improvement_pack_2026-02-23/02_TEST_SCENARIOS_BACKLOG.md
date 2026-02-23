# 02 — Test Scenarios Backlog (Practical, Explicit)

Date: 2026-02-23  
Purpose: a backlog of **high-value** tests you can add without bloating maintenance cost.

How to use:
- Treat this as your “test backlog”.
- Convert Wave 1 into GitHub issues first.
- Each item includes suggested layer + location + acceptance criteria (AC).

---

## Wave 1 (high ROI, low risk)

### W1.1 — WIP limit enforcement (multi-layer)
**Risk addressed:** silent over-commitment; a core Kanban invariant  
**Preferred layers:** Application unit + API integration + E2E (single happy-path)
- Application test location: `backend/tests/Taskdeck.Application.Tests/Services/CardServiceTests.cs` (or your equivalent)
- API test location: `backend/tests/Taskdeck.Api.Tests/CardsApiTests.cs`
- E2E test location: `frontend/taskdeck-web/tests/e2e/smoke.spec.ts` (new test)

**AC**
- When column has WIP limit N and already has N cards:
  - adding a new card returns **400/409** (whichever your policy is) with stable error contract
  - moving a card into the column returns the same error
  - E2E shows a visible toast/error and card is not moved

---

### W1.2 — Drag/drop persistence (columns + cards)
**Risk addressed:** UI “looks moved” but state isn’t persisted or reorder math breaks  
**Layer:** E2E (because it covers UI + backend + realtime)
- File: `frontend/taskdeck-web/tests/e2e/smoke.spec.ts` (new tests)

**AC**
- Column reorder:
  - reorder columns
  - refresh page
  - order persists
- Card move:
  - move card to another column
  - refresh page
  - card remains in new column

---

### W1.3 — Error contract completeness (API contract guardrail)
**Risk addressed:** inconsistent errors break frontend + reduce trust  
**Layer:** API integration
- File: `backend/tests/Taskdeck.Api.Tests/ApiErrorContractApiTests.cs` (expand)

**AC**
- For representative 400, 401, 403, 404, 409:
  - response is JSON
  - includes `errorCode` and non-empty `message`
  - includes correlation header `X-Request-Id` echo if your middleware guarantees it

---

### W1.4 — “Sandbox is never on outside Development” guardrail
**Risk addressed:** accidental unsafe import/export in prod posture  
**Layer:** Application unit and/or API integration
- Option A (unit): instantiate sandbox settings with environment != Development and assert forced off
- Option B (API): boot host as Production and call sandbox endpoints, assert 403

**AC**
- Regardless of configuration flags, non-Development runtime rejects sandbox-gated endpoints.

---

### W1.5 — Starter pack apply idempotency + conflict safety (multi-step)
**Risk addressed:** duplicated columns/cards or partial apply  
**Layer:** API integration
- File: `backend/tests/Taskdeck.Api.Tests/StarterPacksApiTests.cs` (expand)

**AC**
- Applying same manifest twice:
  - second apply is idempotent (no duplicates)
- Applying manifest that conflicts:
  - returns dry-run conflict report
  - does not mutate board state

---

## Wave 2 (medium ROI)

### W2.1 — Auth token tampering tests
**Layer:** API integration  
**AC**
- invalid signature → 401 with `Unauthorized`
- expired token → 401 with clear errorCode (if implemented)
- token with mismatched user claim → 401 or 403 (policy-defined)

### W2.2 — Board access role matrix (read vs write)
**Layer:** API integration + frontend unit  
**AC**
- Viewer can read but cannot mutate.
- Editor can mutate but cannot change access (if policy).
- Owner can do all.

### W2.3 — Realtime: reconnect + missed events
**Layer:** E2E  
**AC**
- simulate disconnect/reconnect
- ensure board updates catch up (polling fallback, if present)

---

## Wave 3 (advanced / harness)

### W3.1 — OpenAPI drift test
**Layer:** CI + API integration  
**AC**
- build OpenAPI JSON; validate parse; optional diff vs committed snapshot

### W3.2 — Property-based tests for manifest validator
**Layer:** backend (FsCheck)  
**AC**
- random manifests never crash parser
- validator returns structured error list, never throws

### W3.3 — Mutation testing (scheduled)
**Layer:** CI scheduled workflow  
**AC**
- mutation score report produced as artifact; no PR gate initially

---

## Wave 4 (product hardening / performance)

### W4.1 — Micro-bench: log query & export
**Layer:** BenchmarkDotNet or k6 (manual or scheduled)  
**AC**
- establish baseline; detect 2x regression

---

## Notes
- Don’t implement all of this at once.
- Each wave should land with:
  - new tests
  - updated `docs/TESTING_GUIDE.md` (if you track totals)
  - updated `docs/MANUAL_TEST_CHECKLIST.md` if behavior changed
