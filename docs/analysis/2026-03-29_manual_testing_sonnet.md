Here's a comprehensive summary of everything I found across all the areas I tested:

---

## Manual Testing Report — Taskdeck Frontend
**Date/Time (UTC):** 2026-03-29 ~14:00  
**Branch/URL:** http://localhost:5173  
**User session:** lalal (role: Editor)  
**DB baseline:** Existing (pre-populated with onboarding board, calendar board)

---

## ✅ What Works Well

### Workspace Shell & Navigation
- **Home** loads with correct state: onboarding loop, "Needs attention" counters, "Next step" suggestions, "Boards" summary. All accurate.
- **Today** view loads correctly with all expected sections: Pending Review, Needs Triage, Overdue Cards, Due Today, Blocked Cards, and onboarding loop. All counters present.
- **Command palette (Ctrl+K):** Opens correctly, arrow key navigation works, Enter would activate, Escape closes without navigating. ✅
- **Keyboard shortcuts dialog (`?`):** Opens with full reference (Global, Board Navigation, Editor), Escape closes it. ✅
- **Sidebar navigation:** All routes (Home, Today, Review, Boards, Inbox, Notifications, Chat, Settings, Preferences) are reachable and highlight correctly.
- **Quick Capture (Ctrl+Shift+C):** Opens instantly, accepts text, saves and auto-navigates to Inbox. ✅
- **Onboarding dismiss/replay:** Dismissing on Today shows "REPLAY SETUP" correctly. State persists. ✅
- **Escape stack on board route:** Escape with no open surface correctly navigates to `/workspace/boards`. Escape with a form/panel open closes only that surface. ✅

### Boards, Columns & Cards
- **Board creation:** Creates inline, navigates to `/workspace/boards/{id}`, success toast shown. ✅
- **Board rename (via Board Settings):** Heading updates immediately. ✅
- **Archive/unarchive board:** Board removed from default list, appears in `/workspace/archive`. "Restore Board" works — board reappears in list. ✅
- **Column creation (via button click):** Works, success toast shown. ✅
- **Card creation (inline):** Works, success toast shown, card count updates. ✅
- **Card modal (click or Enter on selected card):** Opens with all fields — Title, Description, Due Date, Mark as blocked (with contextual block reason textarea), Labels, Comments, metadata. ✅
- **Edit card title/description/blocked state/labels:** All persist correctly and render in the lane. ✅
- **Labels — create/assign:** Label manager opens, color picker works with live preview. Assigning a label shows chip on the card. ✅
- **WIP limit — visual feedback:** Setting a limit shows the `count/limit` indicator in the column header, "WIP limit exceeded" warning appears in red when exceeded. ✅
- **Card drag to another column:** Works from the DRAG CARD handle; persists on refresh. ✅
- **Column drag/reorder:** Works from the column's ⠿ handle; persists on refresh. ✅
- **Filter panel (f key):** Opens with Search, Due Date (dropdown), Status (blocked only checkbox), Labels. Text filtering correctly narrows the visible card set with active filter pills. State persists when re-opening within the session. ✅
- **Keyboard board navigation (j/k):** Card selection highlight works. ✅

### Ops Console
- **`health.check` template:** Runs successfully, output shows "Health check: OK / Status: Healthy", logs visible. ✅
- **Logs tab:** Displays entries with timestamp, level, source, message, and correlation ID. ✅

### Other Surfaces
- **Inbox:** Capture items listed with excerpt; clicking shows full detail with Triage/Ignore/Cancel actions. ✅
- **Review:** Loads with correct empty state and all proposal counters at 0. ✅
- **Archive:** Loads, "Archived Boards" section lists boards correctly, filter and Restore work. ✅
- **Notifications:** Loads correctly. ✅
- **Activity view:** Loads with Board History mode selector and board dropdown. Board/entity selectors functional. ✅
- **Chat (Automation Chat):** Loads, shows LLM configured notice, existing sessions listed, session creation form present. ✅

---

## 🐛 Bugs Found

### **BUG-1: `Delete Card` has no confirmation dialog** *(High severity — data loss)*
Clicking "Delete Card" in the card modal immediately deletes the card without any confirmation step ("Are you sure? This cannot be undone."). There's no destructive-action guard at all.

### **BUG-2: Column name form — Enter key doesn't submit; triggers inline add-card instead** *(Medium severity)*
When creating a column via the top-bar "Add Column" button, the column name text field does NOT respond to Enter to submit. Instead, pressing Enter appears to trigger an interaction with the already-existing column (opening the inline add-card form). The user must click the "Create" button. This is inconsistent with the inline add-card form, which seems to also not always submit on Enter correctly.

### **BUG-3: WIP enforcement is warning-only, not blocking** *(Medium severity / known gap check)*
When a WIP limit is set to 1 on a column that already has 2 cards, the UI shows "WIP limit exceeded" but still allows the "+ Add Card" affordance to open. The system warns but does not block the add action. The checklist expected "operation blocked with visible error feedback." 

Additionally, when clicking "Add" on a new card while WIP is exceeded, the modal that opened was for a *different pre-existing card* (the "First test card (edited)") rather than the new card — this is a likely event-target/focus bug causing the wrong modal to open.

### **BUG-4: Chat response — raw markdown rendered as plaintext** *(Medium severity)*
In the Automation Chat view, the last assistant message ends with `--` and `###` visible as literal text. Markdown is not being rendered in the chat message body, and/or the message was truncated mid-response leaving a raw heading marker visible.

### **BUG-5: Archive board action causes a ~30-second renderer freeze** *(Medium severity)*
Clicking "Move to Archive" in Board Settings caused a ~30-second browser freeze (CDP timeout). The action ultimately succeeded, but the hang is concerning for perceived reliability and could mask actual failures in slower environments.

### **BUG-6: No success toast on "Restore Board" from Archive** *(Low severity — UX)*
Restoring a board from `/workspace/archive` silently removes it from the list without any toast/confirmation message. Other mutating actions (create board, create column, create card, update card) all show toasts. Inconsistency.

---

## ⚠️ Observations / Design Findings

### **OBS-1: Label color not shown in card modal label selector**
In the card edit modal, the Labels section renders each label as a plain `[ ] Bug`-style checkbox, without showing the label color swatch. Since labels are color-coded, the absence of color in the selector makes them harder to identify quickly, especially with many labels. The label manages to show color on the card chip itself (BUG chip in red), but not in the picker.

### **OBS-2: Activity view defaults to an Archived board**
When opening `/workspace/activity`, the board dropdown pre-selects the first board alphabetically — which happens to be "calendar (Archived)". The fetch returns "No board activity yet" for it. This creates a misleading cold state. Defaulting to the most-recently-active non-archived board would be more discoverable.

### **OBS-3: Activity view shows "No board activity yet" for boards with real mutations**
For the "Test Board Alpha (Renamed)" board, which had columns created, cards added/moved/edited, labels assigned, the Activity view still shows "No board activity yet." Either the audit trail backend wasn't recording events for these operations during this session, or the board history fetch has a bug.

### **OBS-4: Ops Console accessible via direct URL despite feature flag being off**
In Settings → Feature Flags, "Ops Console" is unchecked. However, navigating directly to `/workspace/ops/cli` still loads the Ops Console successfully. The feature flag doesn't gate direct URL access — it presumably only removes the link from the sidebar.

### **OBS-5: "PRECISION MODE ACTIVE" label in sidebar — unclear meaning**
The red "PRECISION MODE ACTIVE" text under the Taskdeck logo is visible at all times but has no tooltip, click behavior, or documentation link. It's unclear to a user what this mode is and how to toggle it. A tooltip or link to docs would reduce confusion.

### **OBS-6: Card creation toast has leading space in card name**
The success toast after creating "First test card" reads: `Card " First test card" created successfully` — note the leading space inside the quotes. Minor cosmetic issue suggesting the card title isn't being trimmed before display in the toast.

### **OBS-7: "DRAG CARD" label always visible on cards**
Every card shows `:: DRAG CARD` as a persistent header above the card title. This is quite prominent and occupies vertical space. In a dense board this could feel cluttered. It may be intended for discoverability, but the `::` dotted grid icon alone (which is the actual drag handle) would be cleaner; the text label could be hover-only.

### **OBS-8: Ops Console feature flag is off but the surface is fully operational**
Noted above (OBS-4), but worth flagging separately: the discoverability of Ops Console comes through "Workspace > Settings" link in the role-guidance text on the Ops page itself — but navigating to Settings from that link and then back is a UX detour. The Ops Console's own guidance ("Runnable templates: health.check") and role context are good, but the discoverability path for non-admin users who didn't know the direct URL is unclear.

---

## Coverage Summary

| Area | Status |
|---|---|
| Home / shell navigation | ✅ Pass |
| Today view / onboarding | ✅ Pass |
| Command palette & keyboard shortcuts | ✅ Pass |
| Board creation/rename/archive/restore | ✅ Pass (with freeze bug on archive) |
| Column create/WIP limit/reorder | ✅ Pass (WIP only warns, doesn't block) |
| Card CRUD / modal / labels | ✅ Pass (no delete confirmation) |
| Drag/drop cards & columns + refresh persistence | ✅ Pass |
| Filter panel (f key, text/status) | ✅ Pass |
| Keyboard board nav (j/k/Enter/n/Escape) | ✅ Pass |
| Quick capture → Inbox flow | ✅ Pass |
| Inbox detail + triage actions | ✅ Pass |
| Review (empty state) | ✅ Pass |
| Archive view + filter + restore | ✅ Pass (missing success toast) |
| Activity view (board history) | ⚠️ No activity recorded |
| Automation Chat | ⚠️ Markdown not rendered |
| Ops Console (CLI Runner + Logs) | ✅ Pass |
| Notifications | ✅ Pass |
| Settings / Feature Flags | ✅ Pass (flag gating gap noted) |