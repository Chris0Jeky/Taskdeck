# Taskdeck Manual Product Audit

Last Updated: 2026-03-26

## Purpose

Record a documentation-led manual audit of the current Taskdeck product surface, then validate it by actually using the running application and selected API endpoints.

This is a runtime analysis artifact, not a roadmap reset.
It captures:
- what the current repo and product appear to do
- what worked in a live pass
- where runtime behavior diverged from docs, UX expectations, or test posture
- what engineering follow-up looks most justified

## Scope

Primary audit goal:
- read the active product docs and verify the actual product behavior against them

Surfaces exercised:
- auth and workspace shell
- `Home`
- first-run board setup
- `Boards`
- board-scoped capture and `Inbox`
- `Review`
- proposal approval and execution
- provenance links from review, inbox, notifications, and cards
- `Today`
- `Activity`
- `Ops`
- `Chat`
- `Archive`
- selected API error-contract and sandbox checks

## Runtime Context

Run date:
- 2026-03-26

Environment:
- repo root: `C:\Users\jekyt\source\Taskdeck`
- frontend URL: `http://localhost:5173`
- backend URL: `http://localhost:5000`
- backend readiness: `GET /health/ready` returned `200`
- frontend availability: `GET http://localhost:5173` returned `200`

Observed running processes:
- backend `Taskdeck.Api.exe` listening on `5000`
- frontend dev server listening on `5173`

Execution mode:
- headed Playwright browser for the manual workflow pass

## Docs Reviewed First

Authoritative and user-facing docs reviewed before runtime testing:
- [AGENTS.md](C:/Users/jekyt/source/Taskdeck/AGENTS.md)
- [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md)
- [IMPLEMENTATION_MASTERPLAN.md](C:/Users/jekyt/source/Taskdeck/docs/IMPLEMENTATION_MASTERPLAN.md)
- [GOLDEN_PRINCIPLES.md](C:/Users/jekyt/source/Taskdeck/docs/GOLDEN_PRINCIPLES.md)
- [TESTING_GUIDE.md](C:/Users/jekyt/source/Taskdeck/docs/TESTING_GUIDE.md)
- [START_HERE.md](C:/Users/jekyt/source/Taskdeck/docs/START_HERE.md)
- [USER_MANUAL.md](C:/Users/jekyt/source/Taskdeck/docs/USER_MANUAL.md)
- [MANUAL_TEST_CHECKLIST.md](C:/Users/jekyt/source/Taskdeck/docs/MANUAL_TEST_CHECKLIST.md)

## Claimed Product Shape

The active docs describe Taskdeck as a local-first, capture-first, review-first workspace with this core loop:

`Home -> Inbox/Capture -> Review -> Board`

The docs also claim the shipped product currently supports:
- `Home` as the default landing page
- `Today` as the daily agenda surface
- `Inbox` as the low-friction intake surface
- `Review` as the proposal trust gate
- `Boards` as the main work surface
- advanced surfaces including `Notifications`, `Chat`, `Activity`, `Ops`, `Access`, and `Archive`
- workspace modes, including a `Workbench` mode that keeps all shipped tools visible in the main nav

See:
- [START_HERE.md](C:/Users/jekyt/source/Taskdeck/docs/START_HERE.md)
- [USER_MANUAL.md](C:/Users/jekyt/source/Taskdeck/docs/USER_MANUAL.md)

## What The Product Demonstrably Does

Based on the live runtime pass, the current app can demonstrably:
- register a new user and route into the workspace
- land on `Home`
- create a board through the first-run setup flow
- apply an `Engineering sprint` starter shape during setup
- create a board-scoped capture from a board
- persist the capture into `Inbox`
- triage that capture into a proposal
- show the resulting proposal in `Review`
- approve the proposal
- execute the proposal and create the board card
- show provenance links from card to capture and proposal
- show a proposal outcome notification and route it back into board-scoped review
- create a manual chat session and receive an assistant response
- run the `health.check` Ops template and display its logs
- open `Archive`, `Activity`, `Today`, and advanced operator surfaces directly
- return stable API error payloads for the sampled auth and validation cases

## Manual Workflow Summary

The main live workflow executed was:
1. Register a new account.
2. Land on `Home`.
3. Start setup and create `Manual QA Board` from the `Engineering sprint` shape.
4. Use `Capture here` to create a board-scoped capture.
5. Open `Inbox`.
6. Start triage on the capture.
7. Open the linked proposal in `Review`.
8. View diff.
9. Approve the proposal.
10. Execute the proposal.
11. Confirm the new card exists on the board.
12. Inspect provenance from card, inbox, review, and notifications.
13. Exercise `Today`, `Activity`, `Ops`, `Chat`, and `Archive`.

Outcome:
- the core `Home -> Capture -> Review -> Board` loop worked end to end
- several mismatches and reliability issues appeared around freshness, discoverability, realtime posture, and docs accuracy

## Findings

### P2 - SignalR realtime is not healthy in the live local app

Observed behavior:
- browser console logged repeated failures for `/hubs/boards/negotiate`
- the failure was specifically a CORS preflight problem
- the preflight response included `Access-Control-Allow-Origin`
- the preflight response did not include `Access-Control-Allow-Credentials: true`

Why this matters:
- the repo claims shipped realtime board updates and a board realtime lifecycle
- in the tested runtime, that path is not actually healthy
- the app may remain usable via fallback polling, but the advertised primary realtime path is degraded

Evidence:
- docs claims in [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md:74), [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md:108), and [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md:707)
- CORS policy setup in [Program.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Api/Program.cs:318) through [Program.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Api/Program.cs:322)
- no `AllowCredentials()` call in that policy

Console error summary:
- `Access to fetch at 'http://localhost:5000/hubs/boards/negotiate?negotiateVersion=1' from origin 'http://localhost:5173' has been blocked by CORS policy`
- `The value of the 'Access-Control-Allow-Credentials' header in the response is '' which must be 'true'`

Recommendation:
- make the frontend CORS policy hub-compatible when SignalR uses credentialed requests
- rerun realtime-specific manual and E2E validation after the policy change

### P2 - Inbox triage completion stays stale until manual refresh

Observed behavior:
- after clicking `Start Triage`, the capture stayed in `Triaging...` in the Inbox detail view
- the backend had already produced the proposal
- the Inbox view did not transition itself to `Proposal Created`
- clicking `Refresh Detail` immediately updated the item and exposed `Open Proposal`

Why this matters:
- this is the core golden path
- the user gets misleading feedback at exactly the trust-critical handoff from intake to review
- it makes the product feel stalled even though the backend succeeded

Likely cause:
- the store only performs a single immediate refresh after enqueue
- if the queue finishes after that one fetch, the UI remains stale until the user refreshes manually

Evidence:
- refresh button and detail states in [InboxView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/InboxView.vue:343), [InboxView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/InboxView.vue:552), [InboxView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/InboxView.vue:557), and [InboxView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/InboxView.vue:567)
- triage flow in [captureStore.ts](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/store/captureStore.ts:189), [captureStore.ts](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/store/captureStore.ts:215), and [captureStore.ts](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/store/captureStore.ts:216)

Recommendation:
- add short-lived polling or push-driven refresh after triage enqueue
- stop polling once the item reaches `Triaged`, `ProposalCreated`, `Ignored`, or `Failed`

### P2 - Workbench mode and docs overstate what is actually visible

Observed behavior:
- switching to `Workbench` did not expose all advanced surfaces in the main nav
- `Activity`, `Ops`, `Access`, and `Archive` were still absent from the nav
- those surfaces only became reachable through direct routes or by enabling feature flags in `Settings`

Why this matters:
- the docs say Workbench keeps all shipped tools visible in the nav
- the shell copy says `Keep every workspace surface visible for hands-on work`
- actual navigation behavior does not match that statement

Docs evidence:
- [USER_MANUAL.md](C:/Users/jekyt/source/Taskdeck/docs/USER_MANUAL.md:51) through [USER_MANUAL.md](C:/Users/jekyt/source/Taskdeck/docs/USER_MANUAL.md:58)
- [START_HERE.md](C:/Users/jekyt/source/Taskdeck/docs/START_HERE.md:113) through [START_HERE.md](C:/Users/jekyt/source/Taskdeck/docs/START_HERE.md:122)

Code evidence:
- Workbench copy in [AppShell.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/components/shell/AppShell.vue:55)
- nav items gated by flags in [AppShell.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/components/shell/AppShell.vue:134), [AppShell.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/components/shell/AppShell.vue:144), [AppShell.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/components/shell/AppShell.vue:174), and [AppShell.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/components/shell/AppShell.vue:184)
- default advanced flags off in [feature-flags.ts](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/types/feature-flags.ts:16) through [feature-flags.ts](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/types/feature-flags.ts:20)

Recommendation:
- either update the docs and shell copy to reflect feature-flagged visibility
- or change Workbench mode so it actually enables those shipped advanced nav items by default

### P3 - Board history is narrower than the UI and checklist imply

Observed behavior:
- in `Activity`, fetching board history for the audited board returned no entries
- that happened even after creating a board, creating a card through proposal execution, and generating clearly board-scoped activity
- user history for the same account did show the expected card creation event

Why this matters:
- the UI guidance implies board history is a broad board-scoped audit view
- the checklist expects entries to appear there
- the current implementation appears to only include audit rows whose entity is literally the board itself

Docs and UI evidence:
- expectations in [MANUAL_TEST_CHECKLIST.md](C:/Users/jekyt/source/Taskdeck/docs/MANUAL_TEST_CHECKLIST.md:217) and [MANUAL_TEST_CHECKLIST.md](C:/Users/jekyt/source/Taskdeck/docs/MANUAL_TEST_CHECKLIST.md:218)
- board-history view language in [ActivityView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ActivityView.vue:141) and [ActivityView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ActivityView.vue:511)

Implementation evidence:
- history service delegates directly to board-only repository calls in [HistoryService.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Application/Services/HistoryService.cs:28)
- repository board query only returns `EntityType == "board"` rows in [AuditLogRepository.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Infrastructure/Repositories/AuditLogRepository.cs:131), [AuditLogRepository.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Infrastructure/Repositories/AuditLogRepository.cs:137), and [AuditLogRepository.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Infrastructure/Repositories/AuditLogRepository.cs:144)

Recommendation:
- decide explicitly whether board history means:
  - only board-entity mutations
  - or all activity scoped to the board
- then align the UI copy, manual checklist, and backend query semantics to that decision

### P3 - Review still leaks raw UUIDs into the happy path

Observed behavior:
- review cards displayed affected entities as `Card <guid>`
- diff rows displayed raw target IDs such as `create card:<guid>`
- review also exposed raw triage run IDs inline

Why this matters:
- the product doctrine explicitly rejects raw IDs in the happy path
- the review surface is part of the core novice-facing loop
- this weakens readability and trust instead of improving it

Policy evidence:
- [GOLDEN_PRINCIPLES.md](C:/Users/jekyt/source/Taskdeck/docs/GOLDEN_PRINCIPLES.md:33)

Implementation evidence:
- diff preview construction in [AutomationProposalService.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:282)
- affected-entity label building in [AutomationProposalService.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:370), [AutomationProposalService.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:552), and [AutomationProposalService.cs](C:/Users/jekyt/source/Taskdeck/backend/src/Taskdeck.Application/Services/AutomationProposalService.cs:559)
- review rendering points in [ReviewView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ReviewView.vue:559), [ReviewView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ReviewView.vue:563), [ReviewView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ReviewView.vue:590), and [ReviewView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ReviewView.vue:615)

Recommendation:
- show entity names where possible, not IDs
- keep raw IDs behind an explicit advanced details affordance if they are needed for debugging

### P3 - Active testing docs are stale

Observed behavior:
- current API integration test run passed `322` tests, not `309`
- current Playwright suite ran `31` tests total, not the older `24/24` framing in the active docs
- the current default full run produced `29 passed`, `1 failed`, `1 skipped`

Why this matters:
- active testing docs are supposed to reflect verified reality
- these docs currently overstate stability and understate suite growth

Docs evidence:
- [TESTING_GUIDE.md](C:/Users/jekyt/source/Taskdeck/docs/TESTING_GUIDE.md:18)
- [TESTING_GUIDE.md](C:/Users/jekyt/source/Taskdeck/docs/TESTING_GUIDE.md:22)
- [TESTING_GUIDE.md](C:/Users/jekyt/source/Taskdeck/docs/TESTING_GUIDE.md:23)
- [TESTING_GUIDE.md](C:/Users/jekyt/source/Taskdeck/docs/TESTING_GUIDE.md:28)
- [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md:509)
- [STATUS.md](C:/Users/jekyt/source/Taskdeck/docs/STATUS.md:553)

Recommendation:
- refresh `STATUS.md` and `TESTING_GUIDE.md` with current counts and current E2E outcome
- note that the observed E2E failure appears flaky rather than deterministically product-blocking

## Notable Behaviors That Worked Well

- the first-run setup flow successfully created a useful board and applied a starter blueprint
- board-scoped capture correctly carried board context into Inbox and Review
- proposal approval and execution worked end to end
- card provenance showed capture and proposal origins together in the card modal
- proposal notifications routed back into the correct board-scoped review location
- Ops role messaging for an `Editor` was actionable and more legible than a generic permission denial
- quick command palette and keyboard shortcuts dialog both worked in the live pass
- `Today` correctly reflected a completed onboarding loop after the core path was exercised

## Other Runtime Notes

### Review open-board routing

During one live click from Review, `Open Board` appeared to land on the board list instead of the specific board.

However:
- a later targeted retest from the same review route correctly navigated to `/workspace/boards/{boardId}`
- the implementation in [ReviewView.vue](C:/Users/jekyt/source/Taskdeck/frontend/taskdeck-web/src/views/ReviewView.vue:371) is correct

Conclusion:
- this was observed once but is not yet a solid deterministic bug
- treat it as a watch item, not a confirmed finding

### Workspace-mode Playwright failure

The only failing case in the full Playwright run was:
- `home landing and workspace mode preference should persist across navigation and reload`

Observed full-suite failure:
- timeout while waiting for the `PUT /api/workspace/preferences` response

Important nuance:
- rerunning that exact test alone passed immediately
- manual runtime verification also showed the server-backed mode persisted as `workbench`

Conclusion:
- current evidence points to an E2E flake or concurrency/race issue, not a clean deterministic product regression

## Automated Verification Executed

### Frontend E2E

Command:

```powershell
cd frontend/taskdeck-web
npx playwright test --reporter=line
```

Result:
- `29 passed`
- `1 failed`
- `1 skipped`

Failing test:
- `tests/e2e/smoke.spec.ts:14:1`
- `home landing and workspace mode preference should persist across navigation and reload`

Isolated rerun:

```powershell
cd frontend/taskdeck-web
npx playwright test tests/e2e/smoke.spec.ts -g "home landing and workspace mode preference should persist across navigation and reload" --reporter=line
```

Result:
- `1 passed`

Interpretation:
- likely flake or shared-state timing issue in the suite

### Backend API Integration

Command:

```powershell
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1
```

Result:
- `322 passed`
- `0 failed`

## API Spot Checks

Observed live API contract checks:
- `GET /api/llm/chat/sessions` without auth -> `401` with `{ errorCode, message }`
- `GET /api/ops/cli/templates` without auth -> `401` with `{ errorCode, message }`
- `GET /api/archive/items` without auth -> `401` with `{ errorCode, message }`
- `GET /api/llm-queue/status/not-a-real-status` with auth -> `400` with `{ errorCode: "ValidationError", message }`
- `GET /api/export/database` with auth and sandbox disabled -> `403`
- `GET /api/workspace/preferences` with auth returned persisted `workspaceMode = "workbench"`

These sampled checks aligned with the documented stable error-contract posture.

## Commands Run

Repo and doc discovery:

```powershell
Get-Content -Raw docs/STATUS.md
Get-Content -Raw docs/IMPLEMENTATION_MASTERPLAN.md
Get-Content -Raw docs/GOLDEN_PRINCIPLES.md
Get-Content -Raw docs/TESTING_GUIDE.md
Get-Content -Raw docs/START_HERE.md
Get-Content -Raw docs/USER_MANUAL.md
Get-Content -Raw docs/MANUAL_TEST_CHECKLIST.md
```

Runtime checks:

```powershell
Invoke-WebRequest http://localhost:5000/health/ready
Invoke-WebRequest http://localhost:5000/api/boards
Invoke-WebRequest http://localhost:5173
```

Automated verification:

```powershell
cd frontend/taskdeck-web
npx playwright test --reporter=line
npx playwright test tests/e2e/smoke.spec.ts -g "home landing and workspace mode preference should persist across navigation and reload" --reporter=line

cd C:\Users\jekyt\source\Taskdeck
dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj -c Release -m:1
```

Selected live API checks:

```powershell
Invoke-WebRequest http://localhost:5000/api/llm/chat/sessions
Invoke-WebRequest http://localhost:5000/api/ops/cli/templates
Invoke-WebRequest http://localhost:5000/api/archive/items
Invoke-WebRequest http://localhost:5000/api/llm-queue/status/not-a-real-status
Invoke-WebRequest http://localhost:5000/api/export/database
Invoke-WebRequest http://localhost:5000/api/workspace/preferences
Invoke-WebRequest -Method Options http://localhost:5000/hubs/boards/negotiate?negotiateVersion=1
```

## Bottom Line

Taskdeck's main promised loop is real:
- register
- create a board
- capture
- triage
- review
- apply
- continue on the board

That substrate is not hypothetical.
It works in the live product.

The most important issues are now around:
- freshness and trust cues in the golden path
- realtime health versus claimed realtime capability
- discoverability and docs accuracy around Workbench and advanced surfaces
- review readability for novice-facing flows

This is not a broken product.
It is a product whose core flow works, but whose runtime polish and documentation coherence still lag the stated legibility standard.
