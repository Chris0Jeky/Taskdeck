import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewAppliedDecisionRecord from '../../../components/review/ReviewAppliedDecisionRecord.vue'
import type { Proposal, ProposalOperation } from '../../../types/automation'

function makeOperation(overrides: Partial<ProposalOperation> = {}): ProposalOperation {
  return {
    id: 'operation-1',
    proposalId: 'proposal-applied',
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
    id: 'proposal-applied',
    sourceType: 'Queue',
    sourceReferenceId: 'capture-1',
    boardId: 'board-1',
    requestedByUserId: '31f21efa-8ce7-4e85-8c18-0eefac9edcb7',
    status: 'Applied',
    riskLevel: 'Low',
    summary: 'Apply the exact recorded change',
    diffPreview: null,
    validationIssues: null,
    createdAt: '2026-08-24T08:00:00.000Z',
    updatedAt: '2026-08-24T09:30:00.000Z',
    expiresAt: '2026-08-25T08:00:00.000Z',
    decidedAt: '2026-08-24T09:00:00.000Z',
    decidedByUserId: '31f21efa-8ce7-4e85-8c18-0eefac9edcb7',
    appliedAt: '2026-08-24T09:30:00.000Z',
    failureReason: null,
    correlationId: 'correlation-1',
    operations: [
      makeOperation({ id: 'operation-1', sequence: 0 }),
      makeOperation({ id: 'operation-2', sequence: 1, actionType: 'MoveCard' }),
    ],
    presentation: {
      plainSummary: 'Apply the exact recorded change',
      impactSummary: 'Two effective operations were applied.',
      riskCue: 'Low risk.',
      sourceCue: 'Created from Inbox capture triage.',
      operationHeadlines: [
        'Create card "Read-only decision record".',
        'Move card "Read-only decision record" to Done.',
      ],
      affectedEntities: [],
    },
    approvedRevisionId: null,
    ...overrides,
  }
}

describe('ReviewAppliedDecisionRecord', () => {
  it('renders the truthful decision actor, timestamps, and ordered effective operations', () => {
    const wrapper = mount(ReviewAppliedDecisionRecord, { props: { proposal: makeProposal() } })

    expect(wrapper.get('[data-testid="applied-record-outcome"]').text()).toBe('Applied')
    expect(wrapper.get('[data-testid="applied-record-decision"]').text()).toBe('Approved')
    expect(wrapper.get('[data-testid="applied-record-decision-actor"]').text()).toBe(
      '31f21efa-8ce7-4e85-8c18-0eefac9edcb7',
    )
    expect(wrapper.get('[data-testid="applied-record-decision-time"]').text()).toContain('2026')
    expect(wrapper.get('[data-testid="applied-record-applied-time"]').text()).toContain('2026')

    const operations = wrapper.get('[data-testid="applied-record-operations"]').findAll('li')
    expect(operations.map((operation) => operation.text())).toEqual([
      'Create card "Read-only decision record".',
      'Move card "Read-only decision record" to Done.',
    ])
    expect(wrapper.text()).not.toContain('Applied by')
  })

  it('orders operation fallbacks by sequence when presentation headlines are unavailable', () => {
    const wrapper = mount(ReviewAppliedDecisionRecord, {
      props: {
        proposal: makeProposal({
          presentation: undefined,
          operations: [
            makeOperation({ id: 'later', sequence: 8, actionType: 'MoveCard' }),
            makeOperation({ id: 'earlier', sequence: 2, actionType: 'CreateCard' }),
          ],
        }),
      },
    })

    expect(
      wrapper.get('[data-testid="applied-record-operations"]').findAll('li').map((row) => row.text()),
    ).toEqual(['create card · card', 'move card · card'])
  })

  it('labels missing or invalid legacy metadata as not recorded', () => {
    const wrapper = mount(ReviewAppliedDecisionRecord, {
      props: {
        proposal: makeProposal({
          decidedAt: 'not-a-date',
          decidedByUserId: 'legacy-user',
          appliedAt: null,
          operations: [],
          presentation: undefined,
        }),
      },
    })

    expect(wrapper.get('[data-testid="applied-record-decision-actor"]').text()).toBe('Not recorded')
    expect(wrapper.get('[data-testid="applied-record-decision-time"]').text()).toBe('Not recorded')
    expect(wrapper.get('[data-testid="applied-record-applied-time"]').text()).toBe('Not recorded')
    expect(wrapper.get('[data-testid="applied-record-operations-empty"]').text()).toBe('Not recorded')
  })
})
