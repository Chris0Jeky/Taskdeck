# Manual Test Checklist

Use this checklist to manually validate Taskdeck behavior and compare expected outcomes.

## Preconditions

1. Start backend API:
   - `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj`
2. Start frontend:
   - `cd frontend/taskdeck-web`
   - `npm run dev`
3. Open `http://localhost:5173`.
4. Start from a clean board set when possible.

## Boards

1. Create board from board list.
   - Action: click `+ New Board`, enter name, click `Create`.
   - Expected: route changes to `/boards/{id}`, board title appears, success toast appears.
2. Create board with empty name.
   - Action: open create form, leave name empty, try submit.
   - Expected: no board created, form submit blocked by UI.
3. Open board settings and rename board.
   - Action: click `Board Settings`, change name, save.
   - Expected: header updates immediately, success toast appears.
4. Archive board.
   - Action: board settings, enable `Archive this board`, save.
   - Expected: board disappears from default `/boards` list.
5. Unarchive board.
   - Action: reopen board URL, board settings, uncheck archive, save.
   - Expected: board appears again in default `/boards` list.
6. Delete board.
   - Action: board settings, click `Delete Board`, confirm.
   - Expected: redirected to `/boards`, deleted board no longer shown.

## Columns

1. Create column.
   - Action: click `+ Add Column`, enter name, create.
   - Expected: new column appears with correct title and card count badge.
2. Create column with empty name.
   - Action: open add column form with empty input, submit.
   - Expected: no column created.
3. Edit column name.
   - Action: column settings button, change name, save.
   - Expected: column title updates, success toast appears.
4. Set WIP limit.
   - Action: column edit modal, enable WIP limit, set value, save.
   - Expected: badge shows `count/limit`, save succeeds.
5. Validate invalid WIP.
   - Action: enable WIP and set `0`.
   - Expected: save button disabled.
6. Delete empty column.
   - Action: open edit modal for empty column, delete, confirm.
   - Expected: column removed.
7. Delete non-empty column.
   - Action: try deleting column with cards.
   - Expected: alert blocks deletion, column remains unchanged.
8. Reorder columns via drag/drop.
   - Action: drag one column to a different position.
   - Expected: order updates immediately and persists after refresh.

## Cards

1. Add card.
   - Action: click `Add Card`, enter title, click `Add`.
   - Expected: card appears in column, success toast appears.
2. Add card with empty title.
   - Action: open add card form and submit empty title.
   - Expected: no card created.
3. Open card modal.
   - Action: click a card.
   - Expected: `Edit Card` modal opens with populated values.
4. Update title/description.
   - Action: change fields, save.
   - Expected: card updates in lane and success toast appears.
5. Set and clear due date.
   - Action: set due date and save, reopen and clear due date.
   - Expected: date appears after set and disappears after clear.
6. Mark card blocked.
   - Action: check blocked checkbox, provide reason, save.
   - Expected: blocked badge visible on card.
7. Validate blocked reason requirement.
   - Action: check blocked with empty reason.
   - Expected: save button disabled.
8. Move card between columns.
   - Action: drag card to another column.
   - Expected: card appears in target column and leaves source column.
9. Reorder cards within a column.
   - Action: drag card onto another card in same column.
   - Expected: ordering changes and persists after refresh.
10. Delete card.
    - Action: open card modal, click `Delete Card`, confirm.
    - Expected: card removed and success toast appears.

## Labels

1. Open label manager.
   - Action: click `Labels`.
   - Expected: `Manage Labels` modal opens.
2. Create label.
   - Action: click `Create New Label`, enter name/color, create.
   - Expected: label appears in list and success toast appears.
3. Edit label.
   - Action: click label edit icon, change name/color, update.
   - Expected: label updates in list and on cards using it.
4. Delete label.
   - Action: click delete icon, confirm.
   - Expected: label removed from manager and cards.
5. Invalid color.
   - Action: type invalid hex in label form.
   - Expected: create/update button disabled.

## Filters

1. Toggle filter panel with button.
   - Action: click filter icon in header.
   - Expected: `Filter Cards` panel opens and closes.
2. Toggle filter panel with keyboard.
   - Action: press `f`.
   - Expected: panel toggles open/close.
3. Search filter.
   - Action: enter unique text.
   - Expected: only matching cards remain visible.
4. Due-date filters.
   - Action: select each due-date filter option.
   - Expected: visible cards match selected due-date rule.
5. Blocked-only filter.
   - Action: enable blocked-only.
   - Expected: only blocked cards visible.
6. Label filter.
   - Action: select one or more labels.
   - Expected: only cards with selected labels are visible.
7. Clear all filters.
   - Action: click `Clear all`.
   - Expected: all cards become visible again.
8. Filter persistence while panel toggles.
   - Action: apply search filter, close panel with `f`, reopen.
   - Expected: filter input value and filtered results remain in session.

## Keyboard Shortcuts

1. Navigate cards.
   - Action: press `j` or `ArrowDown`, then `k` or `ArrowUp`.
   - Expected: selected card highlight moves accordingly.
2. Navigate columns.
   - Action: press `h`/`ArrowLeft` and `l`/`ArrowRight`.
   - Expected: selection context changes by column.
3. Open selected card.
   - Action: press `Enter`.
   - Expected: selected card opens in modal.
4. Create card in selected column.
   - Action: press `n`.
   - Expected: add-card form opens and input focuses.
5. Toggle help.
   - Action: press `?`.
   - Expected: keyboard help modal opens/closes.
6. Escape closes overlays.
   - Action: press `Escape` in card modal, board settings, column modal, label manager, keyboard help, and inline add-card form.
   - Expected: active overlay/form closes each time without side effects.

## WIP and Error Flows

1. WIP rejection on create.
   - Action: set column WIP to `1`, add two cards.
   - Expected: second card rejected, first remains, error toast/message shown.
2. WIP rejection on move.
   - Action: target column at WIP limit, drag another card into it.
   - Expected: move rejected, card stays in source column, error shown.

## CLI Manual Checks

1. List boards as JSON.
   - Action: `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards list --json`
   - Expected: valid JSON array (camelCase keys).
2. Create board as JSON.
   - Action: `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards create DemoBoard --json`
   - Expected: valid JSON object including `id`, `name`, `isArchived`.
3. Update board as JSON.
   - Action: `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj boards update --board <id> --name Updated --json`
   - Expected: valid JSON object with updated values.
4. Create column as JSON.
   - Action: `dotnet run --project backend/src/Taskdeck.Cli/Taskdeck.Cli.csproj columns create --board <id> --name Todo --json`
   - Expected: valid JSON object with `id`, `boardId`, `name`, `position`.
5. Add/list cards as JSON.
   - Action: add card with `cards add ... --json`, then list via `cards list --board <id> --json`.
   - Expected: JSON object for add and JSON array for list, with matching `id`.
6. Usage error exit path.
   - Action: run invalid usage, e.g. `boards update --name MissingBoard --json`.
   - Expected: usage message printed to stderr, non-zero exit code (`2`).

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
