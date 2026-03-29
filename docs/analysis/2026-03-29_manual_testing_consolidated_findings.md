# Manual Testing — Consolidated Findings Report
**Date:** 2026-03-29
**Sessions:** User (fresh registration, lalal account) + Claude Sonnet automated walkthrough
**Environment:** localhost:5173 (frontend) + localhost:5000 (backend)
**DB baseline:** Fresh registration + pre-seeded boards (onboarding, calendar)

---

## Executive Summary

Two manual testing sessions were run on 2026-03-29: one by the product owner doing a fresh-registration walkthrough, and one by Claude Sonnet doing a structured surface-by-surface audit. Together they produced **18 bugs** and **8 design observations** spanning data isolation, dark-mode theming, UI responsiveness, chat utility, and board stability.

The most critical findings are:

1. **Data isolation failure** — a fresh-registered user sees automation queue items from days before their registration. This is a multi-user isolation bug.
2. **Board auto-switching** — the active board switches between boards every few seconds unprompted, making the board surface nearly unusable.
3. **Excessive board polling** — boards are fetched ~3 times/second, causing visual thrashing and wasted network/CPU.
4. **Inbox capture entry point missing** — the primary product thesis (near-zero-friction capture) has no visible capture button in the Inbox view for a fresh user.
5. **Chat generates text but doesn't create tasks/cards** — the chat surface produces natural language but never emits proposals or mutations despite a live LLM being configured.
6. **Dark mode theming gaps** — Edit Card modal, Edit Column modal, and Filter Cards panel all render white/light backgrounds against the dark workspace shell.

---

## Section 1 — Critical Bugs (P0/P1)

### BUG-C1: Fresh user sees other users' automation queue data
**Severity:** P0 — data isolation / privacy
**Surface:** Automation Queue (`/workspace/automation/queue`)
**Reproduction:** Register a new account. Navigate to Automation Queue. Items from 2026-03-27 are visible with `Completed` and `Failed` statuses.
**Expected:** New user sees empty queue (0 items).
**Actual:** 9 total items (6 Completed, 3 Failed) created on 27/03/2026 are visible, including failure messages with internal error details.
**Impact:** Any user can see every other user's queue history. Exposes internal error strings (e.g., `BoardId is required to triage capture items into proposals`). Violates the isolation guarantee required before any external exposure.
**Root cause hypothesis:** Queue API likely fetches all items without scoping to the authenticated user's identity. The LLM queue controller may be missing a `userId` predicate in its list query.

---

### BUG-C2: Board keeps auto-switching between boards
**Severity:** P0 — core loop broken
**Surface:** Board view (`/workspace/boards/{id}`)
**Reproduction:** Create a second board (e.g., calendar from default templates) while already having the onboarding board. Navigate to the calendar board. Within a few seconds the view switches to the onboarding board, then briefly back to calendar, then back to onboarding.
**Expected:** Board view stays on the user-selected board until the user navigates away.
**Actual:** Board auto-switches every few seconds with no user input. Pattern: calendar → onboarding (a few seconds) → calendar (1 second) → onboarding (persistent).
**Root cause hypothesis:** A polling/subscription side-effect is triggering a board-selection store mutation. When the boards list is polled and returned in a different order, the "active board" selector may be choosing the first item in the list rather than preserving the user's selection. Alternatively, a SignalR reconnect event is resetting the board context.

---

### BUG-C3: Board list polling at ~3 requests/second
**Severity:** P1 — performance / UX
**Surface:** Board canvas / boardStore
**Reproduction:** Navigate to any board. Open browser devtools → Network tab. Filter by `/api/boards`. Observe repeated requests at approximately 3-per-second intervals.
**Expected:** Board list fetched once on mount, then updated via SignalR events or on explicit user action.
**Actual:** Continuous polling loop visible in the network tab. Causes flickering and unnecessary re-renders.
**Root cause hypothesis:** A `setInterval` or reactive watcher on `boardStore` is triggering repeated fetches without a proper debounce or interval guard. Likely introduced with the realtime collaboration layer.

---

### BUG-C4: Inbox capture — no visible entry point for fresh users
**Severity:** P1 — core thesis broken
**Surface:** Inbox view (`/workspace/inbox`)
**Reproduction:** Register fresh. Navigate to Inbox. Look for a "Capture" button, compose area, or text entry.
**Expected:** Prominent "New Capture" or "Add to Inbox" affordance. The product thesis is near-zero-friction capture — the Inbox must have an obvious entry point.
**Actual:** No visible capture button or compose area in the Inbox view. The only working path is `Ctrl+Shift+C` (Quick Capture) which is a hidden keyboard shortcut undiscoverable to new users.
**Note:** Quick Capture (`Ctrl+Shift+C`) itself works and auto-navigates to Inbox (confirmed by Sonnet session). The gap is discoverability: there is no in-surface button that a first-time user would find.

---

### BUG-C5: Chat does not create tasks or proposals — only generates text
**Severity:** P1 — core feature non-functional
**Surface:** Automation Chat
**Reproduction:** Open Chat. Start a session. Ask: "can you create new onboarding tasks for people who aren't technical?" or "can you create a 'show documentation' task?". Observe response.
**Expected:** Chat should recognize task-creation intent and either (a) emit a structured proposal into the Review queue, or (b) use the instruction queue to create a card — all subject to user review (review-first principle).
**Actual:** Chat responds with detailed natural language advice (e.g., "Okay, let's create a 'Show Documentation' task specifically designed for non-technical new hires. The key here is...") but does not create any card, proposal, or queue item. No board mutations occur. The LLM is generating prose, not structured actions.
**Root cause hypothesis:** The chat system prompt / tool-use configuration is not wired to the `AutomationProposalService`. The LLM is responding as a general assistant rather than a task-management agent that emits structured proposals.

---

## Section 2 — High Severity Bugs (P1)

### BUG-H1: Delete Card has no confirmation dialog
**Severity:** P1 — data loss risk
**Surface:** Card modal
**Reproduction:** Open any card modal → click "Delete Card".
**Expected:** Confirmation dialog: "Are you sure? This cannot be undone."
**Actual:** Card deleted immediately with no confirmation. The action is irreversible and undiscoverable to new users.

---

### BUG-H2: Edit Card modal — white/light theme in dark workspace
**Severity:** P1 — dark mode regression
**Surface:** Card edit modal
**Reproduction:** Open any card in the board. The card modal background appears primarily white/light.
**Expected:** Modal uses the dark workspace theme (`--td-*` design tokens).
**Actual:** White background breaks the visual context. Likely the modal container is missing dark-mode token application.

---

### BUG-H3: Edit Column modal — white/light theme
**Severity:** P1 — dark mode regression
**Surface:** Column edit dialog
**Reproduction:** Click the column settings/edit icon → observe the edit form.
**Expected:** Dark-themed modal consistent with the board surface.
**Actual:** White/light background, only text fields appear styled.

---

### BUG-H4: Filter Cards panel — white/light theme
**Severity:** P1 — dark mode regression
**Surface:** Filter panel (`f` key or filter button on board)
**Reproduction:** Press `f` on the board → observe the filter panel.
**Expected:** Dark-themed panel consistent with the board.
**Actual:** White/light background. Only partial styling applied.

---

### BUG-H5: Text-selected cards are not draggable
**Severity:** P1 — interaction regression
**Surface:** Board cards
**Reproduction:** Left-click and drag starting from text inside a card (e.g., the card title), releasing without creating a drag action. The text becomes browser-highlighted (blue selection). Now attempt to drag the card by its handle.
**Expected:** Card is draggable regardless of whether any text within it is browser-selected.
**Actual:** Once text is selected inside a card, the card becomes non-draggable until the selection is cleared. The drag handle interaction is blocked by the active text selection state.

---

## Section 3 — Medium Severity Bugs (P2)

### BUG-M1: Collaborators panel flickers between "LIVE / No active collaborators" and listing the current user
**Severity:** P2 — UX confusion
**Surface:** Board view — collaborators presence strip
**Reproduction:** Navigate to a board. Observe the "No active collaborators" → after a few seconds → current user appears as collaborator under "LIVE".
**Expected:** Presence state initializes immediately and is stable.
**Actual:** The panel flickers between the empty state and the user's own presence marker. This is likely a race between the initial board-load and the SignalR `BoardJoined` notification settling.

---

### BUG-M2: Column name form — Enter key does not submit; triggers wrong action
**Severity:** P2 — UX friction
**Surface:** Add Column modal
**Reproduction:** Click "Add Column" → type a name → press Enter.
**Expected:** Column created, modal closes.
**Actual:** Enter does not submit the form. Instead it appears to trigger an interaction on the underlying board (e.g., opening an inline add-card form on an existing column). User must click "Create" manually.

---

### BUG-M3: WIP enforcement is warning-only, not blocking
**Severity:** P2 — known policy gap
**Surface:** Column WIP limit
**Reproduction:** Set WIP limit to 1 on a column with 2 cards. Click "+ Add Card".
**Expected:** "Add Card" is disabled or blocked with a clear error when WIP is exceeded.
**Actual:** Warning displays ("WIP limit exceeded") but the add affordance remains clickable. Additionally, confirming "Add" while WIP-exceeded opened the modal for an existing card rather than the new card — a likely focus/event-target bug.

---

### BUG-M4: Chat — raw markdown rendered as plaintext
**Severity:** P2 — chat UX
**Surface:** Automation Chat messages
**Reproduction:** Send any message in Chat that produces a response with markdown headings/bold/lists.
**Expected:** Markdown rendered to HTML (bold, headings, lists visible).
**Actual:** Raw markdown tokens (`###`, `--`, `**`) appear as literal text. The chat message renderer is not applying markdown parsing.

---

### BUG-M5: Archive board action causes ~30-second browser freeze
**Severity:** P2 — performance
**Surface:** Board Settings → "Move to Archive"
**Reproduction:** Click "Move to Archive" in Board Settings.
**Expected:** Archive action completes in under 2 seconds; board removed from list; success toast shown.
**Actual:** Browser freezes for approximately 30 seconds (CDP/Playwright timeout) before the action resolves. The action ultimately succeeds but the hang is severe.

---

### BUG-M6: No success toast on "Restore Board"
**Severity:** P2 — UX inconsistency
**Surface:** Archive view → Restore Board
**Reproduction:** Click "Restore Board" in `/workspace/archive`.
**Expected:** Success toast: "Board restored." Board removed from archive list.
**Actual:** Board is silently removed from the archive list with no toast. All other mutating actions show toasts.

---

## Section 4 — Low Severity / Cosmetic (P3)

### BUG-L1: Card creation toast has leading space in card name
**Severity:** P3
**Surface:** Card creation toast
**Reproduction:** Create a card named "First test card".
**Actual:** Toast reads: `Card " First test card" created successfully` — note leading space inside quotes. Card title not trimmed before toast interpolation.

---

### BUG-L2: "DRAG CARD" text label always visible on cards
**Severity:** P3 — visual noise
**Surface:** Board cards
**Observation:** Every card shows `:: DRAG CARD` as a persistent label. The `::` dotted-grid icon is the actual drag handle — the text label adds vertical noise on dense boards.
**Suggested fix:** Show text only on hover; rely on the icon for affordance in compact mode.

---

## Section 5 — Design Observations

### OBS-1: Label color not shown in card modal label selector
**Surface:** Card modal → Labels section
**Detail:** Each label shows as a plain `[ ] Bug`-style checkbox without the label color swatch. Color appears correctly on the card chip but not in the picker, making multi-label disambiguation harder.

---

### OBS-2: Activity view defaults to an archived board ✓ RESOLVED
**Surface:** `/workspace/activity`
**Detail:** The board dropdown pre-selects the first board alphabetically, which may be "calendar (Archived)". This shows "No board activity yet" as the cold state. Default should be the most-recently-active non-archived board.
**Resolution:** Fixed in PR #581 — board selector now sorts non-archived first, then by most-recently-updated descending.

---

### OBS-3: Activity view shows no history for boards with real mutations ✓ RESOLVED
**Surface:** `/workspace/activity`
**Detail:** Boards with confirmed mutations (column creates, card adds/moves/edits, label assigns) show "No board activity yet". Either audit events aren't being recorded for these operations, or the board-history fetch has a scoping bug.
**Resolution:** Fixed in PR #581 — audit logging wired for all board/card/column/label mutations via `IHistoryService.LogActionAsync` with `SafeLogAsync` resilience wrapper to prevent audit failures from crashing mutations.

---

### OBS-4: Ops Console accessible via direct URL despite feature flag being off
**Surface:** Settings → Feature Flags → Ops Console (unchecked)
**Detail:** Navigating to `/workspace/ops/cli` directly still loads the Ops Console. The feature flag only removes the sidebar link; it does not gate the route itself. For internal-only surfaces this is a discoverability risk (not a security risk, given JWT auth is enforced separately).

---

### OBS-5: "PRECISION MODE ACTIVE" label — unclear meaning and no tooltip
**Surface:** Sidebar, below Taskdeck logo
**Detail:** Red "PRECISION MODE ACTIVE" text is always visible but has no tooltip, click behavior, or documentation link. A new user has no way to know what this means or how to change it.

---

### OBS-6: Ops Console feature flag discoverability path is confusing
**Surface:** Ops Console → Settings link
**Detail:** The Ops Console's own guidance says to check Settings to enable feature flags, but the round-trip (Ops → Settings → back) is a detour. The Ops Console itself should indicate it's behind a flag and offer a one-click "enable" shortcut or at minimum a direct link.

---

### OBS-7: Board password / registration accepts almost any password
**Surface:** Registration
**Detail:** Registration accepts very weak passwords. For a local-first developer tool this is acceptable in the short term, but should be noted for any external exposure path.

---

### OBS-8: "LIVE" / collaborator presence initializes with a delay
**Surface:** Board view — collaborator strip
**Detail:** Related to BUG-M1 above. The `No active collaborators` → user appears pattern is consistently observed. The first-render collaborator state should initialize from the SignalR join event atomically rather than in two render passes.

---

## Section 6 — What Works Well

The following surfaces were confirmed working correctly in both sessions:

| Surface | Status |
|---|---|
| Home / shell navigation | ✅ Pass |
| Today view / onboarding loop | ✅ Pass |
| Command palette (Ctrl+K) | ✅ Pass |
| Keyboard shortcuts dialog (?) | ✅ Pass |
| Quick Capture (Ctrl+Shift+C) → Inbox auto-nav | ✅ Pass |
| Onboarding dismiss / replay | ✅ Pass |
| Escape stack behavior | ✅ Pass |
| Board creation / rename | ✅ Pass |
| Archive board / restore | ✅ Pass (with 30s freeze bug) |
| Column creation (click) | ✅ Pass |
| Card creation (inline) | ✅ Pass |
| Card modal — all fields | ✅ Pass |
| Edit title/description/blocked/labels | ✅ Pass |
| Label create/assign/color picker | ✅ Pass |
| WIP limit visual feedback | ✅ Pass (policy enforcement gap noted) |
| Card drag (from handle) | ✅ Pass |
| Column reorder (from handle) | ✅ Pass |
| Filter panel (f key, text/status) | ✅ Pass |
| Keyboard board nav (j/k/Enter) | ✅ Pass |
| Inbox detail + triage actions | ✅ Pass |
| Review (empty state) | ✅ Pass |
| Archive view + filter | ✅ Pass |
| Notifications | ✅ Pass |
| Settings / Feature Flags UI | ✅ Pass |
| Ops Console — health.check template | ✅ Pass |
| Ops Console — logs tab | ✅ Pass |

---

## Section 7 — Issue Priority Matrix

| ID | Title | Severity | Area |
|---|---|---|---|
| BUG-C1 | Fresh user sees other users' queue data | **P0** | Data isolation |
| BUG-C2 | Board auto-switches between boards | **P0** | Board navigation |
| BUG-C3 | Board list polling at ~3 req/s | **P1** | Performance |
| BUG-C4 | Inbox capture — no visible entry point | **P1** | Core thesis |
| BUG-C5 | Chat generates text, not proposals/tasks | **P1** | Chat/automation |
| BUG-H1 | Delete Card — no confirmation dialog | **P1** | Data safety |
| BUG-H2 | Edit Card modal — white theme | **P1** | Dark mode |
| BUG-H3 | Edit Column modal — white theme | **P1** | Dark mode |
| BUG-H4 | Filter Cards panel — white theme | **P1** | Dark mode |
| BUG-H5 | Text-selected cards not draggable | **P1** | Interaction |
| BUG-M1 | Collaborators panel flickers | **P2** | Presence/SignalR |
| BUG-M2 | Column name Enter key doesn't submit | **P2** | Form UX |
| BUG-M3 | WIP enforcement warning-only | **P2** | Policy |
| BUG-M4 | Chat markdown rendered as plaintext | **P2** | Chat UX |
| BUG-M5 | Archive action causes 30s freeze | **P2** | Performance |
| BUG-M6 | No toast on Restore Board | **P2** | UX consistency |
| BUG-L1 | Card toast — leading space in name | **P3** | Cosmetic |
| BUG-L2 | "DRAG CARD" text always visible | **P3** | Visual polish |
| OBS-1 | Label color absent in card modal picker | P3 | UX |
| OBS-2 | Activity view defaults to archived board | P3 | UX |
| OBS-3 | Activity view shows no history | P2 | Data / audit |
| OBS-4 | Ops Console feature flag not route-gated | P3 | Feature flag |
| OBS-5 | "PRECISION MODE ACTIVE" unexplained | P3 | Discoverability |

---

## Section 8 — Proposed Fixes Summary

### Immediate (P0 — block external exposure)

**BUG-C1 (queue data isolation):**
Add `WHERE UserId = @currentUserId` (or equivalent claims-derived filter) to the LLM queue list query in `LlmQueueController`. Verify with a cross-user isolation test. This must be fixed before any external user onboarding.

**BUG-C2 (board auto-switching):**
Audit `boardStore` for any watcher/computed that resets `activeBoardId` on list updates. The `fetchBoards` response must not overwrite the currently-selected board ID unless the board was deleted. Add a `preserveSelection` guard in the store's `setBoards` mutation.

### Near-term (P1 — fix before next demo or user test)

**BUG-C3 (excessive polling):**
Find the interval/watcher driving board-list refetches. Either remove it (rely on SignalR) or increase the minimum interval to ≥30s with jitter. Add a network-request-count assertion to the E2E smoke test.

**BUG-C4 (inbox capture entry point):**
Add a prominent "New Capture" button (or compose area) to the Inbox view header. Wire it to the same flow as `Ctrl+Shift+C`. The button should be the first affordance visible above the capture item list.

**BUG-C5 (chat → proposals):**
The chat system prompt must include tool-use / function-call wiring to the proposal service. Alternatively, add a "Create Task" intent parser that routes recognized patterns to the instruction queue. At minimum, the chat UI should explain what it can/cannot do so users aren't confused by prose-only responses.

**BUG-H1 (delete card — no confirmation):**
Add a `TdDialog` confirmation step before the delete API call. Pattern already exists in `TdDialog` primitives.

**BUG-H2/H3/H4 (dark mode theme gaps):**
Audit the card modal, column edit dialog, and filter panel components for missing `bg-td-surface` / `--td-*` token application. Apply the same dark-mode token classes used on the board canvas.

**BUG-H5 (text-selected cards not draggable):**
On `mousedown` on the drag handle, call `window.getSelection()?.removeAllRanges()` before initiating the drag. This clears any active text selection and unblocks the drag interaction.

### Short-term (P2)

**BUG-M1 (collaborator flicker):** Initialize presence from the initial board-fetch response rather than waiting for SignalR `BoardJoined`. Apply the user's own presence immediately on mount.

**BUG-M2 (column name Enter):** Add `@keydown.enter.prevent` to the column name input that calls the submit handler.

**BUG-M3 (WIP enforcement):** Disable the `+ Add Card` affordance (not just warning) when column count ≥ WIP limit.

**BUG-M4 (chat markdown):** Add a markdown-to-HTML renderer (e.g., `marked` or `markdown-it`) to the chat message renderer component.

**BUG-M5 (archive 30s freeze):** ✓ RESOLVED in PR #578. Root cause: sequential reactive mutations in `deleteBoard()` while BoardView was still mounted caused cascading Vue reactive flushes. Fix: navigate away before clearing state, reorder mutations in `boardCrudStore`, add `finally` block for loading state reset.

**BUG-M6 (no restore toast):** Add a success toast in the archive store's `restoreBoard` action, matching the pattern used in `createBoard`.

### Polish (P3)

- BUG-L1: Trim card title before toast interpolation.
- BUG-L2: Hide "DRAG CARD" text on non-hover; show only on `:hover` of the drag handle zone.
- OBS-1: Show label color swatch in card modal label picker.
- ~~OBS-2: Default activity view to most-recently-active non-archived board.~~ ✓ Fixed in PR #581
- ~~OBS-3: Investigate audit event recording — ensure board mutations emit audit entries.~~ ✓ Fixed in PR #581
- OBS-4: Route-guard Ops Console (and other feature-flagged surfaces) so direct URL access is also gated.
- OBS-5: Add a tooltip to "PRECISION MODE ACTIVE" explaining its meaning and how to disable it.

---

## Related Analysis Files

- `docs/analysis/2026-03-29_manual_testing_sonnet.md` — Sonnet structured audit (surface-by-surface pass)
- `docs/analysis/2026-03-27_demo-rehearsal-runtime-issues.md` — Prior demo rehearsal runtime findings
