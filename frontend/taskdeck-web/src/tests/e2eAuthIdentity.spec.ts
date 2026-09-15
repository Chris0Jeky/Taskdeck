import { describe, expect, it } from 'vitest'
import authSessionSource from '../../tests/e2e/support/authSession.ts?raw'

describe('E2E session identity', () => {
  it('keeps the random username segment fixed-width for stable visual geometry', () => {
    expect(authSessionSource).toContain(
      "String(Math.floor(Math.random() * 1_000_000)).padStart(6, '0')",
    )
  })
})
