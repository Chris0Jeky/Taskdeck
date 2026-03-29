import { describe, it, expect } from 'vitest'
import { normalizeCommandRunStatus } from '../../utils/ops'

describe('normalizeCommandRunStatus', () => {
  it('maps numeric index 0 to Queued', () => {
    expect(normalizeCommandRunStatus(0)).toBe('Queued')
  })

  it('maps numeric index 1 to Running', () => {
    expect(normalizeCommandRunStatus(1)).toBe('Running')
  })

  it('maps numeric index 2 to Completed', () => {
    expect(normalizeCommandRunStatus(2)).toBe('Completed')
  })

  it('maps numeric index 3 to Failed', () => {
    expect(normalizeCommandRunStatus(3)).toBe('Failed')
  })

  it('maps numeric index 4 to TimedOut', () => {
    expect(normalizeCommandRunStatus(4)).toBe('TimedOut')
  })

  it('maps numeric index 5 to Cancelled', () => {
    expect(normalizeCommandRunStatus(5)).toBe('Cancelled')
  })

  it('falls back to Failed for out-of-range numeric index', () => {
    expect(normalizeCommandRunStatus(99)).toBe('Failed')
  })

  it('normalizes case-insensitive string status', () => {
    expect(normalizeCommandRunStatus('queued' as any)).toBe('Queued')
    expect(normalizeCommandRunStatus('RUNNING' as any)).toBe('Running')
    expect(normalizeCommandRunStatus('completed' as any)).toBe('Completed')
  })

  it('falls back to Failed for unknown string status', () => {
    expect(normalizeCommandRunStatus('unknown' as any)).toBe('Failed')
  })
})
