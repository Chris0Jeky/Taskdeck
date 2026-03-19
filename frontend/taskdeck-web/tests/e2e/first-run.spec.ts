import type { APIResponse } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { waitForCardWithTitle, waitForProposalCreated } from './support/captureFlow'

function extractBoardIdFromBoardUrl(url: string): string {
  const match = /\/workspace\/boards\/([a-f0-9-]+)$/i.exec(url)
  if (!match?.[1]) {
    throw new Error(`Expected a board URL but received '${url}'`)
  }

  return match[1]
}

async function parseCreatedCaptureId(response: APIResponse): Promise<string> {
  const payload = await response.json() as { id?: string }
  if (!payload.id) {
    throw new Error('Capture creation response did not contain an id')
  }

  return payload.id
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'first-run')
})

test('first-run path should guide home to capture to review to execute to board', async ({ page, request }) => {
  test.setTimeout(90_000)

  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardName = `First Run ${seed}`
  const cardTitle = `First-run card ${seed}`
  const captureText = `- [ ] ${cardTitle}`

  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await expect(page.getByText('What is Home for?')).toBeVisible()
  await expect(
    page.getByText('No boards yet. Start setup from Home or Today so captures and review can land somewhere useful.')
  ).toBeVisible()

  await page.locator('.td-home__hero-actions').getByRole('button', { name: 'Open Review' }).click()
  await expect(page).toHaveURL(/\/workspace\/review$/)
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
  await expect(page.getByText('No proposals need review yet')).toBeVisible()

  await page.getByRole('button', { name: 'Back to Home' }).click()
  await expect(page).toHaveURL(/\/workspace\/home$/)

  await page.locator('.td-home__hero-actions').getByRole('button', { name: 'Open Today' }).click()
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
    && /\/api\/capture\/items$/i.test(response.url())
    && response.ok())

  await captureModal.getByPlaceholder('Capture a thought, task, or follow-up...').fill(captureText)
  await captureModal.getByRole('button', { name: 'Save Capture' }).click()
  const captureId = await parseCreatedCaptureId(await createCaptureResponse)
  await expect(captureModal).toHaveCount(0)

  await boardActionRail.getByRole('button', { name: 'Open Inbox' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))
  await expect(page.getByRole('heading', { name: 'Inbox', exact: true })).toBeVisible()
  await expect(page.getByText(`Showing capture items linked to board ${boardId}.`)).toBeVisible()
  await expect(page.getByText('What is Inbox for?')).toBeVisible()

  const captureRow = page.locator('.td-inbox-row').filter({ hasText: cardTitle }).first()
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
  const openProposalButton = page.getByRole('button', { name: 'Open Proposal' })
  await expect(openProposalButton).toBeVisible()
  await openProposalButton.click()

  await expect(page).toHaveURL(new RegExp(`/workspace/review\\?boardId=${boardId}#proposal-${proposalId}`))
  await expect(page.getByText(`Showing proposals for board ${boardId}.`)).toBeVisible()
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve' }).click()
  await expect(proposalCard.getByText('Approved')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Execute' }).click()
  await expect(proposalCard.getByText('Applied')).toBeVisible()

  const createdCard = await waitForCardWithTitle(request, auth, boardId, cardTitle)

  await proposalCard.getByRole('button', { name: 'Open Board' }).click()
  await expect(page).toHaveURL(new RegExp(`/workspace/boards/${boardId}$`))
  const card = page.locator('[data-card-id]').filter({ hasText: createdCard.title }).first()
  await expect(card).toBeVisible()
  await card.getByRole('heading', { name: createdCard.title, exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
  await expect(page.getByText('Capture Origin')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open Capture' })).toHaveAttribute('href', `/workspace/inbox#capture-${captureId}`)
  await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
    'href',
    `/workspace/review?boardId=${boardId}#proposal-${proposalId}`,
  )

  if (triageRunId) {
    await expect(page.getByText(`Triage run: ${triageRunId}`)).toBeVisible()
  }
})

test('home should recover from loading and error states on first-run summary refresh', async ({ page }) => {
  let requestCount = 0
  await page.route('**/api/workspace/home', async (route) => {
    requestCount += 1
    await new Promise((resolve) => setTimeout(resolve, 300))

    if (requestCount === 1) {
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
  await expect(page.getByText('Loading your workspace summary...')).toBeVisible()
  await expect(page.getByRole('alert')).toContainText('Temporary home summary failure')

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await expect(page.getByText('Setup loop')).toBeVisible()
  await expect(page.getByText('No boards yet. Start setup from Home or Today so captures and review can land somewhere useful.')).toBeVisible()
})
