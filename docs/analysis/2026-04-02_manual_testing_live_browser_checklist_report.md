# Manual Testing Report - Live Browser Checklist
**Date:** 2026-04-02
**Primary tester:** Codex (headed Playwright browser session)
**Cross-check input:** Claude automated QA report reviewed and validated selectively
**Environment:** `http://localhost:5173` frontend, `http://localhost:5000` API
**Run mode observed:** live Gemini provider configured in app UI, not mock
**Primary account:** `demo / demo123`
**Commit SHA:** `e5f22488fc8f59640202e33726dde369319bb44d`
**Browser / OS:** Chrome 143 user agent on Windows 10 x64
**DB baseline:** existing seeded demo state plus one new board created during the run
**Artifacts:** [mobile Today screenshot](/C:/Users/jekyt/Downloads/taskdeck-today-mobile-2026-04-01T23-50-28-453Z.png)

---

## Summary

This run used the current live-browser plan at `docs/testing/live-browser-agent-test-plan.md` and cross-referenced the active manual checklist at `docs/MANUAL_TEST_CHECKLIST.md`.

The core product loop remains broadly functional:

- login works
- Home teaches the review-first workflow well
- capture flows into Inbox correctly
- triage can generate review items
- fresh proposals can be approved and applied
- board CRUD and direct board work are usable

The most important confirmed problems from this run are:

1. Chat/tool-calling still surfaces shorthand card IDs that its own proposal parser rejects.
2. Review shows expired approved proposals as if they are still executable.
3. Four shipped routes silently redirect to Home instead of loading their intended views.
4. Automation Chat health messaging does not cleanly distinguish configured vs verified provider readiness.
5. Manual-card provenance absence is handled in the UI but still logs avoidable 404/error noise.
6. Some UI consistency gaps remain: label modal light-theme styling, raw IDs in review diff, board-presence identity formatting.

This report also records which claims from the cross-check report were confirmed, which were not reproduced strongly enough, and which were converted into testing-only follow-up issues.

---

## What Was Exercised

### Core path

- `/login` with seeded demo credentials
- `/workspace/home`
- `/workspace/inbox`
- `/workspace/review`
- `/workspace/boards`
- direct board creation and board-scoped capture
- `/workspace/automations/chat`
- `/workspace/today`
- `/workspace/notifications`
- `/workspace/metrics`

### Supporting route checks

- `/workspace/archive`
- `/workspace/activity`
- `/workspace/ops/cli`
- `/workspace/settings/access`

### Input and interaction checks

- workspace mode changes and reload persistence
- help-callout dismiss and replay
- quick capture including empty validation
- plain capture, dash-separated capture, semicolon-separated capture
- inbox triage to review
- review approve/apply flow
- direct board creation, column creation, card creation
- card edit: title, description, due date, blocked flag, block reason
- label creation and assignment
- card move via card move menu
- board-scoped capture
- mobile-width Today view smoke

---

## Confirmed Working

### Authentication and session

- Login with `demo / demo123` succeeded.
- Session persisted across page reload.
- Login page correctly showed no GitHub OAuth button in the current environment.
- Wrong-password validation worked.
- Duplicate-registration validation worked when checked manually later in the session.

### Home and shell

- Home clearly reinforces `Home -> Inbox/Capture -> Review -> Board`.
- Workspace mode changed from `Guided` to `Agent` and persisted across reload.
- Home help-callout hide/replay behavior worked and persisted across reload.
- Command palette opened with `Ctrl+K` during spot checks.

### Capture, Inbox, Review, Board

- Quick capture worked from Inbox and board surfaces.
- Empty capture validation worked.
- New captures appeared in Inbox immediately.
- Board-scoped capture showed explicit board-context messaging.
- Inbox triage created reviewable proposals for fresh captures.
- Fresh proposals could be approved and executed successfully.
- Board mutations from fresh executed proposals appeared on the target board.

### Board surface

- New board creation worked.
- Adding columns worked.
- Inline card creation worked.
- Card editing worked for:
  - title
  - description
  - due date
  - blocked flag
  - block reason
- Label creation worked.
- Label assignment worked.
- Card move menu worked.
- Metrics updated to reflect board state after changes.

### Today and notifications

- Today view rendered correctly on desktop and mobile-width smoke checks.
- Notifications rendered grouped sections and seeded data without immediate runtime issues.

---

## Confirmed Problems

### P1/P2 product defects

1. Chat/tool-calling read/write ID mismatch
   - Chat responds with short IDs such as `d2d8c7d2`.
   - Follow-up action requests fail with `Invalid card ID: d2d8c7d2`.
   - Seeded as [#677](https://github.com/Chris0Jeky/Taskdeck/issues/677).

2. Expired proposals still presented as executable and cannot be dismissed
   - Old approved proposal was shown as `Approved, ready to apply`.
   - Forced execution path reached the API and failed with `Proposal has expired`.
   - **Additional finding (maintainer-confirmed):** expired proposals are stuck in the review list with no dismiss/clear action available. Users cannot remove them.
   - **Integration test evidence:** `LiveBrowserRegressionApiTests.ApproveProposal_WhenExpired_CurrentlySucceeds_Bug678` confirms the service layer does not re-check `ExpiresAt` — approve returns 200 even when the proposal is past expiry.
   - Seeded as [#678](https://github.com/Chris0Jeky/Taskdeck/issues/678).

3. Archive, Activity, Ops, and Access routes silently redirect to Home
   - Direct navigation to each route landed at `/workspace/home`.
   - Confirmed by checking `window.location.pathname`.
   - **Maintainer-confirmed (2026-04-02):** this is a real routing issue, not feature flags being off. Routes redirect regardless of user access level.
   - Seeded as [#681](https://github.com/Chris0Jeky/Taskdeck/issues/681).

4. Automation Chat health state is misleading before verification
   - Initial state: `Live LLM configured`.
   - After verify: `Live LLM unavailable` with `Live provider response parsing failed.`
   - Seeded as [#679](https://github.com/Chris0Jeky/Taskdeck/issues/679).

5. Manual-card provenance absence still logs 404/error noise
   - UI shows `No capture provenance available.`
   - Console logs repeated `Capture provenance not found` errors for manual cards.
   - Seeded as [#680](https://github.com/Chris0Jeky/Taskdeck/issues/680).

### Confirmed UX and presentation issues

6. Review diff displays raw IDs instead of readable targets
   - `View Diff` showed lines like `create card:<uuid>`.
   - Seeded as [#682](https://github.com/Chris0Jeky/Taskdeck/issues/682).

7. Board presence label changes identity format mid-session
   - Idle state showed `demo`.
   - Editing state showed `demo@taskdeck.local (editing)`.
   - Seeded as [#683](https://github.com/Chris0Jeky/Taskdeck/issues/683).

8. Label manager still uses light-theme styling inside dark shell
   - Confirmed visually and by inspecting rendered classes/markup.
   - Seeded as [#684](https://github.com/Chris0Jeky/Taskdeck/issues/684).

9. Route transition performance warning exceeded budget
   - Console warning: `route-transition` took `722ms` vs `300ms` budget.
   - Not seeded as a standalone bug from this run alone.

---

## Important Clarifications From This Run

### Review "Apply to board" was not generally broken

An earlier impression during the session was that `Apply to board` did nothing.

What actually happened:

- the frontend uses a native `confirm(...)` before execution
- the automation tooling did not surface that dialog clearly at first
- once confirm was forced to return true, a real execute request was sent
- fresh proposals executed correctly
- the old seeded proposal failed because it had expired

Conclusion:

- the actionable bug is proposal expiry presentation, not a dead execute button
- this is tracked in [#678](https://github.com/Chris0Jeky/Taskdeck/issues/678)

### Live provider vs mock

This app instance was not running in mock mode.

Observed UI evidence:

- Automation Chat reported Gemini configured
- verification then reported live provider parsing failure

This was treated as environment/runtime state, not seeded as a standalone repository bug from this run.

---

## Cross-Check Against Claude Report

### Confirmed from the external report

- live provider was active instead of mock
- chat card-ID truncation issue
- route redirects to Home for:
  - archive
  - activity
  - ops
  - access
- label manager light-theme inconsistency
- raw IDs in review diff
- board-presence identity inconsistency

### Not confirmed strongly enough in this run

- toast bleed across auth pages
- stale `Registration successful` toast on later login
- duplicate WIP-limit toasts
- metrics selector intermittently redirecting Home
- unexpected localhost redirect signal attributed via NavSentinel
- spontaneous workspace mode switching between sessions
- inbox triage actions being effectively undiscoverable

These were not promoted to product bugs from this run. Instead, targeted regression-test issues were seeded to catch them if they are real and recurring:

- [#685](https://github.com/Chris0Jeky/Taskdeck/issues/685)
- [#686](https://github.com/Chris0Jeky/Taskdeck/issues/686)
- [#687](https://github.com/Chris0Jeky/Taskdeck/issues/687)
- [#688](https://github.com/Chris0Jeky/Taskdeck/issues/688)
- grouped under [#689](https://github.com/Chris0Jeky/Taskdeck/issues/689)

---

## Coverage Against The Live Browser Plan

### Substantially covered

- `1. Authentication and Session`
- `2. Home / First-Run Experience`
- `3. Board CRUD` (partial but meaningful)
- `4. Column Management` (partial)
- `5. Card CRUD and Interaction` (partial)
- `6. Labels`
- `7. Capture / Inbox Flow`
- `8. Review / Proposal Flow`
- `10. Chat / LLM Interaction` (partial)
- `11. Today View`
- `12. Notifications` (smoke-level)
- `14. Board Metrics Dashboard`
- `24. Responsive / Visual` (mobile-width Today smoke)
- `27. Full Core Loop` (effectively covered in a fresh board-scoped slice)

### Spot-checked but not completed end-to-end

- `9. Automation Queue`
- `13. Command Palette / Global Search`
- `18. Archive and Recovery`
- `19. Activity / Audit Trail`
- `20. Ops Console`
- `21. Board Access / Collaboration`
- `23. Keyboard and Accessibility`
- `25. Error and Edge Cases`
- `26. Performance Smoke`

### Not reached

- `15. Starter Packs`
- `16. Export / Import`
- `17. GDPR Data Portability`
- full collaboration / second-user access checks

---

## Coverage Against The Manual Checklist

### Covered or partially covered

- `A. Authentication and Workspace Shell`
- `B. Boards, Columns, Cards, Labels`
- `D. Automations, Chat, and Proposals`
- `E. Inbox and Notifications Continuity` (board-scoped inbox path covered)
- `J. Board Metrics Dashboard`
- portions of `C. Filters and Keyboard Workflow`
- portions of `N. Review Card UX`
- portions of `V. Capture Realignment Manual Slice`

### Not covered in this run

- `F. Ops Console and Logs`
- `G. Archive and Recovery` full workflow
- `H. GitHub OAuth Login`
- `I. GDPR Data Portability and Account Deletion`
- `K. MCP Server Validation`
- `M. Backup and Restore DR Drill`
- `O. Activity View`
- `P. API Spot Checks` in a structured pass
- `Q. Observability Smoke`
- `R/S/W/X/Y` deeper manual packs

---

## Issues Seeded From This Session

### Product issues

- [#677](https://github.com/Chris0Jeky/Taskdeck/issues/677) Chat tool-calling should use card identifiers the proposal parser accepts
- [#678](https://github.com/Chris0Jeky/Taskdeck/issues/678) Review should not present expired proposals as ready to apply
- [#679](https://github.com/Chris0Jeky/Taskdeck/issues/679) Automation Chat health should distinguish configured from verified provider availability
- [#680](https://github.com/Chris0Jeky/Taskdeck/issues/680) Manual cards should treat missing capture provenance as an expected empty state
- [#681](https://github.com/Chris0Jeky/Taskdeck/issues/681) Archive, Activity, Ops, and Access routes silently redirect to Home
- [#682](https://github.com/Chris0Jeky/Taskdeck/issues/682) Proposal diff should show human-readable operation targets instead of raw IDs
- [#683](https://github.com/Chris0Jeky/Taskdeck/issues/683) Board header presence label should not switch between username and email formats
- [#684](https://github.com/Chris0Jeky/Taskdeck/issues/684) Label manager modal still uses light-theme styling inside the dark workspace shell

### Testing follow-through issues

- [#685](https://github.com/Chris0Jeky/Taskdeck/issues/685) Auth-flow regression coverage for transient toast state and stale success messaging
- [#686](https://github.com/Chris0Jeky/Taskdeck/issues/686) WIP-limit regression coverage to prevent duplicate toast emission
- [#687](https://github.com/Chris0Jeky/Taskdeck/issues/687) Route and workspace-state stability coverage for metrics, mode persistence, and unexpected origin changes
- [#688](https://github.com/Chris0Jeky/Taskdeck/issues/688) Inbox triage action visibility/discoverability coverage
- [#689](https://github.com/Chris0Jeky/Taskdeck/issues/689) Manual-QA regression coverage follow-through tracker

---

## Recommended Next Pass

The next manual pass should focus on what this run intentionally left shallow:

1. Starter Packs
2. GDPR data portability
3. Export/import
4. structured API spot checks
5. second-user collaboration and board access
6. a dedicated keyboard/accessibility pass
7. deeper Ops and Activity verification once route availability is fixed

That would convert this report from a strong core-loop and route-health pass into a broader full-checklist checkpoint.
