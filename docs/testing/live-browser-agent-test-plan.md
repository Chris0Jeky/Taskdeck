# Live Browser Agent Test Plan

Last Updated: 2026-04-02

Use this plan to drive an LLM agent (Playwright MCP, browser-use, or similar) through a full interactive session against a running Taskdeck instance.

Companion docs:
- `docs/MANUAL_TEST_CHECKLIST.md` (umbrella manual checklist)
- `docs/TESTING_GUIDE.md` (test operations reference)
- `docs/product/DEMO_PLAYBOOK.md` (stakeholder demo script)

---

## Environment Setup

### Option A: Mock LLM (safe, deterministic, no API keys needed)

This is the recommended default. Chat will return canned responses but all surfaces are functional.

```bash
# Terminal 1 — Backend
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj

# Terminal 2 — Frontend
cd frontend/taskdeck-web
npm install
npm run dev
# Opens on http://localhost:5173

# Terminal 3 — Seed demo data (after backend is running)
cd frontend/taskdeck-web
npm run demo:seed
```

Demo seed creates:
- Two users: `demo` / `demo123` and `collab` / `demo123`
- Four boards (Client Onboarding, Content Calendar, Blank, Archived)
- Inbox captures, queue entries, a chat session, comments with @mentions, Ops logs

### Option B: Live LLM (real chat responses, requires API key)

Use this when you want the chat and tool-calling surfaces to produce real LLM responses.

```bash
# Terminal 1 — Backend with Gemini (recommended for cost)
export Llm__EnableLiveProviders=true
export Llm__AllowLiveProvidersInDevelopment=true
export Llm__Provider=Gemini
export Llm__Gemini__ApiKey=<your_gemini_key>
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj

# OR with OpenAI:
export Llm__Provider=OpenAI
export Llm__OpenAi__ApiKey=<your_openai_key>
```

Default models: `gemini-2.5-flash` (Gemini), `gpt-4o-mini` (OpenAI). Override with `Llm__Gemini__Model` or `Llm__OpenAi__Model`.

Verify provider health after startup:
```
GET http://localhost:5000/api/llm/chat/health          # config check
GET http://localhost:5000/api/llm/chat/health?probe=true  # real upstream call
```

### Option C: GitHub OAuth (optional, requires GitHub OAuth app)

To test OAuth login, set these before starting the backend:
```bash
export GitHubOAuth__ClientId=<your_client_id>
export GitHubOAuth__ClientSecret=<your_client_secret>
```

When not configured, the OAuth button simply won't appear on the login page.

### Default URLs

| Surface | URL |
|---|---|
| Frontend | `http://localhost:5173` |
| API | `http://localhost:5000` |
| Swagger | `http://localhost:5000/swagger` |

---

## Test Plan

### 1. Authentication and Session

- [ ] Navigate to `/login`, register a new account with username/password
- [ ] Log out, log back in with the same credentials
- [ ] Try logging in with wrong password -- verify error message appears
- [ ] Try registering with a duplicate username -- verify rejection
- [ ] Verify session persists across page reload (refresh, confirm still logged in)
- [ ] If GitHub OAuth is configured: verify "Sign in with GitHub" button appears on `/login`
- [ ] If GitHub OAuth is configured: click it, complete auth, verify redirect back and session created
- [ ] If GitHub OAuth is NOT configured: verify no GitHub button appears

### 2. Home / First-Run Experience

- [ ] After first login, land on HomeView -- verify workspace summary renders
- [ ] Verify workspace mode selector is visible (guided / workbench / agent)
- [ ] Switch between workspace modes, verify the mode persists after page reload
- [ ] Check that contextual help callouts appear on first visit
- [ ] Dismiss a help callout, reload, verify it stays dismissed
- [ ] Click "replay" on a dismissed callout, verify it reappears

### 3. Board CRUD

- [ ] Create a new board with a name and description
- [ ] Verify the board appears in the boards list (`/boards`)
- [ ] Open the board, verify it loads with default columns
- [ ] Rename the board from board settings
- [ ] Create a second board, switch between boards
- [ ] Archive a board, verify it disappears from the boards list
- [ ] Restore it from `/workspace/archive`, verify it reappears

### 4. Column Management

- [ ] Add a new column to a board
- [ ] Rename a column
- [ ] Reorder columns via drag-and-drop using the `Drag Column` handle
- [ ] Refresh page -- verify new order persists
- [ ] Set WIP limit on a column, verify count/limit indicator appears
- [ ] Attempt to exceed WIP by adding/moving cards -- verify operation is blocked
- [ ] Delete an empty column

### 5. Card CRUD and Interaction

- [ ] Create a card inline in a column with a title
- [ ] Open card modal (click or `Enter`), edit title, description, due date, blocked status
- [ ] Assign labels to a card
- [ ] Move a card between columns using drag-and-drop via `Drag Card` handle
- [ ] Move a card using keyboard shortcut (Alt+Arrow)
- [ ] Use the "move to" action menu on a card
- [ ] Delete a card -- verify confirmation dialog appears first
- [ ] Create 5+ cards, verify they all render correctly

### 6. Labels

- [ ] Open Label Manager from board toolbar
- [ ] Create a new label with a name and color
- [ ] Assign it to a card, verify visual indicator appears
- [ ] Rename the label, verify the change reflects on cards
- [ ] Delete a label, verify it's removed from cards

### 7. Capture / Inbox Flow (Core Loop)

- [ ] Open the capture modal (`Ctrl+Shift+C` or board action rail "Capture here")
- [ ] Type a simple task like "Fix the login bug" -- submit
- [ ] Type a multi-item capture: "Fix login bug - Add dark mode - Update docs" (dash-separated)
- [ ] Type semicolon-delimited: "Task A; Task B; Task C"
- [ ] Verify items appear in the Inbox (`/workspace/inbox`)
- [ ] Triage an inbox item -- approve/assign to board
- [ ] Batch-select multiple inbox items and triage together
- [ ] Try capturing empty input -- verify validation
- [ ] Verify board-scoped inbox shows board context banner

### 8. Review / Proposal Flow (Core Loop)

- [ ] Navigate to `/workspace/review`
- [ ] Verify proposal cards render with sticky action footer and constrained height
- [ ] Expand a proposal's collapsible detail section, verify risk color-coding
- [ ] Approve a proposal -- verify "Approved" status and approve-to-apply cue
- [ ] Execute the approved proposal (two-step: approve first, then execute separately)
- [ ] Verify the board mutation is applied after execution
- [ ] Reject a different proposal -- verify it's removed without board changes
- [ ] View diff for a proposal
- [ ] Verify applied proposals are hidden by default
- [ ] Use keyboard-accessible links dropdown on a proposal card

### 9. Automation Queue

- [ ] Navigate to the Automation Queue view
- [ ] Submit an instruction-first request (e.g., "Create a column called Done")
- [ ] Verify a proposal is generated
- [ ] Verify board-context guardrails are shown in the composer

### 10. Chat / LLM Interaction

- [ ] Navigate to `/workspace/automations/chat`
- [ ] Create a board-scoped chat session
- [ ] Send a non-actionable message -- verify response renders
- [ ] Send a tool-calling question: "What columns does my board have?"
  - Expected: intermediate "Looking up..." status, then response with actual column names
- [ ] Send: "What cards are in <column-name>?"
  - Expected: response with card titles from that column
- [ ] Send: "Show me details for card <card-title>"
  - Expected: card details (title, description, labels, due date)
- [ ] Send: "Search for cards about <keyword>"
  - Expected: matching cards listed
- [ ] Send: "What labels are on this board?"
  - Expected: label names and colors
- [ ] Send a proposal-generating instruction: "Move the first card to Done"
  - Expected: response links to a reviewable proposal
- [ ] Send a multi-instruction message: "Add a column called Testing and create a card called Unit Tests"
  - Expected: multiple proposals generated
- [ ] Check provider health indicator (live/mock/degraded badge)
- [ ] Send a nonsensical message -- verify graceful response
- [ ] Rapidly send 3 messages -- verify no race condition or truncation

### 11. Today View

- [ ] Navigate to `/workspace/today`
- [ ] Verify daily agenda renders (review, triage, overdue, due-today, blocked summary cards)
- [ ] If first visit: verify onboarding state appears
- [ ] Dismiss onboarding, reload -- verify it stays dismissed
- [ ] Use replay control to bring back onboarding
- [ ] Check board setup shortcuts work from Today view

### 12. Notifications

- [ ] Trigger a notification (e.g., approve a proposal, add a comment with @mention)
- [ ] Navigate to `/workspace/notifications`
- [ ] Verify notification appears with type-colored left border and type badge
- [ ] Verify same-type grouping and time-based section headers
- [ ] Mark a single notification as read
- [ ] Use "Mark all read" -- verify batch update
- [ ] Navigate to Notification Preferences, toggle a preference, save, reload -- verify persisted
- [ ] Verify board-scoped notifications show board context banner

### 13. Command Palette / Global Search

- [ ] Press `Ctrl+K` to open command palette
- [ ] Type a board name -- verify it appears in results
- [ ] Type a card title -- verify cross-board search results
- [ ] Use keyboard (arrow keys + Enter) to navigate and select a result
- [ ] Search with no results -- verify empty state message
- [ ] Verify "Load more" pagination if many results exist
- [ ] Press `Escape` to close without navigation

### 14. Board Metrics Dashboard (ANL-01)

- [ ] Navigate to `/workspace/metrics` from sidebar
- [ ] Select a board with cards -- verify metrics render
- [ ] Adjust date range filter (last 7 days, last 30 days)
- [ ] Filter by label -- verify metrics scope to that label
- [ ] Switch boards -- verify metrics reload

### 15. Starter Packs

- [ ] Open a board, find the starter-pack catalog button
- [ ] Browse available packs (label packs, column packs, blueprint packs)
- [ ] Search within the catalog
- [ ] Preview a pack (dry-run) -- verify conflict report if applicable
- [ ] Apply a starter pack to a board
- [ ] Re-apply the same pack -- verify idempotent behavior (no duplicates)

### 16. Export / Import

- [ ] Export a board to JSON
- [ ] Verify the downloaded file contains board data
- [ ] Import the JSON into a new board
- [ ] Verify the imported board matches the original

### 17. GDPR Data Portability (SEC-08)

- [ ] Call `GET /api/account/export` (via browser console or API tool) -- verify user-scoped JSON export
- [ ] Verify the export contains only the requesting user's data
- [ ] Attempt `POST /api/account/delete` with wrong password -- verify rejection
- [ ] Attempt with correct password but wrong confirmation phrase -- verify rejection
- [ ] (Caution) Attempt with correct password and `"DELETE MY ACCOUNT"` -- verify account deactivation
- [ ] Attempt to log in with the deleted account -- verify rejection

### 18. Archive and Recovery

- [ ] Archive a board from board settings
- [ ] Navigate to `/workspace/archive` -- verify the board appears
- [ ] Restore the board -- verify it reappears in boards list
- [ ] Archive a board with cards, restore, verify cards are intact
- [ ] Filter archive by entity type -- verify list narrows

### 19. Activity / Audit Trail

- [ ] Navigate to `/workspace/activity`
- [ ] Perform a board mutation (create/move/delete card)
- [ ] Verify the activity log records the mutation
- [ ] Use mode selector (board, entity, user)
- [ ] Fetch board history -- verify timeline includes board-level and child mutations

### 20. Ops Console

- [ ] Navigate to `/workspace/ops/cli`
- [ ] Verify templates load
- [ ] Run `health.check` template -- verify output
- [ ] Switch to logs tab, query logs -- verify entries returned
- [ ] Fetch logs by correlation ID for a recent run

### 21. Board Access / Collaboration

- [ ] View board access settings
- [ ] (If multi-user) Share a board with the `collab` user
- [ ] Verify access control -- non-shared user can't see the board

### 22. Profile and Settings

- [ ] Navigate to Profile Settings
- [ ] Update display name or preferences
- [ ] Verify changes persist after reload
- [ ] Check Export/Import settings page

### 23. Keyboard and Accessibility

- [ ] Verify skip-to-content link appears on Tab from page top
- [ ] Tab through the main navigation -- verify focus rings visible
- [ ] Open `?` keyboard shortcuts help, verify three sections (Global, Board, Editor)
- [ ] Use keyboard-only to: create a board, add a card, move a card, approve a proposal
- [ ] Open and close dialogs with Escape key
- [ ] Verify Escape stack: each press closes only the topmost surface
- [ ] On a board with no open surfaces, Escape navigates to `/workspace/boards`
- [ ] Board navigation: `h/l` columns, `j/k` cards, `Enter` open, `n` new card, `f` filter

### 24. Responsive / Visual

- [ ] Resize browser to mobile width -- verify layout doesn't break
- [ ] Verify design tokens apply (glass morphism, focus rings, consistent spacing)
- [ ] Verify no layout shift when dragging cards
- [ ] Verify sidebar footer stays pinned at bottom

### 25. Error and Edge Cases

- [ ] Disconnect network, try an action -- verify error toast appears
- [ ] Reconnect, verify SignalR reconnects (or polling fallback activates)
- [ ] Open two tabs on the same board, make changes in one -- verify realtime update in the other
- [ ] Try to access a board ID that doesn't exist -- verify 404 handling
- [ ] Try XSS in card title: `<script>alert('xss')</script>` -- verify it's escaped
- [ ] Try very long card title (500+ chars) -- verify truncation or validation
- [ ] Submit rapid-fire captures (10 in quick succession) -- verify no data loss

### 26. Performance Smoke

- [ ] Load a board with 50+ cards -- verify it renders within ~2 seconds
- [ ] Open Inbox with many items -- verify virtual scrolling kicks in
- [ ] Navigate between routes rapidly -- verify no stale state or loading flicker
- [ ] Check that lazy route splitting works (no full-bundle load on initial page)

### 27. Full Core Loop (End-to-End Golden Path)

This is the most important test -- the **capture -> review -> board** golden path:

1. Start on Home
2. Open capture (`Ctrl+Shift+C`), enter: "Implement user avatars"
3. Navigate to Inbox, verify the item arrived
4. Triage the item to a specific board
5. Navigate to Review, verify a proposal was generated
6. Inspect the proposal diff
7. Approve the proposal
8. Execute the approved proposal
9. Navigate to the Board, verify the card now exists in the correct column
10. Move the card to a "Done" column
11. Check Activity view -- verify the full trail: capture -> triage -> proposal -> approve -> execute -> move
12. Check Notifications -- verify relevant notifications were generated
13. Open Metrics -- verify the board metrics reflect the new card

---

## Seeded Data Quick Reference

After running `npm run demo:seed`, log in as `demo` / `demo123`:

| Surface | What to expect |
|---|---|
| Boards | "DEMO: Client Onboarding Demo" (populated), "DEMO: Content Calendar", "DEMO: Blank Board" |
| Inbox | Pre-seeded captures with onboarding text |
| Queue | One success, one failure queue entry |
| Chat | "Stakeholder Demo" session |
| Notifications | Comment @mention notifications |
| Ops | Seeded health check and boards list log entries |
| Archive | "DEMO: Archived Board" |

---

## Provider Health Verification

Before running chat/tool-calling tests, verify the LLM provider is active:

```bash
# Config check (no upstream call)
curl http://localhost:5000/api/llm/chat/health -H "Authorization: Bearer <token>"

# Live probe (makes a real upstream call, uses tokens)
curl "http://localhost:5000/api/llm/chat/health?probe=true" -H "Authorization: Bearer <token>"
```

Response includes `provider` (Mock/OpenAI/Gemini), `isAvailable`, and `degradedReason` if applicable.
