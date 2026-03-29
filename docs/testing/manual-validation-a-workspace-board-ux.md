# Manual Validation Slice A: Workspace Shell, Board Lifecycle, and Keyboard UX

Last Updated: 2026-03-29
Companion: `docs/MANUAL_TEST_CHECKLIST.md` (umbrella checklist)

## Purpose

Step-indexed manual validation for workspace shell behavior, board lifecycle operations, and keyboard-driven UX. This slice validates the structural foundation that all other Taskdeck surfaces depend on.

## Environment Setup

### Prerequisites

1. Clean backend database (remove `backend/src/Taskdeck.Api/taskdeck.db` if present).
2. Start backend:
   ```bash
   dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
   ```
3. Start frontend:
   ```bash
   cd frontend/taskdeck-web
   npm run dev
   ```
4. Open `http://localhost:5173` (or fallback port printed by Vite).
5. Use a modern Chromium-based browser (Chrome/Edge latest) for primary validation. Firefox for cross-browser spot check.

### Run Metadata (Record Before and After Each Run)

| Field | Value |
|---|---|
| Date/time (UTC) | |
| Commit SHA | |
| Browser and version | |
| OS | |
| DB baseline | `fresh` / `existing` |
| Env flags changed | |
| Artifacts collected | |

---

## Scenario A-01: Registration Flow

**Goal:** Verify new user registration creates an authenticated session.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/register` | Registration form renders with username, email, and password fields |
| 2 | Fill valid username, email, and password | Fields accept input without validation errors |
| 3 | Submit registration form | User is authenticated and redirected to `/workspace/home` |
| 4 | Verify session token exists | Token is stored (check DevTools > Application > Local Storage for `td_token`) |

**Evidence:** Screenshot of `/workspace/home` after registration. Note any redirect delays.

---

## Scenario A-02: Login Flow

**Goal:** Verify login with valid credentials.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Log out (or open incognito) | Redirected to `/login` |
| 2 | Navigate to `/login` | Login form renders with username and password fields |
| 3 | Enter valid credentials from A-01 | Fields accept input |
| 4 | Submit login form | Redirected to `/workspace/home` with authenticated session |

**Evidence:** Screenshot of workspace home after login.

---

## Scenario A-03: Auth Guard (Unauthenticated Workspace Access)

**Goal:** Verify unauthenticated users cannot reach workspace routes.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Clear all tokens (DevTools > Application > Local Storage, remove `td_token`) | Token removed |
| 2 | Navigate to `/workspace/boards` directly | Redirected to `/login?redirect=/workspace/boards` |
| 3 | Navigate to `/workspace/home` directly | Redirected to `/login?redirect=/workspace/home` |

**Evidence:** Screenshot showing redirect URL with `redirect` query parameter.

---

## Scenario A-04: Workspace Shell Navigation

**Goal:** Verify sidebar navigation and shell layout.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Log in and land on `/workspace/home` | Shell renders with sidebar, topbar, and main content area |
| 2 | Click `Home` in sidebar | Route is `/workspace/home`, content updates |
| 3 | Click `Today` in sidebar | Route is `/workspace/today`, agenda cards render |
| 4 | Click `Boards` in sidebar | Route is `/workspace/boards`, boards list renders |
| 5 | Click `Inbox` in sidebar | Route is `/workspace/inbox`, inbox list renders |
| 6 | Click `Review` in sidebar | Route is `/workspace/review`, review surface renders |
| 7 | Click `Archive` in sidebar | Route is `/workspace/archive`, archive list renders |
| 8 | Click `Notifications` in sidebar | Route is `/workspace/notifications`, notification inbox renders |

**Evidence:** Screenshot of each route transition showing correct URL and content.

---

## Scenario A-05: Command Palette

**Goal:** Verify command palette keyboard model and item activation.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Press `Ctrl+K` (or `Cmd+K` on macOS) | Command palette overlay opens with search input focused |
| 2 | Type a partial route name (e.g., "boa") | Item list filters to matching navigation items |
| 3 | Use `ArrowDown` / `ArrowUp` to select an item | Visual selection indicator moves between items |
| 4 | Press `Enter` on selected item | Palette closes and browser navigates to selected route |
| 5 | Press `Ctrl+K` again to reopen | Palette opens again |
| 6 | Press `Escape` | Palette closes without navigation |
| 7 | Press `Ctrl+K`, type "capture" | "New Capture" action item appears |
| 8 | Select and activate "New Capture" | Capture modal opens, palette closes |

**Evidence:** Screenshot of palette open with filtered results. Note any lag in filtering.

---

## Scenario A-06: Keyboard Shortcuts Help Dialog

**Goal:** Verify `?` key toggles the shortcuts help overlay.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Ensure no input field is focused | Cursor is not in a text entry element |
| 2 | Press `?` | Keyboard shortcuts help dialog opens |
| 3 | Verify dialog content | Three sections visible: Global, Board Navigation, Editor |
| 4 | Verify listed shortcuts match implementation | `Ctrl+K` (command palette), `Ctrl+Shift+C` (quick capture), `?` (help), `Escape` (close top surface), `h/l/j/k` (board nav), `Enter` (open card), `n` (new card), `Shift+N` (new column), `f` (filter panel), `Ctrl+S` / `Ctrl+Enter` / `Alt+1` / `Alt+2` / `Alt+4` (editor) |
| 5 | Press `Escape` | Dialog closes |
| 6 | Press `?` again | Dialog reopens (toggle behavior) |

**Evidence:** Screenshot of shortcuts help dialog content.

---

## Scenario A-07: Quick Capture Modal (Global Shortcut)

**Goal:** Verify `Ctrl+Shift+C` opens the quick capture modal from anywhere.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/home` | Home view renders |
| 2 | Press `Ctrl+Shift+C` | Capture modal opens (teleported to body) |
| 3 | Close capture modal | Modal closes |
| 4 | Navigate to `/workspace/boards` | Boards list renders |
| 5 | Press `Ctrl+Shift+C` | Capture modal opens from boards list context |

**Evidence:** Screenshot of capture modal open from a non-board route.

---

## Scenario A-08: Logout

**Goal:** Verify logout clears session and redirects.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Click logout control in sidebar/topbar | Session cleared, redirected to `/login` |
| 2 | Verify token removed | DevTools > Local Storage: `td_token` absent |
| 3 | Attempt to navigate to `/workspace/home` | Redirected to `/login` |

**Evidence:** Screenshot showing `/login` after logout.

---

## Scenario A-09: Board Creation

**Goal:** Verify creating a board from the boards list.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/boards` | Boards list renders |
| 2 | Use create board action | Board creation form/dialog appears |
| 3 | Enter board name (e.g., "Test Board A09") | Name accepted |
| 4 | Submit creation | Route changes to `/workspace/boards/{id}` |
| 5 | Verify board heading | Heading matches entered name "Test Board A09" |
| 6 | Navigate back to `/workspace/boards` | New board appears in list |

**Evidence:** Screenshot of board view with correct heading and URL.

---

## Scenario A-10: Board Rename

**Goal:** Verify renaming a board via Board Settings.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open Board Settings from toolbar | Board Settings modal opens with current name pre-filled |
| 2 | Change name to "Renamed Board A10" | Name field accepts new value |
| 3 | Save changes | Modal closes, board heading updates to "Renamed Board A10" |
| 4 | Refresh page | Name persists as "Renamed Board A10" |

**Evidence:** Screenshot before and after rename.

---

## Scenario A-11: Board Archive and Unarchive

**Goal:** Verify archive/unarchive lifecycle via Board Settings.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open Board Settings on an active board | "Move to Archive" action visible with explanation text |
| 2 | Click "Move to Archive" | Confirmation dialog appears with reversibility guidance |
| 3 | Confirm archive | Board archived; redirected to `/workspace/boards` |
| 4 | Verify board absent from default boards list | Archived board not visible in `/workspace/boards` |
| 5 | Navigate to `/workspace/archive` | Archived board appears in archive list |
| 6 | Restore the board (from archive or board settings) | Board returns to active state |
| 7 | Navigate to `/workspace/boards` | Restored board reappears in default list |

**Evidence:** Screenshots of boards list before/after archive and archive view showing the board.

---

## Scenario A-12: Board Action Rail

**Goal:** Verify board action rail commands route correctly.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open a board with at least one column and card | Board renders with action rail visible |
| 2 | Click "Capture here" | Capture modal opens scoped to current board |
| 3 | Close capture modal | Returns to board view |
| 4 | Click "Ask assistant" | Navigates to `/workspace/automations/chat?boardId={boardId}` |
| 5 | Navigate back to board | Board view restores |
| 6 | Click "Review proposals" | Navigates to `/workspace/review?boardId={boardId}` |
| 7 | Navigate back to board | Board view restores |
| 8 | Click "Add card" on board with columns | Inline add-card form opens in active column |
| 9 | Click "Add card" on empty board (no columns) | Column creation form opens instead |

**Evidence:** Screenshots of each action rail target route with boardId query preserved.

---

## Scenario A-13: Column Operations

**Goal:** Verify column creation, reordering (drag handle), and WIP limits.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open column creation form (toolbar or `Shift+N`) | Form renders with name input |
| 2 | Create column "Todo" | Column appears on board |
| 3 | Create column "In Progress" | Second column appears |
| 4 | Create column "Done" | Third column appears |
| 5 | Drag "Done" column using the `Drag Column` handle to before "In Progress" | Columns reorder visually |
| 6 | Refresh page | New column order persists |
| 7 | Set WIP limit on "In Progress" column | Count/limit indicator appears (e.g., "0/3") |
| 8 | Attempt to exceed WIP by adding/moving cards beyond limit | Operation blocked with visible error feedback |

**Evidence:** Screenshot of column order before/after drag. Screenshot of WIP indicator and WIP-exceeded error.

---

## Scenario A-14: Card CRUD Operations

**Goal:** Verify card creation, editing, moving, and deletion.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Click add-card toggle in "Todo" column or press `n` | Inline card creation input appears |
| 2 | Type card title and submit | Card appears in "Todo" column |
| 3 | Click card or select with keyboard and press `Enter` | Card modal opens with current values |
| 4 | Edit title and description | Changes reflected in modal fields |
| 5 | Set a due date | Due date saved |
| 6 | Set blocked status with reason | Blocked indicator visible |
| 7 | Assign labels | Label chips appear on card |
| 8 | Save and close modal | Updates persist in column lane view |
| 9 | Drag card to "In Progress" using `Drag Card` handle | Card relocates; column counts update |
| 10 | Refresh page | Card remains in "In Progress" |
| 11 | Open card modal and delete card | Card removed from board |

**Evidence:** Screenshots of card in modal with edits, card after column move, and empty column after delete.

---

## Scenario A-15: Label Manager

**Goal:** Verify label create, update, and delete operations.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open Label Manager from board toolbar | Label manager modal opens |
| 2 | Create label "Bug" with red color | Label appears in manager list |
| 3 | Create label "Feature" with blue color | Second label appears |
| 4 | Rename "Bug" to "Defect" | Label name updates |
| 5 | Delete "Feature" label | Label removed from list |
| 6 | Close label manager | Modal closes |
| 7 | Open a card modal and verify label chips | "Defect" available, "Feature" absent |

**Evidence:** Screenshot of label manager with created labels. Screenshot of card modal label selector.

---

## Scenario A-16: Filter Panel

**Goal:** Verify filter panel toggle, filter application, and session persistence.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Press `f` on a board with multiple cards | Filter panel opens |
| 2 | Apply text filter matching one card | Only matching cards visible |
| 3 | Clear text filter | All cards visible again |
| 4 | Apply label filter | Cards with selected label visible |
| 5 | Apply due-date filter | Cards with matching due dates visible |
| 6 | Apply blocked filter | Only blocked cards visible |
| 7 | Close filter panel (press `f` again) | Panel closes |
| 8 | Reopen filter panel | Previously applied filters still active (session persistence) |
| 9 | Verify toolbar filter indicator | Filtered card count vs total card count displayed |

**Evidence:** Screenshot of filter panel with active filters and filtered card set.

---

## Scenario A-17: Board Keyboard Navigation

**Goal:** Verify vim-style and arrow key navigation on the board.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Ensure board has 2+ columns with 2+ cards each | Board populated |
| 2 | Press `j` or `ArrowDown` | First card in first column selected (visual highlight) |
| 3 | Press `j` again | Next card selected |
| 4 | Press `k` or `ArrowUp` | Previous card selected |
| 5 | Press `l` or `ArrowRight` | Selection moves to first card in next column |
| 6 | Press `h` or `ArrowLeft` | Selection moves back to first card in previous column |
| 7 | Press `Enter` on selected card | Card modal opens |
| 8 | Close card modal | Selection returns to board |
| 9 | Press `n` | Inline new-card input opens in currently selected column |
| 10 | Press `Escape` to close new-card input | Input closes, board canvas remains |

**Evidence:** Screenshots showing selection indicator moving across columns and cards.

---

## Scenario A-18: Escape Behavior Stack

**Goal:** Verify Escape closes only the topmost transient surface per press.

The escape stack contract:
- Each `Escape` keypress closes only the topmost registered surface.
- Surfaces register on the capture-phase escape stack (`useEscapeStack`).
- When no transient surface is open on a board route, `Escape` navigates to `/workspace/boards`.
- Escape inside an input field still functions (the `useKeyboardShortcuts` composable allows Escape even when typing).

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open board and press `?` to open keyboard help | Help dialog visible |
| 2 | Press `Escape` | Help dialog closes; board still visible; no navigation |
| 3 | Press `f` to open filter panel | Filter panel visible |
| 4 | Press `Escape` | Filter panel closes; board still visible |
| 5 | Open Board Settings modal | Modal visible |
| 6 | Press `Escape` | Board Settings modal closes; board still visible |
| 7 | Open Label Manager modal | Modal visible |
| 8 | Press `Escape` | Label Manager closes; board still visible |
| 9 | Open Starter Pack Catalog modal | Modal visible |
| 10 | Press `Escape` | Starter Pack Catalog closes; board still visible |
| 11 | Open inline add-card input | Input active |
| 12 | Press `Escape` | Add-card input cancelled; board still visible |
| 13 | Open column creation form | Form visible |
| 14 | Press `Escape` | Column form closes; board still visible |
| 15 | Ensure no transient surfaces are open | Clean board canvas |
| 16 | Press `Escape` | Navigated to `/workspace/boards` |

Escape stack priority order (topmost first):
1. Keyboard help dialog
2. Label manager modal
3. Starter pack catalog modal
4. Board settings modal
5. Filter panel
6. Column creation form
7. Inline add-card input (via DOM cancel button)
8. Fallback: navigate to `/workspace/boards`

**Evidence:** Record each escape press and confirm only one surface closes per press.

---

## Scenario A-19: Escape in Global Shell Surfaces

**Goal:** Verify Escape behavior for shell-level overlays (command palette, keyboard help).

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Press `Ctrl+K` to open command palette | Palette visible |
| 2 | Press `Escape` | Palette closes; no route change |
| 3 | Press `?` to open keyboard help from shell | Help dialog visible |
| 4 | Press `Escape` | Help dialog closes; no route change |
| 5 | Press `Ctrl+Shift+C` to open capture modal | Capture modal visible |
| 6 | Press `Escape` (or close button) | Capture modal closes; no route change |

**Evidence:** Note whether shell-level escape interferes with board-level escape when on a board route.

---

## Scenario A-20: Drag Handle Safety

**Goal:** Verify drag only initiates from explicit drag handles, not from editable surfaces.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Open a board with columns and cards | Board populated |
| 2 | Attempt to drag a column by clicking on the column header text | Drag does NOT initiate |
| 3 | Drag a column using the explicit `Drag Column` handle (`data-action="drag-column-handle"`) | Drag initiates and column can be repositioned |
| 4 | Click on a card title or metadata area and attempt to drag | Drag does NOT initiate from card body |
| 5 | Drag a card using the explicit `Drag Card` handle | Drag initiates and card can be repositioned |
| 6 | Open add-card input, attempt drag gestures near the input | No unintended drag; text input works normally |
| 7 | Open card modal with editable fields, attempt drag near fields | No unintended drag; editing works normally |

**Evidence:** Note any cases where drag initiates from non-handle surfaces.

---

## Scenario A-21: Today View Agenda

**Goal:** Verify Today view renders agenda summary cards.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/today` | Today view renders |
| 2 | Verify agenda sections | Review, triage, overdue, due-today, and blocked summary cards visible |
| 3 | Verify onboarding loop block | Onboarding block present for new users |
| 4 | Click "Start Useful Board" (from Home or Today) | Board creation flow starts; pick a starter shape |
| 5 | Complete board creation | Board opens; if starter pack applied, workflow present; if failed, board opens with warning |

**Evidence:** Screenshot of Today view with agenda cards.

---

## Scenario A-22: Onboarding Dismiss and Replay

**Goal:** Verify onboarding dismiss/replay state persistence.

| Step | Action | Expected Outcome |
|---|---|---|
| 1 | Navigate to `/workspace/home` or `/workspace/today` | Onboarding guidance visible |
| 2 | Dismiss onboarding | Onboarding UI hidden |
| 3 | Refresh page | Onboarding remains dismissed (state persisted) |
| 4 | Trigger replay of onboarding (if replay affordance exists) | Onboarding guidance reappears |
| 5 | Refresh page after replay | Replayed state persists |

**Evidence:** Screenshots before dismiss, after dismiss, and after replay.

---

## Evidence Capture Guidance

For each scenario:
1. Record **pass** or **fail** in the run metadata.
2. On **fail**: capture a screenshot, note the observed behavior vs expected, and file a linked issue with tag `manual-validation-a`.
3. On **pass with caveats**: note any UX friction, timing anomalies, or visual glitches even if the functional outcome is correct.
4. For keyboard scenarios: note whether shortcuts conflict with browser defaults (e.g., `Ctrl+K` in Firefox opens bookmark sidebar by default).

---

## Automation Candidates

The following scenarios are strong candidates for Playwright E2E automation:

| Scenario | Rationale |
|---|---|
| A-01, A-02, A-03 | Auth flows are deterministic and have no visual-judgment dependency |
| A-05 (steps 1-6) | Command palette keyboard model is fully automatable |
| A-09, A-10 | Board CRUD is API-backed and deterministic |
| A-11 | Archive/unarchive lifecycle is route-verifiable |
| A-17 | Keyboard navigation can be validated by DOM selection attributes |
| A-18 (subset) | Escape stack priority can be validated by surface visibility checks |

Scenarios requiring human judgment (visual coherence, drag feel, timing perception):
- A-13 (drag reorder feel), A-14 (drag card feel), A-20 (drag handle targeting)

---

## Regression Rerun Notes

When rerunning this slice after code changes:
- Always start with a fresh database unless specifically testing migration/existing-data paths.
- Re-run A-18 (escape stack) in full -- escape regressions are common after modal/dialog additions.
- Re-run A-20 (drag handle safety) after any changes to `useBoardDragDrop` or card/column components.
- If keyboard shortcuts change, update A-06 step 4 expected values to match `ShellKeyboardHelp.vue`.

---

## Defect Filing Template

When filing defects found during this slice:

```
Title: [A-XX] <short description>
Labels: manual-validation-a, bug
Body:
  - Scenario: A-XX step Y
  - Expected: <from checklist>
  - Observed: <actual behavior>
  - Evidence: <screenshot/recording link>
  - Environment: <from run metadata>
  - Commit: <SHA>
```
