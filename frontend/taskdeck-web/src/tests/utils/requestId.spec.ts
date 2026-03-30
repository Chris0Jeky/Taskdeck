import { describe, expect, it } from 'vitest'
import { createRequestId } from '../../utils/requestId'

describe('requestId utils', () => {
  it('uses randomUUID when provided', () => {
    const id = createRequestId({
      randomUUID: () => 'uuid-value',
    })

    expect(id).toBe('uuid-value')
  })

  it('falls back to deterministic timestamp+random format', () => {
    const id = createRequestId({
      now: () => 1700000000000,
      random: () => 0.5,
      randomUUID: undefined,
    })

    expect(id).toBe('req-loyw3v28-80000000')
  })

  it('creates request IDs with req- prefix', () => {
    const id = createRequestId({
      now: () => 1,
      random: () => 0,
      randomUUID: undefined,
    })

    expect(id.startsWith('req-')).toBe(true)
  })

  it('uses crypto.randomUUID when no sources provided and crypto is available', () => {
    const id = createRequestId()

    // In test env, crypto.randomUUID is available so it returns a UUID
    expect(typeof id).toBe('string')
    expect(id.length).toBeGreaterThan(0)
  })

  it('uses default Date.now and Math.random when randomUUID is explicitly undefined', () => {
    const id = createRequestId({ randomUUID: undefined })

    expect(id.startsWith('req-')).toBe(true)
    expect(id.length).toBeGreaterThan(4)
  })
})
