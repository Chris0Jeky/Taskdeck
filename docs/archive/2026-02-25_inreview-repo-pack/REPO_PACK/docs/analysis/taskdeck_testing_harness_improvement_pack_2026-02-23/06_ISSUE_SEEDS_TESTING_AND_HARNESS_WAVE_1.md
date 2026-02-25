# 06 — Issue Seeds: Testing + Harness Wave 1

Date: 2026-02-23  
Use: copy/paste into GitHub issues.

---

## Issue 1 — Remove time-based flake in Board domain tests
**Labels:** testing, backend  
**Scope:** `backend/tests/Taskdeck.Domain.Tests/Entities/BoardTests.cs`

### Problem
`Thread.Sleep(10)` makes tests slower and occasionally flaky depending on scheduling/time resolution.

### Tasks
- Replace timestamp delta assertion with monotonic assertion (or add a time provider abstraction).
- Remove `Thread.Sleep`.
- Run: `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj -c Release`

### AC
- No `Thread.Sleep` remains in domain tests.
- Test suite stays green.

---

## Issue 2 — Replace manual polling with Playwright expect.poll in automation E2E
**Labels:** testing, frontend, e2e  
**Scope:** `frontend/taskdeck-web/tests/e2e/automation-ops.spec.ts`

### Tasks
- Replace the `for` loop + `setTimeout` polling with `expect.poll`.
- Centralize timeout and polling interval in a helper.

### AC
- Test failure output is clearer (polling condition shown).
- No ad-hoc sleeps remain in E2E tests.

---

## Issue 3 — Add E2E test: WIP limit enforcement blocks card add/move
**Labels:** testing, frontend, e2e  
**Scope:** `frontend/taskdeck-web/tests/e2e/`

### Tasks
- Create board with a column that has WIP limit 1.
- Add 1 card, then attempt to add/move another card into same column.
- Assert:
  - operation blocked
  - error toast visible
  - state not mutated after refresh.

### AC
- Test passes locally and in CI.
- Uses stable selectors (`getByRole` or `data-action`).

---

## Issue 4 — Add API error contract completeness assertions (401/403/404/409)
**Labels:** testing, backend, api  
**Scope:** `backend/tests/Taskdeck.Api.Tests/ApiErrorContractApiTests.cs`

### Tasks
- Add representative coverage for:
  - 403 forbidden (cross-user)
  - 404 not found (missing resource)
  - 409 conflict (e.g., WIP limit / position conflicts / idempotency conflict)
- Assert:
  - JSON content type
  - errorCode + message present
  - request id header present (if expected)

### AC
- Contract tests cover at least one example per status class above.

---

## Issue 5 — Introduce docs/GOLDEN_PRINCIPLES.md + minimal enforcement script
**Labels:** docs, hardening, testing  
**Scope:** repo root

### Tasks
- Add `docs/GOLDEN_PRINCIPLES.md` (10–15 rules).
- Add `scripts/check-golden-principles.mjs` enforcing 3–5 mechanical rules.
- Wire it into CI (docs-governance job is a good home).

### AC
- CI fails if golden principle violations occur (only for the enforceable subset).
- Principles are short, stable, and referenced from `AGENTS.md`.

---

## Issue 6 — Add OpenAPI generation + parse validation in CI
**Labels:** backend, hardening, testing  
**Scope:** `.github/workflows/`

### Tasks
- Add a job that generates swagger/openapi JSON.
- Validate it parses (node script or jq).
- Upload as artifact.

### AC
- CI produces an OpenAPI artifact for every PR.
- Job is stable and under a few minutes.

---

## Issue 7 — Add scheduled Nightly Quality workflow (non-blocking)
**Labels:** hardening, testing, docs  
**Scope:** `.github/workflows/nightly-quality.yml`

### Tasks
- On schedule:
  - run coverage collection (backend + frontend)
  - run dependency vulnerability checks
  - upload reports as artifacts
- Do not block PR CI initially.

### AC
- Workflow runs cleanly on main and produces artifacts.
