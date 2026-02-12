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
})
