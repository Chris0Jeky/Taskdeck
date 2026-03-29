import { describe, it, expect } from 'vitest'
import {
  normalizeProposalStatus,
  normalizeProposalSourceType,
  normalizeProposalRiskLevel,
} from '../../utils/automation'

describe('normalizeProposalStatus', () => {
  it('maps numeric index to status string', () => {
    expect(normalizeProposalStatus(0)).toBe('PendingReview')
    expect(normalizeProposalStatus(1)).toBe('Approved')
    expect(normalizeProposalStatus(2)).toBe('Rejected')
    expect(normalizeProposalStatus(3)).toBe('Applied')
    expect(normalizeProposalStatus(4)).toBe('Failed')
    expect(normalizeProposalStatus(5)).toBe('Expired')
  })

  it('falls back to PendingReview for out-of-range index', () => {
    expect(normalizeProposalStatus(99)).toBe('PendingReview')
  })

  it('normalizes case-insensitive string status', () => {
    expect(normalizeProposalStatus('approved' as any)).toBe('Approved')
    expect(normalizeProposalStatus('REJECTED' as any)).toBe('Rejected')
  })

  it('falls back to PendingReview for unknown string', () => {
    expect(normalizeProposalStatus('unknown' as any)).toBe('PendingReview')
  })
})

describe('normalizeProposalSourceType', () => {
  it('maps numeric index to source string', () => {
    expect(normalizeProposalSourceType(0)).toBe('Queue')
    expect(normalizeProposalSourceType(1)).toBe('Chat')
    expect(normalizeProposalSourceType(2)).toBe('Manual')
  })

  it('falls back to Manual for out-of-range index', () => {
    expect(normalizeProposalSourceType(99)).toBe('Manual')
  })

  it('normalizes case-insensitive string source', () => {
    expect(normalizeProposalSourceType('queue' as any)).toBe('Queue')
    expect(normalizeProposalSourceType('CHAT' as any)).toBe('Chat')
  })

  it('falls back to Manual for unknown string', () => {
    expect(normalizeProposalSourceType('unknown' as any)).toBe('Manual')
  })
})

describe('normalizeProposalRiskLevel', () => {
  it('maps numeric index to risk string', () => {
    expect(normalizeProposalRiskLevel(0)).toBe('Low')
    expect(normalizeProposalRiskLevel(1)).toBe('Medium')
    expect(normalizeProposalRiskLevel(2)).toBe('High')
    expect(normalizeProposalRiskLevel(3)).toBe('Critical')
  })

  it('falls back to Low for out-of-range index', () => {
    expect(normalizeProposalRiskLevel(99)).toBe('Low')
  })

  it('normalizes case-insensitive string risk', () => {
    expect(normalizeProposalRiskLevel('medium' as any)).toBe('Medium')
    expect(normalizeProposalRiskLevel('HIGH' as any)).toBe('High')
  })

  it('falls back to Low for unknown string', () => {
    expect(normalizeProposalRiskLevel('unknown' as any)).toBe('Low')
  })
})
