# Manual Demo Walkthrough Report

**Date:** 2026-03-27
**Tester:** Claude Code (automated Playwright walkthrough)
**Purpose:** End-to-end demo rehearsal as if presenting to Saul
**Demo user:** demo / demo123

---

## Environment Used

| Property | Value |
|----------|-------|
| **Branch tested** | `fix/db-path-docs-reset-script` (diverged from main at `297e386d`) |
| **Backend** | .NET 8 API on `localhost:5000` |
| **Frontend** | Vue 3 + Vite dev server on `localhost:5173` |
| **LLM provider (runtime)** | Gemini (`gemini-2.5-flash`) was detected by Chat health check — however triage still failed, suggesting Mock was the active provider for queue/proposal processing |
| **Database** | Canonical dev DB (`backend/src/Taskdeck.Api/taskdeck.db`) — NOT a fresh/reset DB |
| **Seed method** | `npm run demo:seed` (no flags — no `--reset`, no `--help`) |
| **Seed state** | DB already contained data from a prior seed run (18:41 timestamps), creating duplicates |
| **Scenario runner** | Not used — only baseline seed |
| **Autopilot** | Not used |
| **Director** | Not used |
| **Playwright mode** | MCP Playwright (manual navigation via tool calls, not stakeholder-demo.spec.ts) |
| **Feature flags** | Default (Guided mode — Activity, Ops, Access, Archive hidden) |

### What was NOT on the tested branch (merged to main after divergence)

The branch I tested from (`fix/db-path-docs-reset-script`) had already been merged to main, but 4 sibling PRs merged around the same time were **not** included in its working tree:

| PR | Title | Branch | Impact on walkthrough |
|----|-------|--------|----------------------|
| **#396** | Make demo:seed starter pack apply idempotent | `fix/seed-starter-pack-idempotency` | **Would have helped**: dry-run check before applying starter packs; re-runs skip cleanly instead of 409 errors. My walkthrough ran on a pre-seeded DB — this would have made the second seed cleaner. |
| **#397** | Fix `--skip-llm` unresolved proposal alias in scenario runner | `fix/skip-llm-proposal-steps` | **No direct impact**: I didn't use the scenario runner. However, this fix means the `client-onboarding` scenario with `--skip-llm` now works — I could have used it as an alternative demo path that avoids triage failures. |
| **#398** | Add `--reset` and `--help` flags to demo:seed | `feat/seed-reset-help-flags` | **Would have helped significantly**: `--reset` deletes all demo boards before seeding, giving a clean slate. My finding #6 (duplicate seeded items) would not have occurred if I had used `npm run demo:seed -- --reset`. |
| **#400** | Add pending ACME capture for demo narrative continuity | `fix/seed-narrative-continuity` | **Would have helped**: adds a third ACME capture (year-end checklist) triaged but left pending, so the Review page shows ACME in both Inbox and Review for narrative continuity. My walkthrough only had Northwind captures pending. |

### Net assessment of branch gap

- **Finding #6 (duplicate seeded items)** is fully addressed by `--reset` flag (#398) and idempotent pack apply (#396)
- **Finding #2 (triage fails)** is NOT addressed — these PRs fix scenario/seed tooling, not the LLM triage pipeline
- **Findings #1, #3, #4, #5, #7–#12** are NOT addressed by any of these PRs
- The `--skip-llm` fix (#397) opens a new demo path via `demo:run -- client-onboarding --skip-llm` that bypasses the triage failure entirely — but only for scenario-driven demos, not ad-hoc UI capture

---

## Executive Summary

The app is **demo-ready for the core loop** (Home -> Inbox/Capture -> Review -> Board). Visual polish, navigation, and information density are strong. Several friction points would hurt a live walkthrough — most notably the capture discoverability, triage failure with Mock provider, and the chat's lack of board context awareness.

---

## What Works Well

### Home (Landing)
- Rich, product-shaped landing with workspace summary, setup loop progress (3/3 steps), and recommended next moves
- Clear "Needs attention" section with pending review count and actionable links
- Recently active boards panel with descriptions
- Three workspace modes (Guided / Workbench / Agent) with smart progressive disclosure — Guided hides Activity, Ops, Access, Archive; Workbench reveals them

### Today (Daily Agenda)
- Well-structured daily view: pending review, needs triage, overdue/due-today/blocked cards
- Onboarding loop with reopen capability
- Recommended next moves section matches Home but from a daily perspective
- Zero counts handled gracefully ("No overdue cards across your boards")

### Board View
- Kanban layout with 5 columns (New Intake, Waiting on Client, Ready for Review, In Progress, Completed)
- Card detail modal is rich: title, description, due date, blocked toggle, labels, threaded comments with @mentions
- Filter panel: search, due date ranges, blocked-only toggle, label checkboxes
- Board action rail: Capture here, Ask assistant, Review proposals, Open Inbox
- Board-scoped capture modal correctly identifies the linked board
- Starter Packs catalog on blank board is polished: 6 packs, search, tags, dry-run preview, one-click apply
- Drag handles visible on cards ("Drag card" label)
- Presence indicator shows "Live" with active collaborator emails

### Review (Proposals)
- Proposal cards show risk level (Low/Medium), affected items, planned changes, source/capture link
- Two-step approve -> apply flow preserves review-first trust
- Status badges (Review required, Approved ready to apply, Applied to board) are clear
- Capture-linked proposals show triage run ID and "Open Capture" link for provenance

### Navigation & UX Polish
- Command palette (Ctrl+K) with all navigation shortcuts
- Keyboard shortcuts modal (?) well-organized into Global, Board Navigation, Editor sections
- Nav badges update correctly (Review count went from 2 to 1 after approval)
- Login/logout cycle is smooth with redirect to Home
- Toast notifications appear for auth state changes
- "System Live" status indicator in topbar

### Notifications
- 7 notifications with proper types (Proposal Outcome, Mention), delivery urgency, deep links
- Per-notification "Mark read" and "Show unread only" filter
- Linked to correct proposals and boards

### Settings & Preferences
- Profile: username, email, user ID, role, Ops access level
- Change password form
- Notification preferences with per-event Immediate/Digest toggle

---

## Problems, Frictions, and Limitations

### CRITICAL for demo

1. **Capture discoverability is poor**
   - The "Capture a note" button on Home just navigates to Inbox — which has **no capture input**
   - The actual Quick Capture modal is only accessible via `Ctrl+Shift+C` (keyboard shortcut) or board-level "Capture here" button
   - For a "near-zero-friction capture" thesis, the primary capture entry point is hidden behind a keyboard shortcut that isn't mentioned anywhere visible on the Inbox page
   - **Impact**: In a live demo, presenter would need to explain "press Ctrl+Shift+C" — breaks the "intuitive" narrative

2. **Triage fails silently with Mock/live provider**
   - Clicking "Start Triage" on a new inbox capture immediately shows "Failed" status
   - No error message, no toast, no explanation of what went wrong
   - Expected with Mock LLM (triage needs real LLM processing), but this is the **core loop break** for a live demo
   - **Impact**: Presenter captures a note -> triages -> it fails -> awkward silence

3. **Chat has no board context awareness**
   - Despite being anchored to "DEMO: Client Onboarding Demo", the LLM responds: "I don't have access to the actual content of your boards"
   - The chat session is board-scoped but the LLM prompt doesn't inject board state
   - **Impact**: Demo of "ask the assistant about your board" falls flat immediately

### MODERATE friction

4. **Apply to board appears stuck**
   - After approving a proposal, clicking "Apply to board" didn't visibly transition the status
   - After manual refresh the status was still "Approved, ready to apply"
   - No toast or error feedback on the apply action
   - May be a timing issue or actual execution failure — either way, unclear to the user

5. **Review badge count stale**
   - Nav badge showed "2" even after approving one proposal; only corrected after page navigation
   - Badge should update reactively after approval action

6. **Duplicate seeded items from repeated demo:seed runs**
   - Running demo:seed twice creates duplicate inbox items and proposals (items from 18:41 and 21:17)
   - The seed script says "reused" for some items but clearly creates new ones too
   - Makes the demo data look messy with repeated entries
   - **Note**: This is addressed on `main` by PR #398 (`--reset` flag) and PR #396 (idempotent pack apply)

7. **Diff view is minimal**
   - "View Diff" shows just `0. create card` — no before/after comparison
   - For a review-first product, the diff is where trust is built — this needs more substance

### MINOR issues

8. **LLM provider confusion**
   - Chat page shows "Gemini (gemini-2.5-flash)" as configured, but demo seed docs say Mock is default
   - The health check warns it "does not prove the upstream provider accepted a live request"
   - Unclear to operator whether LLM is actually working

9. **No inline capture on Inbox page**
   - Inbox is titled "Capture rough notes and turn them into reviewable proposed work" but has zero capture affordance
   - Only shows existing items — the subtitle promise is misleading

10. **Help callouts take significant vertical space**
    - Every page (Home, Today, Inbox, Board) has a "What is this?" expandable callout
    - Good for onboarding, but in a demo they eat ~15% of the viewport each
    - No global "dismiss all guides" toggle

11. **"Advanced" label on Chat and Queue**
    - Chat page header says "Advanced" — this may intimidate Saul or suggest it's not ready
    - Queue link says "Open Queue (Advanced)" — same issue

12. **Card descriptions in New Intake are just title repeats**
    - Cards created by triage (e.g., "Request director ID documents") show the same text as both title and description
    - Feels like placeholder data rather than meaningful work items

---

## Demo Flow Recommendations

### Safest demo path (avoids the broken bits)
1. **Home** — show layout, workspace modes, setup loop
2. **Today** — show daily agenda, pending review count
3. **Board** — open Client Onboarding Demo, show columns/cards/labels/comments
4. **Board capture** — use "Capture here" button (NOT Inbox) to show board-scoped capture
5. **Review** — show pre-seeded proposals, approve one, explain review-first trust
6. **Starter Packs** — open Blank Board, show catalog with preview
7. **Notifications** — show mention-based notifications
8. **Keyboard shortcuts** — press `?` to show power-user shortcuts

### Avoid in demo
- Triage flow (will fail)
- Chat "ask about board" (LLM has no context)
- Apply to board (may appear stuck)
- Inbox page directly (no capture input visible)

---

## Demo Variation Comparison

The walkthrough used the simplest path: `demo:seed` only. Taskdeck ships a rich harness with multiple demo configurations. Here is a comparison of all available variations and what they would have shown differently.

### Seed & Scenario Variations

| Variation | Command | What it seeds | Boards created | Captures | Proposals | LLM required | Cards on board |
|-----------|---------|---------------|----------------|----------|-----------|-------------|----------------|
| **Baseline seed (used)** | `npm run demo:seed` | 2 users, 4 boards (3 active + 1 archived), inbox items, queue samples, chat session, comments, ops logs | Client Onboarding Demo, Content Calendar, Blank Board, Archived Board | 3 captures (ignored, applied, pending) | 2 pre-seeded proposals (applied + pending) | No | ~9 on Client Onboarding (from applied triage) |
| **Baseline seed with `--reset`** | `npm run demo:seed -- --reset` | Same as above but deletes existing demo boards first | Same | Same but no duplicates | Same but no duplicates | No | Same |
| **Scenario: client-onboarding** | `npm run demo:run -- client-onboarding` | 1 capture → triage → proposal → execute (ACME Ltd checklist) | Creates "DEMO: Client Onboarding" with blueprint | 1 (ACME Ltd checklist) | 1 (from triage result) | **Yes** (3 LLM steps: triage, wait-for-proposal, execute) | 7 (from executed proposal) |
| **Scenario: client-onboarding `--skip-llm`** | `npm run demo:run -- client-onboarding --skip-llm` | Board + starter pack only (LLM steps skipped) | Creates board with blueprint | 0 (skipped) | 0 (skipped) | No | 0 (only columns/labels from pack) |
| **Scenario: engineering-sprint** | `npm run demo:run -- engineering-sprint` | 3 cards, 1 blocked, comments, queue instruction | Creates "DEMO: Engineering Sprint" with blueprint | 0 | 0 | No (queue instruction only) | 3 cards + 1 from queue |
| **Scenario: support-triage** | `npm run demo:run -- support-triage` | 3 captures (ignored, applied, pending) | Creates "DEMO: Support Triage" with blueprint | 3 | 3 (from triage) | **Yes** (6 LLM steps) | Cards from applied triage |
| **Scenario: content-calendar** | `npm run demo:run -- content-calendar` | 5+ cards across pipeline columns, due dates, labels | Creates "DEMO: Content Calendar Scenario" with blueprint | 0 | 0 | No (queue instruction only) | 5+ |

### Orchestration Variations

| Variation | Command | What happens | LLM mode | Autopilot | Artifacts produced | Best for |
|-----------|---------|-------------|----------|-----------|-------------------|----------|
| **Director: full Saul rehearsal** | `npm run demo:director -- --scenario client-onboarding --skip-llm --turns 0 --rng-seed saul --output-dir ./demo-artifacts/saul` | Seed → scenario (skip LLM) → no autopilot → Playwright walkthrough → screenshots + logs | No LLM | None | Screenshots (01–10), run-summary.json, trace.ndjson, logs/ | Deterministic rehearsal proof |
| **Director: engineering sprint** | `npm run demo:director -- --scenario engineering-sprint --turns 18 --brain heuristic --loop mixed --rng-seed demo-1` | Seed → scenario → 18 autopilot turns → Playwright walkthrough → full artifacts | No LLM default | 18 turns (heuristic) | Full artifact set | Rich board state demo |
| **Director: CI smoke** | `npm run demo:director:smoke` | Seed → engineering-sprint (skip LLM) → 0 turns → walkthrough → minimal artifacts | No LLM | None | ci-smoke artifacts | CI regression gate |
| **Stakeholder recorder** | `TASKDECK_RUN_DEMO=1 npx playwright test stakeholder-demo.spec.ts --headed` | Seed → optional scenario → optional autopilot → guided Playwright clickthrough with video | Configurable | Configurable | Screenshots, traces, video | Recorded demo for sharing |
| **Autopilot only: heuristic** | `npm run demo:autopilot -- --turns 15 --brain heuristic --rng-seed test-1` | Simulated user actions on existing board (create/move/update cards) | No LLM | 15 turns | None | Populating board state |
| **Autopilot only: LLM-driven** | `npm run demo:autopilot -- --turns 10 --brain taskdeck-chat` | LLM-driven autonomous actions | Gemini/OpenAI required | 10 turns | None | Live LLM demo |

### What my walkthrough would have looked like with each variation

| Variation | Duplicates issue (#6) | Triage failure (#2) | Review content | Board richness | Narrative continuity |
|-----------|----------------------|---------------------|----------------|----------------|---------------------|
| **What I did** (`demo:seed` on stale DB) | Yes — 18:41 + 21:17 duplicates | Yes — immediate failure | 2 pending proposals (Northwind only) | 9 cards (from prior applied triage) | Weak — no ACME in Review |
| **`demo:seed --reset` on main** | No — clean slate | Yes — still fails | 2 pending proposals (ACME + Northwind, per #400) | Same 9 cards | Better — ACME appears in both Inbox and Review |
| **`demo:seed --reset` + `demo:run engineering-sprint`** | No | N/A — no triage steps | Pre-seeded proposals + sprint queue result | Sprint board with 3 cards + queue card | Different narrative — engineering focus |
| **`demo:director --scenario client-onboarding --skip-llm`** | No (fresh DB) | N/A — LLM steps skipped | Only pre-seeded baseline proposals | Board with columns/labels but no triage cards | Board structure visible but empty of triage output |
| **`demo:director --scenario engineering-sprint --turns 18`** | No (fresh DB) | N/A — no triage | Baseline proposals + queue | Sprint board with 3+ cards, autopilot additions | Richest board state |

---

## Commands Used and Actions Taken

### Server startup
| Step | Command / Action | Result |
|------|-----------------|--------|
| 1 | `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj` (background) | Backend started on :5000 |
| 2 | `cd frontend/taskdeck-web && npm run dev` (background) | Frontend started on :5173 |
| 3 | `curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/swagger/index.html` | 200 — backend confirmed up |
| 4 | `curl -s -o /dev/null -w "%{http_code}" http://localhost:5173` | 200 — frontend confirmed up |

### Demo seeding
| Step | Command / Action | Result |
|------|-----------------|--------|
| 5 | `cd frontend/taskdeck-web && npm run demo:seed` | Seeded: 2 users, 4 boards, 3 captures, 2 queue items, 1 chat session, 2 comments, ops logs. Some items reused from prior run, some created fresh — resulting in duplicates. |

### Walkthrough actions (Playwright MCP)
| Step | Page | Action | Tool / Method | Finding |
|------|------|--------|---------------|---------|
| 6 | Home | Navigate to `http://localhost:5173` | `playwright_navigate` | Landed on Home — auto-logged in as demo user |
| 7 | Home | Screenshot + read visible text | `playwright_screenshot` + `playwright_get_visible_text` | Full layout confirmed: nav, workspace modes, setup loop 3/3, boards list, needs attention |
| 8 | Home | Full-page screenshot | `playwright_screenshot(fullPage)` | Complete Home scroll captured |
| 9 | Today | Click nav "Today" link | `playwright_click` | Daily agenda loaded with pending review count, overdue/due/blocked sections |
| 10 | Inbox | Click nav "Inbox" link | `playwright_click` | 6 items listed (duplicates from double seed) |
| 11 | Inbox | Click first inbox item | `playwright_click([data-inbox-index="0"])` | Detail panel showed: captured text, status, actions (Start Triage, Ignore, Cancel) |
| 12 | Home | Click "Capture a note" button | `playwright_click` | Navigated to Inbox — NO capture input appeared |
| 13 | Home | Inspect HTML for capture elements | `playwright_get_visible_html` | Confirmed: no textarea, no capture modal, no input on Inbox page |
| 14 | Any | Press `Ctrl+Shift+C` | `playwright_press_key` | Quick Capture modal opened with textarea |
| 15 | Capture | Fill textarea with demo text | `playwright_fill` | "Follow up with Saul on demo feedback..." entered |
| 16 | Capture | Click "Save Capture" | `playwright_click` | Capture saved, navigated to Inbox, item appeared as "New" |
| 17 | Inbox | Click new capture item → "Start Triage" | `playwright_click` | Status immediately changed to **"Failed"** — no error message, no toast |
| 18 | Review | Click nav "Review" link | `playwright_click` | 6 proposal cards displayed with risk levels, affected items, statuses |
| 19 | Review | Full-page screenshot | `playwright_screenshot(fullPage)` | Rich proposal presentation confirmed |
| 20 | Review | Click "View Diff" on first proposal | `playwright_evaluate` (JS click) | Diff expanded inline: just "0. create card" — very minimal |
| 21 | Review | Click "Approve for board" on pending proposal | `playwright_evaluate` (JS find + click) | Status changed to "Approved, ready to apply" |
| 22 | Review | Click "Apply to board" on approved proposal | `playwright_evaluate` (JS find + click) | Status remained "Approved, ready to apply" — no visible effect, no toast |
| 23 | Review | Click "Refresh Review" | `playwright_evaluate` (JS click) | Counters updated (1 pending, 1 ready), but status still "Approved, ready to apply" |
| 24 | Boards | Click nav "Boards" link | `playwright_click` | 3 boards displayed in grid |
| 25 | Board | Click "DEMO: Client Onboarding Demo" | `playwright_click` | Board loaded: 5 columns, 9 cards in New Intake, labels, filter panel |
| 26 | Board | Click card "Confirm onboarding owner and due date" | `playwright_click` | Card modal: title, description, due date, labels, threaded comments (demo + collab) |
| 27 | Board | Close card modal → Click "Capture here" | `playwright_click` | Board-scoped Quick Capture modal with "linked to DEMO: Client Onboarding Demo" |
| 28 | Board | Close capture modal → Press `Ctrl+K` | `playwright_press_key` | Command palette opened: Go To Home/Today/Review/Boards/Inbox/Notifications/Chat |
| 29 | Board | Check label filter (internal-review) | `playwright_evaluate` (JS checkbox click) | Filter toggled — visual change subtle |
| 30 | Chat | Click nav "Chat" link | `playwright_click` | Automation Chat: Gemini health status, seeded session with board rename conversation |
| 31 | Chat | Fill message "What cards are on this board?" → Send | `playwright_fill` + `playwright_click` | LLM responded: "I don't have access to the actual content of your boards" |
| 32 | Notifications | Click nav "Notifications" | `playwright_click` | 7 unread notifications: proposal outcomes + mention notifications with deep links |
| 33 | Settings | Click nav "Settings" | `playwright_click` | Profile info, password change form, Ops access level |
| 34 | Preferences | Click nav "Preferences" | `playwright_click` | Notification prefs: Mentions/Assignments/Proposal Outcomes with Immediate/Digest toggle |
| 35 | Any | Press `?` key | `playwright_press_key` | Keyboard Shortcuts modal: Global, Board Navigation, Editor sections |
| 36 | Boards | Navigate to Blank Board → Click "Starter Packs" | `playwright_click` | Starter Pack catalog: 6 packs, search, tags, dry-run preview, one-click apply |
| 37 | Auth | Click "Logout" | `playwright_click` | Redirected to login page with "Authentication is required" toast |
| 38 | Auth | Fill demo/demo123 → Click "Sign In" | `playwright_fill` + `playwright_click` | Logged in successfully, redirected to Home |
| 39 | Home | Switch workspace mode to "Workbench" | `playwright_evaluate` (JS select change) | Nav expanded: Activity, Ops, Access, Archive now visible |
| 40 | Home | Switch workspace mode back to "Guided" | `playwright_evaluate` (JS select change) | Nav contracted back to core surfaces |

---

## Screenshots Taken

| # | Name | Description |
|---|------|-------------|
| 01 | landing-page | Home first load |
| 02 | home-full | Full-page Home scroll |
| 03 | today-view | Today daily agenda |
| 04 | inbox | Inbox item list |
| 05 | inbox-item-detail | Capture detail panel |
| 06 | capture-modal | Quick Capture via Ctrl+Shift+C |
| 07-09 | capture flow | Fill and save capture |
| 10-11 | triage | New capture triage -> Failed |
| 12-18 | review | Proposals, diff, approve, apply |
| 19 | boards-list | My Boards grid |
| 20-24 | board-view | Board columns, cards, filter, capture |
| 25-26 | chat | Automation Chat with LLM response |
| 27 | notifications | Notification inbox |
| 28-29 | settings | Profile and preferences |
| 30 | keyboard-help | Shortcuts modal |
| 31-32 | starter-packs | Blank board with pack catalog |
| 33-34 | auth | Logout/login cycle |
| 35 | workbench-mode | Nav with Workbench mode extras |

All screenshots saved to `C:\Users\jekyt\Downloads\` with timestamp suffixes.
