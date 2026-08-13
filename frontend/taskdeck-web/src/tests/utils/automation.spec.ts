import { describe, it, expect } from 'vitest'
import {
  normalizeProposalStatus,
  normalizeProposalSourceType,
  normalizeProposalRiskLevel,
  sortProposalsByRisk,
} from '../../utils/automation'

function makeProposal(id: string, riskLevel: string, createdAt: string) {
  return {
    id,
    riskLevel,
    createdAt,
  } as any
}

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

  it('falls back to Critical for out-of-range index', () => {
    expect(normalizeProposalRiskLevel(99)).toBe('Critical')
  })

  it('normalizes case-insensitive string risk', () => {
    expect(normalizeProposalRiskLevel('medium' as any)).toBe('Medium')
    expect(normalizeProposalRiskLevel('HIGH' as any)).toBe('High')
  })

  it('falls back to Critical for malformed wire values', () => {
    for (const value of [null, undefined, {}, 'unknown', -1, 1.5, 4, Number.NaN, Number.POSITIVE_INFINITY]) {
      expect(normalizeProposalRiskLevel(value)).toBe('Critical')
    }
  })
})

describe('sortProposalsByRisk', () => {
  it('orders all risk levels without mutating the source array', () => {
    const proposals = [
      makeProposal('critical', 'Critical', '2026-08-12T12:00:00Z'),
      makeProposal('low', 'Low', '2026-08-12T08:00:00Z'),
      makeProposal('high', 'High', '2026-08-12T10:00:00Z'),
      makeProposal('medium', 'Medium', '2026-08-12T09:00:00Z'),
    ]
    const originalOrder = proposals.map((proposal) => proposal.id)

    expect(sortProposalsByRisk(proposals).map((proposal) => proposal.id)).toEqual([
      'low',
      'medium',
      'high',
      'critical',
    ])
    expect(proposals.map((proposal) => proposal.id)).toEqual(originalOrder)
  })

  it('preserves source order for deterministic same-risk ties', () => {
    const proposals = [
      makeProposal('same-b', 'Medium', '2026-08-12T09:00:00Z'),
      makeProposal('newest', 'Medium', '2026-08-12T10:00:00Z'),
      makeProposal('same-a', 'Medium', '2026-08-12T09:00:00Z'),
    ]

    expect(sortProposalsByRisk(proposals).map((proposal) => proposal.id)).toEqual([
      'same-b',
      'newest',
      'same-a',
    ])
  })
})
