# Taskdeck — Codex Execution Document (What Codex should implement)

**Purpose:** This document is meant to be copied into your repo (e.g., `docs/CODEX_EXECUTION_PLAN.md`) and then used as the *single* instruction source you point Codex CLI at.

It encodes the improvements we discussed:
- “Harness engineering” upgrades (rules → checks, short AGENTS → docs as source of truth)
- Repo instruction hardening (AGENTS layering + Security Retrofit playbook)
- CI correctness (typecheck/build + concurrency + caching + richer artifacts)
- Risk reduction mechanics (authz regression harness, structural checks, doc governance)

---

## How to run this with Codex CLI (do this every time)

1) **Tell Codex to read this plan and follow repo AGENTS**
   - Example prompt to Codex:
     > Read `docs/CODEX_EXECUTION_PLAN.md` and implement tasks in order. Keep diffs small, run the required checks, and include results. Prefer one PR/branch per “Delivery Unit”.

2) **Work in delivery units**
   - Each “Delivery Unit” below should be implemented as a **separate branch/PR** unless noted.

3) **After each unit**
   - Run required checks (backend tests + frontend typecheck/build/vitest)
   - Update `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md` if reality/plan changed
   - Include command outputs in the PR description

---

# Global constraints (apply to all units)

## Priority rules
- Always read `docs/STATUS.md` first; it defines Current Focus.
- Keep changes scoped; avoid “drive-by refactors”.
- If a rule can be made mechanical (CI check / test / linter / structural test), prefer that over prose.

## Required checks (local + CI should match)
- Backend: `dotnet test backend/Taskdeck.sln -c Release`
- Frontend: `cd frontend/taskdeck-web && npm run typecheck && npm run build && npx vitest --run`
- If E2E flows touched: Playwright smoke suite (per existing config)

---

# Delivery Unit 0 — Align repo-wide AGENTS + add security playbook

## 0.1 Update root `AGENTS.md` (phase-agnostic; STATUS drives focus)
Replace/patch root `AGENTS.md` to include:
- “Always start here” + precedence: `docs/STATUS.md` > root AGENTS > subfolder AGENTS
- Work protocol: plan before edits; report checks after edits
- Definition of Done: behavior changes ship with tests; docs updated when reality changes
- Security baseline: do not trust client identity; consistent authn/authz; no secrets/PII in logs
- Required checks (with `-c Release` and frontend typecheck/build)

**AC:**
- Root AGENTS becomes a stable standard, not phase-specific.
- It explicitly instructs agents to consult `docs/STATUS.md` and `docs/IMPLEMENTATION_MASTERPLAN.md`.

## 0.2 Add/replace `backend/src/Taskdeck.Api/AGENTS.md` with “Security Retrofit Playbook”
Create a directory-specific `AGENTS.md` under `backend/src/Taskdeck.Api/` that enforces:
- retrofit sequence (one controller family at a time)
- `[Authorize]` posture + claims-based actor identity
- mandatory integration tests for 401/403/cross-user
- consistent error semantics (401 vs 403 vs 404 policy is defined elsewhere, but tests enforce whichever policy repo adopts)

**AC:**
- Any agent editing API controllers sees a strong “do not improvise” retrofit protocol.

---

# Delivery Unit 1 — CI hardening and parity with documented verification commands

## 1.1 Fix frontend CI gap: run typecheck + build
Edit `.github/workflows/ci.yml`:
- In `frontend-unit`, add:
  - `npm run typecheck`
  - `npm run build`
  - keep vitest

**AC:**
- CI fails on TS/build breakages even when tests pass.

## 1.2 Add CI concurrency cancellation
At workflow top-level, add:
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```

**AC:**
- New pushes cancel older in-flight runs for same branch/PR.

## 1.3 Add caching (NuGet + Playwright browsers)
- Cache NuGet packages (`~/.nuget/packages` and Windows equivalent).
- Cache Playwright browsers (`~/.cache/ms-playwright`) in `e2e-smoke`.

**AC:**
- CI runtime improves materially; no functional changes.

## 1.4 Improve failure artifacts (backend TRX + Vitest JUnit optional)
- Add `--logger trx` to backend `dotnet test` calls and upload `TestResults/` on failure.
- Optionally configure Vitest to output a junit report and upload it on failure.

**AC:**
- When CI fails, artifacts make diagnosis fast.

---

# Delivery Unit 2 — Mechanical invariants (“harness engineering” checks)

## 2.1 Add a minimal docs-index governance check (lightweight)
Add a small script (Node or bash) and a CI job to assert:
- required active docs exist:
  - `docs/STATUS.md`
  - `docs/IMPLEMENTATION_MASTERPLAN.md`
  - `docs/TESTING_GUIDE.md`
  - `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/INDEX.md` links to active docs and archive directory
- `docs/STATUS.md` contains a “Last Updated:” date line

**AC:**
- Doc drift becomes a CI failure, not a silent slowdown.

## 2.2 Add structural architecture boundaries check (lightweight)
Add a test project or a simple reflection-based test that enforces:
- Domain doesn’t depend on Infrastructure or Api
- Application doesn’t depend on Api
- Infrastructure can depend on Application/Domain but not on Api
(choose the exact boundaries consistent with your solution)

**AC:**
- Accidental layering violations are caught in CI.

---

# Delivery Unit 3 — Authz regression harness (make security work cheap)

## 3.1 Create a test helper library for API integration tests
In `backend/tests/Taskdeck.Api.Tests` add helpers to make these easy:
- Create users/tokens
- Create board fixtures
- Assert standardized error payload shape (`errorCode`, `message`) where applicable
- One-liners for:
  - `AssertUnauthorized(...)`
  - `AssertForbidden(...)`
  - `AssertNotFoundOrForbidden(...)` (depending on policy)
  - `AssertCrossUserIsolation(...)`

**AC:**
- Adding the “401/403/cross-user matrix” for any endpoint is low-friction.

## 3.2 Standardize error contract (if not already fully standardized)
If your API returns structured errors, codify:
- a consistent `errorCode` enum/strings
- a consistent response shape
- tests asserting it

**AC:**
- Error shape is treated as a contract.

---

# Delivery Unit 4 — Security retrofit slice (first controller family)

**Note:** pick the next controller family from `docs/STATUS.md` / masterplan. Do not expand scope.

## 4.1 Retrofit one controller family end-to-end
For that family:
- Add `[Authorize]` where appropriate
- Replace query/body actor ID usage with claims-derived actor identity
- Ensure board access checks exist (read vs write)
- Add integration tests:
  - unauthenticated → 401
  - authenticated but no access → 403 (or 404, per repo policy)
  - cross-user isolation
  - happy path

**AC:**
- No endpoint in that family relies on caller-supplied actor identity.
- Tests prevent regressions.

---

# Delivery Unit 5 — Observability/diagnosability “boring wins”

## 5.1 Correlation ID propagation (if not already consistent)
- Ensure backend logs include request correlation ID.
- Ensure frontend API client attaches request ID header (or adopts existing convention).
- Ensure ops/log query flows can locate correlated events.

**AC:**
- A single request can be traced across FE → API → worker logs.

## 5.2 Add minimal performance counters for risky subsystems
Add lightweight timing + counts (structured logs) for:
- log query endpoints (duration, result size)
- automation proposal execution (duration, success/failure)

**AC:**
- You can detect regressions in hot paths without heavy tooling.

---

# Delivery Unit 6 — CI/DevEx finishing touches (optional but recommended)

## 6.1 Add PR template
Create `.github/pull_request_template.md` with required checkboxes:
- tests run
- docs updated
- risk notes (security/behavior changes)

**AC:**
- PRs become self-documenting and consistent.

## 6.2 Add issue templates (optional)
Add `.github/ISSUE_TEMPLATE/` for:
- bug report (repro steps, expected/actual)
- tech debt / refactor proposal (scope, risk, exit criteria)
- security/hardening task (401/403/cross-user matrix checklist)

---

# Notes for Codex on MCP usage (do not guess)
- If you need up-to-date OpenAI/Codex config, consult OpenAI Docs MCP.
- If you need library/framework usage details, consult Context7.
- For repo-wide search/refactor audits, prefer ripgrep MCP.
- For UI flows and E2E, prefer Playwright MCP.

---

# Stop conditions / escalation
If any unit:
- expands scope unintentionally
- breaks required checks
- forces a major redesign
Stop and propose options (with risks) instead of pushing forward.

