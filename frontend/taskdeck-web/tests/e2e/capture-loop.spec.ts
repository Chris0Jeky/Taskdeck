import type { APIRequestContext } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'
import { pollUntil } from './support/polling'

interface CaptureProvenanceDto {
  captureItemId: string
  triageRunId: string | null
  proposalId: string | null
  promptVersion: string | null
}

interface CaptureItemDto {
  id: string
  status: number | string
  provenance?: CaptureProvenanceDto | null
}

interface CardDto {
  id: string
  title: string
}

const captureStatus = {
  New: 0,
  Triaging: 1,
  Triaged: 2,
  ProposalCreated: 3,
  Converted: 4,
  Ignored: 5,
  Failed: 6,
} as const

function isStatus(value: number | string, target: keyof typeof captureStatus): boolean {
  return value === captureStatus[target] || value === target
}

async function createCaptureItem(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  captureText: string,
): Promise<CaptureItemDto> {
  const response = await request.post(`${API_BASE_URL}/capture/items`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: {
      boardId,
      text: captureText,
      source: 'Typed',
    },
  })

  await assertOk(response, `Create capture item for board ${boardId}`)
  return await response.json() as CaptureItemDto
}

async function waitForProposalCreated(
  request: APIRequestContext,
  auth: AuthResult,
  captureId: string,
): Promise<CaptureItemDto> {
  return await pollUntil(async () => {
    const response = await request.get(`${API_BASE_URL}/capture/items/${encodeURIComponent(captureId)}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    await assertOk(response, `Fetch capture item ${captureId}`)
    return await response.json() as CaptureItemDto
  }, (item) => isStatus(item.status, 'ProposalCreated') && !!item.provenance?.proposalId, {
    description: `capture triage for ${captureId} completed`,
    abortIf: (item) => (isStatus(item.status, 'Failed') ? `Capture triage failed for ${captureId}` : undefined),
  })
}

async function listBoardCards(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
): Promise<CardDto[]> {
  const response = await request.get(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })
  await assertOk(response, `List cards for board ${boardId}`)
  return await response.json() as CardDto[]
}

async function waitForCardWithTitle(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  expectedTitle: string,
): Promise<CardDto> {
  const cards = await pollUntil(
    () => listBoardCards(request, auth, boardId),
    (cardsList) => cardsList.some((card) => card.title === expectedTitle),
    {
      description: `card '${expectedTitle}' to appear on board ${boardId}`,
      timeoutMs: 40000,
    },
  )

  const matchingCard = cards.find((card) => card.title === expectedTitle)
  if (!matchingCard) {
    throw new Error(`Expected card '${expectedTitle}' to appear on board ${boardId} but it was not present`)
  }

  return matchingCard
}

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

  await expect(page).toHaveURL(new RegExp(`/workspace/review#proposal-${proposalId}`))
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
  await card.click()

  await expect(page.getByRole('heading', { name: 'Edit Card' })).toBeVisible()
  await expect(page.getByText('Capture Origin')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open Capture' })).toHaveAttribute('href', `/workspace/inbox#capture-${captureId}`)
  await expect(page.getByRole('link', { name: 'Open Proposal' })).toHaveAttribute(
    'href',
    `/workspace/review#proposal-${proposalId}`,
  )

  if (triageRunId) {
    await expect(page.getByText(`Triage run: ${triageRunId}`)).toBeVisible()
  }
})
