# Manual Test Checklist

Use this checklist to manually validate current Taskdeck behavior on `main`.

Last Updated: 2026-04-04
Companion Active Docs:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/GOLDEN_PRINCIPLES.md`

## Run Metadata Template (Required)

Record this in the issue or PR comment before and after each manual run:
- date/time (UTC)
- commit SHA
- browser and version
- OS
- DB baseline (`fresh` or `existing`)
- env flags changed (if any)
- artifacts collected (screenshots, logs, request IDs)

## Scope and Boundaries

In scope:
- Core board workflow: boards, columns, cards, labels, filters, drag/drop, keyboard flows.
- Workspace shell: navigation, command palette keyboard model, shortcuts help.
- Advanced surfaces: automations (queue/proposals/chat), capture inbox/triage/provenance flow, ops (CLI templates/logs), archive, activity.
- API contract spot checks for auth, access, queue, archive, automation, chat, and ops endpoints.

Out of scope (known implementation boundaries on current `main`):
- Database export/import is intentionally sandbox-gated and returns `403` unless `DevelopmentSandbox.Enabled` is true in Development.
- Database import is file-replacement based; if SQLite file locks are active, import may fail and should be retried during a quiescent period.

## Preconditions

1. Start backend API:
   - `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj`
2. Start frontend:
   - `cd frontend/taskdeck-web`
   - `npm run dev`
3. Open the frontend URL printed by Vite (default `http://localhost:5173`; fallback `http://localhost:4173` or `http://localhost:5001` when `5173` is restricted).
4. Register/login a test user in UI (or use API bootstrap).

Fallback when `localhost:5173` is blocked (`listen EACCES`):
- `npm run dev` now auto-selects a fallback port (`4173`, then `5001`) and starts without manual overrides.
- launcher now chooses a bindable fallback port and skips occupied candidates (including existing Taskdeck listeners) for new Vite processes.
- strict-port startup avoids implicit Vite port auto-increment drift.
- backend Development CORS defaults include `http://localhost:4173` and `http://localhost:5001`, so frontend auth/API requests remain allowed when fallback ports are used.
- If you need an explicit port, run:
  - `cd frontend/taskdeck-web`
  - `npm run dev -- --host localhost --port 5001`
- Troubleshooting note: some Windows local environments reserve or restrict `localhost:5173` for user-space listeners, which surfaces as `listen EACCES`.

Optional clean start:
- Stop API process.
- Remove `backend/src/Taskdeck.Api/taskdeck.db`.
- Restart API.

## C0. Container Deployment Hardening Matrix (`#142`)

Goal:
- validate deployment hardening behavior beyond happy-path startup for the compose baseline.

Automated path:
1. From repo root, run:
   - `powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080`
2. Expected:
   - secret-gated compose rendering fails when `TASKDECK_JWT_SECRET` is missing
   - reverse-proxy security headers are present on `/`
   - unauthorized proxy paths remain deterministic (`/api/boards` and `/hubs/boards/negotiate` return `401`)
   - start/restart/stop flow succeeds and leaves no running baseline services

Manual-only checks (non-automatable in generic local script):
1. Backend direct exposure posture:
   - verify deployment path does not expose the backend container directly to public ingress while forwarded-header trust is enabled.
2. Edge TLS termination posture:
   - for non-local environments, confirm HTTPS terminates at edge/proxy and only private-network HTTP reaches the compose stack.
3. Host restart operational check (staging/ops rehearsal):
   - restart container host/daemon per environment runbook, then verify stack recovers with expected service health.

## A. Authentication and Workspace Shell

1. Register new user from `/register`.
   - Expected: redirected/authenticated into workspace.
   - **Data isolation check (fresh registration):** After registering, navigate to `/workspace/automation/queue`. Expected: 0 items. Queue scoping failure (`#508`) has been resolved; verify regression.
2. Login with valid credentials from `/login`.
   - Expected: routed to `/workspace/home`.
3. Attempt workspace route while logged out.
   - Expected: redirected to `/login` with redirect query.
4. Navigate directly to `/workspace/archive`, `/workspace/activity`, `/workspace/ops/cli`, and `/workspace/settings/access`.
   - Expected: each route loads its intended view — no silent redirect to Home.
   - Bug fixed (`#681`/`#691`): feature flags for shipped surfaces now default to `true`.
5. Open `/workspace/today`.
   - Expected: agenda shows review/triage/overdue/due-today/blocked summary cards and the onboarding loop block.
5. Start `Start Useful Board` from `Home` or `Today`, pick a starter shape, and create a board.
   - Expected: board opens immediately; when a starter pack applies successfully, the starter workflow is present, and when it fails, the board still opens with a warning.
6. Dismiss onboarding from `Home` or `Today`, refresh, then replay it.
   - Expected: dismiss/replay state persists and the guided setup path is recoverable without trapping experienced users.
7. Open command palette via `Ctrl+K`/`Cmd+K`, use arrow keys to select an item, and press `Enter`.
   - Expected: command activates the selected item and closes the palette; `Escape` closes palette without navigation.
8. Open shortcuts help via `?` and close with `Escape`.
   - Expected: dialog toggles correctly.
9. Logout from top bar.
   - Expected: token/session cleared, redirected to `/login`.

**Auth-flow toast regression (PR #742):**
- Attempt login with wrong password.
  - Expected: error toast appears with the server-provided reason (e.g. "Invalid credentials").
- Attempt registration with a duplicate email.
  - Expected: error toast appears with guidance about the duplicate account.
- Login successfully after a failed attempt.
  - Expected: error toast from the failed attempt does not persist; success toast "Logged in successfully" appears.
- Sign in with GitHub OAuth (if configured).
  - Expected: success toast "Signed in with GitHub" appears; error toast appears on OAuth failure.

## B. Boards, Columns, Cards, Labels

1. Create board from workspace boards page.
   - Expected: route becomes `/workspace/boards/{id}` and heading matches board name.
2. Rename board in Board Settings.
   - Expected: heading updates and board remains accessible.
3. Archive and unarchive board via Board Settings.
   - Expected: archived board hidden from default boards list; unarchived board reappears.
4. Use `Archive Board` action in Board Settings.
   - Expected: redirected to `/workspace/boards`, archived board absent from default list (soft-delete behavior).
5. Use the board action rail on a populated board.
   - Expected:
     - `Capture here` opens a capture modal scoped to the current board.
     - `Ask assistant` opens `/workspace/automations/chat?boardId={boardId}`.
     - `Review proposals` opens `/workspace/review?boardId={boardId}`.
     - `Add card` opens the inline add-card affordance for the active column, or the add-column form when the board is empty.

6. Create two columns, then reorder columns by drag/drop using the `Drag Column` handle.
   - Expected: visual order changes and persists on refresh.
7. Set WIP limit on a column.
   - Expected: `count/limit` indicator visible.
8. Attempt to exceed WIP by adding/moving cards.
   - Expected: operation blocked with visible error feedback.
   - WIP limit enforcement bug (`#517`) has been resolved; verify regression.

**WIP-limit toast deduplication regression (PR #745):**
- Set a WIP limit of 1 on a column, add a card, then try to add a second card.
  - Expected: exactly ONE error toast appears. No duplicate toasts.
- Try to move a card into the same WIP-limit-reached column.
  - Expected: exactly ONE error toast. No duplicate toasts.

9. Create card inline.
   - Expected: card appears in target column.
10. Open card modal (`Enter` on selected card or click).
   - Expected: modal opens with current values.

**Manual card provenance empty state (PR #754):**
- Open a card that was created manually (not via capture/inbox).
  - Expected: card detail shows "No capture provenance available." in the provenance area. No error shown. No blank/broken provenance section.
- Open a card created via the capture/inbox flow.
  - Expected: card detail shows full capture provenance (source, timestamp, original capture text). The "No capture provenance available." message does NOT appear for captured cards.
- For captured cards, verify the provenance empty state does not flash during the initial load of the captured card's modal.
  - Expected: empty state is only shown after load completes and provenance is confirmed absent.

11. Edit title/description, set due date, block with reason, assign labels.
    - Expected: updates persist and render in lane.
12. Move card to another column via drag/drop using the `Drag Card` handle.
    - Expected: card relocates and counts update.
13. Delete card from modal.
    - Expected: confirmation dialog shown first ("Delete this card? This cannot be undone."), then card removed on confirm.
    - Card deletion confirmation bug (`#513`) has been resolved; verify regression.

14. Open label manager and perform create/update/delete.
    - Expected: label list and card chips reflect changes.
    - Expected: label manager modal uses dark workspace theme (design tokens) — no jarring light-theme styling.
    - Bug fixed (`#684`/`#692`): modal migrated from hardcoded light-theme classes to design-token-driven dark theme.

**Board header presence label format (PR #744):**
- Open a board with at least one other presence member (or open the same board in two browser tabs with the same user).
  - Expected: the current user's presence indicator shows their **username** (e.g. "alice"), NOT their email (e.g. "alice@example.com").
  - Expected: when you open a card for editing, the presence label stays as username — it does not switch to email.
  - Expected: presence indicators for OTHER users show whatever name the server provides (unaffected by the fix).

## C. Filters and Keyboard Workflow

1. Toggle filter panel with `f`.
   - Expected: panel opens/closes.
2. Apply text, due-date, blocked, and label filters.
   - Expected: visible card set matches filter logic.
3. Close and reopen filter panel in-session.
   - Expected: filter state persists during session.

4. Board keyboard navigation:
   - `h/l` or arrows for column movement, `j/k` for card selection.
   - `Enter` opens selected card.
   - `n` opens inline new card form.
5. Escape-close behavior:
   - Expected contract:
     - Escape closes only the top-most transient surface (dialog/panel/form) per key press.
     - When no transient surface is open on a board route, Escape navigates to `/workspace/boards`.
     - Escape inside input-assist closes the suggestion panel without cascading to unrelated board/workspace actions.

## D. Automations, Chat, and Proposals

1. Open `/workspace/automations/chat` and check the LLM health banner.
   - Expected: banner shows three distinct states — amber "configured" (before verification), green "verified" (after successful probe), or red "failed" (after failed probe). Initial state should NOT appear as healthy/green without verification.
   - Enhancement (`#679`/`#693`): health banner now distinguishes configured vs verified vs failed provider states.
2. Create session.
   - Expected: session appears and can be selected.
2. Send non-actionable message.
   - Expected: assistant response appears.
3. Create board-scoped chat session and send actionable instruction with `Request proposal generation` enabled.
   - Expected: assistant response includes proposal reference.
4. Send a tool-calling question: "What columns does my board have?" or "What cards are in <column>?"
   - Expected: intermediate "Looking up..." status messages appear via SignalR, then a response with actual board data.
5. Send a multi-instruction message: "Add a column called Testing and create a card called Unit Tests".
   - Expected: multiple proposals generated from a single message.
6. Open `/workspace/review` and locate proposal.
   - Expected: review cards render with sticky action footer, constrained card height, collapsible detail sections with risk color-coding, and keyboard-accessible links dropdown. Legacy `/workspace/automations/proposals` links redirect here.
7. Open proposal, inbox, notification, and card provenance links that target the same board-scoped proposal.
   - Expected: all land on `/workspace/review?boardId={boardId}#proposal-{proposalId}` or the equivalent routed location with board context preserved.
8. Approve proposal.
   - Expected: status transitions to `Approved`. Approve→apply cue is visible.
9. Execute proposal with confirmation (two-step: approve first, then execute as separate action).
   - Expected: status transitions to `Applied`.
10. View diff for proposal.
    - Expected: diff shows human-readable operation descriptions (e.g., `Create card "Fix login bug" in column "To Do"`) instead of raw GUIDs.
    - Expected: diff panel has "Operation details" heading and proper word-wrapping.
    - Bug fixed (`#682`/`#697`): raw GUID targets replaced with card titles and column names; falls back to raw GUID when resolution fails.
11. Verify applied proposals are hidden by default; use clear/dismiss action to manage them.
12. Verify expired proposal handling in Review:
    - Expected: expired proposals show distinct "Expired" status badge — not "Approved, ready to apply".
    - Expected: expired proposals have a Dismiss button and explanatory notice.
    - Expected: dismissing an expired proposal removes it from the review list.
    - Expected: Apply/Approve buttons are not shown for expired proposals.
    - Expected: proposals that expire while the page is open transition to expired state reactively (60-second clock).
    - Bug fixed (`#678`+`#690`/`#696`): expired proposals no longer appear actionable; dismiss action now available.

## D2. Router Auth Guard and Workspace State (PR #748)

1. Workspace routes require authentication.
   - Navigate to `/workspace/boards` while logged out.
   - Expected: redirected to `/login?redirect=%2Fworkspace%2Fboards`.
   - Log in. Expected: redirected back to `/workspace/boards`.

2. Expired token cleanup.
   - Manually set an expired JWT in localStorage (`taskdeck_token` key with an `exp` in the past), then navigate to any `/workspace/` route.
   - Expected: token is cleared from localStorage, user redirected to `/login`.

3. Workspace mode persistence across navigation.
   - Switch workspace mode (if applicable) on Home, then navigate to Inbox, then back.
   - Expected: workspace mode is unchanged after navigation within the workspace.

4. Logout clears workspace state.
   - Log in, navigate into a board, then logout from the top bar.
   - Expected: after logging back in, workspace state is fresh (no stale board context from previous session).

## E. Inbox and Notifications Continuity

1. Open `/workspace/inbox?boardId={boardId}` after creating a board-scoped capture.
   - Expected: inbox header shows the board context banner and list fetch stays scoped to that board.
2. Open a capture detail with proposal provenance from the board-scoped inbox.
   - Expected: `Open Proposal` keeps the same `boardId` query when routing into Review.
3. Open `/workspace/notifications?boardId={boardId}`.
   - Expected: notifications header shows the board context banner and refresh/unread filtering stays scoped to that board.
4. Open a proposal notification and a board-only notification.
   - Expected: proposal notifications route to board-scoped Review, while board-only items route back to the related board.

## F. Ops Console and Logs

1. Open `/workspace/ops/cli`.
   - Expected: templates load.
2. Run `health.check` template.
   - Expected: successful run and output preview containing health text.
3. Switch to logs tab and query logs.
   - Expected: entries returned for broad query.
4. Fetch logs by correlation ID for a recent run.
   - Expected: run-correlated entries returned.

## G. Archive and Recovery

1. Open `/workspace/archive` and refresh.
   - Expected: items load without error.
2. Filter by entity type.
   - Expected: list narrows correctly.
3. Restore an available archived item.
   - Expected: restore succeeds, item removed from list, success toast.
4. Validate board archive/unarchive coherence against archive view.
   - Expected: archived boards are visible in `/workspace/archive` and can be restored there; restored boards return to default boards list.

## H. GitHub OAuth Login (CLD-03, `#539`)

Prerequisite: GitHub OAuth must be configured (`GitHubOAuth:ClientId` and `GitHubOAuth:ClientSecret` in backend config). When not configured, the OAuth button should not appear.

1. Navigate to `/login` with OAuth configured.
   - Expected: "Sign in with GitHub" button visible alongside username/password form.
2. Navigate to `/login` without OAuth configured (env vars absent).
   - Expected: only username/password form visible; no GitHub button.
3. Click "Sign in with GitHub".
   - Expected: redirected to GitHub authorization page.
4. Complete GitHub authorization (with a test GitHub account).
   - Expected: redirected back to app with `oauth_code` query param, code exchanged, authenticated into workspace.
5. Log out and log back in with the same GitHub account.
   - Expected: existing linked account recognized, session restored.
6. Verify `GET /api/auth/providers` without auth token.
   - Expected: 200 with provider discovery payload indicating GitHub availability.

## I. GDPR Data Portability and Account Deletion (SEC-08, `#83`)

1. Log in as a user with boards, cards, captures, chat sessions, notifications, and audit history.
2. Call `GET /api/account/export` with bearer token.
   - Expected: JSON response with versioned payload (`v1.0`) containing all user-scoped data: boards, notifications, captures, proposals, chat sessions, audit trail, preferences.
3. Verify the export contains only the requesting user's data (no cross-user leakage).
4. Call `POST /api/account/delete` with wrong password.
   - Expected: 401 or 400 — deletion rejected.
5. Call `POST /api/account/delete` with correct password but wrong confirmation phrase.
   - Expected: 400 — deletion rejected; error indicates exact phrase required.
6. Call `POST /api/account/delete` with correct password and `"DELETE MY ACCOUNT"` confirmation.
   - Expected: account deactivated, PII anonymized, audit references cleaned.
7. Attempt to log in with the deleted account credentials.
   - Expected: login rejected — account is deactivated.
8. Attempt to use the old JWT token (saved before deletion) on any authenticated endpoint.
   - Expected: `401` with `ApiErrorResponse` — token is invalidated even though it hasn't expired.
   - Enhancement (`#671`/`#698`+`#728`): `TokenValidationMiddleware` checks `IsActive` and compares token `iat` against `TokenInvalidatedAt` on every authenticated request; `ActiveUserValidationMiddleware` provides runtime active-user enforcement with 30-second in-memory cache invalidated on deletion/deactivation; ADR-0021 documents the design decision.
9. Verify JWT invalidation latency after account deletion.
   - Expected: within 30 seconds of account deletion, any request using the old JWT returns `401` (cache TTL is 30 seconds).
10. Verify audit trail contains `DataExported`, `AccountDeletionRequested`, `AccountAnonymized` actions.

## J. Board Metrics Dashboard (ANL-01, `#77`)

1. Open `/workspace/metrics` from sidebar navigation.
   - Expected: metrics dashboard renders with board selector.
2. Select a board with cards in multiple columns.
   - Expected: metric charts/summaries render for the selected board.
3. Adjust date range filter (e.g., last 7 days, last 30 days).
   - Expected: metrics update to reflect the chosen period.
4. Filter by label.
   - Expected: metrics scoped to cards with the selected label.
5. Switch to a different board.
   - Expected: metrics reload for the new board context.
6. Verify `GET /api/metrics/boards/{boardId}?from=&to=&labelId=` with bearer token.
   - Expected: 200 with structured metrics payload.
7. Verify unauthenticated access to metrics endpoint.
   - Expected: 401.
8. Verify metrics correctness on a board with many cards (10+ cards across 3+ columns).
   - Expected: WIP counts per column match actual card counts; blocked count matches blocked cards; throughput and cycle time reflect audit log data.
   - Enhancement (`#675`/`#724`): metrics now use SQL-level filtering via dedicated repository methods instead of in-memory filtering; results should be identical but more scalable.

## K. MCP Server Validation (MCP-01/MCP-02, `#652`/`#653`)

Prerequisite: MCP stdio mode requires starting the API with `--mcp` flag.

1. Start API with `--mcp` flag: `dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj -- --mcp`
   - Expected: process starts in MCP stdio host mode (no HTTP listener).
2. Configure `mcp.example.json` in Claude Code or Cursor as the MCP client config.
3. From the MCP client, request `taskdeck://boards` resource.
   - Expected: JSON listing of boards with id, name, columnCount, cardCount, isArchived, updatedAt fields.
4. Verify board listing matches the user's boards (scoped by `StdioUserContextProvider` identity).
5. Verify archived boards are included with `isArchived: true`.
6. Request `taskdeck://boards/{boardId}` resource for an owned board.
   - Expected: board detail including columns and metadata.
7. Request `taskdeck://boards/{boardId}/cards` resource.
   - Expected: card listing for the board.
8. Request `taskdeck://captures` resource.
   - Expected: captures listing scoped to the authenticated user.
9. Request `taskdeck://proposals` resource.
   - Expected: proposals listing scoped to the authenticated user.
10. Use the `search_cards` tool with a keyword.
    - Expected: matching cards returned from user's boards.
11. Use the `create_card` tool with board, column, and title.
    - Expected: a **proposal ID** is returned (not a card directly). Write tools must produce proposals per GP-06.
12. Use the `move_card` tool to move a card.
    - Expected: proposal ID returned for the move operation.
13. Use the `dismiss_proposal` tool on an owned proposal.
    - Expected: proposal dismissed successfully.
14. Attempt to access another user's proposal via `get_proposal_status` tool.
    - Expected: access denied or empty result — user-scoped enforcement.
    - Enhancement (`#653`/`#739`): user-scoping checks added on `GetProposalDetail`, `GetProposalStatus`, and `DismissProposal` after adversarial review.

## L. LLM Tool-Calling Chat (LLM-06 Phase 1, `#649`)

1. Open `/workspace/automations/chat` and create a board-scoped session.
2. Send: "What columns does my board have?"
   - Expected: response includes column names from the board context, driven by `list_board_columns` tool.
3. Send: "What cards are in Backlog?" (or substitute actual column name).
   - Expected: response includes card titles from the named column via `list_cards_in_column` tool.
4. Send: "Show me details for card <card-title>".
   - Expected: response includes card details (title, description, labels, due date) via `get_card_details` tool.
5. Send: "Search for cards about authentication".
   - Expected: response includes matching cards via `search_cards` tool.
6. Send: "What labels are on this board?"
   - Expected: response includes label names and colors via `get_board_labels` tool.
7. Verify intermediate tool status messages appear (e.g., "Looking up cards...") via SignalR `ToolStatusEvent`.
8. Verify read-to-write card ID continuity: list cards in a column, then ask to move one by the short ID surfaced in the response.
   - Expected: proposal created successfully using the 8-char hex prefix — no "Invalid card ID" error.
   - Bug fixed (`#677`/`#695`): `CardIdPrefixResolver` resolves short IDs to full GUIDs via board-scoped prefix matching.
9. Verify multi-turn tool calling: ask a question that requires 2+ tool calls in sequence.
   - Expected: orchestrator completes within 5 rounds and 60 seconds.
   - Expected: repeated identical tool calls (infinite loop) are detected and aborted early with a clear message.
   - Enhancement (`#674`/`#694`): SHA256-based loop detection with error-retry bypass.
10. With Mock provider: verify deterministic pattern-based dispatch produces predictable responses.
11. Send a write instruction: "Create a card called 'Fix login bug' in the To Do column".
    - Expected: assistant invokes `propose_create` tool; response references a created proposal; tool-status indicator shows "Creating proposal..." via SignalR.
    - Enhancement (`#650`/`#731`): 6 write tool executors produce proposals per GP-06.
12. Send: "Move card 'Fix login bug' to Done".
    - Expected: `propose_move` tool invoked; proposal created for card movement.
13. Send: "Archive card 'Fix login bug'".
    - Expected: `propose_archive` tool invoked; proposal created for archival.
14. Send a multi-action instruction: "Add a column called Testing and create a card called Unit Tests in it".
    - Expected: multiple proposals generated from a single message via write tools.
15. Navigate to Review after a write tool instruction and verify the proposal is present.
    - Expected: proposal card shows the operation from the chat instruction; apply flow works normally.
16. Verify non-tool chat messages feel responsive (no perceptible double-LLM-call latency).
    - Expected: simple conversational messages return without tool-calling overhead.
    - Enhancement (`#672`/`#727`): `ChatService` reuses orchestrator text when no tools called, halving latency.

## L2. ChangePassword Security Fix Validation (SEC-20, `#722`)

1. Log in as User A. Call `POST /api/auth/change-password` with `{ "currentPassword": "...", "newPassword": "..." }` (no `userId` in body).
   - Expected: password changed successfully for the calling user.
2. Verify old password no longer works for login.
3. Verify new password works for login.
4. Call `POST /api/auth/change-password` without bearer token.
   - Expected: `401`.
5. Call `POST /api/auth/change-password` with body containing `"userId": "<other-user-id>"` alongside valid passwords.
   - Expected: the `userId` field is **ignored** — only the caller's own password is changed, never another user's.
   - Bug fixed (`#722`/`#732`): endpoint previously accepted `UserId` from body, allowing any authenticated user to change another user's password.

## L3. OAuth/Auth Edge Case Regression (TST-40, `#707`)

1. Attempt login with blank username/password.
   - Expected: deterministic validation error, not 500.
2. Attempt login with inactive/deleted account.
   - Expected: clear rejection message distinguishable from wrong-password.
3. Register with duplicate email.
   - Expected: conflict error with actionable guidance.
4. Use an expired JWT on any authenticated endpoint.
   - Expected: `401` with `ApiErrorResponse`.
5. Use a JWT with `iat` before `TokenInvalidatedAt` (after account deletion).
   - Expected: `401` — token invalidated even though not expired.
   - Note: `ExternalLoginAsync` username fallback Substring overflow bug was found and fixed in this wave (`#737`).

## M. Backup and Restore DR Drill (OPS-08, `#86`)

Prerequisite: scripts are in `scripts/backup.sh`, `scripts/restore.sh` (Unix) and `scripts/backup.ps1`, `scripts/restore.ps1` (Windows).

1. Run backup script on a live database with existing data.
   - Expected: timestamped backup file created, `PRAGMA integrity_check` passes, file permissions restricted (chmod 600 / restricted ACL).
2. Verify backup includes WAL file when database is in WAL mode.
3. Create 9 backups to test retention.
   - Expected: only 7 most recent backups retained; oldest 2 cleaned up.
4. Run restore script against a backup file.
   - Expected: interactive confirmation prompt appears (unless `--yes` flag used), magic-bytes check passes, integrity check passes, safety copy of live DB created before overwrite.
5. Run restore with `--yes` flag.
   - Expected: confirmation prompt skipped.
6. Run PowerShell equivalents on Windows and verify matching behavior.
7. Execute the `backup-restore-drill` rehearsal scenario per `docs/ops/rehearsal-scenarios/backup-restore-drill.md`.

## N. Review Card UX (UX-19, `#613`)

1. Open `/workspace/review` with proposals present.
   - Expected: proposal cards render with sticky action footer pinned at bottom.
2. Verify card height is constrained — long proposal details do not produce unbounded card height.
3. Verify action buttons (Approve, Reject) are visible without scrolling within the card.
4. Click a proposal to expand collapsible detail sections.
   - Expected: risk color-coding visible on detail sections.
5. Verify the approve→apply two-step flow: approve first, then execute as a separate action.
6. Verify keyboard-accessible links dropdown on proposal cards.

## O. Activity View

1. Open `/workspace/activity`.
   - Expected: view loads and allows mode selection (`board`, `entity`, `user`).
2. Fetch board history using the board selector.
   - Expected: timeline entries include board-level mutations *and* card/column/label activity within that board.
3. Fetch entity history using entity type + board context + entity selectors.
   - Expected: timeline entries display without manual raw ID entry.
4. Fetch user history in `user` mode.
   - Expected: current-user timeline entries display.

## P. API Spot Checks

Assume API at `http://localhost:5000`.

1. Register/login:
   - `POST /api/auth/register`
   - `POST /api/auth/login`
   - Expected: token and user payload.
2. Chat unauthorized check:
   - `GET /api/llm/chat/sessions` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
3. Ops unauthorized check:
   - `GET /api/ops/cli/templates` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
4. Archive unauthorized check:
   - `GET /api/archive/items` without bearer token.
   - Expected: `401` with JSON body containing `errorCode` and `message`.
5. Queue status validation:
   - `GET /api/llm-queue/status/not-a-real-status`.
   - Expected: `400` with validation-style `errorCode` and `message`.
6. Automation execute without `Idempotency-Key`:
   - `POST /api/automation/proposals/{id}/execute` without header.
   - Expected: `400` with validation-style `errorCode` and `message`.
7. Database export/import sandbox gate:
   - `GET /api/export/database` and `POST /api/import/database` with bearer token while sandbox is disabled.
   - Expected: `403` with JSON body containing `errorCode` and `message`.
8. GDPR data export:
   - `GET /api/account/export` with bearer token.
   - Expected: `200` with versioned JSON payload containing user-scoped data.
9. GDPR account deletion unauthorized:
   - `POST /api/account/delete` with wrong password.
   - Expected: `401` or `400` with JSON body containing `errorCode` and `message`.
10. Board metrics unauthorized:
    - `GET /api/metrics/boards/{boardId}` without bearer token.
    - Expected: `401` with JSON body containing `errorCode` and `message`.
11. Auth provider discovery:
    - `GET /api/auth/providers`.
    - Expected: `200` with provider availability payload (no auth required).

## P2. Post-Wave-2 Automated Test Verification (TST-34 to TST-52, `#740`–`#755`)

These areas now have extensive automated test coverage (~586 new tests). Manual checks below confirm the automated coverage corresponds to real runtime behavior.

### P2.1. SignalR Presence Lifecycle (covered by `#706`/`#751`)

1. Open the same board in two browser tabs (same user).
   - Expected: presence shows 1 user, not 2 duplicate entries.
2. Close one tab.
   - Expected: user still appears in presence (other tab still connected).
3. Close both tabs, then reopen the board.
   - Expected: presence resets cleanly on reconnect.
4. Open the same board as two different users.
   - Expected: both users visible in presence. Actions in one tab trigger realtime updates in the other.
5. Set editing focus on a card, then navigate away.
   - Expected: editing indicator clears for that user.

### P2.2. Notification Delivery and Deduplication (covered by `#719`/`#746`)

1. Create a card comment mentioning another user.
   - Expected: mentioned user receives a Mention notification.
2. Repeat the same mention in a new comment.
   - Expected: second notification is created (deduplication is by key, not by content).
3. Open notifications, mark all read, then check count.
   - Expected: unread count drops to 0; mark-all-read is scoped to the current board context if board filter is active.
4. Verify notifications do not leak across users: log in as User B and check for User A's notifications.
   - Expected: none visible.

### P2.3. Export/Import Round-Trip (covered by `#713`/`#752`)

1. Create a board with columns, cards (including special characters: emoji, unicode, HTML entities), labels, and WIP limits.
2. Export as JSON via board settings.
3. Create a new board and import the exported JSON.
   - Expected: all data preserved — card titles, descriptions, labels, column order, WIP limits.
4. Export the same board as CSV.
   - Expected: CSV is well-formed; fields with commas and quotes are properly escaped.

### P2.4. Board Metrics Accuracy (covered by `#718`/`#749`)

1. On a board with cards that have been moved to a "Done" column:
   - Open `/workspace/metrics`, select the board.
   - Expected: throughput count matches the number of cards moved to Done in the selected date range.
2. Verify WIP counts match actual card counts per column (count manually).
3. Block a card with a reason, then check metrics.
   - Expected: blocked card count includes the newly blocked card.

### P2.5. Archive Conflict Detection (covered by `#715`/`#755`)

1. Archive a board, then rename a column on another board to match a column name from the archived board.
2. Attempt to restore the archived board.
   - Expected: if column name conflicts exist, the conflict detection strategy applies (Rename appends suffix, or Fail returns 409 depending on configuration).

### P2.6. API Error Contract Consistency (covered by `#714`/`#753`)

1. Send `POST /api/boards` with empty `{ "name": "" }`.
   - Expected: 400 with `{ "errorCode": "...", "message": "..." }` shape (GP-03 contract).
2. Send `GET /api/boards/{non-existent-guid}` with valid auth.
   - Expected: 404 with GP-03 error contract.
3. Send `POST /api/boards` with malformed JSON body.
   - Expected: 400 (may be ProblemDetails from ASP.NET middleware, not GP-03 — this is documented behavior).

## P3. Tech-Debt, Security, and Feature Hardening Wave (`#765`–`#770`, `#776`)

These 7 PRs resolve tech-debt, security, and feature gaps with two rounds of adversarial review each. ~65 new tests (32 backend + 33 frontend).

### P3.1. Agent API Fix (covered by `#758`/`#776`)

1. `GET /api/agents` with valid bearer token.
   - Expected: `200` with JSON array of agent profiles (previously returned 500 due to `DateTimeOffset` ORDER BY in SQLite).
2. `GET /api/agents/{id}/runs?limit=5` with valid bearer token.
   - Expected: `200` with JSON array limited to 5 entries, ordered by CreatedAt descending.
3. `GET /api/agents` without bearer token.
   - Expected: `401`.

### P3.2. DataExport Exception Logging (covered by `#759`/`#766`)

1. Trigger a data export (`GET /api/account/export`) with valid bearer token.
   - Expected: `200` with versioned JSON payload; no error-level log entries for `DataExportService` on success.
2. If backend logs are observable: verify that `OperationCanceledException` during export does NOT produce an `Error`-level log entry (only genuine failures should log at Error).

### P3.3. Streaming Chat Token Usage (covered by `#763`/`#768`)

1. Open `/workspace/automations/chat`. Create a board-scoped session. Send a message.
   - Expected: response streams in real-time as before.
2. After streaming completes, refresh the page.
   - Expected: the assistant response is visible in chat history (previously, streamed responses were not persisted as `ChatMessage` records).

### P3.4. EF Core Version Alignment (covered by `#760`/`#767`)

1. `dotnet build backend/Taskdeck.sln -c Release` — verify 0 errors.
2. `dotnet test backend/Taskdeck.sln -c Release -m:1` — verify all tests pass.
3. Visit `http://localhost:5000/swagger` — verify Swagger UI loads correctly.

### P3.5. Tool Argument Replay (covered by `#673`/`#770`)

1. Open `/workspace/automations/chat`. Create a board-scoped session. Ask: "What cards are in Backlog?" (or first column name).
   - Expected: coherent multi-turn response using tool calls.
2. Follow up with: "Tell me more about the first card." then another follow-up.
   - Expected: multi-turn tool-calling maintains context across rounds. Original tool arguments are preserved in provider replay messages.

## Q. Observability Smoke (OBS-01)

1. In `backend/src/Taskdeck.Api/appsettings.Development.json`, set:
   - `"Observability": { "EnableConsoleExporter": true }`
2. Restart API and perform:
   - one board or card mutation from UI
   - one ops command run from `/workspace/ops/cli`
3. Call `GET /health/ready`.
4. In backend console output, verify:
   - HTTP telemetry is emitted (request spans/metrics)
   - custom `taskdeck.*` metrics appear (`queue.backlog`, `worker.*`, `heartbeat.staleness`)
   - request spans include `taskdeck.correlation_id`
5. Revert `EnableConsoleExporter` to `false` after smoke validation.

## R. Known-Gap Triage (From Product Notes)

Run these checks even if they currently fail; log outcome explicitly.

1. Drag/edit conflict regression check:
   - Repro: open add-card input or card modal field, perform non-handle drag gestures near editable surfaces.
   - Target behavior: only explicit drag handles initiate board/card drag; editable interactions do not trigger unintended movement.
2. Ops and automation form ergonomics:
   - Repro: create requests using ops/automation UIs.
   - Target behavior: contextual autocomplete/options, reduced manual input burden.
3. Sidebar shortcuts affordance:
   - Repro: test on shorter and taller viewports.
   - Target behavior: shortcuts/help affordance remains discoverable without deep scrolling.

## S. Manual Findings Regression Pack (MAN-2026-02-21)

Use this section to retest the exact findings captured in `docs/archive/2026-02-25_docs-cleanup/notesFromManualTesting.txt`.
Issue wave:
- umbrella: `#173`
- mapped issues: `#174`, `#175`, `#176`, `#177`, `#178`, `#179`

For each test below, capture:
- pass/fail
- screenshot or short clip where UX is involved
- request ID or API payload snippet for backend/auth failures
- linked defect issue if outcome deviates from target behavior

### K1. Auth login/register regression checks (`#174`)

1. Register a new account with unique username/email.
   - Expected: registration succeeds and user can access workspace.
2. Attempt to register again with same username/email.
   - Expected: deterministic duplicate/validation response (no ambiguous invalid-identity message).
3. Immediately attempt login using the same valid account credentials.
   - Expected: login succeeds; duplicate-registration failure does not poison login state.
4. Attempt login with wrong password for existing user.
   - Expected: error clearly indicates invalid credentials path, distinct from account-state failures.
5. Attempt login with valid email but wrong username (or vice versa where supported).
   - Expected: deterministic and consistent identity validation response.

### K2. Starter-pack catalog breadth checks (`#175`)

1. Open starter-pack catalog on a board.
   - Expected: first-party pack set includes expanded categories beyond current baseline.
2. Search for at least three domain-specific pack intents (example: engineering, support, content).
   - Expected: relevant packs are discoverable by search/filter.
3. Open previews for at least five different packs.
   - Expected: each preview renders metadata and planned changes without runtime errors.

### K3. Starter-pack warning-first apply UX checks (`#176`)

1. Apply a pack to a clean board.
   - Expected: apply success path is unchanged and explicit.
2. Re-apply same pack.
   - Expected: non-blocking conflicts are surfaced as warnings (or actionable conflict context), not opaque hard-stop.
3. Apply a second pack with known overlap.
   - Expected: warning vs blocking conflicts are clearly classified.
4. Verify user guidance text.
   - Expected: remediation options are explicit (what can be skipped, retried, or corrected).

### K4. Archive/delete lifecycle consistency checks (`#177`)

1. Open board settings for an active board and inspect lifecycle controls.
   - Expected: archive/delete controls are non-duplicative and semantically clear.
2. Archive board from board settings.
   - Expected: board leaves default list and appears in archive workspace.
3. In archive workspace, inspect available actions.
   - Expected: action model matches documented lifecycle (restore/visibility/delete semantics are clear).
4. Validate hidden archived visibility path.
   - Expected: hidden archived items can be surfaced via explicit command/filter/control.
5. Restore board and confirm list transitions.
   - Expected: restored board returns to default boards list with consistent lifecycle state.

### K5. Card drag affordance checks (`#178`)

1. On a dense board, attempt drag from multiple card surface areas (top, center, near metadata).
   - Expected: drag initiation is practical and not constrained to a tiny corner-only hit area.
2. Attempt editing title/description fields and nearby interactions.
   - Expected: improved drag area does not reintroduce accidental drag during edit flows.
3. Drag across columns repeatedly.
   - Expected: behavior is reliable, with no intermittent non-responsiveness from handle targeting.

### K6. Ops role discoverability and permission-guidance checks (`#179`)

1. Log in as non-admin user and open `/workspace/ops/cli`.
   - Expected: current role/capability context is visible or discoverable from the screen.
2. Run admin-restricted template (example: `boards.list` if still restricted).
   - Expected: permission message includes actionable next steps (not just denial text).
3. Discover runnable templates for current role.
   - Expected: user can identify allowed templates without trial-and-error.
4. Navigate to role-assignment/help path from ops surface (or linked settings/docs).
   - Expected: clear guidance exists for obtaining required role/permissions.
   - Reference: `docs/ops/TASKDECK_HUMAN_OPERATIONS.md` (`A5 Ops CLI role-assignment workflow`).

## T. Post-Run Documentation Check

If behavior, commands, or known gaps changed, update:
- `docs/STATUS.md`
- `docs/IMPLEMENTATION_MASTERPLAN.md`
- `docs/TESTING_GUIDE.md`
- `docs/MANUAL_TEST_CHECKLIST.md`
- `docs/ops/OBSERVABILITY_BASELINE.md` (when telemetry/dashboard/alert contract changes)

## U. Final Automated Smoke Before Merge

1. Backend:
   - `dotnet test backend/Taskdeck.sln -c Release -m:1`
2. Frontend unit/build:
   - `cd frontend/taskdeck-web && npx vitest --run --reporter=verbose`
   - `cd frontend/taskdeck-web && npm run typecheck && npm run build`
3. Frontend E2E:
   - `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db npx playwright test --reporter=line`
   - fallback when `5173` is unavailable:
     `cd frontend/taskdeck-web && TASKDECK_E2E_DB=taskdeck.e2e.local.db TASKDECK_E2E_FRONTEND_PORT=5001 TASKDECK_E2E_API_CORS_ORIGINS=http://localhost:5001 npx playwright test --reporter=line`

## V. Capture Realignment Manual Slice (Shipped CAP MVP)

Status:
- active; run this slice as part of regular regression/manual confidence checks for shipped capture behavior (`#200` to `#211`)

Goal:
- validate thesis-critical behavior: low-friction capture + review-first trusted automation

Slice checks:
1. Capture speed and UX:
   - open capture surface from keyboard-first entrypoint
   - submit artifact and verify immediate feedback
   - expected: capture flow feels under 10 seconds in normal local use
2. Inbox behavior:
   - verify list/detail semantics (excerpt in list, full text in detail)
   - verify status transitions and explicit user actions
3. Proposal trust gate:
   - trigger triage from inbox
   - verify board mutations occur only through proposal review/apply flow
4. Provenance visibility:
   - verify created proposal/card references capture source and remains user-legible
5. Policy/contract checks:
   - verify unauthenticated/cross-user/missing-resource behavior remains `401/403/404`
   - verify error payload shape remains `{ errorCode, message }`

## W. Testing Harness Wave 1 Manual Slice (`#254` to `#260`)

Status:
- planned; execute while TST-16/TST-17/TST-18 and harness guardrail issues are being implemented

Goal:
- provide manual confidence on persistence and error-contract paths that are being regression-hardened in the testing-harness wave

Slice checks:
1. Drag/drop persistence roundtrip (`#256`):
   - reorder columns and refresh page
   - move a card to another column and refresh page
   - expected: both persisted orders remain intact after refresh
2. WIP enforcement UX confirmation (already-covered, keep monitoring):
   - fill a limited column then try add/move one extra card
   - expected: operation blocked with visible feedback and no state mutation on refresh
3. Representative API error-contract checks (`#257`):
   - run one request each for `400/401/403/404/409`
   - expected: JSON payload includes non-empty `errorCode` and `message`; request-id echo present where middleware guarantees it
4. Sandbox safety check (already-covered, keep monitoring):
   - call database export/import endpoints in non-sandbox posture
   - expected: deterministic `403` rejection
5. Starter-pack idempotency/conflict confirmation (already-covered, keep monitoring):
   - apply the same pack twice and verify no duplicates
   - execute a known conflict path and verify dry-run conflict report with no mutation

## X. Workspace Shell, Board Lifecycle, and Keyboard UX (Slice A)

Status:
- active; detailed step-indexed checklist in `docs/testing/manual-validation-a-workspace-board-ux.md`

Goal:
- validate workspace shell navigation, board CRUD lifecycle, keyboard navigation model, escape behavior stack, drag handle safety, and filter panel UX

Scope (22 scenarios, A-01 through A-22):
1. Auth flows: registration, login, auth guard redirect (A-01 to A-03)
2. Shell navigation: sidebar routes, command palette, keyboard shortcuts help, quick capture modal, logout (A-04 to A-08)
3. Board lifecycle: creation, rename, archive/unarchive, action rail (A-09 to A-12)
4. Board operations: columns, cards, labels, filter panel (A-13 to A-16)
5. Keyboard UX: vim-style board navigation, escape behavior stack (board and shell), drag handle safety (A-17 to A-20)
6. Today view and onboarding: agenda, dismiss/replay persistence (A-21 to A-22)

Reference: `docs/testing/manual-validation-a-workspace-board-ux.md` for full step tables, evidence guidance, automation candidates, and defect filing template.
## Y. Authz Policy, Cross-User Isolation, and API Error Contracts (Slice B, `#131`)

Status:
- active; comprehensive two-user authz matrix covering all controller families

Goal:
- validate authorization enforcement, cross-user data isolation, and error payload contracts across all protected API surfaces

Full checklist:
- `docs/testing/manual-validation-b-authz-contracts.md`

Summary scope:
1. Unauthenticated access denial (401) on all `[Authorize]` controller families (B-01 to B-32)
2. Cross-user board-scoped isolation: UserB cannot access UserA's boards/columns/cards/labels/comments/webhooks/starter-packs/exports/audit (B-40 to B-60)
3. Cross-user non-board-scoped isolation: captures/chat/proposals/archive/notifications/queue/workspace return only the authenticated user's data (B-70 to B-81)
4. True-missing vs cross-user denial indistinguishability (B-90 to B-96)
5. Error payload contract verification for auth/validation/sandbox paths (B-100 to B-110)
6. Advanced controller families: ops/logs/users/abuse/llm-quota/agents/knowledge/webhooks/external-imports (B-130 to B-175)

---

## Incident Rehearsals

For operational failure diagnosis and recovery validation beyond functional testing, see the incident rehearsal program:

- `docs/ops/INCIDENT_REHEARSAL_CADENCE.md` -- schedule and rotation
- `docs/ops/rehearsal-scenarios/` -- scenario templates
- `docs/ops/EVIDENCE_TEMPLATE.md` -- evidence package format
- `docs/ops/rehearsals/` -- completed rehearsal evidence
