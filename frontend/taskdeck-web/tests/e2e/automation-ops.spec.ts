import type { APIRequestContext } from '@playwright/test'
import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import { assertOk } from './support/httpAsserts'
import { pollUntil } from './support/polling'

interface ChatMessageDto {
  proposalId: string | null
}

interface ChatSessionDto {
  recentMessages: ChatMessageDto[]
}

interface ProposalDto {
  id: string
  summary: string
}

async function waitForProposalInSession(
  request: APIRequestContext,
  token: string,
  sessionId: string,
): Promise<string> {
  const sessionWithProposal = await pollUntil(
    async () => {
      const response = await request.get(`${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(sessionId)}`, {
        headers: { Authorization: `Bearer ${token}` },
      })
      await assertOk(response, `fetch chat session ${sessionId}`)
      return await response.json() as ChatSessionDto
    },
    (session) => session.recentMessages.some((m) => !!m.proposalId),
    { description: 'proposal reference in chat session' },
  )

  const proposalId = sessionWithProposal.recentMessages.find((m) => !!m.proposalId)?.proposalId
  if (!proposalId) {
    throw new Error('Expected a proposal reference in chat session')
  }

  return proposalId
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await registerAndAttachSession(page, request, 'ops')
})

test('chat session should create and return assistant response', async ({ page }) => {
  await page.goto('/workspace/automations/chat')

  await page.getByPlaceholder('Session title').fill(`Session ${Date.now()}`)
  await page.getByRole('button', { name: 'Create Session' }).click()

  await expect(page.getByText('Session', { exact: false }).first()).toBeVisible()

  await page.getByPlaceholder('Describe an automation instruction...').fill('summarize this board status')
  await page.getByRole('button', { name: 'Send Message' }).click()

  await expect(page.getByText('Assistant').first()).toBeVisible()
})

test('ops cli should run health.check template', async ({ page }) => {
  await page.goto('/workspace/ops/cli')

  await expect(page.getByRole('heading', { name: 'Ops Console' })).toBeVisible()

  const templateInput = page.getByRole('combobox', { name: 'Command template' })
  await templateInput.fill('health.check')
  await page.getByRole('button', { name: 'Run Template' }).click()

  await expect(page.getByText('Health check: OK')).toBeVisible()
})

test('chat proposal flow should create, approve, and execute proposal', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Automation E2E',
    description: 'automation e2e board',
    columnNamePrefix: 'Backlog',
  })
  const uniqueCardTitle = `E2E Card ${seed}`

  await page.goto('/workspace/automations/chat')
  await page.getByPlaceholder('Session title').fill(`Proposal Session ${seed}`)
  await page.getByPlaceholder('Board context (optional)').fill(boardId)
  await page.getByRole('button', { name: 'Create Session' }).click()

  const sessionId = await page.locator('.td-chat-meta').first().getAttribute('data-session-id')
  if (!sessionId) {
    throw new Error('Expected chat session header to expose data-session-id')
  }

  await page.getByPlaceholder('Describe an automation instruction...').fill(`create card "${uniqueCardTitle}"`)
  const requestProposalCheckbox = page.getByRole('checkbox', { name: 'Request proposal generation' })
  await requestProposalCheckbox.check()
  await expect(requestProposalCheckbox).toBeChecked()
  await page.getByRole('button', { name: 'Send Message' }).click()

  const proposalId = await waitForProposalInSession(request, auth.token, sessionId)
  await expect(page.locator('.td-message-proposal').filter({ hasText: proposalId }).first()).toBeVisible()

  const proposalResponse = await request.get(`${API_BASE_URL}/automation/proposals/${encodeURIComponent(proposalId)}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
  })
  await assertOk(proposalResponse, `fetch proposal ${proposalId}`)
  const proposal = await proposalResponse.json() as ProposalDto

  await page.goto('/workspace/review')
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()

  const proposalCard = page.locator('.td-review-card').filter({ hasText: proposal.summary }).first()
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve for board' }).click()
  await expect(proposalCard.getByText('Approved')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Apply to board' }).click()
  await expect(proposalCard.getByText('Applied')).toBeVisible()
})
