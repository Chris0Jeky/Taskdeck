import { describe, it, expect } from 'vitest'
import { normalizeRestoreStatus } from '../../utils/archive'

describe('normalizeRestoreStatus', () => {
  it('maps numeric index to status string', () => {
    expect(normalizeRestoreStatus(0)).toBe('Available')
    expect(normalizeRestoreStatus(1)).toBe('Restored')
    expect(normalizeRestoreStatus(2)).toBe('Expired')
    expect(normalizeRestoreStatus(3)).toBe('Conflict')
  })

  it('falls back to Available for out-of-range index', () => {
    expect(normalizeRestoreStatus(99)).toBe('Available')
  })

  it('normalizes case-insensitive string status', () => {
    expect(normalizeRestoreStatus('restored' as any)).toBe('Restored')
    expect(normalizeRestoreStatus('EXPIRED' as any)).toBe('Expired')
  })

  it('falls back to Available for unknown string', () => {
    expect(normalizeRestoreStatus('unknown' as any)).toBe('Available')
  })
})
