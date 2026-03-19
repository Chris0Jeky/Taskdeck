import type { APIRequestContext } from '@playwright/test'
import { API_BASE_URL, type AuthResult } from './authSession'
import { assertOk } from './httpAsserts'
import { pollUntil } from './polling'

export interface CaptureProvenanceDto {
  captureItemId: string
  triageRunId: string | null
  proposalId: string | null
  promptVersion: string | null
}

export interface CaptureItemDto {
  id: string
  status: number | string
  provenance?: CaptureProvenanceDto | null
}

export interface CardDto {
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

export async function createCaptureItem(
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

export async function waitForProposalCreated(
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

export async function listBoardCards(
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

export async function waitForCardWithTitle(
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
    throw new Error(`Expected card '${expectedTitle}' to appear on board ${boardId}, but it was not present after polling`)
  }

  return matchingCard
}
