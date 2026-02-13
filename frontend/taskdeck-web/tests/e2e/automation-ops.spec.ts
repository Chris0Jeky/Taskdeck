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

const API_BASE_URL = 'http://localhost:5000/api'

async function bootstrapAuthenticatedSession(page: Page, request: APIRequestContext) {
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
}

test.beforeEach(async ({ page, request }) => {
  await bootstrapAuthenticatedSession(page, request)
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
