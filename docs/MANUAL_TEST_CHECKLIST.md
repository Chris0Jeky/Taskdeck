# Manual Test Checklist

Use this checklist to manually validate current Taskdeck behavior on `main`.

Last Updated: 2026-02-20
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## Scope and Boundaries

In scope:
- Core board workflow: boards, columns, cards, labels, filters, drag/drop, keyboard flows.
- Workspace shell: navigation, command palette keyboard model, shortcuts help.
- Advanced surfaces: automations (queue/proposals/chat), ops (CLI templates/logs), archive, activity.
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
3. Open `http://localhost:5173`.
4. Register/login a test user in UI (or use API bootstrap).

Optional clean start:
- Stop API process.
- Remove `backend/src/Taskdeck.Api/taskdeck.db`.
- Restart API.

## A. Authentication and Workspace Shell

1. Register new user from `/register`.
   - Expected: redirected/authenticated into workspace.
2. Login with valid credentials from `/login`.
   - Expected: routed to `/workspace/boards`.
3. Attempt workspace route while logged out.
   - Expected: redirected to `/login` with redirect query.
4. Open command palette via `Ctrl+K`/`Cmd+K`, use arrow keys to select an item, and press `Enter`.
   - Expected: command activates the selected item and closes the palette; `Escape` closes palette without navigation.
5. Open shortcuts help via `?` and close with `Escape`.
   - Expected: dialog toggles correctly.
6. Logout from top bar.
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

5. Create two columns, then reorder columns by drag/drop using the `Drag Column` handle.
   - Expected: visual order changes and persists on refresh.
6. Set WIP limit on a column.
   - Expected: `count/limit` indicator visible.
7. Attempt to exceed WIP by adding/moving cards.
   - Expected: operation blocked with visible error feedback.

8. Create card inline.
   - Expected: card appears in target column.
9. Open card modal (`Enter` on selected card or click).
   - Expected: modal opens with current values.
10. Edit title/description, set due date, block with reason, assign labels.
    - Expected: updates persist and render in lane.
11. Move card to another column via drag/drop using the `Drag Card` handle.
    - Expected: card relocates and counts update.
12. Delete card from modal.
    - Expected: card removed.

13. Open label manager and perform create/update/delete.
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
4. Open `/workspace/automations/proposals` and locate proposal.
   - Expected: proposal card visible with status/actions.
5. Approve proposal.
   - Expected: status transitions to `Approved`.
6. Execute proposal with confirmation.
   - Expected: status transitions to `Applied`.
7. View diff for proposal.
   - Expected: diff payload displays.

## E. Ops Console and Logs

1. Open `/workspace/ops/cli`.
   - Expected: templates load.
2. Run `health.check` template.
   - Expected: successful run and output preview containing health text.
3. Switch to logs tab and query logs.
   - Expected: entries returned for broad query.
4. Fetch logs by correlation ID for a recent run.
   - Expected: run-correlated entries returned.

## F. Archive and Recovery

1. Open `/workspace/archive` and refresh.
   - Expected: items load without error.
2. Filter by entity type.
   - Expected: list narrows correctly.
3. Restore an available archived item.
   - Expected: restore succeeds, item removed from list, success toast.
4. Validate board archive/unarchive coherence against archive view.
   - Expected: archived boards are visible in `/workspace/archive` and can be restored there; restored boards return to default boards list.

## G. Activity View

1. Open `/workspace/activity`.
   - Expected: view loads and allows mode selection (`board`, `entity`, `user`).
2. Fetch board history using the board selector.
   - Expected: timeline entries display.
3. Fetch entity history using entity type + board context + entity selectors.
   - Expected: timeline entries display without manual raw ID entry.
4. Fetch user history in `user` mode.
   - Expected: current-user timeline entries display.

## H. API Spot Checks

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

## I. Observability Smoke (OBS-01)

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

## J. Known-Gap Triage (From Product Notes)

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

## K. Post-Run Documentation Check

If behavior, commands, or known gaps changed, update:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/OBSERVABILITY_BASELINE.md` (when telemetry/dashboard/alert contract changes)

## L. Final Automated Smoke Before Merge

1. Backend:
   - `dotnet test backend/Taskdeck.sln -c Release -m:1`
2. Frontend unit/build:
   - `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
   - `cd frontend/taskdeck-web && npm run typecheck && npm run build`
3. Frontend E2E:
   - `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line`
