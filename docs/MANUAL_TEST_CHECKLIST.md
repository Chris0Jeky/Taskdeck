# Manual Test Checklist

Use this checklist to manually validate current Taskdeck behavior on `main`.

## Scope And Boundaries

- In scope:
  - Boards, columns, cards, labels, filters, keyboard workflows, drag and drop, toasts.
  - CLI commands for boards, columns, and cards.
- Out of scope on current `main`:
  - Auth/login/register endpoints and permission-enforced runtime flows.
  - Export/import API endpoints.
  - LLM queue and audit/history runtime API endpoints.

Expected boundary behavior right now:
- `GET /api/auth/login` and similar side-track endpoints should return `404` because those controllers are not active on `main`.

## Preconditions

1. Start backend API:
   - `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj`
2. Start frontend:
   - `cd frontend/taskdeck-web`
   - `npm run dev`
3. Open `http://localhost:5173`.
4. Optional clean start:
   - stop API process
   - delete `backend/src/Taskdeck.Api/taskdeck.db` if present
   - start API again

## Boards (UI)

1. Create board from list page.
   - Action: click `+ New Board`, enter name, click `Create`.
   - Expected: routed to `/boards/{id}`, board title visible, success toast.
2. Create board from empty-state CTA.
   - Action: on empty list, click `+ Create Board`.
   - Expected: create form opens; submit creates board and navigates to board.
3. Cancel board creation.
   - Action: open create form, click `Cancel`.
   - Expected: form closes, no board created.
4. Reject empty board name.
   - Action: try submit empty/whitespace board name.
   - Expected: no board created.
5. Rename board.
   - Action: open `Board Settings`, change name, click `Save Changes`.
   - Expected: header updates and success toast appears.
6. Update board description only.
   - Action: change description only, save.
   - Expected: description updates and success toast appears.
7. Archive board.
   - Action: in board settings, check `Archive this board`, save.
   - Expected: board is hidden from `/boards` default list.
8. Delete board from settings.
   - Action: click `Delete Board`, confirm.
   - Expected: redirected to `/boards`, board disappears from default list.
   - Note: backend implementation is soft-delete via archive behavior.
9. Open board detail from list.
   - Action: click board card on `/boards`.
   - Expected: routed to `/boards/{id}` with columns/cards loaded.
10. Use back button in board view.
    - Action: click left arrow in board header.
    - Expected: routed back to `/boards`.

## Columns (UI)

1. Create column.
   - Action: click `+ Add Column`, enter name, create.
   - Expected: column appears with count badge.
2. Cancel column creation.
   - Action: open add-column form, click `Cancel`.
   - Expected: form closes, no column created.
3. Reject empty column name.
   - Action: submit column form with empty value.
   - Expected: no column created.
4. Edit column name.
   - Action: column settings icon, change name, save.
   - Expected: name updates, success toast.
5. Set WIP limit.
   - Action: enable WIP in column edit, set value, save.
   - Expected: badge shows `count/limit`.
6. Reject invalid WIP limit.
   - Action: set WIP to `0`.
   - Expected: save disabled and/or request rejected with validation error.
7. Delete empty column.
   - Action: delete from column edit modal.
   - Expected: column removed, success toast.
8. Delete non-empty column.
   - Action: try deleting a column with cards.
   - Expected: deletion blocked, column remains, conflict-style error toast/message.
9. Reorder columns via drag and drop.
   - Action: drag one column to a new position.
   - Expected: order updates and persists after refresh.

## Cards (UI)

1. Add card inline.
   - Action: `Add Card` in a column, enter title, click `Add`.
   - Expected: card appears and success toast.
2. Cancel inline card creation.
   - Action: open inline form, click `Cancel`.
   - Expected: form closes, card not created.
3. Reject empty card title.
   - Action: submit inline form with empty title.
   - Expected: no card created.
4. Open card modal.
   - Action: click card.
   - Expected: `Edit Card` modal with current values.
5. Update card title/description.
   - Action: change values, save.
   - Expected: lane card updates and success toast.
6. Set and clear due date.
   - Action: set due date and save, reopen and clear, save.
   - Expected: due date appears then is removed.
7. Mark blocked with reason.
   - Action: check blocked and provide reason, save.
   - Expected: blocked badge visible on card.
8. Validate blocked reason requirement.
   - Action: check blocked but leave reason empty.
   - Expected: save disabled.
9. Assign labels on card.
   - Action: in card modal, toggle one or more labels, save.
   - Expected: labels appear on card and persist after modal reopen.
10. Remove labels from card.
    - Action: uncheck labels, save.
    - Expected: labels removed from card UI.
11. Move card across columns.
    - Action: drag card to another column.
    - Expected: card moves, source/target counts update, success toast.
12. Reorder cards in same column.
    - Action: drag card over another card in same lane.
    - Expected: positions update and persist after refresh.
13. Delete card.
    - Action: open card modal, click `Delete Card`, confirm.
    - Expected: card removed, success toast.

## Labels (UI)

1. Open label manager.
   - Action: click `Labels`.
   - Expected: `Manage Labels` modal opens.
2. Create label.
   - Action: add name and valid hex color, click create.
   - Expected: label appears in list and success toast.
3. Edit label.
   - Action: edit name/color and save.
   - Expected: updates in manager and cards using that label.
4. Delete label.
   - Action: delete and confirm.
   - Expected: removed from label list and card chips.
5. Reject invalid color.
   - Action: enter non-hex value.
   - Expected: create/update disabled.

## Filters (UI)

1. Toggle panel from button.
   - Action: click filter icon.
   - Expected: panel opens/closes.
2. Toggle panel from keyboard.
   - Action: press `f`.
   - Expected: panel opens/closes.
3. Text search filter.
   - Action: enter unique text.
   - Expected: only matching cards remain.
4. Due-date filter variants.
   - Action: test `overdue`, `due-today`, `due-week`, `no-date`.
   - Expected: results match selected due-date rule.
5. Blocked-only filter.
   - Action: enable blocked-only.
   - Expected: only blocked cards shown.
6. Label filter.
   - Action: select one or multiple labels.
   - Expected: cards with at least one selected label remain.
7. Remove one filter chip.
   - Action: click `x` on one active filter chip.
   - Expected: only that filter is removed.
8. Clear all filters.
   - Action: click `Clear all`.
   - Expected: filters reset and all cards visible.
9. In-session persistence.
   - Action: apply filters, close panel, reopen panel.
   - Expected: filter state remains while session is active.

## Keyboard Shortcuts (UI)

1. Card navigation:
   - Action: `j`/`ArrowDown`, `k`/`ArrowUp`.
   - Expected: selected card changes in current column.
2. Column navigation:
   - Action: `h`/`ArrowLeft`, `l`/`ArrowRight`.
   - Expected: selected column context changes.
3. Open selected card:
   - Action: `Enter`.
   - Expected: selected card modal opens.
4. Open add-card form in selected column:
   - Action: `n`.
   - Expected: inline add-card form opens and input receives focus.
5. Toggle keyboard help:
   - Action: `?`.
   - Expected: help modal opens/closes.
6. Escape close behavior:
   - Action: press `Escape` in card modal, board settings, column edit, label manager, filter panel, keyboard help, inline add-card form.
   - Expected: current open overlay/form closes.
7. Input focus shortcut guard:
   - Action: focus text input/textarea, press navigation shortcuts.
   - Expected: shortcuts do not fire while typing (except `Escape`).

## WIP/Error Flows (UI/API)

1. WIP rejection on create:
   - Action: set column WIP to `1`, add two cards in same column.
   - Expected: second add rejected; first card remains.
2. WIP rejection on move:
   - Action: target column already at WIP limit, drag another card into it.
   - Expected: move rejected; card stays in source column.
3. Not found route binding safety:
   - Action: call API with mismatched board/card or board/column IDs.
   - Expected: `404` with `errorCode: NotFound`.

## API Manual Checks (Current Active Surface)

Assume API at `http://localhost:5000`.

1. List boards:
   - `Invoke-RestMethod http://localhost:5000/api/boards`
   - Expected: array response, `200`.
2. Create board:
   - `Invoke-RestMethod http://localhost:5000/api/boards -Method Post -ContentType application/json -Body '{"name":"Manual API Board"}'`
   - Expected: created board object, `201`.
3. Board not found:
   - `Invoke-WebRequest http://localhost:5000/api/boards/00000000-0000-0000-0000-000000000001`
   - Expected: `404` and JSON with `errorCode`.
4. Reorder columns validation:
   - send incomplete column ID set to `/api/boards/{boardId}/columns/reorder`.
   - Expected: `400 ValidationError`.
5. Side-track endpoint absence on current main:
   - `Invoke-WebRequest http://localhost:5000/api/auth/login`
   - Expected: `404`.

## CLI Manual Checks

1. List boards (json):
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards list --json`
   - Expected: JSON array, camelCase keys.
2. Create board (json):
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards create DemoBoard --json`
   - Expected: JSON object with `id`, `name`, `isArchived`.
3. Update board:
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards update --board <id> --name Updated --json`
   - Expected: updated board JSON.
4. Archive and list archived:
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards update --board <id> --archive --json`
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards list --include-archived --json`
   - Expected: archived board returned with `isArchived: true`.
5. Create column:
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj columns create --board <id> --name Todo --json`
   - Expected: column JSON with `id`, `boardId`, `name`, `position`.
6. Add, move, list cards:
   - `cards add ... --json`
   - `cards move ... --json`
   - `cards list --board <id> --json`
   - Expected: consistent card IDs/positions after move.
7. Usage error path:
   - `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards update --name MissingBoard --json`
   - Expected: usage message on stderr, exit code `2`.

## Post-Run Docs Check

After manual validation, update:
- `docs/STATUS.md` (date + validated outcomes if changed)
- `docs/TESTING_GUIDE.md` (totals/commands if changed)
- this file if behavior or endpoint surface changed.

## Final Smoke Before Merge

1. Backend unit:
   - `dotnet test backend/tests/Taskdeck.Domain.Tests/Taskdeck.Domain.Tests.csproj`
   - `dotnet test backend/tests/Taskdeck.Application.Tests/Taskdeck.Application.Tests.csproj`
2. Backend integration/contracts:
   - `dotnet test backend/tests/Taskdeck.Api.Tests/Taskdeck.Api.Tests.csproj`
   - `dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj`
3. Frontend unit:
   - `cd frontend/taskdeck-web && npx vitest run`
4. E2E smoke:
   - `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test`
