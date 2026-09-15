import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import ReviewAppliedDecisionRecord from '../../../components/review/ReviewAppliedDecisionRecord.vue'
import ReviewProposalCard from '../../../components/review/ReviewProposalCard.vue'
import { resetProposalDisplayNamesForTests } from '../../../composables/useProposalDisplayNames'
import type { Proposal, ProposalOperation } from '../../../types/automation'
import { formatRecordedOperationActionLabel } from '../../../utils/recordedOperationPresentation'

const mocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
  getColumns: vi.fn(),
}))

vi.mock('../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
}))

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
    status: 'Expired',
    riskLevel: 'Low',
    summary: 'Recorded operation fallback',
    diffPreview: null,
    validationIssues: null,
    createdAt: '2026-09-01T10:00:00.000Z',
    updatedAt: '2026-09-01T10:00:00.000Z',
    expiresAt: '2026-09-01T11:00:00.000Z',
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'correlation-1',
    operations: [makeOperation()],
    ...overrides,
  } as Proposal
}

describe('recorded operation presentation residuals (#1434)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetProposalDisplayNamesForTests()
    mocks.getBoards.mockResolvedValue([])
    mocks.getColumns.mockResolvedValue([])
  })

  it('treats malformed legacy action values as unavailable copy instead of throwing', () => {
    expect(formatRecordedOperationActionLabel(undefined as never)).toBe('')
    expect(formatRecordedOperationActionLabel(null as never)).toBe('')
  })

  it('uses the shared lifecycle action wording in an applied decision fallback', () => {
    const wrapper = mount(ReviewAppliedDecisionRecord, {
      props: {
        proposal: makeProposal({
          status: 'Applied',
          decidedAt: '2026-09-01T10:30:00.000Z',
          appliedAt: '2026-09-01T10:45:00.000Z',
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

  it('states the recorded-operation fallback once across its banner and supporting note', async () => {
    const proposal = makeProposal()
    const wrapper = mount(ReviewProposalCard, {
      props: {
        proposal,
        isExpired: true,
        isBusy: false,
        selectedDiffProposalId: proposal.id,
        selectedDiff: null,
        selectedDiffMode: 'stored',
        selectedDiffInvalidReason: null,
        selectedDiffRevised: false,
        captureHref: '/workspace/inbox',
        proposalHref: `/workspace/review#proposal-${proposal.id}`,
      },
      global: { stubs: { RouterLink: RouterLinkStub } },
    })
    await flushPromises()

    expect(wrapper.get('[data-testid="review-diff-banner"]').text()).toContain(
      'showing the proposal\'s recorded operations',
    )
    expect(wrapper.get('[data-testid="review-diff-stored-ops-note"]').text()).toBe(
      'No stored preview was captured.',
    )
    wrapper.unmount()
  })
})
