import type { APIRequestContext } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'

interface ImportResultDto {
  success: boolean
  boardId: string | null
}

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

async function createBoardWithColumn(request: APIRequestContext, auth: AuthResult, seed: string): Promise<string> {
  const authHeader = { Authorization: `Bearer ${auth.token}` }
  const importResponse = await request.post(`${API_BASE_URL}/import/boards?userId=${encodeURIComponent(auth.user.id)}`, {
    headers: authHeader,
    data: {
      name: `Capture Loop ${seed}`,
      description: 'capture triage e2e board',
      columns: [
        {
          name: `Inbox ${seed}`,
          position: 0,
          wipLimit: null,
        },
      ],
      cards: [],
      labels: [],
    },
  })

  expect(importResponse.ok()).toBeTruthy()
  const importResult = await importResponse.json() as ImportResultDto
  expect(importResult.success).toBeTruthy()
  expect(importResult.boardId).toBeTruthy()
  return importResult.boardId!
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

  expect(response.ok()).toBeTruthy()
  return await response.json() as CaptureItemDto
}

async function waitForProposalCreated(
  request: APIRequestContext,
  auth: AuthResult,
  captureId: string,
): Promise<CaptureItemDto> {
  for (let i = 0; i < 40; i += 1) {
    const response = await request.get(`${API_BASE_URL}/capture/items/${encodeURIComponent(captureId)}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    expect(response.ok()).toBeTruthy()

    const item = await response.json() as CaptureItemDto
    if (isStatus(item.status, 'ProposalCreated') && item.provenance?.proposalId) {
      return item
    }

    if (isStatus(item.status, 'Failed')) {
      throw new Error(`Capture triage failed for ${captureId}`)
    }

    await new Promise((resolve) => setTimeout(resolve, 500))
  }

  throw new Error(`Timed out waiting for capture proposal creation for ${captureId}`)
}

async function listBoardCards(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
): Promise<CardDto[]> {
  const response = await request.get(`${API_BASE_URL}/boards/${encodeURIComponent(boardId)}/cards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })
  expect(response.ok()).toBeTruthy()
  return await response.json() as CardDto[]
}

async function waitForCardWithTitle(
  request: APIRequestContext,
  auth: AuthResult,
  boardId: string,
  expectedTitle: string,
): Promise<CardDto> {
  for (let i = 0; i < 80; i += 1) {
    const cards = await listBoardCards(request, auth, boardId)
    const matchingCard = cards.find((card) => card.title === expectedTitle)
    if (matchingCard) {
      return matchingCard
    }

    await new Promise((resolve) => setTimeout(resolve, 500))
  }

  throw new Error(`Timed out waiting for card '${expectedTitle}' in board ${boardId}`)
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'capture-loop')
})

test('capture triage should create proposal and apply card with provenance links', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed)
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

  await expect(page).toHaveURL(new RegExp(`/workspace/automations/proposals#proposal-${proposalId}`))
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
    `/workspace/automations/proposals#proposal-${proposalId}`,
  )

  if (triageRunId) {
    await expect(page.getByText(`Triage run: ${triageRunId}`)).toBeVisible()
  }
})
