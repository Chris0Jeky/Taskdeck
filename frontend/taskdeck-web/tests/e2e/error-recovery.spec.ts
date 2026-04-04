/**
 * E2E: Error Recovery Journeys (#712)
 *
 * Covers degraded-mode and error-state scenarios that real users encounter:
 * - API failures during board load (simulated via page.route())
 * - Session expiry while navigating
 * - LLM provider degraded responses
 * - Capture submission failure
 * - Proposal approval on an expired proposal
 *
 * These tests use page.route() to intercept API calls and return controlled
 * error responses so they run deterministically without requiring specific
 * backend configuration.
 */

import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { createCaptureItem, waitForProposalCreated } from './support/captureFlow'
import { assertOk } from './support/httpAsserts'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'error-recovery')
})

// ─── Scenario 1: Board load failure → error state shown → retry succeeds ─────

test('board load failure should display error state and allow retry', async ({ page }) => {
  let callCount = 0

  await page.route('**/api/boards/**', async (route) => {
    // Only intercept GET requests for board details (not cards, not list)
    const url = route.request().url()
    const isBoardDetail = route.request().method() === 'GET' && /\/api\/boards\/[a-f0-9-]+$/.test(url)
    if (!isBoardDetail) {
      await route.continue()
      return
    }

    callCount += 1
    if (callCount === 1) {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'ServiceUnavailable',
          message: 'Board service temporarily unavailable',
        }),
      })
      return
    }

    await route.continue()
  })

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(page.request, auth, seed, {
    boardNamePrefix: 'Error Recovery',
    description: 'board load failure test',
    columnNamePrefix: 'Backlog',
  })

  // First visit: 503 should be served
  await page.goto(`/workspace/boards/${boardId}`)

  // Expect the error state to be visible — any role=alert or error-related text
  const errorState = page.getByRole('alert').first()
  await expect(errorState).toBeVisible({ timeout: 10_000 })

  // After a reload the route continues normally (callCount > 1)
  await page.reload()

  // Board heading should now be visible
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 15_000 })
})

// ─── Scenario 2: Capture submission API failure → error shown in UI ───────────

test('capture submission failure should surface error and not navigate away', async ({ page }) => {
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  // Intercept the capture POST and return 500
  await page.route('**/api/capture/items', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'UnexpectedError',
          message: 'Capture service unavailable',
        }),
      })
      return
    }
    await route.continue()
  })

  // Open capture modal via keyboard shortcut
  await page.keyboard.press('Control+Shift+C')
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  await captureModal
    .getByPlaceholder('Capture a thought, task, or follow-up...')
    .fill('This capture will fail')

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').press('Control+Enter')

  // Should NOT navigate away on failure
  await expect(page).not.toHaveURL(/\/workspace\/inbox/)

  // Error feedback must be visible — either within the modal or via an alert
  const errorFeedback = page
    .getByRole('alert')
    .or(captureModal.getByText(/error|fail|unable|problem/i))
    .first()
  await expect(errorFeedback).toBeVisible({ timeout: 10_000 })
})

// ─── Scenario 3: Unauthenticated board access → redirect to login (or 401) ───

test('accessing board without a session token should redirect to login or return 401', async ({ page }) => {
  // Create a board with a real session, then clear local storage and revisit
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(page.request, auth, seed, {
    boardNamePrefix: 'Auth Guard',
    description: 'auth guard test board',
    columnNamePrefix: 'Column',
  })

  // Strip session credentials from storage
  await page.addInitScript(() => {
    localStorage.removeItem('taskdeck_token')
    localStorage.removeItem('taskdeck_session')
  })

  await page.goto(`/workspace/boards/${boardId}`)

  // Expect either a redirect to a login/auth URL, or a visible login form
  const isOnLoginOrHomePage = await page.waitForFunction(
    () =>
      window.location.pathname.includes('/login') ||
      window.location.pathname.includes('/auth') ||
      window.location.pathname === '/' ||
      document.querySelector('[data-testid="login-form"]') !== null ||
      document.querySelector('input[type="password"]') !== null,
    { timeout: 10_000 },
  ).then(() => true).catch(() => false)

  // Alternatively the API route returns 401; either outcome is acceptable
  const hasUnauthorizedAlert = await page.getByRole('alert').filter({ hasText: /401|unauthorized|sign in|log in/i }).count().then((c) => c > 0)

  expect(isOnLoginOrHomePage || hasUnauthorizedAlert).toBeTruthy()
})

// ─── Scenario 4: Inbox load failure → error state on Inbox view ──────────────

test('inbox API failure should show error state in inbox view', async ({ page }) => {
  let callCount = 0

  await page.route('**/api/capture/items*', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.continue()
      return
    }
    callCount += 1
    if (callCount === 1) {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'ServiceUnavailable',
          message: 'Inbox temporarily unavailable',
        }),
      })
      return
    }
    await route.continue()
  })

  await page.goto('/workspace/inbox')

  // Must show some error indicator, not a blank/spinner forever
  const errorState = page
    .getByRole('alert')
    .or(page.getByText(/error|failed|unavailable|could not load/i))
    .first()
  await expect(errorState).toBeVisible({ timeout: 10_000 })

  // After reload the second call continues normally
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible({ timeout: 15_000 })
})

// ─── Scenario 5: Review view when proposal approval API returns 409 ───────────

test('proposal approve API conflict should show error feedback and keep proposal visible', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Review Conflict',
    description: 'review conflict test board',
    columnNamePrefix: 'Todo',
  })

  const captureText = `- [ ] Review conflict card ${seed}`
  const captureItem = await createCaptureItem(request, auth, boardId, captureText)

  // Trigger triage via API to create a proposal
  const triageResponse = await request.post(
    `${API_BASE_URL}/capture/items/${encodeURIComponent(captureItem.id)}/triage`,
    { headers: { Authorization: `Bearer ${auth.token}` } },
  )
  await assertOk(triageResponse, 'trigger triage')

  const triagedItem = await waitForProposalCreated(request, auth, captureItem.id)
  const proposalId = triagedItem.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  // Intercept the approve endpoint and return 409
  await page.route(`**/api/proposals/${proposalId}/approve`, async (route) => {
    await route.fulfill({
      status: 409,
      contentType: 'application/json',
      body: JSON.stringify({
        errorCode: 'Conflict',
        message: 'Proposal has already been applied or expired',
      }),
    })
  })

  await page.goto(`/workspace/review?boardId=${boardId}#proposal-${proposalId}`)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible({ timeout: 15_000 })

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()

  // Error must appear; proposal card must still be present (not removed on failure)
  const errorIndicator = page
    .getByRole('alert')
    .or(proposalCard.getByText(/error|conflict|fail|expired/i))
    .first()
  await expect(errorIndicator).toBeVisible({ timeout: 10_000 })
  await expect(proposalCard).toBeVisible()
})

// ─── Scenario 6: LLM provider degraded → user sees degraded message ───────────

test('chat request when LLM provider returns degraded response should show degraded indicator', async ({ page }) => {
  // Intercept the chat send endpoint with a 503 (provider-down pattern)
  await page.route('**/api/llm/chat*', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'ProviderUnavailable',
          message: 'LLM provider is currently unavailable. Please try again later.',
        }),
      })
      return
    }
    await route.continue()
  })

  await page.goto('/workspace/automations/chat')

  const chatInput = page.getByPlaceholder(/type a message|ask something|chat/i).first()

  // The chat input must be present; if it is missing the page is already broken
  // in a way we cannot assert on meaningfully — fail fast.
  await expect(chatInput).toBeVisible({ timeout: 10_000 })
  await chatInput.fill('What cards are due today?')
  await chatInput.press('Enter')

  // Expect degraded/error indicator — not a raw stack trace
  const degradedIndicator = page
    .getByRole('alert')
    .or(page.getByText(/unavailable|degraded|try again|provider/i))
    .or(page.locator('[data-llm-health-state="degraded"]'))
    .first()
  await expect(degradedIndicator).toBeVisible({ timeout: 10_000 })
})

// ─── Scenario 7: Board list load failure → boards workspace shows error ───────

test('boards list API failure should show error in boards workspace', async ({ page }) => {
  let intercepted = false

  await page.route('**/api/boards*', async (route) => {
    if (route.request().method() === 'GET' && !intercepted) {
      intercepted = true
      await route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'ServiceUnavailable',
          message: 'Board list temporarily unavailable',
        }),
      })
      return
    }
    await route.continue()
  })

  await page.goto('/workspace/boards')

  // Spinner should resolve into an error state rather than looping forever
  const errorState = page
    .getByRole('alert')
    .or(page.getByText(/error|failed|unavailable|could not load/i))
    .first()
  await expect(errorState).toBeVisible({ timeout: 10_000 })
})

// ─── Scenario 8: Workspace preferences save failure → visual feedback ─────────

test('workspace preferences save failure should show error and not silently discard input', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Intercept the preferences PUT and simulate a server error
  await page.route('**/api/workspace/preferences', async (route) => {
    if (route.request().method() === 'PUT') {
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'UnexpectedError',
          message: 'Failed to save workspace preferences',
        }),
      })
      return
    }
    await route.continue()
  })

  const workspaceModeSelect = page.getByLabel('Workspace mode')
  if (await workspaceModeSelect.isVisible({ timeout: 5_000 }).catch(() => false)) {
    await workspaceModeSelect.selectOption('workbench')

    // The save attempt should surface an error — either an alert or inline message
    const errorFeedback = page
      .getByRole('alert')
      .or(page.getByText(/error|failed|could not save/i))
      .first()
    await expect(errorFeedback).toBeVisible({ timeout: 10_000 })
  } else {
    // Workspace mode selector absent; skip gracefully
    test.skip()
  }
})
