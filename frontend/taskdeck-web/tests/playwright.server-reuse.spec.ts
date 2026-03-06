import { describe, expect, it } from 'vitest'

import { resolveReuseExistingServer } from '../playwright.server-reuse'

describe('playwright server reuse resolution', () => {
  it('reuses existing servers locally by default when no fresh runtime is required', () => {
    expect(resolveReuseExistingServer({})).toBe(true)
  })

  it('disables reuse in CI by default', () => {
    expect(resolveReuseExistingServer({ CI: 'true' })).toBe(false)
  })

  it('disables reuse when demo live-provider overrides require a fresh backend', () => {
    expect(
      resolveReuseExistingServer(
        {
          TASKDECK_RUN_DEMO: '1',
          Llm__Gemini__ApiKey: 'gemini-key',
        },
        { requiresFreshServer: true },
      ),
    ).toBe(false)
  })

  it('still allows an explicit reuse override when the caller intentionally wants it', () => {
    expect(
      resolveReuseExistingServer(
        {
          TASKDECK_RUN_DEMO: '1',
          TASKDECK_E2E_REUSE_EXISTING_SERVER: '1',
        },
        { requiresFreshServer: true },
      ),
    ).toBe(true)
  })
})
