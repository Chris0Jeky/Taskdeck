import type { APIRequestContext } from '@playwright/test'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { registerUserSession } from '../../tests/e2e/support/authSession'

describe('E2E session identity', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('keeps the random username segment fixed-width for stable visual geometry', async () => {
    vi.spyOn(Date, 'now').mockReturnValue(1_700_000_000_000)
    vi.spyOn(Math, 'random').mockReturnValue(1 / 1_000_000)

    let postedData: Record<string, unknown> | undefined
    const request = {
      post: vi.fn(async (_url: string, options?: { data?: Record<string, unknown> }) => {
        postedData = options?.data
        return {
          ok: () => true,
          json: async () => ({
            token: 'test-token',
            user: {
              id: 'test-user',
              username: postedData?.username,
              email: postedData?.email,
            },
          }),
        }
      }),
    } as unknown as APIRequestContext

    await registerUserSession(request, 'visual')

    expect(postedData).toMatchObject({
      username: 'e2e-visual-1700000000000-000001',
      email: 'e2e-visual-1700000000000-000001@taskdeck.local',
    })
  })
})
