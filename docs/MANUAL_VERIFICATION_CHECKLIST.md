# Manual Verification Checklist

Generated: 2026-04-01

Covers: 5 open PRs (#665-#669) and recent merged PRs (#568-#664).

## How to Use

- [ ] checkboxes for each verification step
- Prerequisites listed per section
- Expected results are specific and observable
- Each step should be verifiable in under 2 minutes
- Record pass/fail, browser, OS, and commit SHA per session

## Prerequisites (All Sections)

1. Backend running: `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj`
2. Frontend running: `cd frontend/taskdeck-web && npm run dev`
3. Frontend at `http://localhost:5173` (or fallback port printed by Vite)
4. API at `http://localhost:5000`, Swagger at `http://localhost:5000/swagger`
5. Registered and logged-in test user
6. At least one board with columns and cards created

---

## Section 1: Review Card Sticky Footer and Constrained Height (PR #665)

**What changed:** Review proposal cards now have max-height with scroll, sticky action footer, and capped entity/operation sections.

### Prerequisites
- At least 2-3 proposals in the Review queue (create board-scoped chat sessions, send actionable messages with "Request proposal generation" enabled, or triage inbox captures)
- At least one proposal with many affected entities (apply a starter pack to generate a multi-entity proposal)

### Verifications

- [ ] Navigate to `/workspace/review`. Expand all collapsible sections on a card with many affected entities. Verify the action buttons (View Diff / Approve / Reject) remain visible at the bottom of the card without scrolling the page.
- [ ] On the same card, scroll within the card body. Verify the sticky footer (action buttons) stays pinned to the card bottom with a solid background — no transparency gap between card content and footer.
- [ ] Open a proposal card with a long entity list (7+ entities). Verify the entity list section is capped at approximately 12rem height with its own inner scrollbar rather than expanding the card unboundedly.
- [ ] Open a proposal card with a long planned-changes list. Verify the operation list section is also capped with inner scroll.
- [ ] Resize browser to 640px width (mobile breakpoint). Verify action buttons render full-width with correct padding and no horizontal overflow.
- [ ] On a short proposal card (1-2 entities), verify scrolling within the card does not interfere with page-level scroll — the card should not consume scroll events when its content does not overflow.
- [ ] Verify the card max-height is approximately 70vh on desktop and 80vh on mobile (640px viewport).

---

## Section 2: GDPR Data Export and Account Deletion (PR #666)

**What changed:** Added `GET /api/account/export` (data export) and `POST /api/account/delete` (account deletion with safeguards).

### Prerequisites
- Logged-in user with some boards, captures, chat sessions, notifications
- A second test user account for isolation checks
- API tool (curl, Postman, or Swagger UI) with valid JWT bearer token

### Verifications — Data Export

- [ ] `GET /api/account/export` with valid bearer token. Verify response is a JSON object with `version: "1.0"` and contains sections for boards, notifications, captures, proposals, chatSessions, auditTrail, and preferences.
- [ ] Verify the exported data contains ONLY the requesting user's data — no boards/captures/notifications from other users.
- [ ] Call export endpoint a second time. Verify response is identical in structure (idempotent).
- [ ] Call export endpoint without bearer token. Verify `401` response.

### Verifications — Account Deletion

- [ ] `POST /api/account/delete` with body `{ "password": "wrong-password", "confirmationPhrase": "DELETE MY ACCOUNT" }`. Verify response is a rejection (password mismatch) — account NOT deleted.
- [ ] `POST /api/account/delete` with body `{ "password": "correct-password", "confirmationPhrase": "delete my account" }` (wrong case). Verify response is a rejection (confirmation phrase must be exact).
- [ ] `POST /api/account/delete` with body `{ "password": "correct-password", "confirmationPhrase": "DELETE MY ACCOUNT" }`. Verify response indicates success and account is deactivated.
- [ ] Attempt to log in with the deleted account's credentials. Verify login fails — deactivated user cannot authenticate.
- [ ] Call export or delete endpoint without bearer token. Verify `401` response.

---

## Section 3: Board Metrics Dashboard (PR #667)

**What changed:** New `/workspace/metrics` route with throughput, cycle time, WIP, and blocked card charts.

### Prerequisites
- At least one board with cards that have been moved between columns (ideally some moved to the rightmost "done" column)
- At least one card with a blocked reason set

### Verifications — Navigation and Loading

- [ ] Navigate to `/workspace/metrics`. Verify the page loads with a board selector dropdown and date range picker.
- [ ] Verify "Metrics" appears in the sidebar navigation under workbench tools (available in guided/agent modes).
- [ ] Select a board from the dropdown. Verify summary cards appear: throughput total, average cycle time, WIP count, blocked count.

### Verifications — Charts and Data

- [ ] Verify the throughput trend renders as a CSS bar chart with bars per day. Hover/inspect bars to confirm they correspond to cards completed on those dates.
- [ ] Verify WIP-by-column renders as a horizontal bar chart. If any column exceeds its WIP limit, verify the bar is highlighted differently (WIP limit violation).
- [ ] Verify cycle time details table shows individual cards with their creation-to-done duration.
- [ ] Verify blocked cards table shows cards with their blocked duration.

### Verifications — Filters and States

- [ ] Change the date range (7/14/30/60/90 days). Verify the charts and summary cards update to reflect the new range.
- [ ] Switch to a different board in the selector. Verify all data reloads for the new board.
- [ ] Select a board with no completed cards. Verify throughput shows "0" with an appropriate empty state — no chart errors.
- [ ] Select a board with no blocked cards. Verify the blocked section shows an empty state message, not a broken chart.
- [ ] Trigger a loading state (slow network or rapid board switching). Verify a loading spinner appears, not a flash of stale data.
- [ ] Trigger an error state (e.g., select a board ID that was deleted). Verify an error message appears with a retry button.

### Verifications — API

- [ ] `GET /api/metrics/boards/{boardId}` with valid bearer token. Verify JSON response contains `throughput`, `cycleTime`, `wipSnapshots`, `blockedCards`.
- [ ] `GET /api/metrics/boards/{boardId}?from=2026-03-01&to=2026-03-31` with date filters. Verify response data is scoped to the specified range.
- [ ] `GET /api/metrics/boards/{boardId}` without bearer token. Verify `401`.
- [ ] `GET /api/metrics/boards/{otherUserBoardId}` with bearer token for a board you do not own. Verify `403` or `404` (no cross-user access).

---

## Section 4: GitHub OAuth Login (PR #668)

**What changed:** Frontend GitHub OAuth button on login page, conditional on server config. Backend exchange endpoint for auth codes.

### Prerequisites
- For full OAuth testing: backend configured with `GitHubOAuth:ClientId` and `GitHubOAuth:ClientSecret` in appsettings or environment
- For config-gating verification: backend running WITHOUT GitHub OAuth config

### Verifications — Config Gating (No GitHub Config)

- [ ] Start the backend WITHOUT `GitHubOAuth:ClientId`/`GitHubOAuth:ClientSecret` configured. Navigate to `/login`. Verify the "Sign in with GitHub" button does NOT appear.
- [ ] `GET /api/auth/providers` with or without auth. Verify response includes `gitHub: false`.

### Verifications — Config Gating (With GitHub Config)

- [ ] Start the backend WITH `GitHubOAuth:ClientId` and `GitHubOAuth:ClientSecret` set. Navigate to `/login`. Verify a "Sign in with GitHub" button IS visible.
- [ ] `GET /api/auth/providers`. Verify response includes `gitHub: true`.

### Verifications — Existing Password Login Still Works

- [ ] With GitHub OAuth configured, navigate to `/login`. Enter valid username/password. Verify login succeeds and redirects to `/workspace/home` — password login is unaffected by OAuth config.
- [ ] Register a new user via `/register` with username/password. Verify registration still works.

### Verifications — OAuth Flow (Requires Real GitHub App)

- [ ] Click "Sign in with GitHub" on `/login`. Verify redirect to GitHub authorization page.
- [ ] Authorize the app on GitHub. Verify redirect back to Taskdeck with `oauth_code` query parameter.
- [ ] Verify the `oauth_code` is exchanged automatically — user is logged in and redirected to `/workspace/home`.
- [ ] Verify the URL is cleaned after exchange — no `oauth_code` in the address bar after login.
- [ ] Refresh the page. Verify no re-exchange attempt (code is single-use, URL was cleaned).

### Verifications — Graceful Degradation

- [ ] If `/api/auth/providers` endpoint fails (e.g., network error), verify the login page still renders with password fields — GitHub button simply does not appear. No error shown to user.

---

## Section 5: LLM Tool-Calling — Chat Read Tools (PR #669)

**What changed:** LLM can now call read tools (`list_board_columns`, `list_cards_in_column`, `get_card_details`, `search_cards`, `get_board_labels`) during chat to dynamically query board state.

### Prerequisites
- A board with at least 3 columns and 5+ cards across columns, some with labels
- Mock LLM provider (default) — tool-calling works with mock dispatch

### Verifications — Basic Tool-Calling

- [ ] Navigate to `/workspace/automations/chat`. Create a new board-scoped session (select a board). Send: "What cards are in my Backlog?" (or the name of your first column). Verify the response lists actual card names from that column — not a generic "I can't access your board" message.
- [ ] Send: "What columns does this board have?" Verify the response lists the actual column names.
- [ ] Send: "Search for cards about onboarding" (or a keyword that matches card titles). Verify results reference actual matching cards.
- [ ] Send: "What labels are on this board?" Verify the response lists actual label names and/or colors.

### Verifications — Multi-Turn

- [ ] In the same session, ask a follow-up: "Tell me more about the first card you found." Verify the LLM uses `get_card_details` to fetch details and provides card-specific information (title, description, column, labels).
- [ ] Ask another follow-up referencing previous context. Verify multi-turn coherence — the LLM remembers what it found in earlier turns.

### Verifications — Fallback Behavior

- [ ] Send a non-board message like "What is the capital of France?" Verify the LLM responds with text (no tool calls) and does not crash or error.
- [ ] Send an actionable instruction with proposal generation enabled: "Create a card called Test in Backlog." Verify the existing proposal creation path still works — a proposal is generated, not a tool-call-only response.

### Verifications — Status Events

- [ ] While the LLM is processing a tool-calling request, observe the chat UI. Verify intermediate status messages appear (e.g., "Looking up cards in Backlog...") before the final response.

### Verifications — Safety

- [ ] Verify all tool calls in this PR are read-only. Send: "Delete the first card." Verify this does NOT execute a delete — it either creates a proposal (via existing flow) or declines. No direct mutations from tool calls.

### Verifications — API

- [ ] `GET /api/llm/chat/sessions` without bearer token. Verify `401`.
- [ ] Create a chat session scoped to another user's board. Verify `403` or `404` — tool calls cannot leak cross-user board data.

---

## Section 6: Review UX Improvements (Merged PRs #641, #634, #659, #661)

**What changed:** Collapsible detail sections with risk color-coding (#641), hide applied proposals by default with clear/dismiss (#634), action buttons moved above details (#659, #661), two-step approve-then-apply flow clarity.

### Verifications

- [ ] Navigate to `/workspace/review`. Verify applied proposals are hidden by default. Look for a "Show completed" toggle or filter — enabling it should reveal Applied/Rejected proposals.
- [ ] Verify a "Clear applied" or "Dismiss" button exists for batch-clearing applied proposals.
- [ ] On an active proposal card, verify action buttons (Approve / Reject / View Diff) appear above or at the top of the card details, not buried at the bottom.
- [ ] Approve a proposal. Verify the status visually changes and an "Apply to Board" button appears — the two-step flow (Approve then Apply) is clearly communicated.
- [ ] Verify risk level badges use color-coding: Low = green-ish, Medium = amber, High = red.
- [ ] Verify detail sections (Affected entities, Planned changes, Provenance) are collapsible. Click to expand/collapse and confirm content toggles.
- [ ] Verify proposal count badges update when the filter changes between showing/hiding completed items.

---

## Section 7: Capture and Triage Pipeline (Merged PRs #632, #643, #639, #592, #607)

**What changed:** Delimiter-based capture triage for natural-language text (#632), dash context hint and delimiter separation (#643), meaningful error messages for failed captures (#639), transcript paste/file capture (#592), batch triage and suggestion editing (#607).

### Verifications — Delimiter Parsing

- [ ] Create a capture with dash-separated text: "ACME Ltd - Send engagement letter - Schedule onboarding call - Confirm payroll". Triage it. Verify it produces 3 individual task proposals (not one giant task with the full text as title).
- [ ] Create a capture with semicolons: "Fix login bug; Update docs; Review PR". Triage it. Verify it splits into 3 tasks.
- [ ] Create a capture with a single natural-language sentence: "I need to reorganize the onboarding board." Triage it. Verify it produces a single task card without failing — the full text becomes the description.

### Verifications — Error Messages

- [ ] Force a triage failure (e.g., capture text that exceeds validation limits or triggers an edge case). Verify the inbox shows a meaningful error message — not just a bare "FAILED" tag. The message should indicate what went wrong.
- [ ] After a failure, verify the original capture text is preserved and retryable.

### Verifications — Batch Triage

- [ ] In the Inbox, select multiple captures using checkboxes. Verify a batch triage action is available. Execute it. Verify all selected items are triaged.
- [ ] After batch triage, verify suggestion editing — click on a triaged suggestion and modify the proposed title/description before approving.

### Verifications — Transcript Capture

- [ ] Paste a multi-line transcript into the capture input. Verify it is accepted as a single capture artifact.
- [ ] Verify the transcript capture appears in the Inbox with a readable excerpt.

---

## Section 8: Chat and LLM Improvements (Merged PRs #635, #644, #602, #586, #589, #591, #582)

**What changed:** Fix chat response truncation and raw JSON display (#635, #644), chat-to-proposal NLP gap fix (#602), LLM instruction extraction (#586), board-context-aware prompting (#589), multi-instruction parsing (#591), error UX for failed proposal parsing (#582).

### Verifications

- [ ] Open `/workspace/automations/chat`. Create a board-scoped session. Send an actionable message like "Create a card called Weekly Standup in the To Do column" with proposal generation enabled. Verify a proposal reference appears in the response — not just prose text.
- [ ] Send a message that would produce a long response. Verify the response is NOT truncated mid-sentence or mid-JSON. If truncation occurs, verify a user-friendly "Response was truncated" message appears instead of raw partial JSON.
- [ ] Send a message and verify the response never shows raw JSON (e.g., `{ "reply": "..." }`) to the user. All responses should be rendered as readable text.
- [ ] Send a multi-instruction message: "Create a card called A and another called B." Verify both instructions are parsed and reflected in the proposal.
- [ ] Send a message that triggers a proposal parsing failure. Verify a meaningful error message appears — not a blank response or stack trace.

---

## Section 9: Board and Card UX Fixes (Merged PRs #637, #630, #636, #590, #578)

**What changed:** Card drag handle layout shift fix (#637), board horizontal scrollbar visibility (#630), sidebar footer pinned (#636), keyboard card movement and move-to menu (#590), archive board freeze fix (#578).

### Verifications

- [ ] On a board with cards, hover over the drag handle on any card. Verify the card does NOT visually shift or change height on hover — no layout jump.
- [ ] On a board with 5+ columns, verify the horizontal scrollbar is visible within the viewport — not hidden below the fold requiring a page scroll to reach it.
- [ ] With many sidebar items visible, scroll the page. Verify the Shortcuts and Logout buttons in the sidebar footer remain visible (pinned) at the bottom of the sidebar.
- [ ] On a board, select a card with keyboard (j/k navigation). Press a keyboard shortcut to move the card to another column (check the shortcuts help for the exact key). Verify the card moves. Alternatively, use the "Move to" action menu to move a card to a specific column.
- [ ] Open Board Settings and click "Move to Archive". Verify the action completes in under 5 seconds — no 30-second browser freeze.

---

## Section 10: Inbox and Notification UX (Merged PRs #631, #646)

**What changed:** Inbox color-coded status tags and text fatigue reduction (#631), notification type differentiation, grouping, and batch actions (#646).

### Verifications — Inbox

- [ ] Navigate to `/workspace/inbox`. Verify status tags (Failed, Triaging, Applied to Board, Ignored, New) use distinct colors — not all the same gray. Failed = red-ish, Applied = green-ish, Triaging = amber-ish.
- [ ] Verify long capture excerpts are truncated with ellipsis rather than spanning unlimited lines.
- [ ] Verify the overall inbox list is scannable — text weight and spacing reduce eye fatigue compared to a wall of white text.

### Verifications — Notifications

- [ ] Navigate to `/workspace/notifications`. Verify different notification types (proposal updates, board changes, system) have visual differentiation — distinct left-border colors, icons, or badge styles.
- [ ] Verify a "Mark all read" or batch action button exists at the top of the notification list.
- [ ] Verify consecutive notifications of the same type are grouped where possible (e.g., "3 automation proposals updated" instead of 3 separate identical cards).

---

## Section 11: Today View and Home Improvements (Merged PRs #633, #629)

**What changed:** Today view reduced card density and visual hierarchy (#633), Home primary action card color softened (#629).

### Verifications

- [ ] Navigate to `/workspace/home`. Verify the "Next Step" / primary action card uses a softer color — not full bright red (`#ff5352`). It should have a warm accent (like an ember-tinted border or subtle background) rather than looking like an error state.
- [ ] Navigate to `/workspace/today`. Verify zero-count stat cards (e.g., "Blocked: 0") are visually de-emphasized compared to non-zero counts.
- [ ] Verify spacing between Today cards is comfortable — not cramped. Cards should have visible breathing room.
- [ ] Verify the visual hierarchy makes it easy to scan: the most urgent items (overdue, blocked) should stand out more than neutral items.

---

## Section 12: Global Search and Command Palette (Merged PRs #603, #645)

**What changed:** Global search and quick-action launcher via Ctrl+K (#603), offset pagination for search (#645).

### Verifications

- [ ] Press `Ctrl+K` (or `Cmd+K` on Mac). Verify the command palette opens.
- [ ] Type a search query (e.g., a card title substring). Verify matching results appear — cards, boards, or other entities.
- [ ] Select a result with arrow keys and press Enter. Verify navigation to the correct resource (board, card modal, etc.).
- [ ] Type a search query that returns many results. Verify pagination or "load more" works — results beyond the first page are accessible.
- [ ] Press `Escape`. Verify the palette closes without side effects.

---

## Section 13: Starter Pack Dark Theme (Merged PR #640)

**What changed:** StarterPackCatalogModal migrated from hardcoded light Tailwind classes to design tokens.

### Verifications

- [ ] On a board, open the Starter Pack catalog. Verify the modal background matches the dark theme — no bright white background.
- [ ] Verify text in the modal uses dark-theme-appropriate colors — readable on dark backgrounds.
- [ ] Verify status indicators (success/warning/error badges on packs) remain visually distinct in dark theme.
- [ ] If light theme is available (`[data-theme="light"]`), switch to it and verify the modal also renders correctly.
- [ ] Apply a starter pack via the modal. Verify the apply/dry-run flow works — no functional regression from the styling migration.

---

## Section 14: Activity and Audit Trail (Merged PRs #581, #584)

**What changed:** Fix activity audit trail not recording board mutations (#581), enrich audit log entries with changed field details (#584).

### Verifications

- [ ] Create a card, edit its title, move it to another column. Navigate to `/workspace/activity`. Select the board in board mode. Verify audit entries appear for each mutation (card created, title changed, card moved).
- [ ] Click on an audit entry. Verify it shows changed field details — not just "Card updated" but specifics like "title changed from X to Y" or "moved from column A to column B".
- [ ] Verify entity history mode works: select a card entity and verify its individual history timeline.
- [ ] Verify user history mode: switch to user mode and confirm your own recent actions appear.

---

## Section 15: Accessibility and WCAG Remediation (Merged PR #604)

**What changed:** Accessibility audit and WCAG-focused remediation pass.

### Verifications

- [ ] Tab through the main navigation (sidebar links, command palette trigger, user menu). Verify focus indicators are visible on every interactive element.
- [ ] On the board view, verify screen reader landmarks exist: main content area, navigation, and complementary regions have appropriate ARIA roles or HTML5 semantic elements.
- [ ] Open a modal (card edit, starter pack catalog). Verify focus is trapped inside the modal — tabbing does not escape to the background. Verify `Escape` closes the modal.
- [ ] Verify color contrast: text on all primary surfaces (sidebar, board, cards, modals) meets WCAG AA contrast ratio (4.5:1 for normal text).

---

## Section 16: Saved Views and Keyboard Shortcuts (Merged PRs #585, #590)

**What changed:** Saved views and productivity shortcuts (#585), board keyboard card movement (#590).

### Verifications

- [ ] Check if a "Saved Views" feature is accessible (sidebar or board toolbar). If present, create a saved view with specific filters applied. Navigate away and return to the saved view. Verify filters are restored.
- [ ] On a board, use keyboard shortcuts to move a card between columns. Press `?` to check the shortcuts help for the exact key bindings. Verify the card physically moves and persists on refresh.
- [ ] Verify the "Move to" action menu on a card: right-click or use the card context menu to select a target column. Verify the card moves.

---

## Section 17: Security and Auth (Merged PRs #569, #599)

**What changed:** GitHub OAuth backend (#569), JWT token library update (#599).

### Verifications

- [ ] Register and log in with password. Verify JWT is issued and stored (check browser storage / network response).
- [ ] Make authenticated API calls. Verify the JWT is accepted — no `401` errors for valid tokens.
- [ ] Let a session sit for the token expiry period (or manually create an expired token). Verify expired tokens are rejected with `401`.
- [ ] Verify `/api/auth/providers` endpoint is accessible and returns the expected provider configuration.

---

## Section 18: Backup/Restore and Ops (Merged PRs #663, #606)

**What changed:** Backup/restore automation and DR drill playbook (#663), SBOM generation and release provenance (#606).

### Verifications

- [ ] Check `docs/ops/` for backup/restore runbook. Verify documentation exists and references concrete commands.
- [ ] If backup scripts exist in `scripts/`, verify they can be invoked without errors (dry-run if possible).
- [ ] Verify SBOM generation workflow exists in `.github/workflows/`. Check a recent nightly or release run for SBOM artifacts.

---

## Section 19: MCP Prototype (Merged PR #664)

**What changed:** Minimal MCP prototype for `taskdeck://boards` via stdio.

### Verifications

- [ ] Check for MCP-related CLI entry point or configuration. Verify `docs/MCP_TOOLING_GUIDE.md` references the new prototype.
- [ ] If a local MCP server can be started, verify it responds to a `taskdeck://boards` resource request with board data.

---

## Section 20: Dependency and CI Updates (Merged PRs #593-#600, #595, #642, #658)

**What changed:** Dependency bumps (xunit, JWT libraries, Swashbuckle 10, eslint, @types/node, actions), CI pipeline updates.

### Verifications

- [ ] `dotnet build backend/Taskdeck.sln -c Release` — verify 0 errors, no new warnings from dependency upgrades.
- [ ] `dotnet test backend/Taskdeck.sln -c Release -m:1` — verify all tests pass.
- [ ] `cd frontend/taskdeck-web && npm run build` — verify 0 errors.
- [ ] `cd frontend/taskdeck-web && npx vitest --run` — verify all tests pass.
- [ ] Visit `http://localhost:5000/swagger`. Verify Swagger UI loads correctly (Swashbuckle 10 upgrade may change the UI appearance).
- [ ] Check a recent CI run on GitHub Actions. Verify all required checks (`ci-required.yml`) pass.

---

## Section 21: Known Bug Regression Checks

These are previously reported bugs. Verify they remain fixed or track their current status.

### Fixes Delivered in Recent PRs

- [ ] **Board polling throttle (#568):** Open a board with devtools Network tab filtered to `/api/boards`. Verify requests are NOT firing at 3/second — should be throttled to a reasonable interval (30s+) or event-driven via SignalR.
- [ ] **Archive board freeze (#578):** Archive a board. Verify it completes in under 5 seconds, no browser hang.
- [ ] **Activity view defaults (#581):** Open `/workspace/activity`. Verify the board selector defaults to a recently-active non-archived board, not an alphabetically-first archived board.
- [ ] **Chat raw JSON (#635, #644):** Send a chat message. Verify no raw JSON in the response display.
- [ ] **Card drag handle shift (#637):** Hover over a card's drag handle. Verify no layout shift.
- [ ] **Sidebar footer (#636):** With a long sidebar, verify Shortcuts/Logout stay visible at the bottom.

### Known Open Bugs (Check Current Status)

- [ ] **Delete Card no confirmation (#513):** Open a card modal, click Delete Card. Check if a confirmation dialog appears before deletion. (Was P1, may or may not be fixed.)
- [ ] **WIP limit warning-only (#517):** Set WIP limit to 1 on a column with 1 card. Try adding another card. Check if the add action is blocked or just warned.

---

## Final Automated Smoke

After manual verification, run the full automated suite to confirm no regressions:

```bash
# Backend
dotnet test backend/Taskdeck.sln -c Release -m:1

# Frontend unit + build
cd frontend/taskdeck-web && npx vitest --run --reporter=verbose
cd frontend/taskdeck-web && npm run typecheck && npm run build

# Frontend E2E
cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line
```
