# Manual Demo Walkthrough Report

**Date:** 2026-03-27
**Tester:** Claude Code (automated Playwright walkthrough)
**Purpose:** End-to-end demo rehearsal as if presenting to Saul
**Backend:** .NET 8 API on localhost:5000 (Mock LLM default, Gemini configured)
**Frontend:** Vue 3 + Vite on localhost:5173
**Demo user:** demo / demo123

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
1. **Home** — show layout, workspace modes, setup loop ✅
2. **Today** — show daily agenda, pending review count ✅
3. **Board** — open Client Onboarding Demo, show columns/cards/labels/comments ✅
4. **Board capture** — use "Capture here" button (NOT Inbox) to show board-scoped capture ✅
5. **Review** — show pre-seeded proposals, approve one, explain review-first trust ✅
6. **Starter Packs** — open Blank Board, show catalog with preview ✅
7. **Notifications** — show mention-based notifications ✅
8. **Keyboard shortcuts** — press `?` to show power-user shortcuts ✅

### Avoid in demo
- ❌ Triage flow (will fail)
- ❌ Chat "ask about board" (LLM has no context)
- ❌ Apply to board (may appear stuck)
- ❌ Inbox page directly (no capture input visible)

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
