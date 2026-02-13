import type { APIRequestContext, Page } from '@playwright/test'
import { expect, test } from '@playwright/test'

interface AuthUser {
  id: string
  username: string
  email: string
}

interface AuthResult {
  token: string
  user: AuthUser
}

interface BoardDto {
  id: string
}

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

const API_BASE_URL = 'http://localhost:5000/api'

async function bootstrapAuthenticatedSession(page: Page, request: APIRequestContext): Promise<AuthResult> {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const username = `e2e-ops-${unique}`
  const email = `${username}@taskdeck.local`
  const password = 'E2ePassword123!'

  const response = await request.post(`${API_BASE_URL}/auth/register`, {
    data: { username, email, password },
  })
  expect(response.ok()).toBeTruthy()

  const auth = await response.json() as AuthResult
  await page.addInitScript((payload: { token: string; session: { userId: string; username: string; email: string } }) => {
    localStorage.setItem('taskdeck_token', payload.token)
    localStorage.setItem('taskdeck_session', JSON.stringify(payload.session))
  }, {
    token: auth.token,
    session: {
      userId: auth.user.id,
      username: auth.user.username,
      email: auth.user.email,
    },
  })

  return auth
}

async function createBoardWithColumn(request: APIRequestContext, token: string, seed: string): Promise<string> {
  const authHeader = { Authorization: `Bearer ${token}` }

  const createBoardResponse = await request.post(`${API_BASE_URL}/boards`, {
    headers: authHeader,
    data: {
      name: `Automation E2E ${seed}`,
      description: 'automation e2e board',
    },
  })
  expect(createBoardResponse.ok()).toBeTruthy()
  const board = await createBoardResponse.json() as BoardDto

  const createColumnResponse = await request.post(`${API_BASE_URL}/boards/${board.id}/columns`, {
    headers: authHeader,
    data: {
      boardId: board.id,
      name: `Backlog ${seed}`,
      hasWipLimit: null,
      wipLimit: null,
    },
  })
  expect(createColumnResponse.ok()).toBeTruthy()

  return board.id
}

async function waitForProposalInSession(
  request: APIRequestContext,
  token: string,
  sessionId: string,
): Promise<string> {
  for (let i = 0; i < 30; i += 1) {
    const response = await request.get(`${API_BASE_URL}/llm/chat/sessions/${encodeURIComponent(sessionId)}`, {
      headers: { Authorization: `Bearer ${token}` },
    })
    expect(response.ok()).toBeTruthy()

    const session = await response.json() as ChatSessionDto
    const proposalId = session.recentMessages.find((m) => !!m.proposalId)?.proposalId
    if (proposalId) {
      return proposalId
    }

    await new Promise((resolve) => setTimeout(resolve, 500))
  }

  throw new Error('Timed out waiting for proposal reference in chat session')
}

let auth: AuthResult

test.beforeEach(async ({ page, request }) => {
  auth = await bootstrapAuthenticatedSession(page, request)
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

  const templateSelect = page.locator('select').first()
  await templateSelect.selectOption('health.check')
  await page.getByRole('button', { name: 'Run Template' }).click()

  await expect(page.getByText('Health check: OK')).toBeVisible()
})

test('chat proposal flow should create, approve, and execute proposal', async ({ page, request }) => {
  const seed = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const boardId = await createBoardWithColumn(request, auth.token, seed)
  const uniqueCardTitle = `E2E Card ${seed}`

  await page.goto('/workspace/automations/chat')
  await page.getByPlaceholder('Session title').fill(`Proposal Session ${seed}`)
  await page.getByPlaceholder('Board ID (optional)').fill(boardId)
  await page.getByRole('button', { name: 'Create Session' }).click()

  const sessionMetaText = await page.locator('.td-chat-meta').first().innerText()
  const sessionId = sessionMetaText.replace('Session ', '').trim()

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
  expect(proposalResponse.ok()).toBeTruthy()
  const proposal = await proposalResponse.json() as ProposalDto

  await page.goto('/workspace/automations/proposals')
  await expect(page.getByRole('heading', { name: 'Automations' })).toBeVisible()

  const proposalCard = page.locator('.td-proposal-card').filter({ hasText: proposal.summary }).first()
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve' }).click()
  await expect(proposalCard.getByText('Approved')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Execute' }).click()
  await expect(proposalCard.getByText('Applied')).toBeVisible()
})
