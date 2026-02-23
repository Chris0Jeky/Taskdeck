# 01 — Testing Review and Priorities (Repo Scan)

Date: 2026-02-23  
Scope: **backend + frontend automated tests**, CI gates, “guardrails”, and agent/harness posture as reflected in the repository.

---

## 0) Snapshot of what you already have (this is a strong baseline)

### Automated test layers (current repo)
- **Backend**
  - Domain unit tests: `backend/tests/Taskdeck.Domain.Tests`
  - Application/service unit tests (mocked dependencies): `backend/tests/Taskdeck.Application.Tests`
  - API integration tests (real DB + WebApplicationFactory): `backend/tests/Taskdeck.Api.Tests`
  - CLI contract tests: `backend/tests/Taskdeck.Cli.Tests`
  - Architecture boundary tests: `backend/tests/Taskdeck.Architecture.Tests`
- **Frontend**
  - Unit tests (components + stores + utilities + API modules): `frontend/taskdeck-web/src/tests`
  - E2E tests (Playwright): `frontend/taskdeck-web/tests/e2e`
- **Manual**
  - `docs/MANUAL_TEST_CHECKLIST.md` is unusually comprehensive for a solo product.

### Guardrails and CI posture (current repo)
- CI pipeline includes:
  - docs governance (`scripts/check-docs-governance.mjs`)
  - GitHub ops governance (`scripts/check-github-ops-governance.mjs`)
  - backend architecture test job
  - backend unit test matrix (Windows + Linux)
  - API integration matrix
  - frontend unit + typecheck + build matrix
  - container build validation
  - Playwright smoke gate (after everything else)
- Repo includes agent posture artifacts:
  - `AGENTS.md` + folder-specific AGENTS
  - `.codex/config.toml` with MCP servers (ripgrep, playwright, chrome-devtools, docker, etc.)
  - `.claude/settings.local.json` allowlist (command safety boundary)

**Result:** your baseline is already closer to “real product engineering discipline” than most early solo repos.

---

## 1) What I would improve next (priority order)

### Priority 0 — Remove known flake patterns (fast, high ROI)
1) **Eliminate time-based flake in domain tests**
   - `backend/tests/Taskdeck.Domain.Tests/Entities/BoardTests.cs` uses `Thread.Sleep(10)` to force timestamp deltas.
   - Replace with one of:
     - (Preferred) weaken assertion to monotonic time (`UpdatedAt >= originalUpdatedAt`) and delete sleep.
     - (Optional, heavier) introduce a time provider abstraction in Domain and inject controllable time in tests.

2) **Replace manual polling loops with Playwright-native polling**
   - `frontend/taskdeck-web/tests/e2e/automation-ops.spec.ts` has a manual `for + setTimeout(500)` polling loop.
   - Prefer `expect.poll` so failure output is clearer and timeouts are centralized.

3) **Centralize E2E “seed + auth + board setup” helpers**
   - You already have `registerAndAttachSession` and fixture bootstrap helpers.
   - Consolidate into a single “E2E harness module” so every new test doesn’t re-invent seeding.

### Priority 1 — Add “contract & drift detection” guardrails (compounding)
These are *harness* improvements: small initial work, long-term payoff.

4) **OpenAPI drift guardrail**
   - Generate OpenAPI JSON in CI and:
     - validate it parses (sanity), and
     - optionally compare against a committed `docs/generated/openapi.json` snapshot (breaking-change awareness).

5) **Frontend API contract alignment**
   - Option A: generate TS types from OpenAPI, compile-check them
   - Option B: keep your current hand-written types, but add a “schema smoke” test that checks critical response shapes.

6) **Coverage reporting: start by collecting, not gating**
   - You already have Vitest coverage config, but CI does not surface it.
   - For .NET: start collecting coverage as an artifact for Domain+Application.
   - For frontend: add a nightly/weekly coverage job (don’t slow PR CI yet).

### Priority 2 — Expand “multi-component” / “multi-layer” test coverage
7) **Frontend route-level integration tests**
   - In addition to component tests, add a small set of tests that mount:
     - router + stores + a full view (e.g., BoardView) and assert a user journey.
   - This catches “wiring errors” that pure unit tests miss, without full E2E cost.

8) **Backend transactional behavior tests**
   - Add at least 2–3 “integration-through-service” tests for operations that have:
     - multi-entity updates,
     - failure rollbacks,
     - idempotency.
   - If you don’t want a new project, do it as **API integration tests** (already present).

### Priority 3 — Add targeted fuzz/property tests for high-risk parsers & user input
9) **Property-based tests for:**
   - starter pack manifest parsing/validation
   - query builder/filter parsing
   - export/import payload boundary validation

### Priority 4 — Quality-of-tests assurance (advanced, optional)
10) **Mutation testing (sampled)**
   - Run mutation tests on a small subset (Domain + a couple key services) on a schedule.
   - Use it as “signal”, not a hard gate.

---

## 2) “Newbie traps” to avoid as your test suite grows

### Trap A: adding tests that don’t enforce anything
- A test that only checks “returns Ok” can be weak.
- Prefer: check state transitions, invariants, and negative cases.

### Trap B: E2E overgrowth
- E2E is expensive and flaky relative to unit/integration.
- Keep E2E: **happy paths + 2–3 critical regression guards** (auth, realtime, drag/drop, WIP).

### Trap C: unstable selectors
- You did the right thing by using roles and `data-*` hooks.
- Standardize:
  - `data-testid` for unit tests
  - `data-action` for interactions
  - `aria-*` and roles for accessibility + E2E stability.

### Trap D: tests that depend on wall-clock time
- Don’t sleep; don’t assume time differences.
- If you need time: inject a clock or assert monotonic ordering.

---

## 3) Definition of “Better” for Taskdeck tests (explicit targets)

These targets are meant to be realistic for a solo product.

### Test confidence targets
- **High confidence** for:
  - authn/authz, cross-user isolation
  - board workflow correctness (create/move/limit/filter)
  - import/export/starter-pack safety boundaries
  - automation proposal approval/execution policy boundaries
- **Medium confidence** for:
  - ops/logs surfaces (mostly contract tests)
  - archive recovery (CRUD + permissions)

### Stability targets
- E2E suite: < 5 minutes locally; < 10 minutes in CI; 0–1 flaky tests at any time.
- “Fast tests” (backend unit + frontend unit): should run constantly during development (watch mode).

---

## 4) Immediate actionable edits (tiny diffs)

### 4.1 Remove `Thread.Sleep` flake
- File: `backend/tests/Taskdeck.Domain.Tests/Entities/BoardTests.cs`
- Replace time delta assertion with monotonic.
- Delete `Thread.Sleep`.

### 4.2 Convert manual polling to `expect.poll`
- File: `frontend/taskdeck-web/tests/e2e/automation-ops.spec.ts`
- Replace the `for` loop that polls `/llm/chat/sessions/{id}` with `expect.poll`.

### 4.3 Add a tiny “E2E Harness” module
- New file: `frontend/taskdeck-web/tests/e2e/support/e2eHarness.ts`
- Export:
  - `createAuthedUser(scope)`
  - `createBoardViaApi(token, ...)`
  - `waitForProposal(sessionId, token, ...)`
  - `cleanupDb(file)` (optional)

---

## 5) Next: pick a wave
Move to:
- `02_TEST_SCENARIOS_BACKLOG.md` and pick *Wave 1*.
