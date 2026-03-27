# Demo Rehearsal Runtime Issues — 2026-03-27

## Context

Attempted a full end-to-end demo rehearsal: start servers from cold, seed data, run the client-onboarding scenario, and walk through all four demo stages (Home, Inbox, Review, Board) via Playwright. The goal was to verify that the seeded demo state matches the demo script (`docs/product/DEMO_SCRIPT.md`) and rehearsal contract (`docs/product/SAUL_DEMO_REHEARSAL_CONTRACT.md`).

The core loop was ultimately verified and passed, but the process of getting there surfaced multiple friction points, bugs, and inconsistencies documented below.

---

## Issue 1: `demo:seed` fails on re-run with starter pack column conflicts (409)

**Severity:** Blocker for iterative rehearsal
**Component:** `frontend/taskdeck-web/scripts/demo-seed.mjs`

**What happened:**
Running `npm run demo:seed` against an existing database (with boards from a prior seed) failed with a 409 from `POST /boards/{id}/starter-packs/apply`. The starter pack apply endpoint detected blocking `ColumnPositionConflict` errors because the board already had columns (e.g., "Backlog" at position 0 conflicts with "New Intake" at position 0).

**Error excerpt:**
```
Error: POST /boards/{id}/starter-packs/apply failed (409)
ColumnPositionConflict: Column position '0' is already occupied by 'Backlog'.
ColumnPositionConflict: Column position '1' is already occupied by 'Ready'.
...
```

**Root cause:**
The seeder's `applyStarterPack()` function unconditionally calls the apply endpoint without checking whether the pack has already been applied. On first run against a fresh board, the default columns (Backlog, Ready, In Progress, Done) are created during board creation. The starter pack then tries to create its own columns at the same positions, causing blocking conflicts.

The seeder docs claim "On reruns against the canonical demo account, it now reuses the seeded artifacts it can identify instead of appending a fresh copy" — but this reuse logic does not extend to starter pack application.

**Suggested fix:**
- Option A: Add a dry-run check in the seeder before applying. If `hasBlockingConflicts` is true and the board already has the expected starter pack columns, skip the apply.
- Option B: Add a `--force-reset` flag to the seeder that deletes and recreates the demo boards from scratch.
- Option C: Make the starter pack apply endpoint idempotent — if the target columns already exist with matching names, treat as a no-op.

---

## Issue 2: No `--clean` or `--reset` flag on `demo:seed`

**Severity:** Friction / DX
**Component:** `frontend/taskdeck-web/scripts/demo-seed.mjs`

**What happened:**
After the 409 failure, there was no way to tell the seeder to start fresh. Passing `--help` did not print usage — it ran the seed and failed again with the same error.

**Impact:**
The only recovery path was to manually stop the backend, find and delete the SQLite database file, restart the backend (which triggers EF Core migration on a fresh DB), and re-run the seed. This is a multi-step manual process that breaks rehearsal flow.

**Suggested fix:**
- Add a `--reset` flag that drops and recreates the demo user's boards before seeding.
- Add `--help` flag support that prints usage instead of running the seed.

---

## Issue 3: SQLite DB file location is non-obvious and duplicated

**Severity:** Friction / DX
**Component:** Database file layout

**What happened:**
When manually deleting the DB to recover from Issue 1, `find` revealed nine `.db` files across the repo:
```
backend/src/Taskdeck.Api/taskdeck.db          (main dev DB)
backend/src/Taskdeck.Api/taskdeck.e2e.ci.db
backend/src/Taskdeck.Api/taskdeck.e2e.codexphase.db
backend/src/Taskdeck.Api/taskdeck.e2e.db
backend/src/Taskdeck.Api/taskdeck.e2e.local.db
backend/tests/.../taskdeck.db                  (test DB)
frontend/taskdeck-web/taskdeck.demo.audit.db
frontend/taskdeck-web/taskdeck.demo.ci.db
taskdeck.db                                    (repo root)
```

It was unclear which DB the running backend was using. Both `backend/src/Taskdeck.Api/taskdeck.db` and `taskdeck.db` (repo root) existed. Both had to be deleted to ensure a clean state.

**Suggested fix:**
- Document the canonical DB path in the demo playbook or README.
- Consider a `demo:reset-db` script that handles the delete + restart cycle.

---

## Issue 4: `dotnet run --project` fails with relative paths from wrong working directory

**Severity:** Friction / DX
**Component:** Backend startup

**What happened:**
Running `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj` from a background shell failed with:
```
MSBUILD : error MSB1009: Project file does not exist.
Switch: backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

The working directory of the background shell was not the repo root, so the relative path did not resolve. Had to use the full absolute path `C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Api/Taskdeck.Api.csproj` to start the backend.

**Impact:** Minor — only affects scripted/automated startup. Interactive users would `cd` to the right directory.

**Suggested fix:**
- Demo scripts that start the backend should resolve the project path relative to the script's own location, or document the expected working directory.

---

## Issue 5: Health endpoint path is `/health/live`, not `/api/health/live`

**Severity:** Minor inconsistency
**Component:** Backend API routing

**What happened:**
First health check attempt used `http://localhost:5000/api/health/live` (returned 404). The correct path is `http://localhost:5000/health/live` (returned 200). The health endpoints are not under the `/api` prefix, unlike all other endpoints.

**Impact:** Confusing for anyone writing scripts or checking health manually. The demo playbook documents `http://localhost:5000/api` as the API base URL, but health is outside that prefix.

**Suggested fix:**
- Document the health endpoint path explicitly in the demo playbook.
- Or consider mounting health under `/api/health` for consistency (may conflict with ASP.NET conventions).

---

## Issue 6: `demo:run -- --clean --skip-llm client-onboarding` fails — proposal alias never resolves

**Severity:** Blocker for deterministic demo scenario
**Component:** `frontend/taskdeck-web/scripts/scenario-json-runner.mjs`, `scripts/scenarios-json/client-onboarding.json`

**What happened:**
The client-onboarding scenario defines these steps:
1. `createBoard`
2. `applyStarterPack`
3. `createCapture` (ACME text)
4. `triageCapture`
5. `waitForCaptureProposal` (alias: `onboardingProposal`, timeout: 90s)
6. `executeProposal` (references alias `onboardingProposal`)

With `--skip-llm`, step 4 (`triageCapture`) enqueues the capture but the LLM worker never processes it into a proposal. Step 5 times out or the alias resolves to the literal string `"onboardingProposal"`, and step 6 sends `POST /automation/proposals/onboardingProposal/approve` which returns 400 because `"onboardingProposal"` is not a valid GUID.

**Error:**
```
Error: [POST] /automation/proposals/onboardingProposal/approve -> 400 Bad Request
"The value 'onboardingProposal' is not valid."
```

**Root cause:**
The scenario assumes an LLM worker will process the triage and produce a proposal. `--skip-llm` disables LLM steps but does not skip or stub the `waitForCaptureProposal` and `executeProposal` steps that depend on LLM output.

**Impact:**
The canonical rehearsal command from `SAUL_DEMO_REHEARSAL_CONTRACT.md` is:
```bash
npm run demo:run -- --clean --skip-llm client-onboarding
```
This command **does not work**. It fails every time.

**Suggested fix:**
- Option A: Mark `waitForCaptureProposal` and `executeProposal` steps with `requiresLlm: true` in the scenario JSON so `--skip-llm` skips them.
- Option B: Have the scenario runner detect unresolved aliases and skip dependent steps with a warning instead of sending invalid IDs.
- Option C: Update the rehearsal contract to remove `--skip-llm` and require a live/mock provider, or document that `--skip-llm` produces a partial scenario (capture + triage only, no proposal approval).

---

## Issue 7: Login API returns validation error instead of auth error for missing user

**Severity:** Minor / DX
**Component:** `backend/src/Taskdeck.Api/Controllers/AuthController.cs`

**What happened:**
Before seeding, attempted to log in as `demo` / `demo123` via curl. The response was:
```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,...}
```

This is a model validation error (400), not an authentication failure (401). The user doesn't exist yet, but the response shape suggests a malformed request rather than invalid credentials.

**Impact:** Confusing during scripted flows — a script checking for 401 to detect "user not registered" would miss this.

**Note:** This may be by design (the request body might have failed model validation before reaching the auth logic). Worth verifying whether the same payload returns 401 when the user exists but the password is wrong.

---

## Issue 8: Playwright selector fragility on Inbox item list

**Severity:** Minor / test automation
**Component:** Frontend Inbox DOM structure

**What happened:**
Several Playwright selectors failed to locate Inbox items:
- `text="New client onboarding - ACME Ltd"` — timeout (text was truncated in the DOM)
- `.td-inbox-item:first-child` — timeout (class name doesn't exist or structure differs)
- `li:has-text("ACME Ltd")` — timeout (items may not be `<li>` elements)
- `text=ACME Ltd >> nth=0` — **worked**

**Root cause:**
The Inbox item list does not use semantic list markup (`<ul>/<li>`) or well-known CSS class hooks for individual items. The visible text is truncated with CSS, so exact text matching fails.

**Impact:** Makes Playwright-based demo automation and E2E tests fragile. Selectors that work depend on partial text matching with nth-child disambiguation.

**Suggested fix:**
- Add `data-testid` attributes to Inbox list items (e.g., `data-testid="inbox-item"` or `data-capture-id="{id}"`).
- Consider using semantic `<ul>/<li>` markup for the item list.

---

## Issue 9: Seeded demo state doesn't match the rehearsal contract's ideal story

**Severity:** Narrative / demo quality
**Component:** `frontend/taskdeck-web/scripts/demo-seed.mjs`

**What happened:**
After seeding, the Review page showed the ACME 7-card proposal as **already applied** (status: Applied), while the pending proposal available for review was for **Northwind** (a different client, 2 cards). The demo script and rehearsal contract expect the presenter to show the ACME proposal being reviewed and applied.

**Root cause:**
The seeder creates two captures: one that gets triaged and applied (ACME), and one that is left pending for review (Northwind). The seeder intentionally applies the ACME proposal to populate the board, leaving only Northwind as the reviewable item.

**Impact:**
During a recording following the demo script, the presenter would show the ACME capture in Inbox, then navigate to Review and find only the Northwind proposal pending — a narrative disconnect. The viewer would expect to see the same ACME proposal they just saw in Inbox.

**Suggested fix:**
- Option A: Add a third ACME capture in the seeder that is triaged but left pending for review, so the ACME story is continuous across Inbox and Review.
- Option B: Change the seeder to leave the first ACME proposal pending instead of auto-applying it, and seed the board cards directly.
- Option C: Update the demo script to acknowledge the Northwind proposal as the review example and frame it as "here's another client's work also flowing through."

---

## Summary

| # | Issue | Severity | Category |
|---|-------|----------|----------|
| 1 | `demo:seed` 409 on re-run (starter pack conflicts) | Blocker | Seed tooling |
| 2 | No `--clean`/`--reset`/`--help` on `demo:seed` | Friction | Seed tooling |
| 3 | DB file location non-obvious and duplicated | Friction | DX |
| 4 | `dotnet run` fails with relative paths from wrong CWD | Friction | DX |
| 5 | Health endpoint not under `/api` prefix | Minor | API consistency |
| 6 | `demo:run --skip-llm` fails — proposal alias unresolved | Blocker | Scenario runner |
| 7 | Login returns 400 validation error for missing user | Minor | Auth API |
| 8 | Inbox items lack test-friendly selectors | Minor | Frontend DOM |
| 9 | Seeded state narrative mismatch (ACME applied, Northwind pending) | Narrative | Demo quality |

**Blockers for a one-command deterministic rehearsal:** Issues 1 and 6.
**Friction that slows rehearsal iteration:** Issues 2, 3, 4.
**Polish items:** Issues 5, 7, 8, 9.

## Recommended Priority

1. Fix Issue 6 (scenario runner `--skip-llm` + proposal steps) — the rehearsal contract command must work.
2. Fix Issue 1 (seed idempotency) — re-running the seed must not fail.
3. Add `--reset` flag to `demo:seed` (Issue 2) — fast recovery without manual DB deletion.
4. Address Issue 9 (narrative continuity) — either adjust seeder or update demo script.
5. Remaining items are lower priority but worth tracking.
