import { describe, expect, it } from 'vitest'
import {
  isTerminalStatus,
  normalizeRunStatus,
  normalizeScopeType,
} from '../../types/agent'

describe('normalizeScopeType', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeScopeType(0)).toBe('Workspace')
    expect(normalizeScopeType(1)).toBe('Board')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeScopeType('workspace' as any)).toBe('Workspace')
    expect(normalizeScopeType('BOARD' as any)).toBe('Board')
  })

  it('falls back to Workspace for unknown values', () => {
    expect(normalizeScopeType(99 as any)).toBe('Workspace')
    expect(normalizeScopeType(-1 as any)).toBe('Workspace')
    expect(normalizeScopeType('unknown' as any)).toBe('Workspace')
  })
})

describe('normalizeRunStatus', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeRunStatus(0)).toBe('Queued')
    expect(normalizeRunStatus(1)).toBe('GatheringContext')
    expect(normalizeRunStatus(6)).toBe('Completed')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeRunStatus('completed' as any)).toBe('Completed')
    expect(normalizeRunStatus('FAILED' as any)).toBe('Failed')
  })

  it('falls back to Queued for unknown values', () => {
    expect(normalizeRunStatus(99 as any)).toBe('Queued')
    expect(normalizeRunStatus(-1 as any)).toBe('Queued')
    expect(normalizeRunStatus('unknown' as any)).toBe('Queued')
  })
})

describe('isTerminalStatus', () => {
  it('recognizes terminal statuses', () => {
    expect(isTerminalStatus('Completed')).toBe(true)
    expect(isTerminalStatus('Failed')).toBe(true)
    expect(isTerminalStatus('Cancelled')).toBe(true)
  })

  it('treats active statuses as non-terminal', () => {
    expect(isTerminalStatus('Queued')).toBe(false)
    expect(isTerminalStatus('Planning')).toBe(false)
  })
})
