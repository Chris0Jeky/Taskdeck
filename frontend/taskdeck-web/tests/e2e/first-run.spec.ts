import type { APIResponse, Response } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { expectApplyConfirmDialog } from './support/applyConfirm'
import { createBoardWithColumn } from './support/boardHelpers'
import { createCaptureItem, triageCaptureItem, waitForCardWithTitle, waitForProposalCreated } from './support/captureFlow'
import { assertOk } from './support/httpAsserts'

function extractBoardIdFromBoardUrl(url: string): string {
  const match = /\/workspace\/boards\/([a-f0-9-]+)$/i.exec(url)
  if (!match?.[1]) {
    throw new Error(`Expected a board URL but received '${url}'`)
  }

  return match[1]
}

async function parseCreatedCaptureId(response: APIResponse | Response): Promise<string> {
  const payload = await response.json() as { id?: string }
  if (!payload.id) {
    throw new Error('Capture creation response did not contain an id')
  }

  return payload.id
}

let auth: AuthResult

const LEGACY_FIRST_RUN_TITLE = 'Legacy first-run path retains frozen selector coverage'

test.beforeEach(async ({ page, request }, testInfo) => {
  auth = await registerAndAttachSession(
    page,
    request,
    'first-run',
    testInfo.title === LEGACY_FIRST_RUN_TITLE ? { theme: 'legacy' } : {},
  )
})

test('Paper first-run path guides setup through capture, review, apply, and board', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardName = `Paper First Run ${seed}`
  const cardTitle = `Paper first-run card ${seed}`

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()
  await page.getByTestId('paper-home-setup-cta').click()

  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()
  await setupDialog.getByPlaceholder('For example: Product Sprint').fill(boardName)
  await setupDialog.getByRole('radio', { name: /Engineering sprint/i }).check()
  await setupDialog.getByRole('button', { name: 'Create Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  const boardId = extractBoardIdFromBoardUrl(page.url())
  await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
  await page.getByRole('button', { name: 'Capture here' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))

  const captureBody = page.getByRole('textbox', { name: 'Capture body' })
  const createCaptureResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/capture\/items$/i.test(response.url()))
  await captureBody.fill(`- [ ] ${cardTitle}`)
  await page.getByRole('button', { name: /^Capture/ }).click()
  const response = await createCaptureResponse
  await assertOk(response, 'create Paper first-run capture')
  const captureId = await parseCreatedCaptureId(response)

  const captureRow = page.locator('.paper-triage__row').filter({ hasText: cardTitle }).first()
  await expect(captureRow).toBeVisible()
  await captureRow.getByRole('button', { name: 'Accept' }).click()
  const triagedCapture = await waitForProposalCreated(request, auth, captureId)
  const proposalId = triagedCapture.provenance?.proposalId
  expect(proposalId).toBeTruthy()

  await page.getByRole('link', { name: /Review$/ }).click()
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(
    page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` }),
  ).toBeVisible({ timeout: 15_000 })

  const approveResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/approve`))
  await page.getByTestId('decision-apply').click()
  await assertOk(await approveResponse, `approve Paper first-run proposal ${proposalId}`)

  const executeResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && response.url().endsWith(`/automation/proposals/${proposalId}/execute`))
  // Hard-assert the final apply confirmation (see capture-loop.spec.ts): the
  // test must FAIL if the phase-2 gate disappears, not execute silently.
  await expectApplyConfirmDialog(page, () => page.getByTestId('decision-apply').click())
  await assertOk(await executeResponse, `execute Paper first-run proposal ${proposalId}`)
  const createdCard = await waitForCardWithTitle(request, auth, boardId, cardTitle)

  await page.goto(`/workspace/boards/${boardId}`)
  await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
  await expect(page.getByRole('button', { name: `Card ${createdCard.title}` })).toBeVisible()
})

test(LEGACY_FIRST_RUN_TITLE, async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardName = `First Run ${seed}`
  const cardTitle = `First-run card ${seed}`
  const captureText = `- [ ] ${cardTitle}`
  const controlCardTitle = `Control card ${seed}`
  const controlCaptureText = `- [ ] ${controlCardTitle}`

  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await expect(page.getByText('What is Home for?')).toBeVisible()
  await expect(
    page.getByText('No boards yet. Start setup from Home or Today so captures and review can land somewhere useful.')
  ).toBeVisible()

  await page.locator('.td-home__hero-actions').getByRole('button', { name: 'Capture a note' }).click()
  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.getByText('What is Inbox for?')).toBeVisible()
  await expect(page.getByText('No capture items yet')).toBeVisible()

  await page.getByTestId('inbox-empty-state').getByRole('button', { name: 'Open Today' }).click()
  await expect(page).toHaveURL(/\/workspace\/today$/)
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
  await expect(page.getByText('What is Today for?')).toBeVisible()

  await page.getByRole('button', { name: 'Start Useful Board' }).click()
  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()
  await setupDialog.getByPlaceholder('For example: Product Sprint').fill(boardName)
  await setupDialog.getByRole('radio', { name: /Engineering sprint/i }).check()
  await setupDialog.getByRole('button', { name: 'Create Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  const boardId = extractBoardIdFromBoardUrl(page.url())

  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  const boardActionRail = page.locator('[data-board-action-rail]')
  await expect(boardActionRail.getByRole('button', { name: 'Capture here' })).toBeVisible()

  await boardActionRail.getByRole('button', { name: 'Capture here' }).click()
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()
  await expect(captureModal.getByText(`This capture will stay linked to ${boardName}.`)).toBeVisible()

  const createCaptureResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST'
    && /\/api\/capture\/items$/i.test(response.url()))

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByRole('button', { name: 'Save Capture' }).click()
  const response = await createCaptureResponse
  await assertOk(response, 'create Legacy first-run capture')
  const captureId = await parseCreatedCaptureId(response)
  await expect(captureModal).toHaveCount(0)

  const controlBoardId = await createBoardWithColumn(request, auth, `${seed}-control`, {
    boardNamePrefix: 'First Run Control',
    description: 'control board for first-run smoke filter assertions',
    columnNamePrefix: 'Inbox',
  })
  const controlCapture = await createCaptureItem(request, auth, controlBoardId, controlCaptureText)
  await triageCaptureItem(request, auth, controlCapture.id)
  const controlTriagedCapture = await waitForProposalCreated(request, auth, controlCapture.id)
  const controlProposalId = controlTriagedCapture.provenance?.proposalId
  expect(controlProposalId).toBeTruthy()

  await boardActionRail.getByRole('button', { name: 'Open Inbox' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.getByText(`Showing capture items linked to board ${boardId}.`)).toBeVisible()
  await expect(page.getByText('What is Inbox for?')).toBeVisible()
  await expect(page.locator('[data-testid="inbox-item"]').filter({ hasText: controlCardTitle })).toHaveCount(0)

  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: cardTitle }).first()
  await expect(captureRow).toBeVisible()
  await captureRow.click()

  const triageButton = page.locator('.td-inbox-detail__actions button').filter({ hasText: 'Start Triage' }).first()
  await expect(triageButton).toBeVisible()
  await triageButton.click()

  const triagedCapture = await waitForProposalCreated(request, auth, captureId)
  const proposalId = triagedCapture.provenance?.proposalId
  const triageRunId = triagedCapture.provenance?.triageRunId
  expect(proposalId).toBeTruthy()

  await page.getByRole('button', { name: 'Refresh Detail' }).click()
  const openProposalButton = page.getByRole('button', { name: 'Open in Review' })
  await expect(openProposalButton).toBeVisible()
  await openProposalButton.click()

  await expect(page).toHaveURL(new RegExp(`/workspace/review\\?boardId=${boardId}#proposal-${proposalId}`))
  await expect(page.locator('.td-review__board-filter').getByText(boardName, { exact: true })).toBeVisible()
  await expect(page.locator(`#proposal-${controlProposalId}`)).toHaveCount(0)
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()

  await expectApplyConfirmDialog(page, () => proposalCard.getByRole('button', { name: 'Apply to board' }).click())
  await expect(proposalCard).not.toBeVisible()

  const createdCard = await waitForCardWithTitle(request, auth, boardId, cardTitle)

  // Toggle "Show completed" to reveal applied proposal and navigate to board
  await page.locator('.td-review__toggle-input').check()
  await expect(proposalCard).toBeVisible()
  // Open collapsed "Technical details" section, then "Links" dropdown, then "Open Board"
  await proposalCard.getByRole('button', { name: /Technical details/ }).click()
  await proposalCard.getByRole('button', { name: /Links/ }).click()
  await proposalCard.getByRole('menuitem', { name: 'Open Board' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/boards/${boardId}$`))
  const card = page.locator('[data-card-id]').filter({ hasText: createdCard.title }).first()
  await expect(card).toBeVisible()
  await card.getByRole('heading', { name: createdCard.title, exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
  await expect(page.getByText('Capture Origin')).toBeVisible()
  const openCaptureLink = page.getByRole('link', { name: 'Open Capture' })
  await expect(openCaptureLink).toHaveAttribute('href', `/workspace/inbox?boardId=${boardId}#capture-${captureId}`)
  await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
    'href',
    `/workspace/review?boardId=${boardId}#proposal-${proposalId}`,
  )

  if (triageRunId) {
    await expect(page.getByText(`Triage run: ${triageRunId}`)).toBeVisible()
  }

  await openCaptureLink.click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}#capture-${captureId}$`))
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.getByText(`Showing capture items linked to board ${boardId}.`)).toBeVisible()
  await expect(page.locator('[data-testid="inbox-item"]').filter({ hasText: controlCardTitle })).toHaveCount(0)
  await expect(page.locator('[data-testid="inbox-item"]').filter({ hasText: cardTitle })).toHaveClass(/td-inbox-row--selected/)
  await expect(page.locator('.td-inbox-detail__text')).toContainText(captureText)
})

test('home should recover from loading and error states on first-run summary refresh', async ({ page }) => {
  let requestCount = 0
  // The FE-15 retry interceptor retries idempotent 5xx up to 3 times, so the
  // initial load plus its 3 retries (4 total) must fail to surface the error
  // state. Only the first request is gated — subsequent retries fail
  // immediately so backoff is the only delay.
  const MAX_FAILED_REQUESTS = 4 // 1 initial + 3 retries (MAX_RETRIES)
  let releaseFirstRequest: (() => void) | null = null
  const firstRequestGate = new Promise<void>((resolve) => { releaseFirstRequest = resolve })

  await page.route('**/api/workspace/home', async (route) => {
    requestCount += 1

    if (requestCount <= MAX_FAILED_REQUESTS) {
      if (requestCount === 1) {
        await firstRequestGate
      }
      await route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          errorCode: 'UnexpectedError',
          message: 'Temporary home summary failure',
        }),
      })
      return
    }

    await route.continue()
  })

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home-loading')).toContainText('Loading your workspace summary...')
  releaseFirstRequest!()
  // Generous timeout because the retry interceptor waits 1s+2s+4s between
  // attempts before surfacing the terminal rejection.
  await expect(page.getByTestId('paper-home-error')).toContainText(
    'Temporary home summary failure',
    { timeout: 20_000 },
  )

  await page.reload()
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()
  await expect(page.getByText('From thought to trusted action')).toBeVisible()
})
