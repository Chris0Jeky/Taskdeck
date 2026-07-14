import { expect, test } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  listBoardCards,
  waitForCardWithTitle,
  waitForProposalCreated,
} from './support/captureFlow'
import { assertOk } from './support/httpAsserts'

let paperAuth: AuthResult

test.describe('Paper capture-review-apply loop', () => {
  test.beforeEach(async ({ page, request }) => {
    paperAuth = await registerAndAttachSession(page, request, 'capture-loop-paper')
  })

  test('captures, reviews, and applies a card through the Paper DOM', async ({ page, request }) => {
    const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
    const boardId = await createBoardWithColumn(request, paperAuth, seed, {
      boardNamePrefix: 'Paper Capture Loop',
      description: 'Paper capture triage e2e board',
      columnNamePrefix: 'Inbox',
    })
    const cardTitle = `Paper capture loop card ${seed}`

    await page.goto(`/workspace/boards/${boardId}`)
    await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
    await page.getByRole('button', { name: 'Capture here' }).click()
    await expect(page).toHaveURL(new RegExp(`/workspace/inbox\\?boardId=${boardId}$`))

    const captureBody = page.getByRole('textbox', { name: 'Capture body' })
    await expect(captureBody).toBeVisible()
    const createCaptureResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST'
      && /\/api\/capture\/items$/i.test(response.url()))
    await captureBody.fill(`- [ ] ${cardTitle}`)
    await page.getByRole('button', { name: 'Capture' }).click()
    const createCaptureResponse = await createCaptureResponsePromise
    await assertOk(createCaptureResponse, 'create Paper capture')
    const capturePayload = await createCaptureResponse.json() as { id?: string }
    expect(capturePayload.id).toBeTruthy()

    const captureRow = page.locator('.paper-triage__row').filter({ hasText: cardTitle }).first()
    await expect(captureRow).toBeVisible()
    await captureRow.getByRole('button', { name: 'Accept' }).click()

    const triagedCapture = await waitForProposalCreated(request, paperAuth, capturePayload.id!)
    const proposalId = triagedCapture.provenance?.proposalId
    expect(proposalId).toBeTruthy()
    expect(await listBoardCards(request, paperAuth, boardId)).toHaveLength(0)

    await page.getByRole('link', { name: /Review$/ }).click()
    await expect(page).toHaveURL(/\/workspace\/review$/)
    await expect(page.getByTestId('paper-review-view')).toBeVisible()
    await expect(
      page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` }),
    ).toBeVisible({ timeout: 15_000 })

    const approveResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST'
      && response.url().endsWith(`/automation/proposals/${proposalId}/approve`))
    await page.getByTestId('decision-apply').click()
    await assertOk(await approveResponsePromise, `approve Paper proposal ${proposalId}`)
    expect(await listBoardCards(request, paperAuth, boardId)).toHaveLength(0)

    const executeResponsePromise = page.waitForResponse((response) =>
      response.request().method() === 'POST'
      && response.url().endsWith(`/automation/proposals/${proposalId}/execute`))
    page.once('dialog', (dialog) => dialog.accept())
    await page.getByTestId('decision-apply').click()
    await assertOk(await executeResponsePromise, `execute Paper proposal ${proposalId}`)
    const createdCard = await waitForCardWithTitle(request, paperAuth, boardId, cardTitle)

    await page.goto(`/workspace/boards/${boardId}`)
    await expect(page.getByTestId('paper-board-lanes')).toBeVisible()
    await expect(page.getByRole('button', { name: `Card ${createdCard.title}` })).toBeVisible()
  })
})

test.describe('Legacy provenance selector coverage', () => {
  let auth: AuthResult

  test.beforeEach(async ({ page, request }) => {
    auth = await registerAndAttachSession(page, request, 'capture-loop-legacy', { theme: 'legacy' })
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
  const captureRow = page.locator('[data-testid="inbox-item"]').filter({ hasText: checklistTaskTitle }).first()
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
  const openProposalButton = page.getByRole('button', { name: 'Open in Review' })
  await expect(openProposalButton).toBeVisible()
  await openProposalButton.click()

  await expect(page).toHaveURL(new RegExp(`/workspace/review\\?boardId=${boardId}#proposal-${proposalId}`))
  const proposalCard = page.locator(`#proposal-${proposalId}`)
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved, ready to apply')).toBeVisible()
  const cardsAfterApprove = await listBoardCards(request, auth, boardId)
  expect(cardsAfterApprove.length).toBe(0)

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
  await expect(proposalCard).not.toBeVisible()

  const createdCard = await waitForCardWithTitle(request, auth, boardId, checklistTaskTitle)

  await page.goto(`/workspace/boards/${boardId}`)
  const card = page.locator('[data-card-id]').filter({ hasText: createdCard.title }).first()
  await expect(card).toBeVisible()
  await card.getByRole('heading', { name: createdCard.title, exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
  await expect(page.getByText('Capture Origin')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open Capture' })).toHaveAttribute(
    'href',
    `/workspace/inbox?boardId=${boardId}#capture-${captureId}`,
  )
  await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
    'href',
    `/workspace/review?boardId=${boardId}#proposal-${proposalId}`,
  )

  if (triageRunId) {
    await expect(page.getByText(`Triage run: ${triageRunId}`)).toBeVisible()
  }
})
})
