import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewAppliedDecisionRecord from '../../../components/review/ReviewAppliedDecisionRecord.vue'
import type { Proposal, ProposalOperation } from '../../../types/automation'
import { formatRecordedOperationActionLabel } from '../../../utils/recordedOperationPresentation'

function makeOperation(overrides: Partial<ProposalOperation> = {}): ProposalOperation {
  return {
    id: 'operation-1',
    proposalId: 'proposal-1',
    sequence: 0,
    actionType: 'CreateCard',
    targetType: 'Card',
    targetId: null,
    parameters: '{}',
    idempotencyKey: 'operation-key-1',
    expectedVersion: null,
    ...overrides,
  }
}

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'proposal-1',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'Applied',
    riskLevel: 'Low',
    summary: 'Recorded operation fallback',
    diffPreview: null,
    validationIssues: null,
    createdAt: '2026-09-01T10:00:00.000Z',
    updatedAt: '2026-09-01T10:00:00.000Z',
    expiresAt: '2026-09-01T11:00:00.000Z',
    decidedAt: '2026-09-01T10:30:00.000Z',
    decidedByUserId: null,
    appliedAt: '2026-09-01T10:45:00.000Z',
    failureReason: null,
    correlationId: 'correlation-1',
    operations: [makeOperation()],
    ...overrides,
  } as Proposal
}

describe('recorded operation presentation residuals (#1434)', () => {
  it('treats malformed legacy action values as unavailable copy instead of throwing', () => {
    expect(formatRecordedOperationActionLabel(undefined)).toBe('')
    expect(formatRecordedOperationActionLabel(null)).toBe('')
  })

  it('uses the shared lifecycle action wording in an applied decision fallback', () => {
    const wrapper = mount(ReviewAppliedDecisionRecord, {
      props: {
        proposal: makeProposal({
          presentation: undefined,
          operations: [makeOperation({ actionType: 'archive-lifecycle' })],
        }),
      },
    })

    expect(
      wrapper.get('[data-testid="applied-record-operations"]').findAll('li').map(row => row.text()),
    ).toEqual(['archive · card'])
    wrapper.unmount()
  })
})
