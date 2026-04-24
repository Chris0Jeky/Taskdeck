# Taskdeck Frontend UX Map

## Executive Summary

Taskdeck is a Trello + capture inbox hybrid with three workspace modes (guided/workbench/agent). The UX is "guided" at the entrypoint (Home/Inbox/Review/Boards = 5 tabs); workbench explodes to 13 tabs. Intelligence is shallow (no explanations, no streaming, no confidence scores). Friction lives in client-side stitching, N+1 API calls, and polling instead of streaming.

## Key Findings

### 1. Route & Navigation Topology
- Core routes: Home, Today, Boards, Inbox, Review (all modes)
- Workbench adds: Views, Notifications, Chat, Calendar, Metrics, Integrations, Activity, Ops (13 tabs total)
- Agent mode: Agents replaces Boards as primary
- Friction: Workbench is an "everything drawer"; no mode-switching nudges

### 2. Capture Flow
Entry: Cmd+Shift+C, command palette, or Inbox button
Flow: CaptureModal → API → InboxView (split-pane list + detail) → triage action
Triage: Polling every 2s to "ready_for_followup" status, then proposal auto-created
Friction: No triage suggestions, no inline hints, slow polling feedback

### 3. Review Surface
Displays proposal cards with: title (AI summary), impact (AI-generated), status, risk (no explanation), capture link
Controls: Approve, Reject (with risk picker), Execute, Dismiss, Toggle Diff
Expiry: Client-side 60-second clock (59s stale window)
Friction: No AI confidence badges, no "why was this proposed?", no re-fetch on expiry

### 4. Automation Chat
Sessions scoped to board context (no in-session switching)
Parse hints and tool metadata shown inline
Clarification prompts: Just "Skip" button
Friction: NO STREAMING, no inline proposal suggestions, user must explicitly request proposal

### 5. Board/Card Experience
Kanban with drag/drop (columns + cards), keyboard nav (j/k/h/l), realtime presence
Friction: Presence can flicker, no conflict detection, opt-in real-time

### 6. Workspace Modes
Three modes (guided/workbench/agent) persisted in localStorage
Mode enforcement: ShellSidebar filters nav items
Friction: Mode switching not obvious, no "upgrade" nudge, agent mode hidden

### 7. Novel Surfaces
AgentsView (read-only, no UI to create), CalendarView (due-date grouping), MetricsView (board-scoped),
OpsConsoleView (power-user CLI), ActivityView (audit trail)
Friction: Shallow empty states, no guidance

### 8. Intelligent Surfaces
Triage suggestions: Not surfaced
Proposal summaries: AI-generated but no confidence/explanation
Parse hints: Shown in chat
Clarifications: In chat only
MISSING: AI badges, confidence scores, inline recommendations

### 9. Keyboard & Command Palette
Global: Cmd+K (palette), Cmd+Shift+C (capture), ? (help)
Board: j/k/h/l nav, Enter to open, n for new
Power tool feel: Yes, but no per-view shortcut modal, no customization

### 10. Friction Points & Technical Debt
N+1 API calls (list + detail), client-side stitching, polling instead of streaming,
missing defaults (triage suggestion, auto-execute, smart board context),
shallow empty states, mode state duplication (localStorage)

### 11. Opportunities for Intelligence
1. Capture Triage: "AI suggests Triage based on length/structure"
2. Proposal Confidence: "Low-risk because it only affects one column" + "Confidence: 87%"
3. Chat Inline Proposals: Chat suggests proposal after clarification
4. Batch Hints: "These 5 items are about Q2 planning. Triage all together?"
5. Board Defaults: "Suggest columns based on Software board?"
6. Activity Context: "3 cards moved to Done in last hour. Performance on Q2 Sprint?"

## Conclusion

Taskdeck is **guided-by-default, power-user-capable**. The core loop (Home → Inbox → Review → Boards) is clean.
Intelligence is present but **not surfaced** (proposals have AI summaries, but no confidence badges).
Sprawl happens in **workbench mode** (13 tabs), hidden from guided users.
Primary opportunity: **Surface AI confidence and reasoning.**
