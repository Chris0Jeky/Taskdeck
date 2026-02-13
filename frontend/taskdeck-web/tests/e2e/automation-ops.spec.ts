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

  await page.getByPlaceholder('Describe an automation instruction...').fill(`create card "${uniqueCardTitle}"`)
  await page.getByLabel('Request proposal generation').check()
  await page.getByRole('button', { name: 'Send Message' }).click()

  await expect(page.locator('.td-message-proposal').last()).toContainText('Proposal:')

  await page.goto('/workspace/automations/proposals')
  await expect(page.getByRole('heading', { name: 'Automations' })).toBeVisible()

  const proposalCard = page.locator('.td-proposal-card').filter({ hasText: uniqueCardTitle }).first()
  await expect(proposalCard).toBeVisible()

  await proposalCard.getByRole('button', { name: 'Approve' }).click()
  await expect(proposalCard.getByText('Approved')).toBeVisible()

  page.once('dialog', (dialog) => dialog.accept())
  await proposalCard.getByRole('button', { name: 'Execute' }).click()
  await expect(proposalCard.getByText('Applied')).toBeVisible()
})
