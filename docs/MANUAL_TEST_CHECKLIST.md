# Manual Test Checklist

Use this checklist to manually validate current Taskdeck behavior on `main`.

Last Updated: 2026-03-29
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Run Metadata Template (Required)

Record this in the issue or PR comment before and after each manual run:
- date/time (UTC)
- commit SHA
- browser and version
- OS
- DB baseline (`fresh` or `existing`)
- env flags changed (if any)
- artifacts collected (screenshots, logs, request IDs)

## Scope and Boundaries

In scope:
- Core board workflow: boards, columns, cards, labels, filters, drag/drop, keyboard flows.
- Workspace shell: navigation, command palette keyboard model, shortcuts help.
- Advanced surfaces: automations (queue/proposals/chat), capture inbox/triage/provenance flow, ops (CLI templates/logs), archive, activity.
- API contract spot checks for auth, access, queue, archive, automation, chat, and ops endpoints.

Out of scope (known implementation boundaries on current `main`):
- Database export/import is intentionally sandbox-gated and returns `403` unless `DevelopmentSandbox.Enabled` is true in Development.
- Database import is file-replacement based; if SQLite file locks are active, import may fail and should be retried during a quiescent period.

## Preconditions

1. Start backend API:
   - `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj`
2. Start frontend:
   - `cd frontend/taskdeck-web`
   - `npm run dev`
3. Open the frontend URL printed by Vite (default `http://localhost:5173`; fallback `http://localhost:4173` or `http://localhost:5001` when `5173` is restricted).
4. Register/login a test user in UI (or use API bootstrap).

Fallback when `localhost:5173` is blocked (`listen EACCES`):
- `npm run dev` now auto-selects a fallback port (`4173`, then `5001`) and starts without manual overrides.
- launcher now chooses a bindable fallback port and skips occupied candidates (including existing Taskdeck listeners) for new Vite processes.
- strict-port startup avoids implicit Vite port auto-increment drift.
- backend Development CORS defaults include `http://localhost:4173` and `http://localhost:5001`, so frontend auth/API requests remain allowed when fallback ports are used.
- If you need an explicit port, run:
  - `cd frontend/taskdeck-web`
  - `npm run dev -- --host localhost --port 5001`
- Troubleshooting note: some Windows local environments reserve or restrict `localhost:5173` for user-space listeners, which surfaces as `listen EACCES`.

Optional clean start:
- Stop API process.
- Remove `backend/src/Taskdeck.Api/taskdeck.db`.
- Restart API.

## C0. Container Deployment Hardening Matrix (`#142`)

Goal:
- validate deployment hardening behavior beyond happy-path startup for the compose baseline.

Automated path:
1. From repo root, run:
   - `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080`
2. Expected:
   - secret-gated compose rendering fails when `TASKDECK_JWT_SECRET` is missing
   - reverse-proxy security headers are present on `/`
   - unauthorized proxy paths remain deterministic (`/api/boards` and `/hubs/boards/negotiate` return `401`)
   - start/restart/stop flow succeeds and leaves no running baseline services

Manual-only checks (non-automatable in generic local script):
1. Backend direct exposure posture:
   - verify deployment path does not expose the backend container directly to public ingress while forwarded-header trust is enabled.
2. Edge TLS termination posture:
   - for non-local environments, confirm HTTPS terminates at edge/proxy and only private-network HTTP reaches the compose stack.
3. Host restart operational check (staging/ops rehearsal):
   - restart container host/daemon per environment runbook, then verify stack recovers with expected service health.

## A. Authentication and Workspace Shell

1. Register new user from `/register`.
   - Expected: redirected/authenticated into workspace.
2. Login with valid credentials from `/login`.
   - Expected: routed to `/workspace/home`.
3. Attempt workspace route while logged out.
   - Expected: redirected to `/login` with redirect query.
4. Open `/workspace/today`.
   - Expected: agenda shows review/triage/overdue/due-today/blocked summary cards and the onboarding loop block.
5. Start `Start Useful Board` from `Home` or `Today`, pick a starter shape, and create a board.
   - Expected: board opens immediately; when a starter pack applies successfully, the starter workflow is present, and when it fails, the board still opens with a warning.
6. Dismiss onboarding from `Home` or `Today`, refresh, then replay it.
   - Expected: dismiss/replay state persists and the guided setup path is recoverable without trapping experienced users.
7. Open command palette via `Ctrl+K`/`Cmd+K`, use arrow keys to select an item, and press `Enter`.
   - Expected: command activates the selected item and closes the palette; `Escape` closes palette without navigation.
8. Open shortcuts help via `?` and close with `Escape`.
   - Expected: dialog toggles correctly.
9. Logout from top bar.
   - Expected: token/session cleared, redirected to `/login`.

## B. Boards, Columns, Cards, Labels

1. Create board from workspace boards page.
   - Expected: route becomes `/workspace/boards/{id}` and heading matches board name.
2. Rename board in Board Settings.
   - Expected: heading updates and board remains accessible.
3. Archive and unarchive board via Board Settings.
   - Expected: archived board hidden from default boards list; unarchived board reappears.
4. Use `Archive Board` action in Board Settings.
   - Expected: redirected to `/workspace/boards`, archived board absent from default list (soft-delete behavior).
5. Use the board action rail on a populated board.
   - Expected:
     - `Capture here` opens a capture modal scoped to the current board.
     - `Ask assistant` opens `/workspace/automations/chat?boardId={boardId}`.
     - `Review proposals` opens `/workspace/review?boardId={boardId}`.
     - `Add card` opens the inline add-card affordance for the active column, or the add-column form when the board is empty.

6. Create two columns, then reorder columns by drag/drop using the `Drag Column` handle.
   - Expected: visual order changes and persists on refresh.
7. Set WIP limit on a column.
   - Expected: `count/limit` indicator visible.
8. Attempt to exceed WIP by adding/moving cards.
   - Expected: operation blocked with visible error feedback.

9. Create card inline.
   - Expected: card appears in target column.
10. Open card modal (`Enter` on selected card or click).
   - Expected: modal opens with current values.
11. Edit title/description, set due date, block with reason, assign labels.
    - Expected: updates persist and render in lane.
12. Move card to another column via drag/drop using the `Drag Card` handle.
    - Expected: card relocates and counts update.
13. Delete card from modal.
    - Expected: card removed.

14. Open label manager and perform create/update/delete.
    - Expected: label list and card chips reflect changes.

## C. Filters and Keyboard Workflow

1. Toggle filter panel with `f`.
   - Expected: panel opens/closes.
2. Apply text, due-date, blocked, and label filters.
   - Expected: visible card set matches filter logic.
3. Close and reopen filter panel in-session.
   - Expected: filter state persists during session.

4. Board keyboard navigation:
   - `h/l` or arrows for column movement, `j/k` for card selection.
   - `Enter` opens selected card.
   - `n` opens inline new card form.
5. Escape-close behavior:
   - Expected contract:
     - Escape closes only the top-most transient surface (dialog/panel/form) per key press.
     - When no transient surface is open on a board route, Escape navigates to `/workspace/boards`.
     - Escape inside input-assist closes the suggestion panel without cascading to unrelated board/workspace actions.

## D. Automations, Chat, and Proposals

1. Open `/workspace/automations/chat` and create session.
   - Expected: session appears and can be selected.
2. Send non-actionable message.
   - Expected: assistant response appears.
3. Create board-scoped chat session and send actionable instruction with `Request proposal generation` enabled.
   - Expected: assistant response includes proposal reference.
4. Open `/workspace/review` and locate proposal.
   - Expected: review card visible with status/actions, readable summary/risk/source/affected-entity cues, and legacy `/workspace/automations/proposals` links redirect here.
5. Open proposal, inbox, notification, and card provenance links that target the same board-scoped proposal.
   - Expected: all land on `/workspace/review?boardId={boardId}#proposal-{proposalId}` or the equivalent routed location with board context preserved.
6. Approve proposal.
   - Expected: status transitions to `Approved`.
7. Execute proposal with confirmation.
   - Expected: status transitions to `Applied`.
8. View diff for proposal.
    - Expected: diff payload displays.

## E. Inbox and Notifications Continuity

1. Open `/workspace/inbox?boardId={boardId}` after creating a board-scoped capture.
   - Expected: inbox header shows the board context banner and list fetch stays scoped to that board.
2. Open a capture detail with proposal provenance from the board-scoped inbox.
   - Expected: `Open Proposal` keeps the same `boardId` query when routing into Review.
3. Open `/workspace/notifications?boardId={boardId}`.
   - Expected: notifications header shows the board context banner and refresh/unread filtering stays scoped to that board.
4. Open a proposal notification and a board-only notification.
   - Expected: proposal notifications route to board-scoped Review, while board-only items route back to the related board.

## F. Ops Console and Logs

1. Open `/workspace/ops/cli`.
   - Expected: templates load.
2. Run `health.check` template.
   - Expected: successful run and output preview containing health text.
3. Switch to logs tab and query logs.
   - Expected: entries returned for broad query.
4. Fetch logs by correlation ID for a recent run.
   - Expected: run-correlated entries returned.

## G. Archive and Recovery

1. Open `/workspace/archive` and refresh.
   - Expected: items load without error.
2. Filter by entity type.
   - Expected: list narrows correctly.
3. Restore an available archived item.
   - Expected: restore succeeds, item removed from list, success toast.
4. Validate board archive/unarchive coherence against archive view.
   - Expected: archived boards are visible in `/workspace/archive` and can be restored there; restored boards return to default boards list.

## H. Activity View

1. Open `/workspace/activity`.
   - Expected: view loads and allows mode selection (`board`, `entity`, `user`).
2. Fetch board history using the board selector.
   - Expected: timeline entries include board-level mutations *and* card/column/label activity within that board.
3. Fetch entity history using entity type + board context + entity selectors.
   - Expected: timeline entries display without manual raw ID entry.
4. Fetch user history in `user` mode.
   - Expected: current-user timeline entries display.

## I. API Spot Checks

Assume API at `http://localhost:5000`.

1. Register/login:
   - `POST /api/auth/register`
   - `POST /api/auth/login`
   - Expected: token and user payload.
2. Chat unauthorized check:
   - `GET /api/llm/chat/sessions` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
3. Ops unauthorized check:
   - `GET /api/ops/cli/templates` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
4. Archive unauthorized check:
   - `GET /api/archive/items` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
5. Queue status validation:
   - `GET /api/llm-queue/status/not-a-real-status`.
   - Expected: `400` with validation-style `errorCode` and `message`.
6. Automation execute without `Idempotency-Key`:
   - `POST /api/automation/proposals/{id}/execute` without header.
   - Expected: `400` with validation-style `errorCode` and `message`.
7. Database export/import sandbox gate:
   - `GET /api/export/database` and `POST /api/import/database` with bearer token while sandbox is disabled.
   - Expected: `403` with JSON body containing `errorCode` and `message`.

## J. Observability Smoke (OBS-01)

1. In `backend/src/Taskdeck.Api/appsettings.Development.json`, set:
   - `"Observability": { "EnableConsoleExporter": true }`
2. Restart API and perform:
   - one board or card mutation from UI
   - one ops command run from `/workspace/ops/cli`
3. Call `GET /health/ready`.
4. In backend console output, verify:
   - HTTP telemetry is emitted (request spans/metrics)
   - custom `taskdeck.*` metrics appear (`queue.backlog`, `worker.*`, `heartbeat.staleness`)
   - request spans include `taskdeck.correlation_id`
5. Revert `EnableConsoleExporter` to `false` after smoke validation.

## K. Known-Gap Triage (From Product Notes)

Run these checks even if they currently fail; log outcome explicitly.

1. Drag/edit conflict regression check:
   - Repro: open add-card input or card modal field, perform non-handle drag gestures near editable surfaces.
   - Target behavior: only explicit drag handles initiate board/card drag; editable interactions do not trigger unintended movement.
2. Ops and automation form ergonomics:
   - Repro: create requests using ops/automation UIs.
   - Target behavior: contextual autocomplete/options, reduced manual input burden.
3. Sidebar shortcuts affordance:
   - Repro: test on shorter and taller viewports.
   - Target behavior: shortcuts/help affordance remains discoverable without deep scrolling.

## L. Manual Findings Regression Pack (MAN-2026-02-21)

Use this section to retest the exact findings captured in `docs/archive/2026-02-25_docs-cleanup/notesFromManualTesting.txt`.
Issue wave:
- umbrella: `#173`
- mapped issues: `#174`, `#175`, `#176`, `#177`, `#178`, `#179`

For each test below, capture:
- pass/fail
- screenshot or short clip where UX is involved
- request ID or API payload snippet for backend/auth failures
- linked defect issue if outcome deviates from target behavior

### K1. Auth login/register regression checks (`#174`)

1. Register a new account with unique username/email.
   - Expected: registration succeeds and user can access workspace.
2. Attempt to register again with same username/email.
   - Expected: deterministic duplicate/validation response (no ambiguous invalid-identity message).
3. Immediately attempt login using the same valid account credentials.
   - Expected: login succeeds; duplicate-registration failure does not poison login state.
4. Attempt login with wrong password for existing user.
   - Expected: error clearly indicates invalid credentials path, distinct from account-state failures.
5. Attempt login with valid email but wrong username (or vice versa where supported).
   - Expected: deterministic and consistent identity validation response.

### K2. Starter-pack catalog breadth checks (`#175`)

1. Open starter-pack catalog on a board.
   - Expected: first-party pack set includes expanded categories beyond current baseline.
2. Search for at least three domain-specific pack intents (example: engineering, support, content).
   - Expected: relevant packs are discoverable by search/filter.
3. Open previews for at least five different packs.
   - Expected: each preview renders metadata and planned changes without runtime errors.

### K3. Starter-pack warning-first apply UX checks (`#176`)

1. Apply a pack to a clean board.
   - Expected: apply success path is unchanged and explicit.
2. Re-apply same pack.
   - Expected: non-blocking conflicts are surfaced as warnings (or actionable conflict context), not opaque hard-stop.
3. Apply a second pack with known overlap.
   - Expected: warning vs blocking conflicts are clearly classified.
4. Verify user guidance text.
   - Expected: remediation options are explicit (what can be skipped, retried, or corrected).

### K4. Archive/delete lifecycle consistency checks (`#177`)

1. Open board settings for an active board and inspect lifecycle controls.
   - Expected: archive/delete controls are non-duplicative and semantically clear.
2. Archive board from board settings.
   - Expected: board leaves default list and appears in archive workspace.
3. In archive workspace, inspect available actions.
   - Expected: action model matches documented lifecycle (restore/visibility/delete semantics are clear).
4. Validate hidden archived visibility path.
   - Expected: hidden archived items can be surfaced via explicit command/filter/control.
5. Restore board and confirm list transitions.
   - Expected: restored board returns to default boards list with consistent lifecycle state.

### K5. Card drag affordance checks (`#178`)

1. On a dense board, attempt drag from multiple card surface areas (top, center, near metadata).
   - Expected: drag initiation is practical and not constrained to a tiny corner-only hit area.
2. Attempt editing title/description fields and nearby interactions.
   - Expected: improved drag area does not reintroduce accidental drag during edit flows.
3. Drag across columns repeatedly.
   - Expected: behavior is reliable, with no intermittent non-responsiveness from handle targeting.

### K6. Ops role discoverability and permission-guidance checks (`#179`)

1. Log in as non-admin user and open `/workspace/ops/cli`.
   - Expected: current role/capability context is visible or discoverable from the screen.
2. Run admin-restricted template (example: `boards.list` if still restricted).
   - Expected: permission message includes actionable next steps (not just denial text).
3. Discover runnable templates for current role.
   - Expected: user can identify allowed templates without trial-and-error.
4. Navigate to role-assignment/help path from ops surface (or linked settings/docs).
   - Expected: clear guidance exists for obtaining required role/permissions.
   - Reference: `docs/ops/TASKDECK_HUMAN_OPERATIONS.md` (`A5 Ops CLI role-assignment workflow`).

## M. Post-Run Documentation Check

If behavior, commands, or known gaps changed, update:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/ops/OBSERVABILITY_BASELINE.md` (when telemetry/dashboard/alert contract changes)

## M. Final Automated Smoke Before Merge

1. Backend:
   - `dotnet test backend/Taskdeck.sln -c Release -m:1`
2. Frontend unit/build:
   - `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
   - `cd frontend/taskdeck-web && npm run typecheck && npm run build`
3. Frontend E2E:
   - `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line`
   - fallback when `5173` is unavailable:
     `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db TASKDECK_E2E_FRONTEND_PORT=5001 TASKDECK_E2E_API_CORS_ORIGINS=http://localhost:5001 npx playwright test --reporter=line`

## N. Capture Realignment Manual Slice (Shipped CAP MVP)

Status:
- active; run this slice as part of regular regression/manual confidence checks for shipped capture behavior (`#200` to `#211`)

Goal:
- validate thesis-critical behavior: low-friction capture + review-first trusted automation

Slice checks:
1. Capture speed and UX:
   - open capture surface from keyboard-first entrypoint
   - submit artifact and verify immediate feedback
   - expected: capture flow feels under 10 seconds in normal local use
2. Inbox behavior:
   - verify list/detail semantics (excerpt in list, full text in detail)
   - verify status transitions and explicit user actions
3. Proposal trust gate:
   - trigger triage from inbox
   - verify board mutations occur only through proposal review/apply flow
4. Provenance visibility:
   - verify created proposal/card references capture source and remains user-legible
5. Policy/contract checks:
   - verify unauthenticated/cross-user/missing-resource behavior remains `401/403/404`
   - verify error payload shape remains `{ errorCode, message }`

## O. Testing Harness Wave 1 Manual Slice (`#254` to `#260`)

Status:
- planned; execute while TST-16/TST-17/TST-18 and harness guardrail issues are being implemented

Goal:
- provide manual confidence on persistence and error-contract paths that are being regression-hardened in the testing-harness wave

Slice checks:
1. Drag/drop persistence roundtrip (`#256`):
   - reorder columns and refresh page
   - move a card to another column and refresh page
   - expected: both persisted orders remain intact after refresh
2. WIP enforcement UX confirmation (already-covered, keep monitoring):
   - fill a limited column then try add/move one extra card
   - expected: operation blocked with visible feedback and no state mutation on refresh
3. Representative API error-contract checks (`#257`):
   - run one request each for `400/401/403/404/409`
   - expected: JSON payload includes non-empty `errorCode` and `message`; request-id echo present where middleware guarantees it
4. Sandbox safety check (already-covered, keep monitoring):
   - call database export/import endpoints in non-sandbox posture
   - expected: deterministic `403` rejection
5. Starter-pack idempotency/conflict confirmation (already-covered, keep monitoring):
   - apply the same pack twice and verify no duplicates
   - execute a known conflict path and verify dry-run conflict report with no mutation

## P. Workspace Shell, Board Lifecycle, and Keyboard UX (Slice A)

Status:
- active; detailed step-indexed checklist in `docs/testing/manual-validation-a-workspace-board-ux.md`

Goal:
- validate workspace shell navigation, board CRUD lifecycle, keyboard navigation model, escape behavior stack, drag handle safety, and filter panel UX

Scope (22 scenarios, A-01 through A-22):
1. Auth flows: registration, login, auth guard redirect (A-01 to A-03)
2. Shell navigation: sidebar routes, command palette, keyboard shortcuts help, quick capture modal, logout (A-04 to A-08)
3. Board lifecycle: creation, rename, archive/unarchive, action rail (A-09 to A-12)
4. Board operations: columns, cards, labels, filter panel (A-13 to A-16)
5. Keyboard UX: vim-style board navigation, escape behavior stack (board and shell), drag handle safety (A-17 to A-20)
6. Today view and onboarding: agenda, dismiss/replay persistence (A-21 to A-22)

Reference: `docs/testing/manual-validation-a-workspace-board-ux.md` for full step tables, evidence guidance, automation candidates, and defect filing template.
## Q. Authz Policy, Cross-User Isolation, and API Error Contracts (Slice B, `#131`)

Status:
- active; comprehensive two-user authz matrix covering all controller families

Goal:
- validate authorization enforcement, cross-user data isolation, and error payload contracts across all protected API surfaces

Full checklist:
- `docs/testing/manual-validation-b-authz-contracts.md`

Summary scope:
1. Unauthenticated access denial (401) on all `[Authorize]` controller families (B-01 to B-32)
2. Cross-user board-scoped isolation: UserB cannot access UserA's boards/columns/cards/labels/comments/webhooks/starter-packs/exports/audit (B-40 to B-60)
3. Cross-user non-board-scoped isolation: captures/chat/proposals/archive/notifications/queue/workspace return only the authenticated user's data (B-70 to B-81)
4. True-missing vs cross-user denial indistinguishability (B-90 to B-96)
5. Error payload contract verification for auth/validation/sandbox paths (B-100 to B-110)
6. Advanced controller families: ops/logs/users/abuse/llm-quota/agents/knowledge/webhooks/external-imports (B-130 to B-175)
