import type { APIRequestContext, Page } from '@playwright/test'
import { expect } from '@playwright/test'

interface AuthUser {
  id: string
  username: string
  email: string
}

export interface AuthResult {
  token: string
  user: AuthUser
}

export interface AttachSessionOptions {
  theme?: 'paper' | 'legacy'
}

export const API_BASE_URL = process.env.TASKDECK_E2E_API_BASE_URL ?? 'http://localhost:5000/api'
export const API_ORIGIN = new URL(API_BASE_URL).origin

function buildSessionInitPayload(auth: AuthResult): { token: string; session: { userId: string; username: string; email: string } } {
  return {
    token: auth.token,
    session: {
      userId: auth.user.id,
      username: auth.user.username,
      email: auth.user.email,
    },
  }
}

export async function registerUserSession(
  request: APIRequestContext,
  scope: string,
): Promise<AuthResult> {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const username = `e2e-${scope}-${unique}`
  const email = `${username}@taskdeck.local`
  const password = 'E2ePassword123!'

  const response = await request.post(`${API_BASE_URL}/auth/register`, {
    data: { username, email, password },
  })

  expect(response.ok()).toBeTruthy()
  return await response.json() as AuthResult
}

export async function attachSessionToPage(
  page: Page,
  auth: AuthResult,
  options: AttachSessionOptions = {},
): Promise<void> {
  const payload = {
    ...buildSessionInitPayload(auth),
    paperMode: options.theme === 'legacy' ? 'off' as const : 'paper' as const,
  }

  await page.addInitScript((initPayload: {
    token: string
    session: { userId: string; username: string; email: string }
    paperMode: 'paper' | 'off'
  }) => {
    localStorage.setItem('taskdeck_token', initPayload.token)
    localStorage.setItem('taskdeck_session', JSON.stringify(initPayload.session))
    // Enable all feature flags so E2E tests can reach gated routes
    localStorage.setItem('taskdeck_feature_flags', JSON.stringify({
      newShell: true,
      newAuth: true,
      newAccess: true,
      newActivity: true,
      newOps: true,
      newAutomation: true,
      newArchive: true,
    }))
    // Paper is the product default. Legacy selector specs opt out explicitly via
    // `{ theme: 'legacy' }`; preserve any mode that a theme-specific spec seeded earlier.
    if (!localStorage.getItem('td.paper.mode.v2')) {
      localStorage.setItem('td.paper.mode.v2', initPayload.paperMode)
    }
  }, payload)
}

export async function registerAndAttachSession(
  page: Page,
  request: APIRequestContext,
  scope: string,
  options: AttachSessionOptions = {},
): Promise<AuthResult> {
  const auth = await registerUserSession(request, scope)
  await attachSessionToPage(page, auth, options)
  return auth
}
