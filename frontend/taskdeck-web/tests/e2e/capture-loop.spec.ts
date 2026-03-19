import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  listBoardCards,
  waitForCardWithTitle,
  waitForProposalCreated,
} from './support/captureFlow'

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'capture-loop')
})

test('capture triage should create proposal and apply card with provenance links', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Capture Loop',
    description: 'capture triage e2e board',
    columnNamePrefix: 'Inbox',
  })
  const checklistTaskTitle = `Capture loop card ${seed}`
  const captureText = `- [ ] ${checklistTaskTitle}`

  const createdCapture = await createCaptureItem(request, auth, boardId, captureText)
  const captureId = createdCapture.id

  await page.goto('/workspace/inbox')
  const captureRow = page.locator('.td-inbox-row').filter({ hasText: checklistTaskTitle }).first()
  await expect(captureRow).toBeVisible()
  await captureRow.click()

  const triageButton = page.locator('.td-inbox-detail__actions button').filter({ hasText: 'Start Triage' }).first()
  await expect(triageButton).toBeVisible()
  await triageButton.click()

  const triagedCapture = await waitForProposalCreated(request, auth, captureId)
  const proposalId = triagedCapture.provenance?.proposalId
  const triageRunId = triagedCapture.provenance?.triageRunId
  expect(proposalId).toBeTruthy()
  const cardsAfterTriage = await listBoardCards(request, auth, boardId)
  expect(cardsAfterTriage.length).toBe(0)

  await page.getByRole('button', { name: 'Refresh Detail' }).click()
  const openProposalButton = page.getByRole('button', { name: 'Open Proposal' })
  await expect(openProposalButton).toBeVisible()
  await openProposalButton.click()

  await expect(page).toHaveURL(new RegExp(`/workspace/review\\?boardId=${boardId}#proposal-${proposalId}`))
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve' }).click()
  await expect(proposalCard.getByText('Approved')).toBeVisible()
  const cardsAfterApprove = await listBoardCards(request, auth, boardId)
  expect(cardsAfterApprove.length).toBe(0)

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Execute' }).click()
  await expect(proposalCard.getByText('Applied')).toBeVisible()

  const createdCard = await waitForCardWithTitle(request, auth, boardId, checklistTaskTitle)

  await page.goto(`/workspace/boards/${boardId}`)
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
