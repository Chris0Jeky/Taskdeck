# Frontend Research Map

Last Updated: 2026-04-24
Status: working note derived from source search
Canonical source: `docs/research/PRODUCT_RESEARCH_SOURCE_OF_TRUTH.md`

## Core Loop

The core visible loop is:

`Home -> Inbox/Capture -> Review -> Boards`

Relevant files:

- `frontend/taskdeck-web/src/views/HomeView.vue`
- `frontend/taskdeck-web/src/views/TodayView.vue`
- `frontend/taskdeck-web/src/views/InboxView.vue`
- `frontend/taskdeck-web/src/views/ReviewView.vue`
- `frontend/taskdeck-web/src/views/BoardView.vue`
- `frontend/taskdeck-web/src/components/common/CaptureModal.vue`

## Navigation

- `workspaceStore` defaults to `guided`.
- `ShellTopbar` exposes a workspace-mode select.
- `ShellSidebar` shows Home, Today, Review, Boards, and Inbox in all modes.
- Workbench exposes many secondary surfaces: views, notifications, chat,
  calendar, metrics, integrations, activity, ops, settings, API keys,
  preferences, access, and archive.

## Capture And Inbox

- Capture modal supports typed/paste text and transcript file upload.
- Inbox orchestration is handled by `useInboxOrchestrator`.
- Capture does not show live extraction confidence, duplicate hints, or
  source-span preview while typing.

## Chat

- `useAutomationChat` posts messages through `chatApi.sendMessage` and reloads
  the selected session.
- Backend SSE stream exists, but the current frontend path is not token-streamed
  into the chat UI.

## Review

- Review card components expose approve, reject, execute, diff, and dismiss
  actions.
- Completed proposal dismissal exists.
- Edit-before-approve does not exist.

## Agents

- Agent profile list, run list, and run detail timeline views exist.
- Empty state says profiles are created via API.
