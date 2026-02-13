# Manual Test Checklist

Use this checklist to manually validate current Taskdeck behavior on `main`.

## Scope and Boundaries

In scope:
- Core board workflow: boards, columns, cards, labels, filters, drag and drop, keyboard flows.
- Workspace shell: navigation, command palette open/close, shortcuts help.
- Advanced surfaces: automations (queue/proposals/chat), ops (CLI templates/logs), archive, activity.
- API contract spot checks for auth, access, queue, archive, automation, chat, and ops endpoints.

Out of scope (known implementation boundaries on current `main`):
- Full claim-based auth enforcement on all legacy controllers is still in progress.
- Some legacy endpoints still accept user identity through query/body parameters.
- `ExportDatabaseAsync` and `ImportDatabaseAsync` are not implemented.
- Command palette navigation currently uses pointer selection (no full keyboard item selection yet).
- Activity filtering still depends on direct IDs, not chooser-driven entity discovery.

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
4. Open command palette via `Ctrl+K`/`Cmd+K` and close with `Escape`.
   - Expected: overlay toggles correctly.
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
4. Delete board via Board Settings.
   - Expected: redirected to `/workspace/boards`, deleted board absent.

5. Create two columns, then reorder columns by drag and drop.
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
11. Move card to another column via drag and drop.
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
   - Expected: modal/panel/form closes in this order of focus.

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

## G. Activity View

1. Open `/workspace/activity`.
   - Expected: view loads and allows mode selection (`board`, `entity`, `user`).
2. Fetch board history with valid board ID.
   - Expected: timeline entries display.
3. Fetch entity and user history with valid IDs.
   - Expected: corresponding entries display.

## H. API Spot Checks

Assume API at `http://localhost:5000`.

1. Register/login:
   - `POST /api/auth/register`
   - `POST /api/auth/login`
   - Expected: token and user payload.
2. Chat unauthorized check:
   - `GET /api/llm/chat/sessions` without bearer token.
   - Expected: `401`.
3. Ops unauthorized check:
   - `GET /api/ops/cli/templates` without bearer token.
   - Expected: `401`.
4. Archive unauthorized check:
   - `GET /api/archive/items` without bearer token.
   - Expected: `401`.
5. Queue status validation:
   - `GET /api/llm-queue/status/not-a-real-status`.
   - Expected: `400 ValidationError`.
6. Automation execute without `Idempotency-Key`:
   - `POST /api/automation/proposals/{id}/execute` without header.
   - Expected: `400 ValidationError`.

## I. Known-Gap Triage (From Product Notes)

Run these checks even if they currently fail; log outcome explicitly.

1. Drag side-effect while editing card/task:
   - Repro: open card modal/edit fields, perform pointer drag gestures.
   - Target behavior: editing interactions should not trigger unintended board drag/move behavior.
2. Archive consistency for board archive/unarchive:
   - Repro: archive board and inspect archive workflows.
   - Target behavior: board archival should be coherently represented in archive/recovery UX and data pipeline.
3. Command palette keyboard selection:
   - Repro: open palette, try arrow/enter item selection.
   - Target behavior: full keyboard-first navigation and activation.
4. Activity discoverability without raw IDs:
   - Repro: attempt history exploration without pre-known IDs.
   - Target behavior: picker/autocomplete-assisted discovery for board/entity/user selectors.
5. Ops and automation form ergonomics:
   - Repro: create requests using ops/automation UIs.
   - Target behavior: contextual autocomplete/options, reduced manual input burden.
6. Escape-to-exit board context:
   - Repro: on board screen with no modal open, press `Escape`.
   - Target behavior: configurable back/exit behavior for rapid keyboard-driven workflow.

## J. Post-Run Documentation Check

If behavior, commands, or known gaps changed, update:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`

## K. Final Automated Smoke Before Merge

1. Backend:
   - `dotnet test backend/Taskdeck.sln -c Release`
2. Frontend unit/build:
   - `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
   - `cd frontend/taskdeck-web && npm run typecheck && npm run build`
3. Frontend E2E:
   - `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line`
