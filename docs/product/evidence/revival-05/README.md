# REVIVAL-05 Paper onboarding evidence

Captured from the isolated `paper-onboarding.spec.ts` Playwright journey against a fresh registered user in Paper mode on 2026-07-13.

## Guided first board

![Paper guided setup modal opened from the zero-board Home state](paper-guided-setup.png)

The Paper Home CTA opens the existing `WorkspaceSetupModal`; the local-only three-step activation milestones remain visible behind it.

## Created board

![First Paper board created by the guided setup](paper-first-board.png)

The same journey creates a blank board, navigates to it, then returns Home and verifies the board milestone advances to `1/3 complete`.

Verification command:

```powershell
$env:TASKDECK_E2E_API_BASE_URL='http://localhost:5101/api'
$env:TASKDECK_E2E_FRONTEND_PORT='5201'
$env:TASKDECK_E2E_DB='taskdeck.e2e.1301.db'
$env:TASKDECK_E2E_WORKERS='1'
npx playwright test tests/e2e/paper-onboarding.spec.ts --project=chromium --workers=1 --reporter=line
```

Result: 1 passed in 16.0 seconds.
